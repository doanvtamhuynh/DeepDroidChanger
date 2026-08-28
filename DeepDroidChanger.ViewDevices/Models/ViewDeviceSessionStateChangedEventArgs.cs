namespace DeepDroidChanger.ViewDevices.Models;

public sealed class ViewDeviceSessionStateChangedEventArgs(
    ViewDeviceSessionState previous,
    ViewDeviceSessionState current) : EventArgs
{
    public ViewDeviceSessionState Previous { get; } = previous;
    public ViewDeviceSessionState Current { get; } = current;
}
