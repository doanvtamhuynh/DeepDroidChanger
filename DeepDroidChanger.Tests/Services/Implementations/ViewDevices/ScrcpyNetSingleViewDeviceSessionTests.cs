using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using DeepDroidChanger.Tests.Fakes;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using DeepDroidChanger.ViewDevices.Runtime;
using ScrcpyNet;
using SharpAdbClient;

namespace DeepDroidChanger.Tests.Services.Implementations.ViewDevices;

[TestClass]
public sealed class ScrcpyNetSingleViewDeviceSessionTests
{
    private const string Serial = "SERIAL-123";

    [TestMethod]
    public async Task StartAsync_ResolvesDeviceStartsClientAndPublishesInitialSize()
    {
        Scrcpy scrcpy = CreateUninitializedScrcpy();
        FakeClient client = new(scrcpy)
        {
            Width = 1080,
            Height = 2220,
            Connected = true
        };
        FakeDeviceResolver resolver = new();
        FakeClientFactory factory = new(client);
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(resolver, factory);
        List<SingleViewDeviceSessionState> states = [];
        List<SingleViewDeviceContentSizeChangedEventArgs> sizes = [];
        session.StateChanged += (_, eventArgs) => states.Add(eventArgs.Current);
        session.ContentSizeChanged += (_, eventArgs) => sizes.Add(eventArgs);

        await session.StartAsync(CancellationToken.None);

        Assert.AreEqual(SingleViewDeviceSessionState.Running, session.State);
        Assert.AreSame(scrcpy, session.Client);
        Assert.AreEqual(1, resolver.ResolveCount);
        Assert.AreEqual(1, factory.CreateCount);
        Assert.AreEqual(1, client.StartCount);
        CollectionAssert.AreEqual(
            new[]
            {
                SingleViewDeviceSessionState.Starting,
                SingleViewDeviceSessionState.Running
            },
            states);
        Assert.HasCount(1, sizes);
        Assert.AreEqual(1080, sizes[0].Width);
        Assert.AreEqual(2220, sizes[0].Height);
    }

    [TestMethod]
    public async Task InitialMetadata_720x1280_FirstDecodedFrame_1280x720_ControlsStartupSize()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Width = 720,
            Height = 1280,
            StartFrameWidth = 1280,
            StartFrameHeight = 720,
            Connected = true
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        List<SingleViewDeviceContentSizeChangedEventArgs> sizes = [];
        session.ContentSizeChanged += (_, eventArgs) => sizes.Add(eventArgs);

        await session.StartAsync(CancellationToken.None);

        Assert.AreEqual(SingleViewDeviceSessionState.Running, session.State);
        Assert.AreEqual(1280, session.ContentWidth);
        Assert.AreEqual(720, session.ContentHeight);
        Assert.HasCount(1, sizes);
        Assert.AreEqual(1280, sizes[0].Width);
        Assert.AreEqual(720, sizes[0].Height);
    }

    [TestMethod]
    public async Task InitialMetadata_720x1280_FirstDecodedFrame_720x1280_DoesNotRaiseDuplicateSize()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Width = 720,
            Height = 1280,
            Connected = true
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        List<SingleViewDeviceContentSizeChangedEventArgs> sizes = [];
        session.ContentSizeChanged += (_, eventArgs) => sizes.Add(eventArgs);

        await session.StartAsync(CancellationToken.None);

        Assert.AreEqual(720, session.ContentWidth);
        Assert.AreEqual(1280, session.ContentHeight);
        Assert.HasCount(1, sizes);
        Assert.AreEqual(720, sizes[0].Width);
        Assert.AreEqual(1280, sizes[0].Height);
    }

    [TestMethod]
    public async Task StartStopStart_BeforeSecondFirstFrame_ContentSizeIsZero()
    {
        FakeClient firstClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        FakeClient secondClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 1080,
            Height = 2220,
            RaiseFrameOnStart = false
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(firstClient, secondClient));

        await session.StartAsync(CancellationToken.None);
        await session.StopAsync(CancellationToken.None);

        Task secondStart = session.StartAsync(CancellationToken.None);
        await secondClient.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.AreEqual(SingleViewDeviceSessionState.Starting, session.State);
        Assert.AreEqual(0, session.ContentWidth);
        Assert.AreEqual(0, session.ContentHeight);

        secondClient.RaiseFrame(1080, 2220);
        await secondStart;
    }

    [TestMethod]
    public async Task StartStopStart_SecondFrameSameDimensions_RaisesFreshSizeEvent()
    {
        FakeClient firstClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        FakeClient secondClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(firstClient, secondClient));
        List<SingleViewDeviceContentSizeChangedEventArgs> sizes = [];
        session.ContentSizeChanged += (_, eventArgs) => sizes.Add(eventArgs);

        await session.StartAsync(CancellationToken.None);
        await session.StopAsync(CancellationToken.None);
        await session.StartAsync(CancellationToken.None);

        Assert.HasCount(2, sizes);
        Assert.AreEqual(720, sizes[1].Width);
        Assert.AreEqual(1280, sizes[1].Height);
    }

    [TestMethod]
    public async Task StartStopStart_SecondFrameDifferentDimensions_UsesNewDimensions()
    {
        FakeClient firstClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        FakeClient secondClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 1080,
            Height = 2220
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(firstClient, secondClient));

        await session.StartAsync(CancellationToken.None);
        await session.StopAsync(CancellationToken.None);
        await session.StartAsync(CancellationToken.None);

        Assert.AreEqual(1080, session.ContentWidth);
        Assert.AreEqual(2220, session.ContentHeight);
    }

    [TestMethod]
    public async Task OldLifecycleDimensions_NeverPromoteSecondLifecycle()
    {
        FakeClient firstClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        FakeClient secondClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 1080,
            Height = 2220,
            RaiseFrameOnStart = false
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(firstClient, secondClient),
            TimeSpan.FromSeconds(1));

        await session.StartAsync(CancellationToken.None);
        FakeClient.CapturedCallbacks staleCallbacks = firstClient.CaptureCallbacks();
        await session.StopAsync(CancellationToken.None);

        Task secondStart = session.StartAsync(CancellationToken.None);
        await secondClient.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        staleCallbacks.RaiseFrame(firstClient, 720, 1280);

        Assert.IsFalse(secondStart.IsCompleted);
        Assert.AreEqual(0, session.ContentWidth);
        Assert.AreEqual(0, session.ContentHeight);

        secondClient.RaiseFrame(1080, 2220);
        await secondStart;
        Assert.AreEqual(1080, session.ContentWidth);
        Assert.AreEqual(2220, session.ContentHeight);
    }

    [TestMethod]
    public async Task StartAsync_SocketConnectedWithoutFrame_DoesNotCompleteAsRunning()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Width = 720,
            Height = 1280,
            RaiseFrameOnStart = false
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client),
            TimeSpan.FromSeconds(1));

        Task startTask = session.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => client.StartCount == 1, TimeSpan.FromSeconds(1));

        Assert.IsFalse(startTask.IsCompleted);
        Assert.AreEqual(SingleViewDeviceSessionState.Starting, session.State);

        client.RaiseFrame(720, 1280);
        await startTask;

        Assert.AreEqual(SingleViewDeviceSessionState.Running, session.State);
    }

    [TestMethod]
    public async Task StartAsync_FirstFrameTimeout_CleansClientAndFails()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Width = 720,
            Height = 1280,
            RaiseFrameOnStart = false
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client),
            TimeSpan.FromMilliseconds(20));

        TimeoutException thrown = await Assert.ThrowsExactlyAsync<TimeoutException>(
            () => session.StartAsync(CancellationToken.None));

        StringAssert.Contains(thrown.Message, "first");
        Assert.AreEqual(SingleViewDeviceSessionState.Failed, session.State);
        Assert.IsNull(session.Client);
        Assert.AreEqual(1, client.StopCount);
        Assert.AreEqual(1, client.DisposeCount);
    }

    [TestMethod]
    public async Task Starting_ClientFailsBeforePromotion_NeverBecomesRunning()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Width = 720,
            Height = 1280,
            RaiseFrameOnStart = false
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client),
            TimeSpan.FromSeconds(1));
        int exitedCount = 0;
        session.Exited += (_, _) => exitedCount++;

        Task startTask = session.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => client.StartCount == 1, TimeSpan.FromSeconds(1));
        client.RaiseExited();

        InvalidOperationException thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => startTask);

        StringAssert.Contains(thrown.Message, "first decoded frame");
        Assert.AreEqual(SingleViewDeviceSessionState.Failed, session.State);
        Assert.IsNull(session.Client);
        Assert.AreEqual(1, client.StopCount);
        Assert.AreEqual(1, client.DisposeCount);
        Assert.AreEqual(0, exitedCount);
    }

    [TestMethod]
    public async Task StartAsync_WhenClientFails_CleansUpAndPreservesDiagnostics()
    {
        InvalidOperationException failure = new("scrcpy start failed");
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            StartException = failure
        };
        FakeDeviceResolver resolver = new();
        FakeClientFactory factory = new(client);
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(resolver, factory);

        InvalidOperationException thrown = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => session.StartAsync(CancellationToken.None));

        Assert.AreSame(failure, thrown);
        Assert.AreEqual(SingleViewDeviceSessionState.Failed, session.State);
        Assert.IsNull(session.Client);
        Assert.AreEqual(1, client.StartCount);
        Assert.AreEqual(1, client.StopCount);
        Assert.AreEqual(1, client.DisposeCount);
        StringAssert.Contains(session.RecentDiagnostics.Single(), "scrcpy start failed");
    }

    [TestMethod]
    public async Task StartAsync_WhenCanceled_StopsAndClosesSession()
    {
        TaskCompletionSource startGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            StartGate = startGate
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        using CancellationTokenSource cancellation = new();

        Task startTask = session.StartAsync(cancellation.Token);
        await WaitUntilAsync(() => client.StartCount == 1, TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => startTask);

        Assert.AreEqual(SingleViewDeviceSessionState.Closed, session.State);
        Assert.IsNull(session.Client);
        Assert.AreEqual(1, client.StopCount);
        Assert.AreEqual(1, client.DisposeCount);
    }

    [TestMethod]
    public async Task DisposeAsync_IsIdempotent()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        await session.StartAsync(CancellationToken.None);

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.AreEqual(SingleViewDeviceSessionState.Closed, session.State);
        Assert.AreEqual(1, client.StopCount);
        Assert.AreEqual(1, client.DisposeCount);
    }

    [TestMethod]
    public async Task StopAsync_IsIdempotentAndReportsClosingThenClosed()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        List<SingleViewDeviceSessionState> states = [];
        session.StateChanged += (_, eventArgs) => states.Add(eventArgs.Current);

        await session.StartAsync(CancellationToken.None);
        await session.StopAsync(CancellationToken.None);
        await session.StopAsync(CancellationToken.None);

        Assert.AreEqual(SingleViewDeviceSessionState.Closed, session.State);
        Assert.IsNull(session.Client);
        Assert.AreEqual(1, client.StopCount);
        Assert.AreEqual(1, client.DisposeCount);
        CollectionAssert.Contains(states, SingleViewDeviceSessionState.Closing);
        Assert.AreEqual(
            SingleViewDeviceSessionState.Closed,
            states[^1]);
    }

    [TestMethod]
    public async Task SameSession_StartStopStart_StaleOldExitDoesNotSuppressNewExit()
    {
        FakeClient firstClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        FakeClient secondClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 1080,
            Height = 2220
        };
        ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(firstClient, secondClient));
        int exitedCount = 0;
        session.Exited += (_, _) => exitedCount++;

        await session.StartAsync(CancellationToken.None);
        FakeClient.CapturedCallbacks staleCallbacks = firstClient.CaptureCallbacks();
        await session.StopAsync(CancellationToken.None);
        await session.StartAsync(CancellationToken.None);

        staleCallbacks.RaiseExited(firstClient);

        Assert.AreEqual(SingleViewDeviceSessionState.Running, session.State);
        Assert.AreSame(secondClient.Client, session.Client);
        Assert.AreEqual(0, exitedCount);

        secondClient.RaiseExited();

        Assert.AreEqual(SingleViewDeviceSessionState.Failed, session.State);
        Assert.AreEqual(1, exitedCount);
    }

    [TestMethod]
    public async Task StaleOldExit_AfterRestart_DoesNotConsumeNewExitMarker()
    {
        FakeClient firstClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        FakeClient secondClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 1080,
            Height = 2220
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(firstClient, secondClient));
        FakeClient.CapturedCallbacks staleCallbacks;
        int exitedCount = 0;
        session.Exited += (_, _) => exitedCount++;

        await session.StartAsync(CancellationToken.None);
        staleCallbacks = firstClient.CaptureCallbacks();
        await session.StopAsync(CancellationToken.None);
        await session.StartAsync(CancellationToken.None);

        staleCallbacks.RaiseExited(firstClient);
        Assert.AreEqual(SingleViewDeviceSessionState.Running, session.State);
        Assert.AreEqual(0, exitedCount);

        secondClient.RaiseExited();
        Assert.AreEqual(SingleViewDeviceSessionState.Failed, session.State);
        Assert.AreEqual(1, exitedCount);
    }

    [TestMethod]
    public async Task StaleOldFrame_AfterRestart_DoesNotCompleteNewFirstFrame()
    {
        FakeClient firstClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        FakeClient secondClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 1080,
            Height = 2220,
            RaiseFrameOnStart = false
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(firstClient, secondClient),
            TimeSpan.FromSeconds(1));

        await session.StartAsync(CancellationToken.None);
        FakeClient.CapturedCallbacks staleCallbacks = firstClient.CaptureCallbacks();
        await session.StopAsync(CancellationToken.None);

        Task secondStart = session.StartAsync(CancellationToken.None);
        await secondClient.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        staleCallbacks.RaiseFrame(firstClient, 1440, 2560);

        Assert.IsFalse(secondStart.IsCompleted);
        Assert.AreEqual(SingleViewDeviceSessionState.Starting, session.State);

        secondClient.RaiseFrame(1080, 2220);
        await secondStart;

        Assert.AreEqual(SingleViewDeviceSessionState.Running, session.State);
        Assert.AreEqual(1080, session.ContentWidth);
        Assert.AreEqual(2220, session.ContentHeight);
    }

    [TestMethod]
    public async Task StaleOldClipboard_AfterRestart_DoesNotRaiseCurrentClipboardEvent()
    {
        FakeClient firstClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        FakeClient secondClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 1080,
            Height = 2220
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(firstClient, secondClient));
        List<string> clipboardValues = [];
        session.ClipboardChanged += (_, eventArgs) => clipboardValues.Add(eventArgs.Text);

        await session.StartAsync(CancellationToken.None);
        FakeClient.CapturedCallbacks staleCallbacks = firstClient.CaptureCallbacks();
        await session.StopAsync(CancellationToken.None);
        await session.StartAsync(CancellationToken.None);

        staleCallbacks.RaiseClipboard(firstClient, "old");
        Assert.IsEmpty(clipboardValues);

        secondClient.RaiseClipboard("new");
        Assert.HasCount(1, clipboardValues);
        Assert.AreEqual("new", clipboardValues[0]);
    }

    [TestMethod]
    public async Task StaleOldFailure_AfterRestart_DoesNotAffectCurrentLifecycle()
    {
        FakeClient firstClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        FakeClient secondClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 1080,
            Height = 2220
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(firstClient, secondClient));

        await session.StartAsync(CancellationToken.None);
        FakeClient.CapturedCallbacks staleCallbacks = firstClient.CaptureCallbacks();
        await session.StopAsync(CancellationToken.None);
        await session.StartAsync(CancellationToken.None);

        staleCallbacks.RaiseFailure(firstClient, new InvalidOperationException("old failure"));

        Assert.AreEqual(SingleViewDeviceSessionState.Running, session.State);
        Assert.IsEmpty(session.RecentDiagnostics);

        secondClient.RaiseFailure(new InvalidOperationException("current failure"));

        Assert.HasCount(1, session.RecentDiagnostics);
        StringAssert.Contains(session.RecentDiagnostics[0], "current failure");
        Assert.AreEqual(SingleViewDeviceSessionState.Running, session.State);
    }

    [TestMethod]
    public async Task CurrentNewClientEvents_StillWorkNormally()
    {
        FakeClient firstClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        FakeClient secondClient = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 1080,
            Height = 2220
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(firstClient, secondClient));
        List<string> clipboardValues = [];
        session.ClipboardChanged += (_, eventArgs) => clipboardValues.Add(eventArgs.Text);

        await session.StartAsync(CancellationToken.None);
        await session.StopAsync(CancellationToken.None);
        await session.StartAsync(CancellationToken.None);

        secondClient.RaiseFrame(1440, 2560);
        secondClient.RaiseClipboard("current");
        secondClient.RaiseFailure(new InvalidOperationException("current failure"));

        Assert.AreEqual(1440, session.ContentWidth);
        Assert.AreEqual(2560, session.ContentHeight);
        CollectionAssert.AreEqual(new[] { "current" }, clipboardValues);
        StringAssert.Contains(session.RecentDiagnostics.Single(), "current failure");
        Assert.AreEqual(SingleViewDeviceSessionState.Running, session.State);
    }

    [TestMethod]
    public async Task Starting_PromotionWinsThenExit_RaisesExitedAndBecomesFailed()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int exitedCount = 0;
        session.Exited += (_, _) =>
        {
            exitedCount++;
            Assert.AreEqual(SingleViewDeviceSessionState.Failed, session.State);
            exited.TrySetResult();
        };

        await session.StartAsync(CancellationToken.None);
        client.RaiseExited();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(1));
        client.RaiseExited();

        Assert.AreEqual(SingleViewDeviceSessionState.Failed, session.State);
        Assert.AreEqual(1, exitedCount);
        await session.StopAsync(CancellationToken.None);
    }

    [TestMethod]
    public void FailedSession_CannotTransitionBackToRunning()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true
        };

        Assert.IsFalse(
            ScrcpyNetSingleViewDeviceSession.IsValidRunningPromotion(
                SingleViewDeviceSessionState.Failed,
                client,
                client));
    }

    [TestMethod]
    public void ReplacedClient_CannotPromoteOldClientToRunning()
    {
        FakeClient currentClient = new(CreateUninitializedScrcpy())
        {
            Connected = true
        };
        FakeClient oldClient = new(CreateUninitializedScrcpy())
        {
            Connected = true
        };

        Assert.IsFalse(
            ScrcpyNetSingleViewDeviceSession.IsValidRunningPromotion(
                SingleViewDeviceSessionState.Starting,
                currentClient,
                oldClient));
    }

    [TestMethod]
    public void RunningConnectedClient_IsInteractive()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true
        };

        Assert.IsTrue(
            ScrcpyNetSingleViewDeviceSession.IsInteractiveClientSnapshot(
                SingleViewDeviceSessionState.Running,
                client,
                isDisposed: false,
                intentionalStop: false));
    }

    [TestMethod]
    public void FailedClient_IsNotInteractive()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true
        };

        Assert.IsFalse(
            ScrcpyNetSingleViewDeviceSession.IsInteractiveClientSnapshot(
                SingleViewDeviceSessionState.Failed,
                client,
                isDisposed: false,
                intentionalStop: false));
    }

    [TestMethod]
    public void ClosingClient_IsNotInteractive()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true
        };

        Assert.IsFalse(
            ScrcpyNetSingleViewDeviceSession.IsInteractiveClientSnapshot(
                SingleViewDeviceSessionState.Closing,
                client,
                isDisposed: false,
                intentionalStop: false));
    }

    [TestMethod]
    public void DisconnectedClient_IsNotInteractive()
    {
        FakeClient client = new(CreateUninitializedScrcpy());

        Assert.IsFalse(
            ScrcpyNetSingleViewDeviceSession.IsInteractiveClientSnapshot(
                SingleViewDeviceSessionState.Running,
                client,
                isDisposed: false,
                intentionalStop: false));
    }

    [TestMethod]
    public void IntentionalStop_IsNotInteractive()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true
        };

        Assert.IsFalse(
            ScrcpyNetSingleViewDeviceSession.IsInteractiveClientSnapshot(
                SingleViewDeviceSessionState.Running,
                client,
                isDisposed: false,
                intentionalStop: true));
    }

    [TestMethod]
    public void NoCurrentClient_IsNotInteractive()
    {
        Assert.IsFalse(
            ScrcpyNetSingleViewDeviceSession.IsInteractiveClientSnapshot(
                SingleViewDeviceSessionState.Running,
                null,
                isDisposed: false,
                intentionalStop: false));
    }

    [TestMethod]
    public async Task InputCommands_SendExpectedScrcpyControlMessages()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        await session.StartAsync(CancellationToken.None);

        await session.SendKeyEventAsync(24);
        await session.SendBackOrScreenOnAsync();

        Assert.HasCount(4, client.Commands);
        Assert.HasCount(2, client.CommandBatches);
        Assert.HasCount(2, client.CommandBatches[0]);
        Assert.HasCount(2, client.CommandBatches[1]);
        KeycodeControlMessage keyDown = (KeycodeControlMessage)client.Commands[0];
        KeycodeControlMessage keyUp = (KeycodeControlMessage)client.Commands[1];
        BackOrScreenOnControlMessage backDown = (BackOrScreenOnControlMessage)client.Commands[2];
        BackOrScreenOnControlMessage backUp = (BackOrScreenOnControlMessage)client.Commands[3];
        Assert.AreEqual(AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN, keyDown.Action);
        Assert.AreEqual((AndroidKeycode)24, keyDown.KeyCode);
        Assert.AreEqual(AndroidKeyEventAction.AKEY_EVENT_ACTION_UP, keyUp.Action);
        Assert.AreEqual((AndroidKeycode)24, keyUp.KeyCode);
        Assert.AreEqual(AndroidKeyEventAction.AKEY_EVENT_ACTION_DOWN, backDown.Action);
        Assert.AreEqual(AndroidKeyEventAction.AKEY_EVENT_ACTION_UP, backUp.Action);
    }

    [TestMethod]
    public async Task FlushControlAsync_ForwardsToCurrentClient()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        await session.StartAsync(CancellationToken.None);

        await session.FlushControlAsync(CancellationToken.None);

        Assert.AreEqual(1, client.FlushCount);
    }

    [TestMethod]
    public async Task CtrlV_DefaultPaste_UsesOneNativePasteMessage()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        await session.StartAsync(CancellationToken.None);

        await session.PasteHostClipboardAsync("test");

        Assert.HasCount(1, client.CommandBatches);
        Assert.HasCount(1, client.CommandBatches[0]);
        Assert.IsTrue(client.TrySendControlCommandsAccepted);
        SetClipboardControlMessage message =
            (SetClipboardControlMessage)client.CommandBatches[0][0];
        Assert.AreEqual(0UL, message.Sequence);
        Assert.IsTrue(message.Paste);
        Assert.AreEqual("test", message.Text);
    }

    [TestMethod]
    public async Task CtrlV_DefaultPaste_Accepted_CompletesNormally()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        await session.StartAsync(CancellationToken.None);

        await session.PasteHostClipboardAsync("hello world");

        Assert.IsTrue(client.TrySendControlCommandsAccepted);
        Assert.HasCount(1, client.CommandBatches);
        Assert.HasCount(1, client.Commands);
        Assert.IsInstanceOfType(client.Commands[0], typeof(SetClipboardControlMessage));
        Assert.IsFalse(client.Commands.Any(message => message is KeycodeControlMessage));
    }

    [TestMethod]
    public async Task CtrlV_DefaultPaste_Rejected_ReportsFailure()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280,
            AcceptControlBatches = false
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        await session.StartAsync(CancellationToken.None);

        InvalidOperationException exception =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => session.PasteHostClipboardAsync("test"));

        StringAssert.Contains(exception.Message, "rejected");
        Assert.AreEqual(0, client.Commands.Count);
        Assert.AreEqual(0, client.CommandBatches.Count);
        Assert.IsFalse(client.TrySendControlCommandsAccepted);
    }

    [TestMethod]
    public async Task CtrlV_EmptyClipboard_IsNoOp()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        await session.StartAsync(CancellationToken.None);

        await session.PasteHostClipboardAsync(string.Empty);

        Assert.AreEqual(0, client.Commands.Count);
        Assert.AreEqual(0, client.CommandBatches.Count);
    }

    [TestMethod]
    public Task SendKey_NoInteractiveClient_Faults()
    {
        return AssertUnavailableActionAsync(
            session => session.SendKeyEventAsync(24));
    }

    [TestMethod]
    public Task Back_NoInteractiveClient_Faults()
    {
        return AssertUnavailableActionAsync(
            session => session.SendBackOrScreenOnAsync());
    }

    [TestMethod]
    public Task ScreenPower_NoInteractiveClient_Faults()
    {
        return AssertUnavailableActionAsync(
            session => session.SetScreenPowerModeAsync(
                AndroidScreenPowerMode.POWER_MODE_OFF));
    }

    [TestMethod]
    public Task Paste_NoInteractiveClient_Faults()
    {
        return AssertUnavailableActionAsync(
            session => session.PasteHostClipboardAsync("text"));
    }

    [TestMethod]
    public Task PasteWithPasteKey_NoInteractiveClient_Faults()
    {
        return AssertUnavailableActionAsync(
            session => session.PasteHostClipboardWithPasteKeyAsync("text"));
    }

    [TestMethod]
    public Task InjectText_NoInteractiveClient_Faults()
    {
        return AssertUnavailableActionAsync(
            session => session.InjectTextAsync("text"));
    }

    [TestMethod]
    public Task ClipboardRequest_NoInteractiveClient_Faults()
    {
        return AssertUnavailableActionAsync(
            session => session.RequestClipboardAsync(ScrcpyCopyKey.Copy));
    }

    [TestMethod]
    public Task PanelAction_NoInteractiveClient_Faults()
    {
        return AssertUnavailableActionAsync(
            session => session.ExpandNotificationPanelAsync());
    }

    [TestMethod]
    public async Task ClipboardAndPanelCommands_SendOrderedScrcpyMessagesAndForwardDeviceClipboard()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        TaskCompletionSource<string> clipboardChanged =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        session.ClipboardChanged += (_, eventArgs) => clipboardChanged.TrySetResult(eventArgs.Text);

        await session.StartAsync(CancellationToken.None);
        await session.PasteHostClipboardAsync("hé");
        await session.InjectTextAsync("typed");
        await session.RequestClipboardAsync(ScrcpyCopyKey.Copy);
        await session.ExpandNotificationPanelAsync();
        await session.ExpandSettingsPanelAsync();
        await session.CollapsePanelsAsync();

        Assert.HasCount(6, client.Commands);
        Assert.HasCount(6, client.CommandBatches);
        Assert.HasCount(1, client.CommandBatches[0]);
        for (int i = 0; i < client.CommandBatches.Count; i++)
            Assert.HasCount(1, client.CommandBatches[i]);
        SetClipboardControlMessage setClipboard =
            (SetClipboardControlMessage)client.Commands[0];
        Assert.AreEqual(0UL, setClipboard.Sequence);
        Assert.IsTrue(setClipboard.Paste);
        Assert.AreEqual("hé", setClipboard.Text);

        Assert.AreEqual("typed", ((InjectTextControlMessage)client.Commands[1]).Text);
        Assert.AreEqual(
            ScrcpyCopyKey.Copy,
            ((GetClipboardControlMessage)client.Commands[2]).CopyKey);
        Assert.IsInstanceOfType(
            client.Commands[3],
            typeof(ExpandNotificationPanelControlMessage));
        Assert.IsInstanceOfType(
            client.Commands[4],
            typeof(ExpandSettingsPanelControlMessage));
        Assert.IsInstanceOfType(
            client.Commands[5],
            typeof(CollapsePanelsControlMessage));

        client.RaiseClipboard("from-device");
        Assert.AreEqual(
            "from-device",
            await clipboardChanged.Task.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public async Task CriticalActions_RejectedByQueue_ReturnFailureWithoutPartialBatch()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280,
            AcceptControlBatches = false
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        await session.StartAsync(CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => session.SendKeyEventAsync((int)AndroidKeycode.AKEYCODE_X));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => session.SendBackOrScreenOnAsync());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => session.SetScreenPowerModeAsync(AndroidScreenPowerMode.POWER_MODE_OFF));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => session.RequestClipboardAsync(ScrcpyCopyKey.Copy));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => session.ExpandNotificationPanelAsync());

        Assert.IsEmpty(client.Commands);
        Assert.IsEmpty(client.CommandBatches);
    }

    [TestMethod]
    public async Task PasteHostClipboardWithPasteKeyAsync_SendsNativePasteClipboardMessage()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        await session.StartAsync(CancellationToken.None);

        await session.PasteHostClipboardWithPasteKeyAsync("hé");

        Assert.HasCount(1, client.Commands);
        SetClipboardControlMessage message = (SetClipboardControlMessage)client.Commands[0];
        Assert.AreEqual(0UL, message.Sequence);
        Assert.IsTrue(message.Paste);
        Assert.AreEqual("hé", message.Text);
    }

    [TestMethod]
    public async Task ScreenPowerCommands_SendDistinctScrcpyMessages()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        await session.StartAsync(CancellationToken.None);

        await session.SetScreenPowerModeAsync(AndroidScreenPowerMode.POWER_MODE_NORMAL);
        await session.SetScreenPowerModeAsync(AndroidScreenPowerMode.POWER_MODE_OFF);

        Assert.HasCount(2, client.Commands);
        Assert.AreEqual(
            AndroidScreenPowerMode.POWER_MODE_NORMAL,
            ((SetScreenPowerModeControlMessage)client.Commands[0]).Mode);
        Assert.AreEqual(
            AndroidScreenPowerMode.POWER_MODE_OFF,
            ((SetScreenPowerModeControlMessage)client.Commands[1]).Mode);
    }

    [TestMethod]
    public async Task FrameSizeChange_UpdatesDimensionsOnlyWhenChanged()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        List<SingleViewDeviceContentSizeChangedEventArgs> sizes = [];
        session.ContentSizeChanged += (_, eventArgs) => sizes.Add(eventArgs);

        await session.StartAsync(CancellationToken.None);
        client.RaiseFrame(1080, 2220);
        client.RaiseFrame(1080, 2220);

        Assert.AreEqual(1080, session.ContentWidth);
        Assert.AreEqual(2220, session.ContentHeight);
        Assert.HasCount(2, sizes);
        Assert.AreEqual(720, sizes[0].Width);
        Assert.AreEqual(1280, sizes[0].Height);
        Assert.AreEqual(1080, sizes[1].Width);
        Assert.AreEqual(2220, sizes[1].Height);
    }

    [TestMethod]
    public async Task Rotation_1080x2220_To2220x1080_RaisesOneNewSize()
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 1080,
            Height = 2220
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        List<SingleViewDeviceContentSizeChangedEventArgs> sizes = [];
        session.ContentSizeChanged += (_, eventArgs) => sizes.Add(eventArgs);

        await session.StartAsync(CancellationToken.None);
        client.RaiseFrame(2220, 1080);
        client.RaiseFrame(2220, 1080);

        Assert.AreEqual(2220, session.ContentWidth);
        Assert.AreEqual(1080, session.ContentHeight);
        Assert.HasCount(2, sizes);
        Assert.AreEqual(2220, sizes[1].Width);
        Assert.AreEqual(1080, sizes[1].Height);
    }

    [TestMethod]
    public void TwoDynamicLoopbackListeners_UseDifferentPorts()
    {
        using TcpListener first = Scrcpy.CreateDynamicLoopbackListener();
        using TcpListener second = Scrcpy.CreateDynamicLoopbackListener();

        int firstPort = ((IPEndPoint)first.LocalEndpoint).Port;
        int secondPort = ((IPEndPoint)second.LocalEndpoint).Port;

        Assert.IsTrue(firstPort > 0);
        Assert.IsTrue(secondPort > 0);
        Assert.AreNotEqual(firstPort, secondPort);
    }

    private static ScrcpyNetSingleViewDeviceSession CreateSession(
        ISharpAdbDeviceResolver resolver,
        IScrcpyNetClientFactory factory,
        TimeSpan? firstFrameTimeout = null)
    {
        return new ScrcpyNetSingleViewDeviceSession(
            new ViewDeviceLaunchOptions(Serial),
            resolver,
            factory,
            new TestLogger<ScrcpyNetSingleViewDeviceSession>(),
            firstFrameTimeout);
    }

    private static async Task AssertUnavailableActionAsync(
        Func<ScrcpyNetSingleViewDeviceSession, Task> action)
    {
        FakeClient client = new(CreateUninitializedScrcpy())
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        await using ScrcpyNetSingleViewDeviceSession session = CreateSession(
            new FakeDeviceResolver(),
            new FakeClientFactory(client));
        await session.StartAsync(CancellationToken.None);
        client.Connected = false;

        InvalidOperationException exception =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => action(session));

        StringAssert.Contains(exception.Message, "control session is unavailable");
        Assert.IsEmpty(client.Commands);
        Assert.IsEmpty(client.CommandBatches);
    }

    private static Scrcpy CreateUninitializedScrcpy()
    {
        return (Scrcpy)RuntimeHelpers.GetUninitializedObject(typeof(Scrcpy));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;
            await Task.Delay(10);
        }

        Assert.Fail("Timed out waiting for the fake ScrcpyNet client.");
    }

    private sealed class FakeDeviceResolver : ISharpAdbDeviceResolver
    {
        private readonly DeviceData _device =
            (DeviceData)RuntimeHelpers.GetUninitializedObject(typeof(DeviceData));

        public int ResolveCount { get; private set; }

        public DeviceData Resolve(string serial)
        {
            ResolveCount++;
            Assert.AreEqual(Serial, serial);
            return _device;
        }
    }

    private sealed class FakeClientFactory(params FakeClient[] clients) : IScrcpyNetClientFactory
    {
        private readonly Queue<FakeClient> _clients = new(clients);

        public int CreateCount { get; private set; }

        public IScrcpyNetClient Create(DeviceData device, ViewDeviceLaunchOptions options)
        {
            CreateCount++;
            Assert.AreEqual(Serial, options.Serial);
            if (_clients.Count == 0)
                throw new InvalidOperationException("No fake client was configured.");

            return _clients.Dequeue();
        }
    }

    private sealed class FakeClient(Scrcpy scrcpy) : IScrcpyNetClient
    {
        public Scrcpy? Client { get; } = scrcpy;
        public bool Connected { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int? StartFrameWidth { get; set; }
        public int? StartFrameHeight { get; set; }
        public IReadOnlyList<string> RecentDiagnostics => [];
        public int StartCount { get; private set; }
        public TaskCompletionSource StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StopCount { get; private set; }
        public int FlushCount { get; private set; }
        public int DisposeCount { get; private set; }
        public Exception? StartException { get; set; }
        public Exception? FlushException { get; set; }
        public TaskCompletionSource? StartGate { get; set; }
        public TaskCompletionSource? FlushGate { get; set; }
        public TaskCompletionSource? StopGate { get; set; }
        public bool RaiseFrameOnStart { get; set; } = true;
        public bool AcceptControlBatches { get; set; } = true;
        public bool TrySendControlCommandsAccepted { get; private set; }
        public List<IControlMessage> Commands { get; } = [];
        public List<IReadOnlyList<IControlMessage>> CommandBatches { get; } = [];

        private EventHandler<ScrcpyNetFrameEventArgs>? _frameReceived;
        private EventHandler<ScrcpyNetErrorEventArgs>? _failed;
        private EventHandler<ScrcpyClipboardChangedEventArgs>? _clipboardChanged;
        private EventHandler? _exited;

        public event EventHandler<ScrcpyNetFrameEventArgs>? FrameReceived
        {
            add => _frameReceived += value;
            remove => _frameReceived -= value;
        }

        public event EventHandler<ScrcpyNetErrorEventArgs>? Failed
        {
            add => _failed += value;
            remove => _failed -= value;
        }

        public event EventHandler<ScrcpyClipboardChangedEventArgs>? ClipboardChanged
        {
            add => _clipboardChanged += value;
            remove => _clipboardChanged -= value;
        }

        public event EventHandler? Exited
        {
            add => _exited += value;
            remove => _exited -= value;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            StartEntered.TrySetResult();
            if (StartException is not null)
                throw StartException;
            if (StartGate is not null)
                await StartGate.Task.WaitAsync(cancellationToken);
            Connected = true;
            if (RaiseFrameOnStart)
                RaiseFrame(StartFrameWidth ?? Width, StartFrameHeight ?? Height);
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCount++;
            if (StopGate is not null)
                await StopGate.Task.WaitAsync(cancellationToken);
            Connected = false;
        }

        public async Task FlushControlAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FlushCount++;
            if (FlushException is not null)
                throw FlushException;
            if (FlushGate is not null)
                await FlushGate.Task.WaitAsync(cancellationToken);
        }

        public void SendControlCommand(IControlMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);
            SendControlCommands([message]);
        }

        public void SendControlCommands(IReadOnlyList<IControlMessage> messages)
        {
            ArgumentNullException.ThrowIfNull(messages);
            IControlMessage[] batch = messages.ToArray();
            CommandBatches.Add(batch);
            Commands.AddRange(batch);
        }

        public bool TrySendControlCommands(IReadOnlyList<IControlMessage> messages)
        {
            ArgumentNullException.ThrowIfNull(messages);
            TrySendControlCommandsAccepted = AcceptControlBatches;
            if (!AcceptControlBatches)
                return false;

            SendControlCommands(messages);
            return true;
        }

        public bool TrySendControlCommand(IControlMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);
            return TrySendControlCommands([message]);
        }

        public void Dispose()
        {
            DisposeCount++;
            Connected = false;
        }

        public void RaiseFrame(int width, int height)
        {
            _frameReceived?.Invoke(this, new ScrcpyNetFrameEventArgs(width, height));
        }

        public void RaiseClipboard(string text)
        {
            _clipboardChanged?.Invoke(this, new ScrcpyClipboardChangedEventArgs(text));
        }

        public void RaiseFailure(Exception exception)
        {
            _failed?.Invoke(this, new ScrcpyNetErrorEventArgs(exception));
        }

        public void RaiseExited()
        {
            Connected = false;
            _exited?.Invoke(this, EventArgs.Empty);
        }

        public CapturedCallbacks CaptureCallbacks()
        {
            return new CapturedCallbacks(
                _frameReceived,
                _failed,
                _clipboardChanged,
                _exited);
        }

        public sealed class CapturedCallbacks(
            EventHandler<ScrcpyNetFrameEventArgs>? frameReceived,
            EventHandler<ScrcpyNetErrorEventArgs>? failed,
            EventHandler<ScrcpyClipboardChangedEventArgs>? clipboardChanged,
            EventHandler? exited)
        {
            public void RaiseFrame(object sender, int width, int height)
            {
                frameReceived?.Invoke(sender, new ScrcpyNetFrameEventArgs(width, height));
            }

            public void RaiseClipboard(object sender, string text)
            {
                clipboardChanged?.Invoke(sender, new ScrcpyClipboardChangedEventArgs(text));
            }

            public void RaiseFailure(object sender, Exception exception)
            {
                failed?.Invoke(sender, new ScrcpyNetErrorEventArgs(exception));
            }

            public void RaiseExited(object sender)
            {
                exited?.Invoke(sender, EventArgs.Empty);
            }
        }
    }
}
