using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface IAddDevicesDialogService
    {
        Task<IReadOnlyList<StoredDeviceConfig>> ShowAddDevicesAsync(CancellationToken cancellationToken);
    }
}
