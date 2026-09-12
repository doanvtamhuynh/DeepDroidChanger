using System.Runtime.CompilerServices;
using System.Windows;
using DeepDroidChanger.ViewDevices.Models;
using DeepDroidChanger.ViewDevices.Runtime;
using ScrcpyNet;
using ScrcpyNet.Wpf;
using SharpAdbClient;
using WpfPoint = System.Windows.Point;

namespace DeepDroidChanger.Tests.Services.Implementations.ViewDevices;

[TestClass]
public sealed class ScrcpyProtocolTests
{
    [TestMethod]
    public void InjectKeycode_UsesScrcpyV123GoldenBytes()
    {
        KeycodeControlMessage message = new()
        {
            Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP,
            KeyCode = (AndroidKeycode)24,
            Repeat = 2,
            Metastate = AndroidMetastate.AMETA_CTRL_ON
        };

        AssertBytes(
            new byte[] { 0, 1, 0, 0, 0, 24, 0, 0, 0, 2, 0, 0, 0x10, 0 },
            message.ToBytes());
    }

    [TestMethod]
    public void TouchDown_UsesScrcpyV123GoldenBytes()
    {
        TouchEventControlMessage message = new()
        {
            Action = AndroidMotionEventAction.AMOTION_EVENT_ACTION_DOWN,
            Position = CreatePosition(10, 20, 1080, 2220)
        };

        AssertBytes(
            new byte[]
            {
                2, 0,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                0, 0, 0, 10,
                0, 0, 0, 20,
                4, 0x38,
                8, 0xAC,
                0xFF, 0xFF,
                0, 0, 0, 1
            },
            message.ToBytes());
    }

    [TestMethod]
    public void TouchUp_UsesScrcpyV123GoldenBytes()
    {
        TouchEventControlMessage message = new()
        {
            Action = AndroidMotionEventAction.AMOTION_EVENT_ACTION_UP,
            PointerId = 42,
            Position = CreatePosition(-1, 2220, 400, 800),
            Buttons = 0
        };

        AssertBytes(
            new byte[]
            {
                2, 1,
                0, 0, 0, 0, 0, 0, 0, 42,
                0xFF, 0xFF, 0xFF, 0xFF,
                0, 0, 8, 0xAC,
                1, 0x90,
                3, 0x20,
                0xFF, 0xFF,
                0, 0, 0, 0
            },
            message.ToBytes());
    }

    [TestMethod]
    public void Scroll_Uses25ByteScrcpyV123PacketAndSerializesButtons()
    {
        ScrollEventControlMessage message = new()
        {
            Position = CreatePosition(10, 20, 1080, 2220),
            HorizontalScroll = -2,
            VerticalScroll = 3,
            Buttons = AndroidMotionEventButtons.AMOTION_EVENT_BUTTON_SECONDARY
        };

        byte[] actual = message.ToBytes().ToArray();

        Assert.AreEqual(25, actual.Length);
        CollectionAssert.AreEqual(
            new byte[]
            {
                3,
                0, 0, 0, 10,
                0, 0, 0, 20,
                4, 0x38,
                8, 0xAC,
                0xFF, 0xFF, 0xFF, 0xFE,
                0, 0, 0, 3,
                0, 0, 0, 2
            },
            actual);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 2 }, actual[21..]);
    }

    [TestMethod]
    public void BackOrScreenOn_UsesScrcpyV123GoldenBytes()
    {
        BackOrScreenOnControlMessage message = new()
        {
            Action = AndroidKeyEventAction.AKEY_EVENT_ACTION_UP
        };

        AssertBytes(new byte[] { 4, 1 }, message.ToBytes());
    }

    [TestMethod]
    public void RotateDevice_UsesOneByteScrcpyV123Message()
    {
        AssertBytes(new byte[] { 11 }, new RotateDeviceControlMessage().ToBytes());
    }

    [TestMethod]
    public void SetScreenPowerMode_UsesV123OffAndNormalValues()
    {
        AssertBytes(
            new byte[] { 10, 2 },
            new SetScreenPowerModeControlMessage
            {
                Mode = AndroidScreenPowerMode.POWER_MODE_NORMAL
            }.ToBytes());
        AssertBytes(
            new byte[] { 10, 0 },
            new SetScreenPowerModeControlMessage
            {
                Mode = AndroidScreenPowerMode.POWER_MODE_OFF
            }.ToBytes());
    }

    [TestMethod]
    public void SingleViewServerArguments_ContainBoundedV123VideoProfile()
    {
        ViewDeviceLaunchOptions options = new("SERIAL")
        {
            MaxSize = 1280,
            MaxFps = 30,
            VideoBitRate = "4M"
        };

        string[] arguments = Scrcpy.BuildServerArguments(
                ScrcpyNetClientFactory.ParseBitrate(options.VideoBitRate),
                options.MaxSize,
                options.MaxFps)
            .ToArray();

        CollectionAssert.Contains(arguments, "1.23");
        CollectionAssert.Contains(arguments, "bit_rate=4000000");
        CollectionAssert.Contains(arguments, "max_size=1280");
        CollectionAssert.Contains(arguments, "max_fps=30");
        CollectionAssert.Contains(arguments, "control=true");
    }

    [TestMethod]
    public void ServerLifecycle_OnlyRemovesOwnedReverseEndpoint()
    {
        FakeAdbOperations adb = new();
        ScrcpyNetServerLifecycle lifecycle = new(CreateDevice(), adb);

        lifecycle.Setup(43123, "server.jar", CancellationToken.None);
        lifecycle.Cleanup();

        CollectionAssert.AreEqual(
            new[]
            {
                ScrcpyNetServerLifecycle.RemoteEndpoint,
                ScrcpyNetServerLifecycle.RemoteEndpoint
            },
            adb.RemovedReverseEndpoints);
        Assert.AreEqual("tcp:43123", adb.CreatedReverseLocal);
        Assert.AreEqual(ScrcpyNetServerLifecycle.RemoteEndpoint, adb.CreatedReverseRemote);
        Assert.AreEqual("server.jar", adb.UploadedServerFile);
    }

    [TestMethod]
    public void UniformContentRect_PillarboxAndLetterboxAreComputedFromSourceAspectRatio()
    {
        Rect pillarbox = ScrcpyDisplayGeometry.CalculateUniformContentRect(400, 800, 800, 800);
        Assert.AreEqual(new Rect(200, 0, 400, 800), pillarbox);

        Rect letterbox = ScrcpyDisplayGeometry.CalculateUniformContentRect(800, 400, 800, 800);
        Assert.AreEqual(new Rect(0, 200, 800, 400), letterbox);
    }

    [TestMethod]
    public void UniformMapping_CoversPortraitLandscapeAndTopLeftCases()
    {
        Assert.IsTrue(ScrcpyDisplayGeometry.TryMapPointToDevice(
            new WpfPoint(0, 0),
            1080,
            2220,
            540,
            1110,
            out WpfPoint portraitTopLeft));
        Assert.AreEqual(new WpfPoint(0, 0), portraitTopLeft);

        Assert.IsTrue(ScrcpyDisplayGeometry.TryMapPointToDevice(
            new WpfPoint(400, 400),
            400,
            800,
            800,
            800,
            out WpfPoint portraitSquareCenter));
        Assert.AreEqual(new WpfPoint(200, 400), portraitSquareCenter);

        Assert.IsTrue(ScrcpyDisplayGeometry.TryMapPointToDevice(
            new WpfPoint(400, 400),
            800,
            400,
            800,
            800,
            out WpfPoint landscapeSquareCenter));
        Assert.AreEqual(new WpfPoint(400, 200), landscapeSquareCenter);
    }

    [TestMethod]
    public void UniformMapping_MapsCenterAndClampsBottomRight()
    {
        Assert.IsTrue(ScrcpyDisplayGeometry.TryMapPointToDevice(
            new WpfPoint(400, 400),
            1920,
            1080,
            800,
            800,
            out WpfPoint center));
        Assert.AreEqual(new WpfPoint(960, 540), center);

        Assert.IsTrue(ScrcpyDisplayGeometry.TryMapPointToDevice(
            new WpfPoint(800, 600),
            800,
            400,
            800,
            800,
            out WpfPoint bottomRight));
        Assert.AreEqual(new WpfPoint(799, 399), bottomRight);
    }

    [TestMethod]
    public void UniformMapping_RejectsBlackBarsOnAllSides()
    {
        Assert.IsFalse(Maps(0, 400, 400, 800, 800, 800));
        Assert.IsFalse(Maps(800, 400, 400, 800, 800, 800));
        Assert.IsFalse(Maps(400, 0, 800, 400, 800, 800));
        Assert.IsFalse(Maps(400, 800, 800, 400, 800, 800));
    }

    [TestMethod]
    public void UniformMapping_HandlesRotatedPortraitDimensions()
    {
        Assert.IsTrue(ScrcpyDisplayGeometry.TryMapPointToDevice(
            new WpfPoint(400, 400),
            2220,
            1080,
            800,
            800,
            out WpfPoint mapped));
        Assert.AreEqual(new WpfPoint(1110, 540), mapped);
    }

    [TestMethod]
    public void PointerState_EmitsOneUpAndOneCancelForEachDown()
    {
        ScrcpyDisplayPointerState state = new();

        Assert.IsTrue(state.BeginPointerDown());
        Assert.IsTrue(state.CanMove);
        Assert.IsTrue(state.EndPointerUp());
        Assert.IsFalse(state.EndPointerUp());
        Assert.IsTrue(state.BeginPointerDown());
        Assert.IsTrue(state.CancelPointer());
        Assert.IsFalse(state.CancelPointer());
        Assert.IsFalse(state.CanMove);
    }

    private static Position CreatePosition(int x, int y, ushort width, ushort height)
    {
        return new Position
        {
            Point = new ScrcpyNet.Point { X = x, Y = y },
            ScreenSize = new ScreenSize { Width = width, Height = height }
        };
    }

    private static bool Maps(
        double x,
        double y,
        int sourceWidth,
        int sourceHeight,
        double targetWidth,
        double targetHeight)
    {
        return ScrcpyDisplayGeometry.TryMapPointToDevice(
            new WpfPoint(x, y),
            sourceWidth,
            sourceHeight,
            targetWidth,
            targetHeight,
            out _);
    }

    private static void AssertBytes(byte[] expected, Span<byte> actual)
    {
        CollectionAssert.AreEqual(expected, actual.ToArray());
    }

    private static DeviceData CreateDevice()
    {
        return (DeviceData)RuntimeHelpers.GetUninitializedObject(typeof(DeviceData));
    }

    private sealed class FakeAdbOperations : IScrcpyNetAdbOperations
    {
        public List<string> RemovedReverseEndpoints { get; } = [];
        public string? CreatedReverseRemote { get; private set; }
        public string? CreatedReverseLocal { get; private set; }
        public string? UploadedServerFile { get; private set; }

        public void CreateReverseForward(DeviceData device, string remote, string local, bool rebind)
        {
            CreatedReverseRemote = remote;
            CreatedReverseLocal = local;
        }

        public void RemoveReverseForward(DeviceData device, string remote)
        {
            RemovedReverseEndpoints.Add(remote);
        }

        public Task ExecuteRemoteCommandAsync(
            string command,
            DeviceData device,
            IShellOutputReceiver receiver,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void UploadMobileServer(
            DeviceData device,
            string serverFile,
            string remotePath,
            CancellationToken cancellationToken)
        {
            UploadedServerFile = serverFile;
        }
    }
}
