using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Fakes;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using DeepDroidChanger.ViewDevices.Runtime;
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
    public async Task Busy_UsesDedicatedState()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession session = new(Serial)
        {
            StartException = new ScrcpyNetDeviceBusyException(Serial)
        };
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            restartDelays: new[] { TimeSpan.Zero });

        await viewModel.InitializeAsync(Serial, "Device");
        Assert.AreEqual(ViewDeviceSessionState.Busy, viewModel.State);
        Assert.IsFalse(viewModel.IsRunning);
        Assert.IsNull(viewModel.ScrcpyClient);
        Assert.IsTrue(viewModel.RetryCommand.CanExecute(null));
        Assert.IsFalse(viewModel.ReconnectCommand.CanExecute(null));
        Assert.AreEqual("ViewDevice_StatusBusy", viewModel.StatusText);
        Assert.AreEqual(1, factory.CreateCount);
        Assert.AreEqual(1, session.StartCount);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task Busy_IsNotRunning()
    {
        await using ViewDeviceViewModel viewModel = await CreateBusyViewModelAsync();

        Assert.IsFalse(viewModel.IsRunning);
    }

    [TestMethod]
    public async Task Busy_HasNoScrcpyClient()
    {
        await using ViewDeviceViewModel viewModel = await CreateBusyViewModelAsync();

        Assert.IsNull(viewModel.ScrcpyClient);
    }

    [TestMethod]
    public async Task Busy_DoesNotAutoRetry()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession session = new(Serial)
        {
            StartException = new ScrcpyNetDeviceBusyException(Serial)
        };
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            restartDelays: new[] { TimeSpan.Zero });

        await viewModel.InitializeAsync(Serial, "Device");

        Assert.AreEqual(ViewDeviceSessionState.Busy, viewModel.State);
        Assert.AreEqual(1, factory.CreateCount);
        Assert.AreEqual(1, session.StartCount);
    }

    [TestMethod]
    public async Task Busy_RetryEnabled()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession session = new(Serial)
        {
            StartException = new ScrcpyNetDeviceBusyException(Serial)
        };
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);

        await viewModel.InitializeAsync(Serial, "Device");

        Assert.IsTrue(viewModel.RetryCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task Busy_RetryWhileLeaseOwned_RemainsBusy()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession first = new(Serial)
        {
            StartException = new ScrcpyNetDeviceBusyException(Serial)
        };
        FakeSession second = new(Serial)
        {
            StartException = new ScrcpyNetDeviceBusyException(Serial)
        };
        var factory = new FakeSessionFactory(first, second);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.RetryCommand.ExecuteAsync(null);

        Assert.AreEqual(ViewDeviceSessionState.Busy, viewModel.State);
        Assert.IsFalse(viewModel.IsRunning);
        Assert.IsNull(viewModel.ScrcpyClient);
        Assert.AreEqual(2, factory.CreateCount);
    }

    [TestMethod]
    public async Task Busy_RetryAfterLeaseRelease_CanRun()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession first = new(Serial)
        {
            StartException = new ScrcpyNetDeviceBusyException(Serial)
        };
        FakeSession second = new(Serial);
        var factory = new FakeSessionFactory(first, second);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.RetryCommand.ExecuteAsync(null);

        Assert.AreEqual(ViewDeviceSessionState.Running, viewModel.State);
        Assert.IsTrue(viewModel.IsRunning);
        Assert.AreSame(second.Client, viewModel.ScrcpyClient);
        Assert.AreEqual(2, factory.CreateCount);
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
    public async Task TrackerReconnecting_FailedSession_IsDisposed()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial, CreateUninitializedScrcpy());
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        await viewModel.InitializeAsync(Serial, "Device");

        session.FailWithoutExit();
        tracker.SetHealth(AdbDeviceTrackerHealth.Reconnecting);
        Task evaluation = viewModel.PendingEvaluationForTesting;
        await evaluation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(ViewDeviceSessionState.AdbUnavailable, viewModel.State);
        Assert.AreEqual(1, session.StopCount);
        Assert.AreEqual(1, session.DisposeCount);
        Assert.IsNull(viewModel.ScrcpyClient);
        Assert.IsFalse(viewModel.HasCurrentSessionForTesting);
        Assert.IsFalse(viewModel.HasEstablishedSessionForTesting);
        AssertInteractiveCommandsDisabled(viewModel);
    }

    [TestMethod]
    public async Task TrackerUnavailableCleanup_CanceledByNewerEvaluation_DoesNotPublishStaleState()
    {
        TaskCompletionSource stopGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession first = new(Serial, CreateUninitializedScrcpy(), stopGate: stopGate);
        FakeSession second = new(Serial, CreateUninitializedScrcpy());
        var factory = new FakeSessionFactory(first, second);
        TestLogger<ViewDeviceViewModel> logger = new();
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            logger: logger);
        await viewModel.InitializeAsync(Serial, "Device");

        List<ViewDeviceSessionState> publishedStates = [];
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewDeviceViewModel.State))
                publishedStates.Add(viewModel.State);
        };
        first.FailWithoutExit();

        tracker.SetHealth(AdbDeviceTrackerHealth.Reconnecting);
        Task staleEvaluation = viewModel.PendingEvaluationForTesting;
        await first.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        tracker.SetHealth(AdbDeviceTrackerHealth.Connected);
        Task currentEvaluation = viewModel.PendingEvaluationForTesting;
        stopGate.TrySetResult();

        await Task.WhenAll(staleEvaluation, currentEvaluation)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsTrue(viewModel.IsRunning);
        Assert.IsFalse(viewModel.IsUnavailable);
        Assert.AreSame(second.Client, viewModel.ScrcpyClient);
        Assert.AreEqual(1, first.DisposeCount);
        Assert.AreEqual(1, second.StartCount);
        Assert.IsFalse(publishedStates.Contains(ViewDeviceSessionState.AdbUnavailable));
        Assert.IsFalse(logger.Messages.Any(message =>
            message.Contains(
                "cleanup while the ADB tracker was unavailable",
                StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task SessionExit_ConcurrentTrackerReconnecting_CleansOnceAndDoesNotRestartWhileAdbUnavailable()
    {
        TaskCompletionSource stopGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession first = new(Serial, CreateUninitializedScrcpy(), stopGate: stopGate);
        FakeSession second = new(Serial, CreateUninitializedScrcpy());
        var factory = new FakeSessionFactory(first, second);
        ControlledDispatcher dispatcher = new();
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            dispatcher: dispatcher,
            restartDelays: new[] { TimeSpan.Zero });
        await viewModel.InitializeAsync(Serial, "Device");

        dispatcher.DeferNext();
        int deferredDetachInvocation = dispatcher.InvocationCount + 1;
        first.Exit();
        await dispatcher.WaitForInvocationAsync(deferredDetachInvocation)
            .WaitAsync(TimeSpan.FromSeconds(2));

        tracker.SetHealth(AdbDeviceTrackerHealth.Reconnecting);
        Task trackerEvaluation = viewModel.PendingEvaluationForTesting;
        await first.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        int oldStateInvocation = dispatcher.InvocationCount + 1;
        Task oldStatePublication = dispatcher.WaitForInvocationAsync(oldStateInvocation);

        try
        {
            stopGate.TrySetResult();
            await trackerEvaluation.WaitAsync(TimeSpan.FromSeconds(2));
            await dispatcher.ReleaseNextAsync();
            await oldStatePublication.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            stopGate.TrySetResult();
        }

        Assert.AreEqual(ViewDeviceSessionState.AdbUnavailable, viewModel.State);
        Assert.IsFalse(viewModel.IsRunning);
        Assert.IsTrue(viewModel.IsUnavailable);
        Assert.AreEqual(1, first.StopCount);
        Assert.AreEqual(1, first.DisposeCount);
        Assert.IsNull(viewModel.ScrcpyClient);
        Assert.IsFalse(viewModel.HasCurrentSessionForTesting);
        Assert.IsFalse(viewModel.HasEstablishedSessionForTesting);
        Assert.AreEqual(1, factory.CreateCount);
        Assert.AreEqual(0, viewModel.RestartAttemptForTesting);
        AssertInteractiveCommandsDisabled(viewModel);
    }

    [TestMethod]
    public async Task TrackerReconnecting_FailedSessionStopFailure_StillDisposesAndBecomesUnavailable()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial, CreateUninitializedScrcpy())
        {
            StopException = new IOException("stop failed")
        };
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            new FakeSessionFactory(session));
        await viewModel.InitializeAsync(Serial, "Device");

        session.FailWithoutExit();
        tracker.SetHealth(AdbDeviceTrackerHealth.Reconnecting);
        await viewModel.PendingEvaluationForTesting.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(ViewDeviceSessionState.AdbUnavailable, viewModel.State);
        Assert.AreEqual(1, session.StopCount);
        Assert.AreEqual(1, session.DisposeCount);
        Assert.IsNull(viewModel.ScrcpyClient);
        Assert.IsFalse(viewModel.HasCurrentSessionForTesting);
        Assert.IsFalse(viewModel.HasEstablishedSessionForTesting);
        AssertInteractiveCommandsDisabled(viewModel);
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
    public async Task InitializeAsync_AdbGetStateException_RetriesOnceAndStartsSession()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        var factory = new FakeSessionFactory(session);
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        int adbCalls = 0;
        adb.RunAdbAsync(Serial, "get-state", Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref adbCalls) == 1
                ? Task.FromException<CommandResult>(new InvalidOperationException("adb unavailable"))
                : Task.FromResult(new CommandResult(0, "device", string.Empty)));
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            adb,
            restartDelays: new[] { TimeSpan.Zero });

        await viewModel.InitializeAsync(Serial, "Device");
        await WaitUntilAsync(
            () => factory.CreateCount == 1 && viewModel.State == ViewDeviceSessionState.Running,
            TimeSpan.FromSeconds(2));

        Assert.AreEqual(2, adbCalls);
        Assert.AreEqual(1, factory.CreateCount);
        Assert.AreEqual(1, session.StartCount);
    }

    [TestMethod]
    public async Task OldExitRecovery_DoesNotCancelNewerOfflineEvaluation()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession first = new(Serial);
        FakeSession failedRetry = new(Serial)
        {
            StartException = new TimeoutException("retry startup failed")
        };
        FakeSession replacement = new(Serial);
        var factory = new FakeSessionFactory(first, failedRetry, replacement);
        ControlledDispatcher dispatcher = new();
        TaskCompletionSource waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource replacementRunning =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            dispatcher: dispatcher,
            restartDelays: new[] { TimeSpan.Zero, TimeSpan.FromHours(1) });
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewDeviceViewModel.State))
            {
                if (viewModel.State == ViewDeviceSessionState.WaitingForDevice)
                    waiting.TrySetResult();
                if (viewModel.State == ViewDeviceSessionState.Running &&
                    factory.CreateCount == 3)
                {
                    replacementRunning.TrySetResult();
                }
            }
        };

        await viewModel.InitializeAsync(Serial, "Device");

        // Pause the old exit handler after it clears the established marker.
        dispatcher.DeferNext();
        int deferredInvocation = dispatcher.InvocationCount + 1;
        first.Exit();
        await dispatcher.WaitForInvocationAsync(deferredInvocation)
            .WaitAsync(TimeSpan.FromSeconds(1));

        // The offline event advances the generation while the old exit
        // handler is paused. Keep the later online evaluation from resetting
        // the retry budget so stale retry consumption remains observable.
        tracker.SetDeviceSilently(new AdbDevice(Serial, AdbDeviceStatus.Offline));
        tracker.SetDevice(new AdbDevice(Serial, AdbDeviceStatus.Offline));
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(ViewDeviceSessionState.WaitingForDevice, viewModel.State);
        Assert.AreEqual(0, viewModel.RestartAttemptForTesting);

        await dispatcher.ReleaseNextAsync();
        Assert.AreEqual(0, viewModel.RestartAttemptForTesting);

        tracker.SetDeviceSilently(new AdbDevice(Serial, AdbDeviceStatus.Online));
        tracker.SetHealth(AdbDeviceTrackerHealth.Reconnecting);
        tracker.SetHealth(AdbDeviceTrackerHealth.Connected);
        await replacementRunning.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(ViewDeviceSessionState.Running, viewModel.State);
        Assert.AreEqual(3, factory.CreateCount);
        Assert.AreEqual(1, failedRetry.StartCount);
        Assert.AreEqual(1, replacement.StartCount);
    }

    [TestMethod]
    public async Task DuplicateFailureSignals_ConsumeOneRetryAttempt()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            new FakeSessionFactory(session),
            restartDelays: new[] { TimeSpan.FromHours(1) });

        await viewModel.InitializeAsync(Serial, "Device");
        int failureGeneration = viewModel.CurrentGenerationForTesting;

        viewModel.ScheduleRestartAfterFailure(failureGeneration);
        viewModel.ScheduleRestartAfterFailure(failureGeneration);

        Assert.AreEqual(1, viewModel.RestartAttemptForTesting);
    }

    [TestMethod]
    public async Task StaleRecovery_DoesNotIncrementRestartAttempt()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            new FakeSessionFactory(new FakeSession(Serial)),
            restartDelays: new[] { TimeSpan.FromHours(1) });

        await viewModel.InitializeAsync(Serial, "Device");
        int staleGeneration = viewModel.CurrentGenerationForTesting;
        tracker.SetHealth(AdbDeviceTrackerHealth.Reconnecting);

        int currentAttempt = viewModel.RestartAttemptForTesting;
        Assert.AreNotEqual(staleGeneration, viewModel.CurrentGenerationForTesting);
        viewModel.ScheduleRestartAfterFailure(staleGeneration);

        Assert.AreEqual(currentAttempt, viewModel.RestartAttemptForTesting);
    }

    [TestMethod]
    public async Task StaleRecoveryExhaustion_DoesNotOverwriteNewRunningState()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            new FakeSessionFactory(new FakeSession(Serial)),
            restartDelays: Array.Empty<TimeSpan>());

        await viewModel.InitializeAsync(Serial, "Device");
        int staleGeneration = viewModel.CurrentGenerationForTesting;
        tracker.SetHealth(AdbDeviceTrackerHealth.Reconnecting);

        Assert.AreEqual(ViewDeviceSessionState.Running, viewModel.State);
        viewModel.ScheduleRestartAfterFailure(staleGeneration);

        Assert.AreEqual(ViewDeviceSessionState.Running, viewModel.State);
    }

    [TestMethod]
    public async Task CurrentFailure_StillSchedulesOneBackoff()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            new FakeSessionFactory(new FakeSession(Serial)),
            restartDelays: new[] { TimeSpan.FromHours(1) });

        await viewModel.InitializeAsync(Serial, "Device");
        int failureGeneration = viewModel.CurrentGenerationForTesting;
        viewModel.ScheduleRestartAfterFailure(failureGeneration);

        Assert.AreEqual(1, viewModel.RestartAttemptForTesting);
        Assert.AreEqual(failureGeneration + 1, viewModel.CurrentGenerationForTesting);
    }

    [TestMethod]
    public async Task OnlineTrackerReset_StillResetsRetryBudget()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            new FakeSessionFactory(new FakeSession(Serial)),
            restartDelays: new[] { TimeSpan.FromHours(1) });

        await viewModel.InitializeAsync(Serial, "Device");
        viewModel.ScheduleRestartAfterFailure(viewModel.CurrentGenerationForTesting);
        Assert.AreEqual(1, viewModel.RestartAttemptForTesting);

        tracker.SetDevice(new AdbDevice(Serial, AdbDeviceStatus.Online));

        Assert.AreEqual(0, viewModel.RestartAttemptForTesting);
    }

    [TestMethod]
    public async Task Reconnect_DisposesOldBeforePublishingReplacement()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var first = new FakeSession(Serial);
        var second = new FakeSession(Serial);
        var factory = new FakeSessionFactory(first, second);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        List<string> timeline = [];
        first.Timeline = timeline;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewDeviceViewModel.IsRunning) &&
                viewModel.IsRunning &&
                ReferenceEquals(viewModel.ScrcpyClient, second.Client))
            {
                timeline.Add("replacement-running");
            }
        };
        await viewModel.InitializeAsync(Serial, "Device");
        timeline.Clear();
        first.Actions.Clear();

        await viewModel.ReconnectCommand.ExecuteAsync(null);

        Assert.AreEqual(ViewDeviceSessionState.Running, viewModel.State);
        Assert.AreEqual(1, first.StopCount);
        Assert.AreEqual(1, first.DisposeCount);
        Assert.AreEqual(1, second.StartCount);
        Assert.AreEqual(2, factory.CreateCount);
        Assert.IsTrue(timeline.IndexOf("dispose") >= 0);
        Assert.IsTrue(
            timeline.IndexOf("dispose") < timeline.IndexOf("replacement-running"));
    }

    [TestMethod]
    public async Task Reconnect_TrackerGenerationChangesWhileStatePublicationBlocked_RemainsConsistent()
    {
        TaskCompletionSource stopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession first = new(Serial, CreateUninitializedScrcpy(), stopGate: stopGate);
        FakeSession second = new(Serial, CreateUninitializedScrcpy());
        var factory = new FakeSessionFactory(first, second);
        ControlledDispatcher dispatcher = new();
        List<string> timeline = [];
        first.Timeline = timeline;
        second.OnStarted = () => timeline.Add("replacement-started");
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            dispatcher: dispatcher);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewDeviceViewModel.IsRunning) &&
                viewModel.IsRunning &&
                ReferenceEquals(viewModel.ScrcpyClient, second.Client))
            {
                timeline.Add("replacement-running");
            }
        };
        await viewModel.InitializeAsync(Serial, "Device");
        timeline.Clear();
        first.Actions.Clear();

        int reconnectStateInvocation = dispatcher.InvocationCount + 2;
        dispatcher.DeferInvocation(reconnectStateInvocation);
        Task reconnect = viewModel.ReconnectCommand.ExecuteAsync(null);
        await dispatcher.WaitForInvocationAsync(reconnectStateInvocation)
            .WaitAsync(TimeSpan.FromSeconds(2));

        int reconnectGeneration = viewModel.CurrentGenerationForTesting;
        tracker.SetHealth(AdbDeviceTrackerHealth.Reconnecting);
        Task trackerEvaluation = viewModel.PendingEvaluationForTesting;
        Assert.AreNotEqual(reconnectGeneration, viewModel.CurrentGenerationForTesting);

        await dispatcher.ReleaseNextAsync();
        await first.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            Assert.AreEqual(ViewDeviceSessionState.Reconnecting, viewModel.State);
            Assert.IsFalse(viewModel.IsRunning);
            Assert.IsNull(viewModel.ScrcpyClient);
            AssertInteractiveCommandsDisabled(viewModel);
        }
        finally
        {
            stopGate.TrySetResult();
        }

        await reconnect.WaitAsync(TimeSpan.FromSeconds(2));
        await trackerEvaluation.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(ViewDeviceSessionState.Running, viewModel.State);
        Assert.IsTrue(viewModel.IsRunning);
        Assert.AreSame(second.Client, viewModel.ScrcpyClient);
        Assert.AreEqual(1, first.StopCount);
        Assert.AreEqual(1, first.DisposeCount);
        Assert.AreEqual(1, second.StartCount);
        Assert.AreEqual(2, factory.CreateCount);
        Assert.IsTrue(timeline.IndexOf("dispose") >= 0);
        Assert.IsTrue(timeline.IndexOf("replacement-running") >= 0);
        Assert.IsTrue(
            timeline.IndexOf("dispose") < timeline.IndexOf("replacement-running"));
    }

    [TestMethod]
    public async Task Reconnect_EntersReconnectingBeforeOldStop()
    {
        ReconnectStopSnapshot snapshot = await RunReconnectStopGateScenarioAsync();

        Assert.AreEqual(ViewDeviceSessionState.Reconnecting, snapshot.State);
        Assert.AreEqual(0, snapshot.DisposeCount);
    }

    [TestMethod]
    public async Task Reconnect_ClearsScrcpyClientBeforeOldStopCompletes()
    {
        ReconnectStopSnapshot snapshot = await RunReconnectStopGateScenarioAsync();

        Assert.IsTrue(snapshot.ClientIsNull);
        Assert.IsFalse(snapshot.IsRunning);
    }

    [TestMethod]
    public async Task Reconnect_DisablesInteractiveCommandsDuringStop()
    {
        ReconnectStopSnapshot snapshot = await RunReconnectStopGateScenarioAsync();

        Assert.IsFalse(snapshot.BackCanExecute);
        Assert.IsFalse(snapshot.HomeCanExecute);
        Assert.IsFalse(snapshot.RecentCanExecute);
        Assert.IsFalse(snapshot.PowerCanExecute);
        Assert.IsFalse(snapshot.ScreenOnCanExecute);
        Assert.IsFalse(snapshot.ScreenOffCanExecute);
        Assert.IsFalse(snapshot.ScreenshotCanExecute);
        Assert.IsFalse(snapshot.PasteCanExecute);
        Assert.IsFalse(snapshot.PanelCanExecute);
    }

    [TestMethod]
    public async Task Reconnect_FirstFrameRequiredBeforeRunning()
    {
        TaskCompletionSource startGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession first = new(Serial);
        FakeSession second = new(Serial, startGate: startGate);
        var factory = new FakeSessionFactory(first, second);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        await viewModel.InitializeAsync(Serial, "Device");

        Task reconnect = viewModel.ReconnectCommand.ExecuteAsync(null);
        await second.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(ViewDeviceSessionState.Reconnecting, viewModel.State);
        Assert.IsFalse(viewModel.IsRunning);
        Assert.IsNull(viewModel.ScrcpyClient);

        startGate.TrySetResult();
        await reconnect;

        Assert.AreEqual(ViewDeviceSessionState.Running, viewModel.State);
        Assert.AreSame(second.Client, viewModel.ScrcpyClient);
    }

    [TestMethod]
    public async Task Reconnect_DoesNotStartDuplicateReplacement()
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
    public async Task BackendSessionFailed_BeforeUiReconnecting_CommandsAreNotInteractive()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            new FakeSessionFactory(session));

        await viewModel.InitializeAsync(Serial, "Device");
        List<string?> notifications = [];
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            notifications.Add(eventArgs.PropertyName);
        };
        session.FailWithoutExit();

        Assert.AreEqual(ViewDeviceSessionState.Running, viewModel.State);
        Assert.IsFalse(viewModel.IsRunning);
        Assert.IsTrue(viewModel.IsUnavailable);
        Assert.IsFalse(viewModel.BackCommand.CanExecute(null));
        Assert.IsFalse(viewModel.PowerCommand.CanExecute(null));
        Assert.IsFalse(viewModel.ScreenshotCommand.CanExecute(null));
        Assert.IsTrue(notifications.Contains(nameof(ViewDeviceViewModel.IsRunning)));
        Assert.IsTrue(notifications.Contains(nameof(ViewDeviceViewModel.IsUnavailable)));
    }

    [TestMethod]
    public async Task EstablishedSessionCleared_CanInteractIsFalse()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        ControlledDispatcher dispatcher = new();
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            new FakeSessionFactory(session),
            dispatcher: dispatcher,
            restartDelays: new[] { TimeSpan.FromHours(1) });

        await viewModel.InitializeAsync(Serial, "Device");
        List<string?> notifications = [];
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            notifications.Add(eventArgs.PropertyName);
        };
        dispatcher.DeferNext();
        int deferredInvocation = dispatcher.InvocationCount + 1;
        session.Exit();
        await dispatcher.WaitForInvocationAsync(deferredInvocation)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsFalse(viewModel.IsRunning);
        Assert.IsTrue(viewModel.IsUnavailable);
        Assert.IsTrue(notifications.Contains(nameof(ViewDeviceViewModel.IsRunning)));
        Assert.IsTrue(notifications.Contains(nameof(ViewDeviceViewModel.IsUnavailable)));
        Assert.IsFalse(viewModel.BackCommand.CanExecute(null));
        Assert.IsFalse(viewModel.ScreenshotCommand.CanExecute(null));

        await dispatcher.ReleaseNextAsync();
    }

    [TestMethod]
    public void CurrentSessionRunningButNotEstablished_CanInteractIsFalse()
    {
        FakeSession session = new(Serial);
        session.SetBackendState(SingleViewDeviceSessionState.Running);

        Assert.IsFalse(ViewDeviceViewModel.IsInteractiveSessionSnapshot(
            ViewDeviceSessionState.Running,
            session,
            establishedSession: null,
            disposed: false));
    }

    [TestMethod]
    public void EstablishedSessionMismatch_CanInteractIsFalse()
    {
        FakeSession current = new(Serial);
        FakeSession established = new(Serial);
        current.SetBackendState(SingleViewDeviceSessionState.Running);
        established.SetBackendState(SingleViewDeviceSessionState.Running);

        Assert.IsFalse(ViewDeviceViewModel.IsInteractiveSessionSnapshot(
            ViewDeviceSessionState.Running,
            current,
            established,
            disposed: false));
    }

    [TestMethod]
    public void CurrentEstablishedRunningSession_CanInteractIsTrue()
    {
        FakeSession session = new(Serial);
        session.SetBackendState(SingleViewDeviceSessionState.Running);

        Assert.IsTrue(ViewDeviceViewModel.IsInteractiveSessionSnapshot(
            ViewDeviceSessionState.Running,
            session,
            session,
            disposed: false));
    }

    [TestMethod]
    public async Task ClipboardReadCompletesAfterReplacement_DoesNotSendToReplacement()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession first = new(Serial);
        FakeSession second = new(Serial);
        var factory = new FakeSessionFactory(first, second);
        FakeClipboardService clipboard = new() { Text = "replacement-safe" };
        TaskCompletionSource<string?> readGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        clipboard.GetGate = readGate;
        TaskCompletionSource secondRunning =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            clipboard: clipboard,
            restartDelays: new[] { TimeSpan.Zero });
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewDeviceViewModel.State) &&
                viewModel.State == ViewDeviceSessionState.Running &&
                factory.CreateCount == 2)
            {
                secondRunning.TrySetResult();
            }
        };

        await viewModel.InitializeAsync(Serial, "Device");
        Task paste = viewModel.PasteHostClipboardCommand.ExecuteAsync(null);
        await clipboard.GetStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        first.Exit();
        await secondRunning.Task.WaitAsync(TimeSpan.FromSeconds(2));
        readGate.TrySetResult("replacement-safe");
        await paste;

        Assert.IsFalse(first.Actions.Contains("paste:replacement-safe"));
        Assert.IsFalse(second.Actions.Contains("paste:replacement-safe"));
    }

    [TestMethod]
    public async Task OldSessionCommandSnapshot_DoesNotExecuteAgainstNewSession()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        TaskCompletionSource oldCommandGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeSession first = new(Serial) { SendBackGate = oldCommandGate };
        FakeSession second = new(Serial);
        var factory = new FakeSessionFactory(first, second);
        TaskCompletionSource secondRunning =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            restartDelays: new[] { TimeSpan.Zero });
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewDeviceViewModel.State) &&
                viewModel.State == ViewDeviceSessionState.Running &&
                factory.CreateCount == 2)
            {
                secondRunning.TrySetResult();
            }
        };

        await viewModel.InitializeAsync(Serial, "Device");
        Task back = viewModel.BackCommand.ExecuteAsync(null);
        await first.SendBackStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        first.Exit();
        await secondRunning.Task.WaitAsync(TimeSpan.FromSeconds(2));
        oldCommandGate.TrySetResult();
        await back;

        Assert.IsTrue(first.Actions.Contains("back"));
        Assert.IsFalse(second.Actions.Contains("back"));
    }

    [TestMethod]
    public async Task Screenshot_IsDisabledWhenBackendSessionAlreadyFailed()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            new FakeSessionFactory(session));

        await viewModel.InitializeAsync(Serial, "Device");
        session.FailWithoutExit();

        Assert.IsFalse(viewModel.ScreenshotCommand.CanExecute(null));
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
    public async Task PowerCommand_UsesAtomicCooldown_AndDoesNotThrottleOtherCommands()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        var factory = new FakeSessionFactory(session);
        TaskCompletionSource firstCooldown = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int delayCalls = 0;
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            powerCooldownDelay: (_, cancellationToken) =>
            {
                if (Interlocked.Increment(ref delayCalls) == 1)
                    return firstCooldown.Task.WaitAsync(cancellationToken);

                return Task.CompletedTask;
            });

        await viewModel.InitializeAsync(Serial, "Device");

        Task firstPower = viewModel.PowerCommand.ExecuteAsync(null);
        await WaitUntilAsync(
            () => session.Actions.Count(action => action == "key:26") == 1,
            TimeSpan.FromSeconds(1));

        Assert.IsFalse(viewModel.PowerCommand.CanExecute(null));
        Task rejectedPower = viewModel.PowerCommand.ExecuteAsync(null);
        await rejectedPower;
        await viewModel.HomeCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "key:26", "key:3" }, session.Actions);
        Assert.AreEqual(1, delayCalls);

        firstCooldown.SetResult();
        await firstPower;

        Assert.IsTrue(viewModel.PowerCommand.CanExecute(null));
        await viewModel.PowerCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "key:26", "key:3", "key:26" }, session.Actions);
        Assert.AreEqual(2, delayCalls);
    }

    [TestMethod]
    public async Task ClipboardAndPanelCommands_UseSingleViewContract_AndDeviceClipboardSyncSkipsSameText()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        var factory = new FakeSessionFactory(session);
        var clipboard = new FakeClipboardService { Text = "hello" };
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            clipboard: clipboard);

        await viewModel.InitializeAsync(Serial, "Device");

        await viewModel.PasteHostClipboardCommand.ExecuteAsync(null);
        await viewModel.PasteHostClipboardWithPasteKeyCommand.ExecuteAsync(null);
        await viewModel.InjectHostClipboardCommand.ExecuteAsync(null);
        await viewModel.CopyClipboardCommand.ExecuteAsync(null);
        await viewModel.CutClipboardCommand.ExecuteAsync(null);
        await viewModel.ExpandNotificationPanelCommand.ExecuteAsync(null);
        await viewModel.ExpandSettingsPanelCommand.ExecuteAsync(null);
        await viewModel.CollapsePanelsCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(
            new[]
            {
                "paste:hello",
                "paste-native:hello",
                "inject:hello",
                "clipboard:Copy",
                "clipboard:Cut",
                "panel:notification",
                "panel:settings",
                "panel:collapse"
            },
            session.Actions);

        clipboard.Text = "same";
        session.RaiseClipboard("same");
        await Task.Delay(50);
        Assert.AreEqual(0, clipboard.SetValues.Count);

        clipboard.Text = "host";
        session.RaiseClipboard("device");
        await WaitUntilAsync(
            () => clipboard.SetValues.Count == 1,
            TimeSpan.FromSeconds(1));
        Assert.AreEqual("device", clipboard.SetValues[0]);
    }

    [TestMethod]
    public async Task ClipboardCommands_SkipWhenHostClipboardIsUnavailable()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        var factory = new FakeSessionFactory(session);
        var clipboard = new FakeClipboardService();
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            clipboard: clipboard);

        await viewModel.InitializeAsync(Serial, "Device");

        await viewModel.PasteHostClipboardCommand.ExecuteAsync(null);
        await viewModel.PasteHostClipboardWithPasteKeyCommand.ExecuteAsync(null);
        await viewModel.InjectHostClipboardCommand.ExecuteAsync(null);

        Assert.IsEmpty(session.Actions);
    }

    [TestMethod]
    public async Task CtrlV_ClipboardBusy_DoesNotSendEmptyClipboard()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        var factory = new FakeSessionFactory(session);
        var clipboard = new FakeClipboardService
        {
            GetException = new ExternalException(
                "clipboard busy",
                unchecked((int)0x800401D0))
        };
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            clipboard: clipboard);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.PasteHostClipboardCommand.ExecuteAsync(null);

        Assert.IsEmpty(session.Actions);
    }

    [TestMethod]
    public async Task ScreenOnOffAndPowerUseScrcpyControls_WhilePowerRemainsKeyToggle()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        var factory = new FakeSessionFactory(session);
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbAsync(Serial, "get-state", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "device", string.Empty));
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory, adb);
        await viewModel.InitializeAsync(Serial, "Device");

        await viewModel.ScreenOnCommand.ExecuteAsync(null);
        await viewModel.ScreenOffCommand.ExecuteAsync(null);
        await viewModel.PowerCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(
            new[] { "screen:normal", "screen:off", "key:26" },
            session.Actions);
        await adb.DidNotReceive().GetSettingAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await adb.DidNotReceive().PutSettingAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task StartupFailureThatSignalsClientExit_ConsumesOneRetryAttempt()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession[] failures = Enumerable
            .Range(0, 6)
            .Select(index => new FakeSession(Serial)
            {
                StartException = new TimeoutException($"first frame {index}"),
                RaiseExitedOnStart = true
            })
            .ToArray();
        var factory = new FakeSessionFactory(failures);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            restartDelays: new[]
            {
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero
            });

        await viewModel.InitializeAsync(Serial, "Device");
        await WaitUntilAsync(
            () => factory.CreateCount == 6 && viewModel.State == ViewDeviceSessionState.Failed,
            TimeSpan.FromSeconds(2));

        CollectionAssert.AreEqual(
            new[] { 250L, 500L, 1000L, 2000L, 5000L },
            ViewDeviceViewModel.DefaultRestartDelays.Select(delay => (long)delay.TotalMilliseconds).ToArray());
        Assert.AreEqual(6, factory.CreateCount);
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
    public async Task StartingSession_ContentSizeEvent_DoesNotPrematurelyResizeUi()
    {
        TaskCompletionSource startGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession session = new(Serial, startGate: startGate)
        {
            ContentSizeDuringStart = (333, 444)
        };
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            new FakeSessionFactory(session));

        Task initialize = viewModel.InitializeAsync(Serial, "Device");
        await session.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(ViewDeviceSessionState.Starting, viewModel.State);
        Assert.AreEqual(0, viewModel.ContentWidth);
        Assert.AreEqual(0, viewModel.ContentHeight);

        startGate.TrySetResult();
        await initialize;

        Assert.AreEqual(333, viewModel.ContentWidth);
        Assert.AreEqual(444, viewModel.ContentHeight);
    }

    [TestMethod]
    public async Task RunningEstablishedSession_ContentSizeStillUpdatesAspect()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        TaskCompletionSource sizeChanged =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            new FakeSessionFactory(session));
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewDeviceViewModel.ContentHeight) &&
                viewModel.ContentWidth == 1080 &&
                viewModel.ContentHeight == 2220)
            {
                sizeChanged.TrySetResult();
            }
        };

        await viewModel.InitializeAsync(Serial, "Device");
        session.RaiseContentSize(1080, 2220);

        await sizeChanged.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(1080d / 2220d, viewModel.DeviceAspectRatio, 0.000001d);
    }

    [TestMethod]
    public async Task EstablishedCleared_LateContentSizeIsIgnored()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        ControlledDispatcher dispatcher = new();
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            new FakeSessionFactory(session),
            dispatcher: dispatcher,
            restartDelays: new[] { TimeSpan.FromHours(1) });

        await viewModel.InitializeAsync(Serial, "Device");
        dispatcher.DeferNext();
        int deferredInvocation = dispatcher.InvocationCount + 1;
        session.RaiseContentSize(333, 444);
        await dispatcher.WaitForInvocationAsync(deferredInvocation)
            .WaitAsync(TimeSpan.FromSeconds(1));

        TaskCompletionSource reconnecting =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewDeviceViewModel.State) &&
                viewModel.State == ViewDeviceSessionState.Reconnecting)
            {
                reconnecting.TrySetResult();
            }
        };
        session.Exit();
        await reconnecting.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await dispatcher.ReleaseNextAsync();

        Assert.AreEqual(720, viewModel.ContentWidth);
        Assert.AreEqual(1280, viewModel.ContentHeight);
    }

    [TestMethod]
    public async Task FailedSession_LateContentSizeIsIgnored()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            new FakeSessionFactory(session),
            restartDelays: new[] { TimeSpan.FromHours(1) });

        await viewModel.InitializeAsync(Serial, "Device");
        session.FailWithoutExit();
        session.RaiseContentSize(333, 444);

        Assert.AreEqual(720, viewModel.ContentWidth);
        Assert.AreEqual(1280, viewModel.ContentHeight);
    }

    [TestMethod]
    public async Task ReplacementSession_DimensionsStillWin()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession first = new(Serial);
        FakeSession second = new(Serial);
        second.SetContentSize(1080, 2220);
        var factory = new FakeSessionFactory(first, second);
        TaskCompletionSource secondRunning =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            restartDelays: new[] { TimeSpan.Zero });
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewDeviceViewModel.State) &&
                viewModel.State == ViewDeviceSessionState.Running &&
                factory.CreateCount == 2)
            {
                secondRunning.TrySetResult();
            }
        };

        await viewModel.InitializeAsync(Serial, "Device");
        first.Exit();
        await secondRunning.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1080, viewModel.ContentWidth);
        Assert.AreEqual(2220, viewModel.ContentHeight);
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
    public async Task Busy_LocalizationUpdates()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession session = new(Serial)
        {
            StartException = new ScrcpyNetDeviceBusyException(Serial)
        };
        var factory = new FakeSessionFactory(session);
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        int busyVersion = 0;
        localization.GetString(Arg.Any<string>()).Returns(call =>
        {
            string key = call.Arg<string>();
            return key == "ViewDevice_StatusBusy"
                ? $"busy-{++busyVersion}"
                : key;
        });
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            localization: localization);

        await viewModel.InitializeAsync(Serial, "Device");
        string before = viewModel.StatusText;
        localization.LanguageChanged += Raise.Event<EventHandler>(localization, EventArgs.Empty);

        Assert.AreEqual(ViewDeviceSessionState.Busy, viewModel.State);
        Assert.AreNotEqual(before, viewModel.StatusText);
        Assert.AreEqual("busy-2", viewModel.StatusText);
    }

    [TestMethod]
    public async Task EvaluationCleanupStopException_IsObservedAndEntersControlledFailedState()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var first = new FakeSession(Serial)
        {
            StopException = new InvalidOperationException("stop failed")
        };
        var factory = new FakeSessionFactory(first);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            restartDelays: new[] { TimeSpan.FromHours(1) });

        await viewModel.InitializeAsync(Serial, "Device");
        tracker.SetDevice(new AdbDevice(Serial, AdbDeviceStatus.Offline));

        await WaitUntilAsync(
            () => viewModel.State == ViewDeviceSessionState.Failed,
            TimeSpan.FromSeconds(2));
        await Task.Delay(100);

        Assert.AreEqual(ViewDeviceSessionState.Failed, viewModel.State);
        Assert.AreEqual(1, first.StopCount);
        Assert.AreEqual(1, first.DisposeCount);
        Assert.AreEqual(1, factory.CreateCount);
    }

    [TestMethod]
    public async Task ReconnectCleanupDisposeException_ReportsFailedWithoutConcurrentReplacement()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var first = new FakeSession(Serial)
        {
            DisposeException = new InvalidOperationException("dispose failed")
        };
        var second = new FakeSession(Serial);
        var factory = new FakeSessionFactory(first, second);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            restartDelays: new[] { TimeSpan.FromHours(1) });

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.ReconnectCommand.ExecuteAsync(null);

        Assert.AreEqual(ViewDeviceSessionState.Failed, viewModel.State);
        Assert.AreEqual(1, first.StopCount);
        Assert.AreEqual(1, first.DisposeCount);
        Assert.AreEqual(1, factory.CreateCount);
        Assert.IsNull(viewModel.ScrcpyClient);
    }

    [TestMethod]
    public async Task StartupThatLeavesSessionFailed_IsNotPublishedAsRunning()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        var session = new FakeSession(Serial)
        {
            LeaveFailedAfterStart = true
        };
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            restartDelays: new[] { TimeSpan.FromHours(1) });

        await viewModel.InitializeAsync(Serial, "Device");
        await WaitUntilAsync(
            () => viewModel.State == ViewDeviceSessionState.Failed,
            TimeSpan.FromSeconds(2));

        Assert.AreEqual(ViewDeviceSessionState.Failed, viewModel.State);
        Assert.IsNull(viewModel.ScrcpyClient);
        Assert.AreEqual(1, factory.CreateCount);
        Assert.AreEqual(1, session.StopCount);
        Assert.AreEqual(1, session.DisposeCount);
    }

    [TestMethod]
    public async Task ExitDuringRunningPublication_DoesNotLeaveDeadRunningClient()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession first = new(Serial, CreateUninitializedScrcpy());
        var factory = new FakeSessionFactory(first);
        ControlledDispatcher dispatcher = new();
        first.OnStarted = dispatcher.DeferNext;
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            dispatcher: dispatcher,
            restartDelays: new[] { TimeSpan.FromHours(1) });
        int runningPublications = 0;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewDeviceViewModel.IsRunning) &&
                viewModel.IsRunning)
            {
                runningPublications++;
            }
        };

        Task pendingPublication = dispatcher.WaitForPendingInvocationAsync();
        Task initialize = viewModel.InitializeAsync(Serial, "Device");
        await pendingPublication.WaitAsync(TimeSpan.FromSeconds(1));
        first.Exit();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => dispatcher.ReleaseNextAsync());
        await initialize;

        Assert.AreEqual(0, runningPublications);
        Assert.AreEqual(ViewDeviceSessionState.Failed, viewModel.State);
        Assert.IsNull(viewModel.ScrcpyClient);
    }

    [TestMethod]
    public async Task Reconnect_OldExitIgnored()
    {
        (Scrcpy? client, _) = await RunOldExitAfterReplacementScenarioAsync();

        Assert.IsNotNull(client);
    }

    [TestMethod]
    public async Task Reconnect_OldStateCallbackIgnored()
    {
        (_, ViewDeviceSessionState state) = await RunOldExitAfterReplacementScenarioAsync();

        Assert.AreEqual(ViewDeviceSessionState.Running, state);
    }

    [TestMethod]
    public async Task ExitDuringPublication_IsEitherRejectedOrRecoveredExactlyOnce()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession first = new(Serial, CreateUninitializedScrcpy());
        FakeSession second = new(Serial, CreateUninitializedScrcpy());
        var factory = new FakeSessionFactory(first, second);
        ControlledDispatcher dispatcher = new();
        first.OnStarted = dispatcher.DeferNext;
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            dispatcher: dispatcher,
            restartDelays: new[] { TimeSpan.Zero });
        TaskCompletionSource secondRunning = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int runningPublications = 0;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewDeviceViewModel.IsRunning) &&
                viewModel.IsRunning)
            {
                runningPublications++;
                if (factory.CreateCount == 2)
                    secondRunning.TrySetResult();
            }
        };

        Task pendingPublication = dispatcher.WaitForPendingInvocationAsync();
        Task initialize = viewModel.InitializeAsync(Serial, "Device");
        await pendingPublication.WaitAsync(TimeSpan.FromSeconds(1));
        first.Exit();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => dispatcher.ReleaseNextAsync());
        await initialize;
        await secondRunning.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, runningPublications);
        Assert.AreSame(second.Client, viewModel.ScrcpyClient);
        Assert.AreEqual(ViewDeviceSessionState.Running, viewModel.State);
    }

    [TestMethod]
    public async Task Reconnect_OldContentSizeIgnored()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession first = new(Serial, CreateUninitializedScrcpy());
        FakeSession second = new(Serial, CreateUninitializedScrcpy());
        second.SetContentSize(1080, 2220);
        var factory = new FakeSessionFactory(first, second);
        ControlledDispatcher dispatcher = new();
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            dispatcher: dispatcher,
            restartDelays: new[] { TimeSpan.Zero });
        TaskCompletionSource secondRunning = new(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewDeviceViewModel.IsRunning) &&
                viewModel.IsRunning &&
                factory.CreateCount == 2)
            {
                secondRunning.TrySetResult();
            }
        };

        await viewModel.InitializeAsync(Serial, "Device");
        int deferredInvocation = dispatcher.InvocationCount + 1;
        dispatcher.DeferNext();
        first.RaiseContentSize(333, 444);
        await dispatcher.WaitForInvocationAsync(deferredInvocation).WaitAsync(TimeSpan.FromSeconds(1));

        first.Exit();
        await secondRunning.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await dispatcher.ReleaseNextAsync();

        Assert.AreEqual(1080, viewModel.ContentWidth);
        Assert.AreEqual(2220, viewModel.ContentHeight);
    }

    [TestMethod]
    public async Task StaleDeviceClipboardAfterReplacement_IsNotWrittenToHost()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession first = new(Serial, CreateUninitializedScrcpy());
        FakeSession second = new(Serial, CreateUninitializedScrcpy());
        var factory = new FakeSessionFactory(first, second);
        FakeClipboardService clipboard = new() { Text = "host" };
        TaskCompletionSource<string?> getGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        clipboard.GetGate = getGate;
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            clipboard: clipboard,
            restartDelays: new[] { TimeSpan.Zero });
        TaskCompletionSource secondRunning = new(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewDeviceViewModel.IsRunning) &&
                viewModel.IsRunning &&
                factory.CreateCount == 2)
            {
                secondRunning.TrySetResult();
            }
        };

        await viewModel.InitializeAsync(Serial, "Device");
        first.RaiseClipboard("old-device");
        await clipboard.GetStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        first.Exit();
        await secondRunning.Task.WaitAsync(TimeSpan.FromSeconds(2));
        getGate.TrySetResult("host");
        await clipboard.GetReturned.Task.WaitAsync(TimeSpan.FromSeconds(1));

        // Dispose waits for the clipboard synchronization gate, giving the
        // stale operation a deterministic completion point without a sleep.
        await viewModel.DisposeAsync();
        CollectionAssert.DoesNotContain(clipboard.SetValues, "old-device");
    }

    [TestMethod]
    public async Task Dispose_IsIdempotent()
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
            await viewModel.DisposeAsync();

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

    [TestMethod]
    public async Task Dispose_UnbindsClientBeforeFlush()
    {
        TaskCompletionSource flushGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession session = new(Serial, CreateUninitializedScrcpy())
        {
            FlushGate = flushGate
        };
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        List<string> timeline = [];
        session.Timeline = timeline;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewDeviceViewModel.ScrcpyClient) &&
                viewModel.ScrcpyClient is null)
            {
                timeline.Add("unbind");
            }
        };

        await viewModel.InitializeAsync(Serial, "Device");
        timeline.Clear();
        session.Actions.Clear();

        Task dispose = viewModel.DisposeAsync().AsTask();
        await session.FlushEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            CollectionAssert.AreEqual(
                new[] { "unbind", "flush" },
                timeline);

            Assert.AreEqual(0, session.StopCount);
            Assert.AreEqual(0, session.DisposeCount);
        }
        finally
        {
            flushGate.TrySetResult();
            await dispose;
        }

        CollectionAssert.AreEqual(
            new[] { "unbind", "flush", "stop", "dispose" },
            timeline);
    }

    [TestMethod]
    public async Task Dispose_FlushOccursBeforeStop()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession session = new(Serial, CreateUninitializedScrcpy())
        {
            Timeline = []
        };
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);

        await viewModel.InitializeAsync(Serial, "Device");
        session.Timeline.Clear();
        await viewModel.DisposeAsync();

        Assert.IsTrue(session.Timeline.IndexOf("flush") >= 0);
        Assert.IsTrue(session.Timeline.IndexOf("flush") < session.Timeline.IndexOf("stop"));
    }

    [TestMethod]
    public async Task Dispose_StopOccursBeforeDispose()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession session = new(Serial, CreateUninitializedScrcpy())
        {
            Timeline = []
        };
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);

        await viewModel.InitializeAsync(Serial, "Device");
        session.Timeline.Clear();
        await viewModel.DisposeAsync();

        Assert.IsTrue(session.Timeline.IndexOf("stop") >= 0);
        Assert.IsTrue(session.Timeline.IndexOf("stop") < session.Timeline.IndexOf("dispose"));
    }

    [TestMethod]
    public async Task Dispose_UnbindVisibleWhileFlushIsBlocked()
    {
        TaskCompletionSource flushGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession session = new(Serial, CreateUninitializedScrcpy())
        {
            FlushGate = flushGate,
            Timeline = []
        };
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewDeviceViewModel.ScrcpyClient) &&
                viewModel.ScrcpyClient is null)
            {
                session.Timeline!.Add("unbind");
            }
        };

        await viewModel.InitializeAsync(Serial, "Device");
        session.Timeline.Clear();
        Task dispose = viewModel.DisposeAsync().AsTask();
        await session.FlushEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            Assert.AreEqual(ViewDeviceSessionState.Closing, viewModel.State);
            Assert.IsNull(viewModel.ScrcpyClient);
            Assert.AreEqual(0, session.StopCount);
            Assert.AreEqual(0, session.DisposeCount);
            CollectionAssert.AreEqual(new[] { "unbind", "flush" }, session.Timeline);
        }
        finally
        {
            flushGate.TrySetResult();
            await dispose;
        }

        Assert.AreEqual(ViewDeviceSessionState.Closed, viewModel.State);
    }

    [TestMethod]
    public async Task Dispose_DoesNotAllowLateSessionPublish()
    {
        TaskCompletionSource startGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession session = new(Serial, CreateUninitializedScrcpy(), startGate: startGate)
        {
            IgnoreStartCancellation = true
        };
        var factory = new FakeSessionFactory(session);
        ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        try
        {
            Task initialize = viewModel.InitializeAsync(Serial, "Device");
            await session.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Task dispose = viewModel.DisposeAsync().AsTask();
            startGate.TrySetResult();
            await Task.WhenAll(initialize, dispose);

            Assert.AreEqual(ViewDeviceSessionState.Closed, viewModel.State);
            Assert.IsFalse(viewModel.IsRunning);
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

    [TestMethod]
    public async Task Dispose_OldDetachCannotClearReplacementClient()
    {
        (Scrcpy? client, _) = await RunOldExitAfterReplacementScenarioAsync();

        Assert.IsNotNull(client);
    }

    [TestMethod]
    public async Task DisposeAsync_FlushFailureStillStopsAndDisposes()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession session = new(Serial, CreateUninitializedScrcpy())
        {
            FlushException = new InvalidOperationException("flush unavailable")
        };
        var factory = new FakeSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);

        await viewModel.InitializeAsync(Serial, "Device");
        session.Actions.Clear();

        await viewModel.DisposeAsync();

        CollectionAssert.AreEqual(
            new[] { "flush", "stop", "dispose" },
            session.Actions);
    }

    private static ViewDeviceViewModel CreateViewModel(
        IAdbDeviceTrackerService tracker,
        ISingleViewDeviceSessionFactory factory,
        IAdbCommandService? adb = null,
        ILocalizationService? localization = null,
        IReadOnlyList<TimeSpan>? restartDelays = null,
        IViewDeviceClipboardService? clipboard = null,
        Func<TimeSpan, CancellationToken, Task>? powerCooldownDelay = null,
        IUiDispatcherService? dispatcher = null,
        TestLogger<ViewDeviceViewModel>? logger = null,
        IDeviceConfigService? config = null,
        IDeviceActionService? actions = null,
        IPackageInstallService? packages = null,
        IPollingService? polling = null)
    {
        if (adb is null)
        {
            adb = Substitute.For<IAdbCommandService>();
            adb.RunAdbAsync(Serial, "get-state", Arg.Any<CancellationToken>())
                .Returns(new CommandResult(0, "device", string.Empty));
        }

        if (localization is null)
        {
            localization = Substitute.For<ILocalizationService>();
            localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        }

        logger ??= new TestLogger<ViewDeviceViewModel>();
        config ??= Substitute.For<IDeviceConfigService>();
        actions ??= Substitute.For<IDeviceActionService>();
        packages ??= Substitute.For<IPackageInstallService>();
        polling ??= Substitute.For<IPollingService>();

        if (restartDelays is null)
        {
            return new ViewDeviceViewModel(
                factory,
                tracker,
                adb,
                Substitute.For<IFilePickerDialogService>(),
                Substitute.For<IViewDeviceScreenshotService>(),
                localization,
                dispatcher ?? new ImmediateDispatcher(),
                clipboard ?? Substitute.For<IViewDeviceClipboardService>(),
                logger,
                config,
                actions,
                packages,
                polling,
                ViewDeviceViewModel.DefaultRestartDelays,
                powerCooldownDelay);
        }

        return new ViewDeviceViewModel(
            factory,
            tracker,
            adb,
            Substitute.For<IFilePickerDialogService>(),
            Substitute.For<IViewDeviceScreenshotService>(),
            localization,
            dispatcher ?? new ImmediateDispatcher(),
            clipboard ?? Substitute.For<IViewDeviceClipboardService>(),
            logger,
            config,
            actions,
            packages,
            polling,
            restartDelays,
            powerCooldownDelay);
    }

    private static async Task<ViewDeviceViewModel> CreateBusyViewModelAsync()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession session = new(Serial)
        {
            StartException = new ScrcpyNetDeviceBusyException(Serial)
        };
        ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            new FakeSessionFactory(session));
        await viewModel.InitializeAsync(Serial, "Device");
        return viewModel;
    }

    private static async Task<(Scrcpy? Client, ViewDeviceSessionState State)>
        RunOldExitAfterReplacementScenarioAsync()
    {
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession first = new(Serial, CreateUninitializedScrcpy());
        FakeSession second = new(Serial, CreateUninitializedScrcpy());
        var factory = new FakeSessionFactory(first, second);
        ControlledDispatcher dispatcher = new();
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory,
            dispatcher: dispatcher,
            restartDelays: new[] { TimeSpan.FromHours(1) });
        TaskCompletionSource secondRunning = new(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ViewDeviceViewModel.IsRunning) &&
                viewModel.IsRunning &&
                factory.CreateCount == 2)
            {
                secondRunning.TrySetResult();
            }
        };

        await viewModel.InitializeAsync(Serial, "Device");
        dispatcher.DeferNext();
        int deferredInvocation = dispatcher.InvocationCount + 1;
        first.Exit();
        await dispatcher.WaitForInvocationAsync(deferredInvocation)
            .WaitAsync(TimeSpan.FromSeconds(1));

        // Request a fresh evaluation while the old exit handler is waiting on
        // its deferred UI callback. This lets the replacement win the shared
        // lifecycle gate deterministically.
        tracker.SetHealth(AdbDeviceTrackerHealth.Reconnecting);
        tracker.SetHealth(AdbDeviceTrackerHealth.Connected);
        await secondRunning.Task.WaitAsync(TimeSpan.FromSeconds(2));

        int invocationBeforeOldState = dispatcher.InvocationCount;
        Task oldStateInvocation = dispatcher.WaitForInvocationAsync(invocationBeforeOldState + 1);
        await dispatcher.ReleaseNextAsync();
        await oldStateInvocation.WaitAsync(TimeSpan.FromSeconds(1));

        return (viewModel.ScrcpyClient, viewModel.State);
    }

    private static async Task<ReconnectStopSnapshot> RunReconnectStopGateScenarioAsync()
    {
        TaskCompletionSource stopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracker = new FakeTracker(new AdbDevice(Serial, AdbDeviceStatus.Online));
        FakeSession first = new(Serial, CreateUninitializedScrcpy(), stopGate: stopGate);
        FakeSession second = new(Serial);
        var factory = new FakeSessionFactory(first, second);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, factory);
        await viewModel.InitializeAsync(Serial, "Device");

        Task reconnect = viewModel.ReconnectCommand.ExecuteAsync(null);
        await first.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            return new ReconnectStopSnapshot(
                viewModel.State,
                viewModel.IsRunning,
                viewModel.ScrcpyClient is null,
                first.DisposeCount,
                viewModel.BackCommand.CanExecute(null),
                viewModel.HomeCommand.CanExecute(null),
                viewModel.RecentCommand.CanExecute(null),
                viewModel.PowerCommand.CanExecute(null),
                viewModel.ScreenOnCommand.CanExecute(null),
                viewModel.ScreenOffCommand.CanExecute(null),
                viewModel.ScreenshotCommand.CanExecute(null),
                viewModel.PasteHostClipboardCommand.CanExecute(null),
                viewModel.ExpandSettingsPanelCommand.CanExecute(null));
        }
        finally
        {
            stopGate.TrySetResult();
            await reconnect;
        }
    }

    private static void AssertInteractiveCommandsDisabled(ViewDeviceViewModel viewModel)
    {
        Assert.IsFalse(viewModel.ReconnectCommand.CanExecute(null));
        Assert.IsFalse(viewModel.BackCommand.CanExecute(null));
        Assert.IsFalse(viewModel.HomeCommand.CanExecute(null));
        Assert.IsFalse(viewModel.RecentCommand.CanExecute(null));
        Assert.IsFalse(viewModel.PowerCommand.CanExecute(null));
        Assert.IsFalse(viewModel.VolumeUpCommand.CanExecute(null));
        Assert.IsFalse(viewModel.VolumeDownCommand.CanExecute(null));
        Assert.IsFalse(viewModel.ScreenOnCommand.CanExecute(null));
        Assert.IsFalse(viewModel.ScreenOffCommand.CanExecute(null));
        Assert.IsFalse(viewModel.ScreenshotCommand.CanExecute(null));
        Assert.IsFalse(viewModel.PasteHostClipboardCommand.CanExecute(null));
        Assert.IsFalse(viewModel.PasteHostClipboardWithPasteKeyCommand.CanExecute(null));
        Assert.IsFalse(viewModel.InjectHostClipboardCommand.CanExecute(null));
        Assert.IsFalse(viewModel.CopyClipboardCommand.CanExecute(null));
        Assert.IsFalse(viewModel.CutClipboardCommand.CanExecute(null));
        Assert.IsFalse(viewModel.ExpandNotificationPanelCommand.CanExecute(null));
        Assert.IsFalse(viewModel.ExpandSettingsPanelCommand.CanExecute(null));
        Assert.IsFalse(viewModel.CollapsePanelsCommand.CanExecute(null));
        Assert.IsFalse(viewModel.MenuCommand.CanExecute(null));
    }

    private sealed record ReconnectStopSnapshot(
        ViewDeviceSessionState State,
        bool IsRunning,
        bool ClientIsNull,
        int DisposeCount,
        bool BackCanExecute,
        bool HomeCanExecute,
        bool RecentCanExecute,
        bool PowerCanExecute,
        bool ScreenOnCanExecute,
        bool ScreenOffCanExecute,
        bool ScreenshotCanExecute,
        bool PasteCanExecute,
        bool PanelCanExecute);

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

    private sealed class ControlledDispatcher : IUiDispatcherService
    {
        private sealed class PendingInvocation(Action action)
        {
            public Action Action { get; } = action;
            public TaskCompletionSource Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly object _gate = new();
        private readonly Queue<PendingInvocation> _pending = new();
        private readonly Dictionary<int, TaskCompletionSource> _invocationWaiters = [];
        private readonly HashSet<int> _deferredInvocations = [];
        private TaskCompletionSource? _pendingWaiter;
        private int _invocationCount;
        private bool _deferNext;

        public bool CheckAccess() => true;

        public int InvocationCount
        {
            get
            {
                lock (_gate)
                    return _invocationCount;
            }
        }

        public void DeferInvocation(int invocation)
        {
            lock (_gate)
                _deferredInvocations.Add(invocation);
        }

        public void DeferNext()
        {
            lock (_gate)
                _deferNext = true;
        }

        public Task WaitForInvocationAsync(int invocation)
        {
            lock (_gate)
            {
                if (_invocationCount >= invocation)
                    return Task.CompletedTask;

                if (!_invocationWaiters.TryGetValue(invocation, out TaskCompletionSource? waiter))
                {
                    waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _invocationWaiters.Add(invocation, waiter);
                }

                return waiter.Task;
            }
        }

        public Task WaitForPendingInvocationAsync()
        {
            lock (_gate)
            {
                if (_pending.Count != 0)
                    return Task.CompletedTask;

                _pendingWaiter ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                return _pendingWaiter.Task;
            }
        }

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PendingInvocation? pending = null;
            lock (_gate)
            {
                int invocation = ++_invocationCount;
                if (_invocationWaiters.Remove(invocation, out TaskCompletionSource? waiter))
                    waiter.TrySetResult();

                if (_deferNext || _deferredInvocations.Remove(invocation))
                {
                    _deferNext = false;
                    pending = new PendingInvocation(action);
                    _pending.Enqueue(pending);
                    _pendingWaiter?.TrySetResult();
                    _pendingWaiter = null;
                }
            }

            if (pending is not null)
                return pending.Completion.Task;

            action();
            return Task.CompletedTask;
        }

        public Task ReleaseNextAsync()
        {
            PendingInvocation pending;
            lock (_gate)
            {
                if (_pending.Count == 0)
                    throw new InvalidOperationException("No deferred dispatcher invocation is pending.");

                pending = _pending.Dequeue();
            }

            try
            {
                pending.Action();
                pending.Completion.TrySetResult();
            }
            catch (Exception exception)
            {
                pending.Completion.TrySetException(exception);
            }

            return pending.Completion.Task;
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

        public void SetDeviceSilently(AdbDevice? device)
        {
            _device = device;
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

    private sealed class FakeClipboardService : IViewDeviceClipboardService
    {
        public string? Text { get; set; }
        public Exception? GetException { get; set; }
        public TaskCompletionSource<string?>? GetGate { get; set; }
        public TaskCompletionSource GetStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource GetReturned { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> SetValues { get; } = [];

        public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetStarted.TrySetResult();
            try
            {
                if (GetException is not null)
                    throw GetException;
                if (GetGate is not null)
                    return await GetGate.Task.WaitAsync(cancellationToken);
                return Text;
            }
            finally
            {
                GetReturned.TrySetResult();
            }
        }

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetValues.Add(text);
            Text = text;
            return Task.CompletedTask;
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
            Client = client ?? CreateUninitializedScrcpy();
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
        public int FlushCount { get; private set; }
        public int DisposeCount { get; private set; }
        public TaskCompletionSource StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StopEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FlushEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? StartException { get; set; }
        public Exception? FlushException { get; set; }
        public Exception? StopException { get; set; }
        public Exception? DisposeException { get; set; }
        public TaskCompletionSource? FlushGate { get; set; }
        public bool IgnoreStartCancellation { get; set; }
        public Action? OnStarted { get; set; }
        public bool LeaveFailedAfterStart { get; set; }
        public bool RaiseExitedOnStart { get; set; }
        public (int Width, int Height)? ContentSizeDuringStart { get; set; }
        public TaskCompletionSource? SendBackGate { get; set; }
        public TaskCompletionSource SendBackStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Actions { get; } = [];
        public List<string>? Timeline { get; set; }

        public event EventHandler<SingleViewDeviceSessionStateChangedEventArgs>? StateChanged;
        public event EventHandler<SingleViewDeviceContentSizeChangedEventArgs>? ContentSizeChanged;
        public event EventHandler<ScrcpyClipboardChangedEventArgs>? ClipboardChanged;
        public event EventHandler? Exited;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            StartEntered.TrySetResult();
            SetState(SingleViewDeviceSessionState.Starting);
            if (StartException is not null)
            {
                if (RaiseExitedOnStart)
                {
                    SetState(SingleViewDeviceSessionState.Failed);
                    Exited?.Invoke(this, EventArgs.Empty);
                }

                throw StartException;
            }
            if (ContentSizeDuringStart is { } size)
                RaiseContentSize(size.Width, size.Height);
            if (_startGate is not null)
            {
                if (IgnoreStartCancellation)
                    await _startGate.Task;
                else
                    await _startGate.Task.WaitAsync(cancellationToken);
            }
            if (LeaveFailedAfterStart)
            {
                SetState(SingleViewDeviceSessionState.Failed);
                return;
            }

            SetState(SingleViewDeviceSessionState.Running);
            ContentSizeChanged?.Invoke(
                this,
                new SingleViewDeviceContentSizeChangedEventArgs(_contentWidth, _contentHeight));
            OnStarted?.Invoke();
            Started.TrySetResult();
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCount++;
            RecordAction("stop");
            StopEntered.TrySetResult();
            if (StopException is not null)
                throw StopException;
            if (_stopGate is not null)
                await _stopGate.Task.WaitAsync(cancellationToken);
            SetState(SingleViewDeviceSessionState.Closed);
        }

        public async Task FlushControlAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FlushCount++;
            RecordAction("flush");
            FlushEntered.TrySetResult();
            if (FlushException is not null)
                throw FlushException;
            if (FlushGate is not null)
                await FlushGate.Task.WaitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            RecordAction("dispose");
            if (DisposeException is not null)
                throw DisposeException;
            return ValueTask.CompletedTask;
        }

        public Task SendKeyEventAsync(int keyCode, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add($"key:{keyCode}");
            return Task.CompletedTask;
        }

        public async Task SendBackOrScreenOnAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SendBackStarted.TrySetResult();
            Actions.Add("back");
            if (SendBackGate is not null)
                await SendBackGate.Task.WaitAsync(cancellationToken);
        }

        public Task SetScreenPowerModeAsync(
            AndroidScreenPowerMode mode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add(mode == AndroidScreenPowerMode.POWER_MODE_OFF ? "screen:off" : "screen:normal");
            return Task.CompletedTask;
        }

        public Task RotateDeviceAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecordAction("rotate");
            return Task.CompletedTask;
        }

        public Task PasteHostClipboardAsync(string text, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add($"paste:{text}");
            return Task.CompletedTask;
        }
        public Task PasteHostClipboardWithPasteKeyAsync(string text, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add($"paste-native:{text}");
            return Task.CompletedTask;
        }
        public Task InjectTextAsync(string text, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add($"inject:{text}");
            return Task.CompletedTask;
        }
        public Task RequestClipboardAsync(ScrcpyCopyKey copyKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add($"clipboard:{copyKey}");
            return Task.CompletedTask;
        }
        public Task ExpandNotificationPanelAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add("panel:notification");
            return Task.CompletedTask;
        }
        public Task ExpandSettingsPanelAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add("panel:settings");
            return Task.CompletedTask;
        }
        public Task CollapsePanelsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add("panel:collapse");
            return Task.CompletedTask;
        }

        public void RaiseClipboard(string text)
        {
            ClipboardChanged?.Invoke(this, new ScrcpyClipboardChangedEventArgs(text));
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

        public void SetContentSize(int width, int height)
        {
            _contentWidth = width;
            _contentHeight = height;
        }

        public void FailWithoutExit()
        {
            SetState(SingleViewDeviceSessionState.Failed);
        }

        public void SetBackendState(SingleViewDeviceSessionState state)
        {
            SetState(state);
        }

        private void SetState(SingleViewDeviceSessionState state)
        {
            SingleViewDeviceSessionState previous = State;
            State = state;
            StateChanged?.Invoke(
                this,
                new SingleViewDeviceSessionStateChangedEventArgs(previous, state));
        }

        private void RecordAction(string action)
        {
            Actions.Add(action);
            Timeline?.Add(action);
        }
    }
}
