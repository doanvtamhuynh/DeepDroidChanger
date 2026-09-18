using System.Runtime.CompilerServices;
using DeepDroidChanger.Tests.Fakes;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using DeepDroidChanger.ViewDevices.Runtime;
using ScrcpyNet;
using SharpAdbClient;

namespace DeepDroidChanger.Tests.Services.Implementations.ViewDevices;

[TestClass]
public sealed class ScrcpyNetSingleViewDeviceSessionRotateTests
{
    [TestMethod]
    public async Task RotateDeviceAsync_WhenRunning_SendsExactlyOneRotateMessage()
    {
        FakeClient client = new((Scrcpy)RuntimeHelpers.GetUninitializedObject(typeof(Scrcpy)))
        {
            Connected = true,
            Width = 720,
            Height = 1280
        };
        await using var session = new ScrcpyNetSingleViewDeviceSession(
            new ViewDeviceLaunchOptions("SERIAL"),
            new FakeResolver(),
            new FakeFactory(client),
            new TestLogger<ScrcpyNetSingleViewDeviceSession>());

        await session.StartAsync(CancellationToken.None);
        await session.RotateDeviceAsync(CancellationToken.None);

        Assert.HasCount(1, client.Commands);
        Assert.IsInstanceOfType<RotateDeviceControlMessage>(client.Commands[0]);
        Assert.HasCount(1, client.Batches);
        Assert.HasCount(1, client.Batches[0]);
    }

    private sealed class FakeResolver : ISharpAdbDeviceResolver
    {
        private readonly DeviceData _device =
            (DeviceData)RuntimeHelpers.GetUninitializedObject(typeof(DeviceData));

        public DeviceData Resolve(string serial)
        {
            Assert.AreEqual("SERIAL", serial);
            return _device;
        }
    }

    private sealed class FakeFactory(FakeClient client) : IScrcpyNetClientFactory
    {
        public IScrcpyNetClient Create(DeviceData device, ViewDeviceLaunchOptions options)
        {
            Assert.AreEqual("SERIAL", options.Serial);
            return client;
        }
    }

    private sealed class FakeClient(Scrcpy scrcpy) : IScrcpyNetClient
    {
        private EventHandler<ScrcpyNetFrameEventArgs>? _frameReceived;

        public Scrcpy? Client { get; } = scrcpy;
        public bool Connected { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public IReadOnlyList<string> RecentDiagnostics => [];
        public List<IControlMessage> Commands { get; } = [];
        public List<IReadOnlyList<IControlMessage>> Batches { get; } = [];

        public event EventHandler<ScrcpyNetFrameEventArgs>? FrameReceived
        {
            add => _frameReceived += value;
            remove => _frameReceived -= value;
        }

        private EventHandler<ScrcpyNetErrorEventArgs>? _failed;
        private EventHandler? _exited;
        private EventHandler<ScrcpyClipboardChangedEventArgs>? _clipboardChanged;

        public event EventHandler<ScrcpyNetErrorEventArgs>? Failed
        {
            add => _failed += value;
            remove => _failed -= value;
        }

        public event EventHandler? Exited
        {
            add => _exited += value;
            remove => _exited -= value;
        }

        public event EventHandler<ScrcpyClipboardChangedEventArgs>? ClipboardChanged
        {
            add => _clipboardChanged += value;
            remove => _clipboardChanged -= value;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Connected = true;
            _frameReceived?.Invoke(this, new ScrcpyNetFrameEventArgs(Width, Height));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Connected = false;
            return Task.CompletedTask;
        }

        public Task FlushControlAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void SendControlCommand(IControlMessage message) => SendControlCommands([message]);

        public void SendControlCommands(IReadOnlyList<IControlMessage> messages)
        {
            Batches.Add(messages.ToArray());
            Commands.AddRange(messages);
        }

        public bool TrySendControlCommand(IControlMessage message)
        {
            SendControlCommand(message);
            return true;
        }

        public bool TrySendControlCommands(IReadOnlyList<IControlMessage> messages)
        {
            SendControlCommands(messages);
            return true;
        }

        public void Dispose() => Connected = false;
    }
}
