using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;

namespace DeepDroidChanger.Services;

public interface IViewDeviceInteractionService
{
    bool AutoReconnect => true;
    void ReleaseOptions(string serial) { }
    ViewDeviceLaunchOptions PrepareOptions(ViewDeviceLaunchOptions options);
    IDisposable Register(ISingleViewDeviceSession session, Func<Task> reconnect, Action<string> reportStatus);
    void Activate(ISingleViewDeviceSession session);
    Task ToggleAudioAsync(ISingleViewDeviceSession session, CancellationToken cancellationToken);
    Task SetKeyboardModeAsync(ISingleViewDeviceSession session, ViewDeviceKeyboardMode mode, CancellationToken cancellationToken);
    Task PushFileAsync(ISingleViewDeviceSession session, string localPath, CancellationToken cancellationToken);
}
