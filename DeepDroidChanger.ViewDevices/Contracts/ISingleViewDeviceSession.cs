using ScrcpyNet;
using DeepDroidChanger.ViewDevices.Models;

namespace DeepDroidChanger.ViewDevices.Contracts;

public interface ISingleViewDeviceSession : IAsyncDisposable
{
    string Serial { get; }
    SingleViewDeviceSessionState State { get; }
    Scrcpy? Client { get; }
    int ContentWidth { get; }
    int ContentHeight { get; }
    IReadOnlyList<string> RecentDiagnostics { get; }

    event EventHandler<SingleViewDeviceSessionStateChangedEventArgs>? StateChanged;
    event EventHandler<SingleViewDeviceContentSizeChangedEventArgs>? ContentSizeChanged;
    event EventHandler? Exited;

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SendKeyEventAsync(int keyCode, CancellationToken cancellationToken = default);
    Task SendBackOrScreenOnAsync(CancellationToken cancellationToken = default);
}
