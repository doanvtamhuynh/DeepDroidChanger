using DeepDroidChanger.ViewDevices.Models;

namespace DeepDroidChanger.ViewDevices.Contracts;

public interface IViewDeviceSession : IAsyncDisposable
{
    string Serial { get; }
    ViewDeviceSessionState State { get; }
    IntPtr NativeWindowHandle { get; }
    int ContentWidth { get; }
    int ContentHeight { get; }
    IReadOnlyList<string> RecentDiagnostics { get; }

    event EventHandler<ViewDeviceSessionStateChangedEventArgs>? StateChanged;
    event EventHandler? NativeWindowReady;
    event EventHandler<ViewDeviceContentSizeChangedEventArgs>? ContentSizeChanged;
    event EventHandler? Exited;

    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken = default);
}
