namespace DeepDroidChanger.Services;

internal sealed class DeviceViewerIpRefreshLifetime : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationToken _viewerLifetimeToken;
    private DeviceViewerIpRefreshOperation? _currentOperation;
    private bool _disposed;

    public DeviceViewerIpRefreshLifetime(CancellationToken viewerLifetimeToken)
    {
        _viewerLifetimeToken = viewerLifetimeToken;
    }

    public DeviceViewerIpRefreshOperation? Start(long sessionGeneration)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_viewerLifetimeToken);
        DeviceViewerIpRefreshOperation? previousOperation;
        DeviceViewerIpRefreshOperation nextOperation;

        lock (_gate)
        {
            if (_disposed)
            {
                cancellation.Dispose();
                return null;
            }

            nextOperation = new DeviceViewerIpRefreshOperation(
                this,
                sessionGeneration,
                cancellation);
            previousOperation = _currentOperation;
            _currentOperation = nextOperation;
        }

        previousOperation?.CancelAndDispose();
        return nextOperation;
    }

    public void CancelCurrent()
    {
        DeviceViewerIpRefreshOperation? operation;
        lock (_gate)
        {
            operation = _currentOperation;
            _currentOperation = null;
        }

        operation?.CancelAndDispose();
    }

    internal bool IsCurrent(DeviceViewerIpRefreshOperation operation, long sessionGeneration)
    {
        lock (_gate)
        {
            return !_disposed
                && ReferenceEquals(_currentOperation, operation)
                && operation.SessionGeneration == sessionGeneration;
        }
    }

    internal void Complete(DeviceViewerIpRefreshOperation operation)
    {
        var shouldDispose = false;
        lock (_gate)
        {
            if (ReferenceEquals(_currentOperation, operation))
            {
                _currentOperation = null;
                shouldDispose = true;
            }
        }

        if (shouldDispose)
            operation.DisposeCancellation();
    }

    public void Dispose()
    {
        DeviceViewerIpRefreshOperation? operation;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            operation = _currentOperation;
            _currentOperation = null;
        }

        operation?.CancelAndDispose();
    }

    internal sealed class DeviceViewerIpRefreshOperation : IDisposable
    {
        private readonly DeviceViewerIpRefreshLifetime _owner;
        private int _disposed;

        internal DeviceViewerIpRefreshOperation(
            DeviceViewerIpRefreshLifetime owner,
            long sessionGeneration,
            CancellationTokenSource cancellation)
        {
            _owner = owner;
            SessionGeneration = sessionGeneration;
            Cancellation = cancellation;
        }

        internal long SessionGeneration { get; }

        internal CancellationTokenSource Cancellation { get; }

        internal CancellationToken Token => Cancellation.Token;

        internal bool IsCurrent(long sessionGeneration) =>
            _owner.IsCurrent(this, sessionGeneration);

        internal void CancelAndDispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            Cancellation.Dispose();
        }

        internal void DisposeCancellation()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Cancellation.Dispose();
        }

        public void Dispose()
        {
            _owner.Complete(this);
        }
    }
}
