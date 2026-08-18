namespace DeepDroidChanger.Services;

/// <summary>
/// Thread-safe, serial-scoped owner of exclusive device action operations.
/// </summary>
public sealed class DeviceActionCoordinatorService : IDeviceActionCoordinatorService
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, ActiveDeviceAction> _activeOperations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DeviceActionOperationSnapshot> _pendingNotifications = new();
    private bool _isPublishingNotifications;

    public event Action<DeviceActionOperationSnapshot>? OperationStateChanged;

    public bool IsBusy(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
            return false;

        lock (_syncRoot)
            return _activeOperations.ContainsKey(NormalizeSerial(serial));
    }

    public DeviceActionOperationSnapshot? GetOperation(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
            return null;

        lock (_syncRoot)
        {
            return _activeOperations.TryGetValue(NormalizeSerial(serial), out ActiveDeviceAction? operation)
                ? operation.CreateSnapshot()
                : null;
        }
    }

    public IReadOnlyList<DeviceActionSessionSnapshot> GetActiveSessions()
    {
        lock (_syncRoot)
        {
            return _activeOperations.Values
                .GroupBy(operation => operation.SessionId)
                .Select(CreateSessionSnapshot)
                .OrderBy(session => session.Operations.Min(operation => operation.OperationId))
                .ToArray();
        }
    }

    public IDeviceActionOperation? TryStart(
        string serial,
        DeviceActionKind kind,
        bool canCancel,
        CancellationToken externalCancellationToken = default,
        Guid? sessionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        if (externalCancellationToken.IsCancellationRequested)
            return null;

        string normalizedSerial = NormalizeSerial(serial);
        Guid effectiveSessionId = sessionId.GetValueOrDefault();
        if (effectiveSessionId == Guid.Empty)
            effectiveSessionId = Guid.NewGuid();
        var operation = new ActiveDeviceAction(
            this,
            normalizedSerial,
            kind,
            canCancel,
            effectiveSessionId,
            externalCancellationToken);

        bool shouldPublish;
        lock (_syncRoot)
        {
            if (_activeOperations.ContainsKey(normalizedSerial))
            {
                operation.DisposeResources();
                return null;
            }

            _activeOperations.Add(normalizedSerial, operation);
            shouldPublish = EnqueueNotificationLocked(operation.CreateSnapshot());
        }

        PublishPendingNotifications(shouldPublish);
        operation.RegisterExternalCancellation();
        return operation;
    }

    public bool TryRequestCancellation(string serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
            return false;

        ActiveDeviceAction? operation;
        lock (_syncRoot)
        {
            if (!_activeOperations.TryGetValue(NormalizeSerial(serial), out operation)
                || !operation.CanCancel)
            {
                return false;
            }
        }

        return RequestCancellation(
            operation,
            DeviceActionCancellationReason.UserStop,
            honorCanCancel: true);
    }

    public bool TryRequestSessionCancellation(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
            return false;

        ActiveDeviceAction[] operations;
        lock (_syncRoot)
        {
            operations = _activeOperations.Values
                .Where(operation => operation.SessionId == sessionId)
                .ToArray();
        }

        bool canceled = false;
        foreach (ActiveDeviceAction operation in operations)
        {
            canceled |= RequestCancellation(
                operation,
                DeviceActionCancellationReason.UserStop,
                honorCanCancel: true);
        }

        return canceled;
    }

    internal void Release(ActiveDeviceAction operation)
    {
        bool shouldPublish;
        lock (_syncRoot)
        {
            bool removed = _activeOperations.TryGetValue(operation.Serial, out ActiveDeviceAction? current)
                && ReferenceEquals(current, operation);
            if (!removed)
            {
                return;
            }

            _activeOperations.Remove(operation.Serial);
            operation.SetState(DeviceActionRuntimeState.Idle);
            shouldPublish = EnqueueNotificationLocked(operation.CreateSnapshot());
        }

        try
        {
            PublishPendingNotifications(shouldPublish);
        }
        finally
        {
            operation.DisposeResources();
        }
    }

    internal bool RequestCancellation(
        ActiveDeviceAction operation,
        DeviceActionCancellationReason reason,
        bool honorCanCancel)
    {
        bool shouldPublish;
        lock (_syncRoot)
        {
            if (!_activeOperations.TryGetValue(operation.Serial, out ActiveDeviceAction? current)
                || !ReferenceEquals(current, operation))
            {
                return false;
            }

            if (honorCanCancel && !operation.CanCancel)
                return false;

            if (operation.State != DeviceActionRuntimeState.Running)
                return false;

            operation.TrySetCancellationReason(reason);
            operation.SetState(DeviceActionRuntimeState.Stopping);
            shouldPublish = EnqueueNotificationLocked(operation.CreateSnapshot());
        }

        try
        {
            PublishPendingNotifications(shouldPublish);
        }
        finally
        {
            operation.Cancel();
        }

        return true;
    }

    private bool EnqueueNotificationLocked(DeviceActionOperationSnapshot snapshot)
    {
        _pendingNotifications.Enqueue(snapshot);
        if (_isPublishingNotifications)
            return false;

        _isPublishingNotifications = true;
        return true;
    }

    private void PublishPendingNotifications(bool shouldPublish)
    {
        if (!shouldPublish)
            return;

        while (true)
        {
            DeviceActionOperationSnapshot snapshot;
            lock (_syncRoot)
            {
                if (_pendingNotifications.Count == 0)
                {
                    _isPublishingNotifications = false;
                    return;
                }

                snapshot = _pendingNotifications.Dequeue();
            }

            Publish(snapshot);
        }
    }

    private void Publish(DeviceActionOperationSnapshot snapshot)
    {
        Delegate[] subscribers = OperationStateChanged?.GetInvocationList() ?? [];
        foreach (Action<DeviceActionOperationSnapshot> subscriber in subscribers.Cast<Action<DeviceActionOperationSnapshot>>())
        {
            try
            {
                subscriber(snapshot);
            }
            catch
            {
                // A presentation subscriber must not break coordinator ownership or notification draining.
            }
        }
    }

    private static DeviceActionSessionSnapshot CreateSessionSnapshot(
        IEnumerable<ActiveDeviceAction> operations)
    {
        DeviceActionOperationSnapshot[] snapshots = operations
            .Select(operation => operation.CreateSnapshot())
            .OrderBy(snapshot => snapshot.Serial, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        DeviceActionOperationSnapshot first = snapshots[0];
        DeviceActionRuntimeState state = snapshots.Any(snapshot =>
            snapshot.State == DeviceActionRuntimeState.Running)
            ? DeviceActionRuntimeState.Running
            : DeviceActionRuntimeState.Stopping;
        bool canCancel = snapshots.Any(snapshot =>
            snapshot.State == DeviceActionRuntimeState.Running
            && snapshot.CanCancel);
        return new DeviceActionSessionSnapshot(
            first.SessionId,
            first.Kind.ToLogicalActionKind(),
            state,
            canCancel,
            snapshots);
    }

    private static string NormalizeSerial(string serial)
    {
        return serial.Trim();
    }

    internal sealed class ActiveDeviceAction : IDeviceActionOperation
    {
        private readonly DeviceActionCoordinatorService _owner;
        private readonly CancellationTokenSource _cancellation;
        private readonly CancellationToken _externalCancellationToken;
        private CancellationTokenRegistration _externalRegistration;
        private int _state = (int)DeviceActionRuntimeState.Running;
        private int _cancellationReason = (int)DeviceActionCancellationReason.None;
        private int _disposed;
        private int _resourcesDisposed;

        public ActiveDeviceAction(
            DeviceActionCoordinatorService owner,
            string serial,
            DeviceActionKind kind,
            bool canCancel,
            Guid sessionId,
            CancellationToken externalCancellationToken)
        {
            _owner = owner;
            Serial = serial;
            Kind = kind;
            CanCancel = canCancel;
            SessionId = sessionId;
            _externalCancellationToken = externalCancellationToken;
            _cancellation = new CancellationTokenSource();
            OperationId = Guid.NewGuid();
        }

        public string Serial { get; }
        public DeviceActionKind Kind { get; }
        public Guid OperationId { get; }
        public Guid SessionId { get; }
        public DeviceActionRuntimeState State => (DeviceActionRuntimeState)Volatile.Read(ref _state);
        public bool CanCancel { get; }
        public DeviceActionCancellationReason CancellationReason =>
            (DeviceActionCancellationReason)Volatile.Read(ref _cancellationReason);
        public CancellationToken CancellationToken => _cancellation.Token;
        public bool IsCancellationRequested => _cancellation.IsCancellationRequested;
        public DeviceActionOperationSnapshot Snapshot => CreateSnapshot();

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _owner.Release(this);
        }

        internal void RegisterExternalCancellation()
        {
            if (_externalCancellationToken.CanBeCanceled)
            {
                _externalRegistration = _externalCancellationToken.Register(
                    static state =>
                    {
                        var operation = (ActiveDeviceAction)state!;
                        operation._owner.RequestCancellation(
                            operation,
                            DeviceActionCancellationReason.External,
                            honorCanCancel: false);
                    },
                    this);
            }
        }

        internal bool TrySetCancellationReason(DeviceActionCancellationReason reason)
        {
            return Interlocked.CompareExchange(
                ref _cancellationReason,
                (int)reason,
                (int)DeviceActionCancellationReason.None) == (int)DeviceActionCancellationReason.None;
        }

        internal void SetState(DeviceActionRuntimeState state)
        {
            Volatile.Write(ref _state, (int)state);
        }

        internal void Cancel()
        {
            try
            {
                if (!_cancellation.IsCancellationRequested)
                    _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A concurrent owner release has already unwound the operation.
            }
        }

        internal void DisposeResources()
        {
            if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
                return;

            _externalRegistration.Dispose();
            _cancellation.Dispose();
        }

        internal DeviceActionOperationSnapshot CreateSnapshot()
        {
            return new DeviceActionOperationSnapshot(
                Serial,
                Kind,
                OperationId,
                State,
                CanCancel,
                CancellationReason,
                SessionId);
        }
    }
}
