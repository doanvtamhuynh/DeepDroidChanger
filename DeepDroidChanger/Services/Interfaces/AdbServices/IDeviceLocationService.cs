using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface IDeviceLocationService
    {
        Task ApplyLocationAsync(string serial, string latitude, string longitude, CancellationToken cancellationToken);
        Task<DeviceLocationResult> ResolveLocationByDeviceIpAsync(string serial, CancellationToken cancellationToken);
        Task<DeviceLocationResult> ApplyAsync(
            string serial,
            ChangeLocationDialogResult result,
            CancellationToken cancellationToken);

        Task<DeviceLocationResult> ApplyCatalogLocationAsync(
            string serial,
            LocationOption location,
            CancellationToken cancellationToken);
    }
}
