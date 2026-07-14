using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface IIpGeolocationService
    {
        Task<IpGeolocationInfo> GetDeviceIpGeolocationAsync(string serial, CancellationToken cancellationToken);
    }
}
