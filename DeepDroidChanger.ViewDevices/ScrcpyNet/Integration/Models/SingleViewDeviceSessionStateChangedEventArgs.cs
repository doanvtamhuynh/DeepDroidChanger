namespace DeepDroidChanger.ViewDevices.Models;

public sealed class SingleViewDeviceSessionStateChangedEventArgs(
    SingleViewDeviceSessionState previous,
    SingleViewDeviceSessionState current) : EventArgs
{
    public SingleViewDeviceSessionState Previous { get; } = previous;
    public SingleViewDeviceSessionState Current { get; } = current;
}
