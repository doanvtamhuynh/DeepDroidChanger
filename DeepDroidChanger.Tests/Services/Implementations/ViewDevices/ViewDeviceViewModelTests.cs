using System.Diagnostics;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Fakes;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using DeepDroidChanger.ViewModels;
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
    public async Task InitializeAsync_OnlineDevice_StartsOneSession()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);

        await viewModel.InitializeAsync(Serial, "Device");

        Assert.AreEqual(ViewDeviceSessionState.Running, viewModel.State);
        Assert.AreEqual(1, factory.CreateCount);
        Assert.AreEqual(1, session.StartCount);
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
    public async Task RunningDevice_BecomesOffline_StopsSessionAndWaitsForDevice()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        await viewModel.InitializeAsync(Serial, "Device");

        tracker.SetDevice(new AdbDevice(Serial, AdbDeviceStatus.Offline));
        await WaitUntilAsync(
            () => viewModel.State == ViewDeviceSessionState.WaitingForDevice,
            TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, session.StopCount);
        Assert.AreEqual(1, session.DisposeCount);
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
    public async Task ReconnectCommand_WhileSessionIsStopping_KeepsNativeHandleUntilStopCompletes()
    {
        TaskCompletionSource stopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var first = new FakeSession(Serial, stopGate: stopGate);
        var second = new FakeSession(Serial);
        var factory = new FakeSessionFactory(first, second);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        await viewModel.InitializeAsync(Serial, "Device");
        IntPtr runningHandle = viewModel.NativeWindowHandle;

        Task reconnect = viewModel.ReconnectCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => first.StopCount == 1, TimeSpan.FromSeconds(2));
        try
        {
            Assert.AreNotEqual(IntPtr.Zero, runningHandle);
            Assert.AreEqual(runningHandle, viewModel.NativeWindowHandle);
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
        var second = new FakeSession(Serial, startGate);
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
    public async Task DisposeAsync_RunningSession_StopsOnceAndDoesNotCreateReplacement()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        var factory = new FakeSessionFactory(session);
        ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        try
        {
            await viewModel.InitializeAsync(Serial, "Device");

            await viewModel.DisposeAsync();
            tracker.SetDevice(new AdbDevice(Serial, AdbDeviceStatus.Online));
            await Task.Delay(TimeSpan.FromMilliseconds(650));

            Assert.AreEqual(ViewDeviceSessionState.Closed, viewModel.State);
            Assert.AreEqual(IntPtr.Zero, viewModel.NativeWindowHandle);
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
        IViewDeviceSessionFactory factory,
        IAdbCommandService? adb = null)
    {
        if (adb is null)
        {
            adb = Substitute.For<IAdbCommandService>();
            adb.RunAdbAsync(Serial, "get-state", Arg.Any<CancellationToken>())
                .Returns(new CommandResult(0, "device", string.Empty));
        }
        ILocalizationService localization = Substitute.For<ILocalizationService>();
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

    private sealed class FakeSessionFactory(params FakeSession[] sessions) : IViewDeviceSessionFactory
    {
        private readonly Queue<FakeSession> _sessions = new(sessions);

        public int CreateCount { get; private set; }

        public IViewDeviceSession Create(ViewDeviceLaunchOptions options)
        {
            CreateCount++;
            if (_sessions.Count == 0)
                throw new InvalidOperationException("No fake session was configured.");

            FakeSession session = _sessions.Dequeue();
            Assert.AreEqual(session.Serial, options.Serial);
            return session;
        }
    }

    private sealed class FakeSession(
        string serial,
        TaskCompletionSource? startGate = null,
        TaskCompletionSource? stopGate = null) : IViewDeviceSession
    {
        public string Serial { get; } = serial;
        public ViewDeviceSessionState State { get; private set; } = ViewDeviceSessionState.Created;
        public IntPtr NativeWindowHandle { get; private set; }
        public int ContentWidth => 720;
        public int ContentHeight => 1280;
        public IReadOnlyList<string> RecentDiagnostics => [];
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }

        public event EventHandler<ViewDeviceSessionStateChangedEventArgs>? StateChanged;
        public event EventHandler? NativeWindowReady;
        public event EventHandler<ViewDeviceContentSizeChangedEventArgs>? ContentSizeChanged;
        public event EventHandler? Exited;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            if (startGate is not null)
                await startGate.Task.WaitAsync(cancellationToken);
            SetState(ViewDeviceSessionState.Running);
            NativeWindowHandle = new IntPtr(1234 + StartCount);
            NativeWindowReady?.Invoke(this, EventArgs.Empty);
            ContentSizeChanged?.Invoke(this, new ViewDeviceContentSizeChangedEventArgs(ContentWidth, ContentHeight));
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCount++;
            if (stopGate is not null)
                await stopGate.Task.WaitAsync(cancellationToken);
            NativeWindowHandle = IntPtr.Zero;
            SetState(ViewDeviceSessionState.Closed);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        public void Exit()
        {
            NativeWindowHandle = IntPtr.Zero;
            SetState(ViewDeviceSessionState.Failed);
            Exited?.Invoke(this, EventArgs.Empty);
        }

        private void SetState(ViewDeviceSessionState state)
        {
            ViewDeviceSessionState previous = State;
            State = state;
            StateChanged?.Invoke(this, new ViewDeviceSessionStateChangedEventArgs(previous, state));
        }
    }
}
