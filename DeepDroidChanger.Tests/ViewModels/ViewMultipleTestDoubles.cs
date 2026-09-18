using System.Runtime.CompilerServices;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using ScrcpyNet;

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
        ["ViewMultipleDevices_Stopped"] = "Stopped",
        ["ViewMultipleDevices_Failed"] = "Failed",
        ["ViewMultipleDevices_Busy"] = "Already open in another viewer",
        ["ViewDevice_TestStatus"] = "Status {0}"
    };

    private static readonly IReadOnlyDictionary<string, string> Vietnamese = new Dictionary<string, string>
    {
        ["ViewMultipleDevices_Connecting"] = "Đang kết nối...",
        ["ViewMultipleDevices_Online"] = "Trực tuyến",
        ["ViewMultipleDevices_Stopped"] = "Đã dừng",
        ["ViewMultipleDevices_Failed"] = "Thất bại",
        ["ViewMultipleDevices_Busy"] = "Thiết bị đang được mở ở cửa sổ xem khác",
        ["ViewDevice_TestStatus"] = "Trạng thái {0}"
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

internal sealed class NoOpViewDeviceWindowService : IViewDeviceWindowService
{
    public int OpenCount { get; private set; }

    public List<string> OpenedSerials { get; } = [];

    public Task OpenAsync(
        string serial,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpenCount++;
        OpenedSerials.Add(serial);
        return Task.CompletedTask;
    }

    public Task CloseAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
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

internal sealed class FakeClipboardService : IViewDeviceClipboardService
{
    public string? Text { get; set; }

    public TaskCompletionSource<string?>? ReadGate { get; set; }

    public int ReadCount { get; private set; }

    public List<string> SetTexts { get; } = [];

    public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        ReadCount++;
        if (ReadGate is not null)
            return await ReadGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        return Text;
    }

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetTexts.Add(text);
        Text = text;
        return Task.CompletedTask;
    }
}

internal sealed class FakeSessionFactory : ISingleViewDeviceSessionFactory
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

    public ISingleViewDeviceSession Create(ViewDeviceLaunchOptions options)
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

internal sealed class FakeViewDeviceSession : ISingleViewDeviceSession
{
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

    public SingleViewDeviceSessionState State { get; private set; } = SingleViewDeviceSessionState.Created;

    public Scrcpy? Client { get; private set; }

    public int ContentWidth { get; private set; }

    public int ContentHeight { get; private set; }

    public IReadOnlyList<string> RecentDiagnostics => [];

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public int DisposeCount { get; private set; }

    public int FlushCount { get; private set; }

    public List<string> PastedTexts { get; } = [];

    public bool IsActive { get; private set; }

    public bool FailStart { get; set; }

    public Exception? StartException { get; set; }

    public bool RejectPaste { get; set; }

    public TaskCompletionSource? StartGate { get; set; }

    public TaskCompletionSource? StopGate { get; set; }

    public TaskCompletionSource? StopStarted { get; set; }

    public TaskCompletionSource? FlushGate { get; set; }

    public List<string>? LifecycleEvents { get; set; }

    public Action<bool>? ActiveChanged { get; set; }

    public event EventHandler<SingleViewDeviceSessionStateChangedEventArgs>? StateChanged;
    public event EventHandler<SingleViewDeviceContentSizeChangedEventArgs>? ContentSizeChanged;
    public event EventHandler<ScrcpyClipboardChangedEventArgs>? ClipboardChanged;
    public event EventHandler? Exited;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartCount++;
        LifecycleEvents?.Add($"start:{Serial}");
        if (StartGate is not null)
            await StartGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        SetState(SingleViewDeviceSessionState.Starting);
        if (FailStart || StartException is not null)
        {
            SetState(SingleViewDeviceSessionState.Failed);
            throw StartException ?? new InvalidOperationException("The configured fake session failed to start.");
        }

        Client = (Scrcpy)RuntimeHelpers.GetUninitializedObject(typeof(Scrcpy));
        SetActive(true);
        SetState(SingleViewDeviceSessionState.Running);
        ContentSizeChanged?.Invoke(
            this,
            new SingleViewDeviceContentSizeChangedEventArgs(ContentWidth, ContentHeight));
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopCount++;
        LifecycleEvents?.Add($"stop:{Serial}");
        StopStarted?.TrySetResult();
        if (StopGate is not null)
            await StopGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        SetActive(false);
        Client = null;
        SetState(SingleViewDeviceSessionState.Closed);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        LifecycleEvents?.Add($"dispose:{Serial}");
        return ValueTask.CompletedTask;
    }

    public Task FlushControlAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FlushCount++;
        if (FlushGate is not null)
            return FlushGate.Task.WaitAsync(cancellationToken);

        return Task.CompletedTask;
    }

    public Task PasteHostClipboardAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (RejectPaste)
            return Task.FromException(new InvalidOperationException("The fake paste queue rejected the command."));

        PastedTexts.Add(text);
        return Task.CompletedTask;
    }

    public Task PasteHostClipboardWithPasteKeyAsync(string text, CancellationToken cancellationToken = default)
    {
        return PasteHostClipboardAsync(text, cancellationToken);
    }

    public Task SendKeyEventAsync(int keyCode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task SendBackOrScreenOnAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task SetScreenPowerModeAsync(
        AndroidScreenPowerMode mode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task RotateDeviceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task InjectTextAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task RequestClipboardAsync(
        ScrcpyCopyKey copyKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task ExpandNotificationPanelAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task ExpandSettingsPanelAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task CollapsePanelsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public void RaiseContentSize(int width, int height)
    {
        ContentWidth = width;
        ContentHeight = height;
        ContentSizeChanged?.Invoke(
            this,
            new SingleViewDeviceContentSizeChangedEventArgs(width, height));
    }

    public void RaiseClipboard(string text)
    {
        ClipboardChanged?.Invoke(this, new ScrcpyClipboardChangedEventArgs(text));
    }

    public void RaiseState(SingleViewDeviceSessionState state)
    {
        SetState(state);
    }

    public void Exit()
    {
        SetActive(false);
        SetState(SingleViewDeviceSessionState.Failed);
        Exited?.Invoke(this, EventArgs.Empty);
    }

    private void SetState(SingleViewDeviceSessionState state)
    {
        SingleViewDeviceSessionState previous = State;
        State = state;
        StateChanged?.Invoke(
            this,
            new SingleViewDeviceSessionStateChangedEventArgs(previous, state));
    }

    private void SetActive(bool active)
    {
        if (IsActive == active)
            return;

        IsActive = active;
        ActiveChanged?.Invoke(active);
    }
}
