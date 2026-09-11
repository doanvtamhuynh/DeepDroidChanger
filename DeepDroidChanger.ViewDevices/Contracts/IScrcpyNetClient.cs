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

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken = default);
    void SendControlCommand(IControlMessage message);
}

public interface IScrcpyNetClientFactory
{
    IScrcpyNetClient Create(DeviceData device, ViewDeviceLaunchOptions options);
}

public interface ISharpAdbDeviceResolver
{
    DeviceData Resolve(string serial);
}
