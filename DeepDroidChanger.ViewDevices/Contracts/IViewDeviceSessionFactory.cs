using DeepDroidChanger.ViewDevices.Models;

namespace DeepDroidChanger.ViewDevices.Contracts;

public interface IViewDeviceSessionFactory
{
    IViewDeviceSession Create(ViewDeviceLaunchOptions options);
}
