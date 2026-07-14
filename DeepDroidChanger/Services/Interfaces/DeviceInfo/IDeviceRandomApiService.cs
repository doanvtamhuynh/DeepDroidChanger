using DeepDroidChanger.Models;
namespace DeepDroidChanger.Services
{
    public interface IDeviceRandomApiService
    {
        Task<DeviceInfoApiDevice> GetRandomDeviceAsync(AccountSession session, RandomDeviceSelection selection, CancellationToken cancellationToken);
    }
}
