using DeepDroidChanger.Models;
namespace DeepDroidChanger.Services
{
    public interface IAdbDeviceService
    {
        Task<IReadOnlyList<AdbDevice>> GetConnectedDevicesAsync(CancellationToken cancellationToken);
        Task<string> GetDeviceTypeAsync(string serial, CancellationToken cancellationToken);
    }
}
