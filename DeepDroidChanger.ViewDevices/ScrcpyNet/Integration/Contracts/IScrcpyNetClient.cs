using ScrcpyNet;
using DeepDroidChanger.ViewDevices.Models;
using SharpAdbClient;

namespace DeepDroidChanger.ViewDevices.Contracts;

public interface IScrcpyNetClient : IDisposable
{
    Scrcpy? Client { get; }
    bool Connected { get; }
    int Width { get; }
    int Height { get; }
    IReadOnlyList<string> RecentDiagnostics { get; }

    event EventHandler<ScrcpyNetFrameEventArgs>? FrameReceived;
    event EventHandler<ScrcpyNetErrorEventArgs>? Failed;
    event EventHandler? Exited;
    event EventHandler<ScrcpyClipboardChangedEventArgs>? ClipboardChanged;

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task FlushControlAsync(CancellationToken cancellationToken = default);
    void SendControlCommand(IControlMessage message);
    void SendControlCommands(IReadOnlyList<IControlMessage> messages);
    bool TrySendControlCommand(IControlMessage message);
    bool TrySendControlCommands(IReadOnlyList<IControlMessage> messages);
}

public interface IScrcpyNetClientFactory
{
    IScrcpyNetClient Create(DeviceData device, ViewDeviceLaunchOptions options);
}

public interface ISharpAdbDeviceResolver
{
    DeviceData Resolve(string serial);
}
