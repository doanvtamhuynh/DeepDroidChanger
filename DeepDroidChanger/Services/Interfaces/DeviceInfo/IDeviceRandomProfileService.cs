using DeepDroidChanger.Models;
namespace DeepDroidChanger.Services
{
    public interface IDeviceRandomProfileService
    {
        Task<DeviceInfoApiDevice> CreateRandomProfileAsync(AccountSession session, RandomDeviceRequest request, CancellationToken cancellationToken);
    }
}
