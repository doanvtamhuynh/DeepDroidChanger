using System.Diagnostics;
using System.Runtime.CompilerServices;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Fakes;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using DeepDroidChanger.ViewModels;
using ScrcpyNet;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.ViewDevices;

[TestClass]
public sealed class ViewDeviceViewModelTests
{
    private const string Serial = "SERIAL-123";

    [TestMethod]
    public async Task InitializeAsync_OfflineDevice_EntersWaitingForDevice()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Offline));
        var factory = new FakeSessionFactory(new FakeSession(Serial));
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);

        await viewModel.InitializeAsync(Serial, "Device");

        Assert.AreEqual(ViewDeviceSessionState.WaitingForDevice, viewModel.State);
        Assert.AreEqual(0, factory.CreateCount);
    }

    [TestMethod]
    public async Task InitializeAsync_UnauthorizedDevice_EntersUnauthorizedWithoutStartingSession()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Unauthorized));
        var factory = new FakeSessionFactory(new FakeSession(Serial));
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);

        await viewModel.InitializeAsync(Serial, "Device");

        Assert.AreEqual(ViewDeviceSessionState.Unauthorized, viewModel.State);
        Assert.AreEqual(0, factory.CreateCount);
    }

    [TestMethod]
    public async Task InitializeAsync_OnlineDevice_StartsSessionAndPublishesScrcpyClient()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        Scrcpy client = CreateUninitializedScrcpy();
        var session = new FakeSession(Serial, client);
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);

        await viewModel.InitializeAsync(Serial, "Device");

        Assert.AreEqual(ViewDeviceSessionState.Running, viewModel.State);
        Assert.AreEqual(1, factory.CreateCount);
        Assert.AreEqual(1, session.StartCount);
        Assert.AreSame(client, viewModel.ScrcpyClient);
    }

    [TestMethod]
    public async Task RepeatedOnlineEvents_DoNotCreateDuplicateSession()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        await viewModel.InitializeAsync(Serial, "Device");

        tracker.SetDevice(new AdbDevice(Serial, AdbDeviceStatus.Online));
        await Task.Delay(TimeSpan.FromMilliseconds(650));

        Assert.AreEqual(ViewDeviceSessionState.Running, viewModel.State);
        Assert.AreEqual(1, factory.CreateCount);
        Assert.AreEqual(1, session.StartCount);
        Assert.AreEqual(0, session.StopCount);
    }

    [TestMethod]
    public async Task SessionExit_RestartsOnlyThatSession()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var first = new FakeSession(Serial);
        var second = new FakeSession(Serial);
        var factory = new FakeSessionFactory(first, second);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        await viewModel.InitializeAsync(Serial, "Device");

        first.Exit();
        await WaitUntilAsync(
            () => factory.CreateCount == 2 && viewModel.State == ViewDeviceSessionState.Running,
            TimeSpan.FromSeconds(3));

        Assert.AreEqual(1, first.StopCount);
        Assert.AreEqual(1, second.StartCount);
        Assert.AreEqual(2, factory.CreateCount);
    }

    [TestMethod]
    public async Task TrackerReconnecting_HealthyRunningSession_RemainsAliveAndIsNotRecreated()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        await viewModel.InitializeAsync(Serial, "Device");

        tracker.SetHealth(AdbDeviceTrackerHealth.Reconnecting);
        await Task.Delay(TimeSpan.FromMilliseconds(150));

        Assert.AreEqual(ViewDeviceSessionState.Running, viewModel.State);
        Assert.AreEqual(1, factory.CreateCount);
        Assert.AreEqual(1, session.StartCount);
        Assert.AreEqual(0, session.StopCount);
        Assert.AreEqual(0, session.DisposeCount);
    }

    [TestMethod]
    public async Task RunningDevice_BecomesOffline_StopsSessionClearsClientAndWaitsForDevice()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial, CreateUninitializedScrcpy());
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        await viewModel.InitializeAsync(Serial, "Device");

        tracker.SetDevice(new AdbDevice(Serial, AdbDeviceStatus.Offline));
        await WaitUntilAsync(
            () => viewModel.State == ViewDeviceSessionState.WaitingForDevice,
            TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, session.StopCount);
        Assert.AreEqual(1, session.DisposeCount);
        Assert.IsNull(viewModel.ScrcpyClient);
        Assert.AreEqual(1, factory.CreateCount);
    }

    [TestMethod]
    public async Task RunningDevice_BecomesUnauthorized_StopsSessionAndEntersUnauthorized()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        await viewModel.InitializeAsync(Serial, "Device");

        tracker.SetDevice(new AdbDevice(Serial, AdbDeviceStatus.Unauthorized));
        await WaitUntilAsync(
            () => viewModel.State == ViewDeviceSessionState.Unauthorized,
            TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, session.StopCount);
        Assert.AreEqual(1, session.DisposeCount);
        Assert.AreEqual(1, factory.CreateCount);
    }

    [TestMethod]
    public async Task InitializeAsync_GetStateReturnsUnauthorized_DoesNotCreateSession()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var factory = new FakeSessionFactory(new FakeSession(Serial));
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbAsync(Serial, "get-state", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "error: device unauthorized"));
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory, adb);

        await viewModel.InitializeAsync(Serial, "Device");

        Assert.AreEqual(ViewDeviceSessionState.Unauthorized, viewModel.State);
        Assert.AreEqual(0, factory.CreateCount);
    }

    [TestMethod]
    public async Task ReconnectCommand_RunningSession_CreatesOneReplacementSession()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var first = new FakeSession(Serial);
        var second = new FakeSession(Serial);
        var factory = new FakeSessionFactory(first, second);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        await viewModel.InitializeAsync(Serial, "Device");

        await viewModel.ReconnectCommand.ExecuteAsync(null);

        Assert.AreEqual(ViewDeviceSessionState.Running, viewModel.State);
        Assert.AreEqual(1, first.StopCount);
        Assert.AreEqual(1, first.DisposeCount);
        Assert.AreEqual(1, second.StartCount);
        Assert.AreEqual(2, factory.CreateCount);
    }

    [TestMethod]
    public async Task ReconnectCommand_WhileSessionIsStopping_PreservesClientUntilStopCompletes()
    {
        TaskCompletionSource stopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Scrcpy client = CreateUninitializedScrcpy();
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var first = new FakeSession(Serial, client, stopGate: stopGate);
        var second = new FakeSession(Serial);
        var factory = new FakeSessionFactory(first, second);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        await viewModel.InitializeAsync(Serial, "Device");

        Task reconnect = viewModel.ReconnectCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => first.StopCount == 1, TimeSpan.FromSeconds(2));
        try
        {
            Assert.AreSame(client, viewModel.ScrcpyClient);
        }
        finally
        {
            stopGate.TrySetResult();
            await reconnect;
        }
    }

    [TestMethod]
    public async Task ReconnectCommand_ConcurrentAndStaleTriggers_DoNotCreateDuplicateSession()
    {
        TaskCompletionSource startGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var first = new FakeSession(Serial);
        var second = new FakeSession(Serial, startGate: startGate);
        var unusedThird = new FakeSession(Serial);
        var factory = new FakeSessionFactory(first, second, unusedThird);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        await viewModel.InitializeAsync(Serial, "Device");

        tracker.SetDevice(new AdbDevice(Serial, AdbDeviceStatus.Online));
        Task reconnect = viewModel.ReconnectCommand.ExecuteAsync(null);
        await WaitUntilAsync(
            () => factory.CreateCount == 2 && second.StartCount == 1,
            TimeSpan.FromSeconds(2));
        tracker.SetHealth(AdbDeviceTrackerHealth.Reconnecting);
        Task concurrentReconnect = viewModel.ReconnectCommand.ExecuteAsync(null);
        startGate.TrySetResult();

        await Task.WhenAll(reconnect, concurrentReconnect);
        await Task.Delay(TimeSpan.FromMilliseconds(650));

        Assert.AreEqual(ViewDeviceSessionState.Running, viewModel.State);
        Assert.AreEqual(1, first.StopCount);
        Assert.AreEqual(1, first.DisposeCount);
        Assert.AreEqual(1, second.StartCount);
        Assert.AreEqual(2, factory.CreateCount);
    }

    [TestMethod]
    public async Task NavigationCommands_SendExactlyOneLogicalActionEach()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        await viewModel.InitializeAsync(Serial, "Device");

        await viewModel.BackCommand.ExecuteAsync(null);
        await viewModel.HomeCommand.ExecuteAsync(null);
        await viewModel.RecentCommand.ExecuteAsync(null);
        await viewModel.PowerCommand.ExecuteAsync(null);
        await viewModel.VolumeUpCommand.ExecuteAsync(null);
        await viewModel.VolumeDownCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(
            new[] { "back", "key:3", "key:187", "key:26", "key:24", "key:25" },
            session.Actions);
    }

    [TestMethod]
    public async Task SessionContentSizeChange_UpdatesDeviceAspectRatio()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        await viewModel.InitializeAsync(Serial, "Device");

        session.RaiseContentSize(1080, 2220);

        await WaitUntilAsync(
            () => viewModel.ContentWidth == 1080 && viewModel.ContentHeight == 2220,
            TimeSpan.FromSeconds(1));

        Assert.AreEqual(1080d / 2220d, viewModel.DeviceAspectRatio, 0.000001d);
    }

    [TestMethod]
    public async Task LanguageChanged_RefreshesStatusText()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Offline));
        var factory = new FakeSessionFactory(new FakeSession(Serial));
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        int waitingStatusVersion = 0;
        localization.GetString(Arg.Any<string>()).Returns(call =>
        {
            string key = call.Arg<string>();
            return key == "ViewDevice_StatusWaitingForDevice"
                ? $"waiting-{++waitingStatusVersion}"
                : key;
        });
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            localization: localization);
        await viewModel.InitializeAsync(Serial, "Device");

        string before = viewModel.StatusText;
        localization.LanguageChanged += Raise.Event<EventHandler>(localization, EventArgs.Empty);

        Assert.AreNotEqual(before, viewModel.StatusText);
        Assert.AreEqual("waiting-2", viewModel.StatusText);
    }

    [TestMethod]
    public async Task DisposeAsync_RunningSession_StopsOnceAndDoesNotCreateReplacement()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial, CreateUninitializedScrcpy());
        var factory = new FakeSessionFactory(session);
        ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        try
        {
            await viewModel.InitializeAsync(Serial, "Device");

            await viewModel.DisposeAsync();
            tracker.SetDevice(new AdbDevice(Serial, AdbDeviceStatus.Online));
            await Task.Delay(TimeSpan.FromMilliseconds(650));

            Assert.AreEqual(ViewDeviceSessionState.Closed, viewModel.State);
            Assert.IsNull(viewModel.ScrcpyClient);
            Assert.AreEqual(1, factory.CreateCount);
            Assert.AreEqual(1, session.StopCount);
            Assert.AreEqual(1, session.DisposeCount);
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    private static ViewDeviceViewModel CreateViewModel(
        IAdbDeviceTrackerService tracker,
        ISingleViewDeviceSessionFactory factory,
        IAdbCommandService? adb = null,
        ILocalizationService? localization = null)
    {
        adb ??= Substitute.For<IAdbCommandService>();
        adb.RunAdbAsync(Serial, "get-state", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "device", string.Empty));

        localization ??= Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());

        return new ViewDeviceViewModel(
            factory,
            tracker,
            adb,
            Substitute.For<IFilePickerDialogService>(),
            Substitute.For<IViewDeviceScreenshotService>(),
            localization,
            new ImmediateDispatcher(),
            new TestLogger<ViewDeviceViewModel>());
    }

    private static Scrcpy CreateUninitializedScrcpy()
    {
        return (Scrcpy)RuntimeHelpers.GetUninitializedObject(typeof(Scrcpy));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (predicate())
                return;
            await Task.Delay(25);
        }

        Assert.Fail("Timed out waiting for the View Device state transition.");
    }

    private sealed class ImmediateDispatcher : IUiDispatcherService
    {
        public bool CheckAccess() => true;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTracker : IAdbDeviceTrackerService
    {
        private AdbDevice? _device;

        public FakeTracker(AdbDevice? device)
        {
            _device = device;
        }

        public event EventHandler<AdbDeviceStateChangedEventArgs>? DeviceStateChanged;
        public event EventHandler<AdbDeviceTrackerHealthChangedEventArgs>? HealthChanged;

        public AdbDeviceTrackerHealth Health { get; private set; } = AdbDeviceTrackerHealth.Connected;
        public IReadOnlyList<AdbDevice> CurrentSnapshot => _device is null ? [] : [_device];

        public AdbDevice? GetDevice(string serial) =>
            string.Equals(_device?.Serial, serial, StringComparison.OrdinalIgnoreCase) ? _device : null;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void SetDevice(AdbDevice? device)
        {
            AdbDevice? previous = _device;
            _device = device;
            string serial = device?.Serial ?? previous?.Serial ?? Serial;
            DeviceStateChanged?.Invoke(this, new AdbDeviceStateChangedEventArgs(serial, previous, device));
        }

        public void SetHealth(AdbDeviceTrackerHealth health)
        {
            AdbDeviceTrackerHealth previous = Health;
            Health = health;
            HealthChanged?.Invoke(this, new AdbDeviceTrackerHealthChangedEventArgs(previous, health));
        }
    }

    private sealed class FakeSessionFactory(params FakeSession[] sessions) : ISingleViewDeviceSessionFactory
    {
        private readonly Queue<FakeSession> _sessions = new(sessions);

        public int CreateCount { get; private set; }

        public ISingleViewDeviceSession Create(ViewDeviceLaunchOptions options)
        {
            CreateCount++;
            if (_sessions.Count == 0)
                throw new InvalidOperationException("No fake session was configured.");

            FakeSession session = _sessions.Dequeue();
            Assert.AreEqual(session.Serial, options.Serial);
            return session;
        }
    }

    private sealed class FakeSession : ISingleViewDeviceSession
    {
        private readonly TaskCompletionSource? _startGate;
        private readonly TaskCompletionSource? _stopGate;
        private int _contentWidth = 720;
        private int _contentHeight = 1280;

        public FakeSession(
            string serial,
            Scrcpy? client = null,
            TaskCompletionSource? startGate = null,
            TaskCompletionSource? stopGate = null)
        {
            Serial = serial;
            Client = client;
            _startGate = startGate;
            _stopGate = stopGate;
        }

        public string Serial { get; }
        public SingleViewDeviceSessionState State { get; private set; } = SingleViewDeviceSessionState.Created;
        public Scrcpy? Client { get; }
        public int ContentWidth => _contentWidth;
        public int ContentHeight => _contentHeight;
        public IReadOnlyList<string> RecentDiagnostics => [];
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }
        public List<string> Actions { get; } = [];

        public event EventHandler<SingleViewDeviceSessionStateChangedEventArgs>? StateChanged;
        public event EventHandler<SingleViewDeviceContentSizeChangedEventArgs>? ContentSizeChanged;
        public event EventHandler? Exited;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            if (_startGate is not null)
                await _startGate.Task.WaitAsync(cancellationToken);
            SetState(SingleViewDeviceSessionState.Running);
            ContentSizeChanged?.Invoke(
                this,
                new SingleViewDeviceContentSizeChangedEventArgs(_contentWidth, _contentHeight));
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCount++;
            if (_stopGate is not null)
                await _stopGate.Task.WaitAsync(cancellationToken);
            SetState(SingleViewDeviceSessionState.Closed);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        public Task SendKeyEventAsync(int keyCode, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add($"key:{keyCode}");
            return Task.CompletedTask;
        }

        public Task SendBackOrScreenOnAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add("back");
            return Task.CompletedTask;
        }

        public void Exit()
        {
            SetState(SingleViewDeviceSessionState.Failed);
            Exited?.Invoke(this, EventArgs.Empty);
        }

        public void RaiseContentSize(int width, int height)
        {
            _contentWidth = width;
            _contentHeight = height;
            ContentSizeChanged?.Invoke(
                this,
                new SingleViewDeviceContentSizeChangedEventArgs(width, height));
        }

        private void SetState(SingleViewDeviceSessionState state)
        {
            SingleViewDeviceSessionState previous = State;
            State = state;
            StateChanged?.Invoke(
                this,
                new SingleViewDeviceSessionStateChangedEventArgs(previous, state));
        }
    }
}
