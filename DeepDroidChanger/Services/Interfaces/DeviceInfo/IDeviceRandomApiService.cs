using DeepDroidChanger.Models;
namespace DeepDroidChanger.Services
{
    public interface IDeviceRandomApiService
    {
        Task<DeviceInfoApiDevice> GetRandomDeviceAsync(
            RandomDeviceSelection selection,
            CancellationToken cancellationToken);
    }
}
