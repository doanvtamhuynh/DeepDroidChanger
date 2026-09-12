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
    public async Task ClientExitBeforeFirstFrame_DoesNotRaisePublicExited()
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
    public async Task ClientExitAfterFirstFrame_RaisesExitedExactlyOnce()
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
    public async Task ScreenPowerAndRotateCommands_SendDistinctScrcpyMessages()
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
        await session.RotateDeviceAsync();

        Assert.HasCount(3, client.Commands);
        Assert.AreEqual(
            AndroidScreenPowerMode.POWER_MODE_NORMAL,
            ((SetScreenPowerModeControlMessage)client.Commands[0]).Mode);
        Assert.AreEqual(
            AndroidScreenPowerMode.POWER_MODE_OFF,
            ((SetScreenPowerModeControlMessage)client.Commands[1]).Mode);
        Assert.IsInstanceOfType(client.Commands[2], typeof(RotateDeviceControlMessage));
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

    private sealed class FakeClientFactory(FakeClient client) : IScrcpyNetClientFactory
    {
        public int CreateCount { get; private set; }

        public IScrcpyNetClient Create(DeviceData device, ViewDeviceLaunchOptions options)
        {
            CreateCount++;
            Assert.AreEqual(Serial, options.Serial);
            return client;
        }
    }

    private sealed class FakeClient(Scrcpy scrcpy) : IScrcpyNetClient
    {
        public Scrcpy? Client { get; } = scrcpy;
        public bool Connected { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public IReadOnlyList<string> RecentDiagnostics => [];
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }
        public Exception? StartException { get; set; }
        public TaskCompletionSource? StartGate { get; set; }
        public TaskCompletionSource? StopGate { get; set; }
        public bool RaiseFrameOnStart { get; set; } = true;
        public List<IControlMessage> Commands { get; } = [];

        public event EventHandler<ScrcpyNetFrameEventArgs>? FrameReceived;
        public event EventHandler<ScrcpyNetErrorEventArgs>? Failed;
        public event EventHandler? Exited;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            if (StartException is not null)
                throw StartException;
            if (StartGate is not null)
                await StartGate.Task.WaitAsync(cancellationToken);
            Connected = true;
            if (RaiseFrameOnStart)
                RaiseFrame(Width, Height);
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCount++;
            if (StopGate is not null)
                await StopGate.Task.WaitAsync(cancellationToken);
            Connected = false;
        }

        public void SendControlCommand(IControlMessage message)
        {
            Commands.Add(message);
        }

        public void Dispose()
        {
            DisposeCount++;
            Connected = false;
        }

        public void RaiseFrame(int width, int height)
        {
            Width = width;
            Height = height;
            FrameReceived?.Invoke(this, new ScrcpyNetFrameEventArgs(width, height));
        }

        public void RaiseFailure(Exception exception)
        {
            Failed?.Invoke(this, new ScrcpyNetErrorEventArgs(exception));
        }

        public void RaiseExited()
        {
            Connected = false;
            Exited?.Invoke(this, EventArgs.Empty);
        }
    }
}
