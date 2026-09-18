using DeepDroidChanger.ViewDevices.Models;

namespace DeepDroidChanger.ViewDevices.Contracts;

public interface ISingleViewDeviceSessionFactory
{
    ISingleViewDeviceSession Create(ViewDeviceLaunchOptions options);
}
