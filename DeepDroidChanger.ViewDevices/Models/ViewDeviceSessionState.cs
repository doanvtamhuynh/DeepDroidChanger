namespace DeepDroidChanger.ViewDevices.Models;

public enum ViewDeviceSessionState
{
    Created,
    CheckingDevice,
    Starting,
    Running,
    WaitingForDevice,
    Unauthorized,
    AdbUnavailable,
    Reconnecting,
    Failed,
    Closing,
    Closed
}
