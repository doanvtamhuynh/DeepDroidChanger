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
    Busy,
    Reconnecting,
    Failed,
    Closing,
    Closed
}
