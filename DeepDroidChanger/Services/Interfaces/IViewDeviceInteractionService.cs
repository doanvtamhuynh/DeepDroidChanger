using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;

namespace DeepDroidChanger.Services;

public interface IViewDeviceInteractionService
{
    ViewDeviceLaunchOptions PrepareOptions(ViewDeviceLaunchOptions options);
    IDisposable Register(ISingleViewDeviceSession session, Func<Task> reconnect, Action<string> reportStatus);
    void Activate(ISingleViewDeviceSession session);
    Task ToggleAudioAsync(ISingleViewDeviceSession session, CancellationToken cancellationToken);
    Task SetKeyboardModeAsync(ISingleViewDeviceSession session, ViewDeviceKeyboardMode mode, CancellationToken cancellationToken);
    Task PushFileAsync(ISingleViewDeviceSession session, string localPath, CancellationToken cancellationToken);
}
