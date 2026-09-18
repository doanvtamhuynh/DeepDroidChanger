namespace DeepDroidChanger.ViewDevices.Contracts;

public interface IScrcpyNetDeviceLeaseCoordinator
{
    IDisposable Acquire(string serial);
}
