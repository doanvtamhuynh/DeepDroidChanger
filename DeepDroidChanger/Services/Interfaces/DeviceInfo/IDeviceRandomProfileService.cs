using DeepDroidChanger.Models;
namespace DeepDroidChanger.Services
{
    public interface IDeviceRandomProfileService
    {
        Task<DeviceInfoApiDevice> CreateRandomProfileAsync(
            RandomDeviceRequest request,
            CancellationToken cancellationToken);
    }
}
