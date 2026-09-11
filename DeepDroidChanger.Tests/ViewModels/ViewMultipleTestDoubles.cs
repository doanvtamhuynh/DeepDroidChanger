using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;

namespace DeepDroidChanger.Tests.ViewModels;

internal sealed class ImmediateUiDispatcher : IUiDispatcherService
{
    public bool CheckAccess() => true;

    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}

internal sealed class FakeLocalizationService : ILocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["ViewMultipleDevices_Connecting"] = "Connecting...",
        ["ViewMultipleDevices_Online"] = "Online",
        ["ViewMultipleDevices_Disconnected"] = "Disconnected",
        ["ViewMultipleDevices_Failed"] = "Failed"
    };

    private static readonly IReadOnlyDictionary<string, string> Vietnamese = new Dictionary<string, string>
    {
        ["ViewMultipleDevices_Connecting"] = "Đang kết nối...",
        ["ViewMultipleDevices_Online"] = "Trực tuyến",
        ["ViewMultipleDevices_Disconnected"] = "Đã ngắt kết nối",
        ["ViewMultipleDevices_Failed"] = "Thất bại"
    };

    public event EventHandler? LanguageChanged;

    public string Language { get; private set; } = "en";

    public string NormalizeLanguage(string languageCode)
    {
        return string.Equals(languageCode, "vi", StringComparison.OrdinalIgnoreCase)
            ? "vi"
            : "en";
    }

    public string GetString(string resourceKey)
    {
        IReadOnlyDictionary<string, string> resources = Language == "vi" ? Vietnamese : English;
        return resources.TryGetValue(resourceKey, out string? value) ? value : resourceKey;
    }

    public void ApplyLanguage(string languageCode)
    {
        Language = NormalizeLanguage(languageCode);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class FakeTracker : IAdbDeviceTrackerService
{
    private readonly object _gate = new();
    private Dictionary<string, AdbDevice> _devices;

    public FakeTracker(IEnumerable<AdbDevice>? devices = null)
    {
        _devices = (devices ?? []).ToDictionary(
            device => device.Serial,
            StringComparer.OrdinalIgnoreCase);
    }

    public event EventHandler<AdbDeviceStateChangedEventArgs>? DeviceStateChanged;
    public event EventHandler<AdbDeviceTrackerHealthChangedEventArgs>? HealthChanged;

    public AdbDeviceTrackerHealth Health { get; private set; } = AdbDeviceTrackerHealth.Connected;

    public IReadOnlyList<AdbDevice> CurrentSnapshot
    {
        get
        {
            lock (_gate)
                return _devices.Values.ToArray();
        }
    }

    public AdbDevice? GetDevice(string serial)
    {
        lock (_gate)
            return _devices.TryGetValue(serial, out AdbDevice? device) ? device : null;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public void SetDevices(IEnumerable<AdbDevice> devices)
    {
        lock (_gate)
        {
            _devices = devices.ToDictionary(
                device => device.Serial,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SetDevice(AdbDevice? current)
    {
        AdbDevice? previous;
        string serial;
        lock (_gate)
        {
            serial = current?.Serial
                ?? _devices.Values.FirstOrDefault()?.Serial
                ?? throw new InvalidOperationException("A serial is required for a device event.");
            _devices.TryGetValue(serial, out previous);
            if (current is null)
                _devices.Remove(serial);
            else
                _devices[serial] = current;
        }

        DeviceStateChanged?.Invoke(
            this,
            new AdbDeviceStateChangedEventArgs(serial, previous, current));
    }

    public void RaiseDeviceStateChanged(
        string serial,
        AdbDevice? previous = null,
        AdbDevice? current = null)
    {
        current ??= GetDevice(serial);
        previous ??= current;
        DeviceStateChanged?.Invoke(
            this,
            new AdbDeviceStateChangedEventArgs(serial, previous, current));
    }

    public void SetHealth(AdbDeviceTrackerHealth health)
    {
        AdbDeviceTrackerHealth previous = Health;
        Health = health;
        HealthChanged?.Invoke(
            this,
            new AdbDeviceTrackerHealthChangedEventArgs(previous, health));
    }
}

internal sealed class FakeDeviceStoreService : IDeviceStoreService
{
    private readonly object _gate = new();
    private IReadOnlyList<StoredDeviceConfig> _devices;
    private int _activeLoads;
    private readonly Dictionary<int, TaskCompletionSource<bool>> _loadSignals = [];

    public FakeDeviceStoreService(IEnumerable<StoredDeviceConfig>? devices = null)
    {
        _devices = (devices ?? []).ToArray();
    }

    public int LoadCount => Volatile.Read(ref _loadCount);

    public int MaxConcurrentLoads => Volatile.Read(ref _maxConcurrentLoads);

    public TaskCompletionSource? LoadGate { get; set; }

    public Func<int, CancellationToken, Task>? LoadHook { get; set; }

    private int _loadCount;
    private int _maxConcurrentLoads;

    public IReadOnlyList<StoredDeviceConfig> Devices
    {
        get
        {
            lock (_gate)
                return _devices;
        }
    }

    public Task WaitForLoadAsync(int expectedLoadCount)
    {
        lock (_gate)
        {
            if (_loadCount >= expectedLoadCount)
                return Task.CompletedTask;

            if (!_loadSignals.TryGetValue(expectedLoadCount, out TaskCompletionSource<bool>? signal))
            {
                signal = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _loadSignals[expectedLoadCount] = signal;
            }

            return signal.Task;
        }
    }

    public void SetDevices(IEnumerable<StoredDeviceConfig> devices)
    {
        lock (_gate)
            _devices = devices.ToArray();
    }

    public async Task<IReadOnlyList<StoredDeviceConfig>> LoadAsync(
        CancellationToken cancellationToken)
    {
        int loadCount = Interlocked.Increment(ref _loadCount);
        int activeLoads = Interlocked.Increment(ref _activeLoads);
        UpdateMaximum(ref _maxConcurrentLoads, activeLoads);
        lock (_gate)
        {
            foreach ((int expectedCount, TaskCompletionSource<bool> signal) in _loadSignals.ToArray())
            {
                if (loadCount >= expectedCount)
                    signal.TrySetResult(true);
            }
        }

        try
        {
            if (LoadHook is not null)
                await LoadHook(loadCount, cancellationToken).ConfigureAwait(false);

            if (LoadGate is not null)
                await LoadGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            lock (_gate)
                return _devices.ToArray();
        }
        finally
        {
            Interlocked.Decrement(ref _activeLoads);
        }
    }

    public Task SaveAsync(
        IEnumerable<StoredDeviceConfig> devices,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetDevices(devices);
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(
        string serial,
        Action<StoredDeviceConfig> update,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            StoredDeviceConfig? device = _devices.FirstOrDefault(
                item => string.Equals(item.Serial, serial, StringComparison.OrdinalIgnoreCase));
            if (device is null)
                return Task.FromResult(false);

            update(device);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<StoredDeviceConfig>> MergeAsync(
        IEnumerable<StoredDeviceConfig> devices,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dictionary<string, StoredDeviceConfig> merged = Devices.ToDictionary(
            item => item.Serial,
            StringComparer.OrdinalIgnoreCase);
        foreach (StoredDeviceConfig device in devices)
            merged[device.Serial] = device;
        SetDevices(merged.Values);
        return Task.FromResult(Devices);
    }

    public Task<bool> RemoveAsync(string serial, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            int originalCount = _devices.Count;
            _devices = _devices
                .Where(item => !string.Equals(item.Serial, serial, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return Task.FromResult(_devices.Count != originalCount);
        }
    }

    private static void UpdateMaximum(ref int location, int value)
    {
        while (true)
        {
            int current = Volatile.Read(ref location);
            if (value <= current ||
                Interlocked.CompareExchange(ref location, value, current) == current)
            {
                return;
            }
        }
    }
}

internal sealed class FakeSessionFactory : IViewDeviceSessionFactory
{
    private readonly Queue<FakeViewDeviceSession> _queuedSessions;

    public FakeSessionFactory(params FakeViewDeviceSession[] sessions)
    {
        _queuedSessions = new Queue<FakeViewDeviceSession>(sessions);
    }

    public int CreateCount { get; private set; }

    public List<FakeViewDeviceSession> Sessions { get; } = [];

    public List<string> LifecycleEvents { get; } = [];

    public int ActiveSessionCount { get; private set; }

    public int MaxActiveSessionCount { get; private set; }

    public IViewDeviceSession Create(ViewDeviceLaunchOptions options)
    {
        CreateCount++;
        FakeViewDeviceSession session = _queuedSessions.Count > 0
            ? _queuedSessions.Dequeue()
            : new FakeViewDeviceSession(options.Serial);
        if (!string.Equals(session.Serial, options.Serial, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The fake session serial did not match launch options.");

        session.LifecycleEvents = LifecycleEvents;
        session.ActiveChanged = active =>
        {
            int current = active
                ? Interlocked.Increment(ref _activeSessionCount)
                : Interlocked.Decrement(ref _activeSessionCount);
            ActiveSessionCount = current;
            int maximum = Volatile.Read(ref _maxActiveSessionCount);
            while (current > maximum &&
                   Interlocked.CompareExchange(
                       ref _maxActiveSessionCount,
                       current,
                       maximum) != maximum)
            {
                maximum = Volatile.Read(ref _maxActiveSessionCount);
            }

            MaxActiveSessionCount = Volatile.Read(ref _maxActiveSessionCount);
        };
        Sessions.Add(session);
        return session;
    }

    private int _activeSessionCount;
    private int _maxActiveSessionCount;
}

internal sealed class FakeViewDeviceSession : IViewDeviceSession
{
    private static int _nextHandle = 1000;

    public FakeViewDeviceSession(
        string serial,
        int contentWidth = 720,
        int contentHeight = 1280)
    {
        Serial = serial;
        ContentWidth = contentWidth;
        ContentHeight = contentHeight;
    }

    public string Serial { get; }

    public ViewDeviceSessionState State { get; private set; } = ViewDeviceSessionState.Created;

    public IntPtr NativeWindowHandle { get; private set; }

    public int ContentWidth { get; private set; }

    public int ContentHeight { get; private set; }

    public IReadOnlyList<string> RecentDiagnostics => [];

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public int DisposeCount { get; private set; }

    public bool IsActive { get; private set; }

    public bool FailStart { get; set; }

    public TaskCompletionSource? StartGate { get; set; }

    public TaskCompletionSource? StopGate { get; set; }

    public List<string>? LifecycleEvents { get; set; }

    public Action<bool>? ActiveChanged { get; set; }

    public event EventHandler<ViewDeviceSessionStateChangedEventArgs>? StateChanged;
    public event EventHandler? NativeWindowReady;
    public event EventHandler<ViewDeviceContentSizeChangedEventArgs>? ContentSizeChanged;
    public event EventHandler? Exited;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartCount++;
        LifecycleEvents?.Add($"start:{Serial}");
        if (StartGate is not null)
            await StartGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (FailStart)
        {
            SetState(ViewDeviceSessionState.Failed);
            throw new InvalidOperationException("The configured fake session failed to start.");
        }

        NativeWindowHandle = new IntPtr(Interlocked.Increment(ref _nextHandle));
        SetActive(true);
        SetState(ViewDeviceSessionState.Running);
        NativeWindowReady?.Invoke(this, EventArgs.Empty);
        ContentSizeChanged?.Invoke(
            this,
            new ViewDeviceContentSizeChangedEventArgs(ContentWidth, ContentHeight));
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopCount++;
        LifecycleEvents?.Add($"stop:{Serial}");
        if (StopGate is not null)
            await StopGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        SetActive(false);
        NativeWindowHandle = IntPtr.Zero;
        SetState(ViewDeviceSessionState.Closed);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        LifecycleEvents?.Add($"dispose:{Serial}");
        return ValueTask.CompletedTask;
    }

    public void RaiseContentSize(int width, int height)
    {
        ContentWidth = width;
        ContentHeight = height;
        ContentSizeChanged?.Invoke(
            this,
            new ViewDeviceContentSizeChangedEventArgs(width, height));
    }

    public void RaiseNativeWindowReady()
    {
        NativeWindowReady?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseState(ViewDeviceSessionState state)
    {
        SetState(state);
    }

    public void Exit()
    {
        SetActive(false);
        NativeWindowHandle = IntPtr.Zero;
        SetState(ViewDeviceSessionState.Failed);
        Exited?.Invoke(this, EventArgs.Empty);
    }

    private void SetState(ViewDeviceSessionState state)
    {
        ViewDeviceSessionState previous = State;
        State = state;
        StateChanged?.Invoke(
            this,
            new ViewDeviceSessionStateChangedEventArgs(previous, state));
    }

    private void SetActive(bool active)
    {
        if (IsActive == active)
            return;

        IsActive = active;
        ActiveChanged?.Invoke(active);
    }
}
