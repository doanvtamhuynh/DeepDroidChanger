using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface IDeviceLocationService
    {
        Task ApplyLocationAsync(string serial, string latitude, string longitude, CancellationToken cancellationToken);
        Task<(string Latitude, string Longitude)> ResolveLocationByDeviceIpAsync(string serial, CancellationToken cancellationToken);
        Task<(string Latitude, string Longitude)> ApplyAsync(
            string serial,
            ChangeLocationDialogResult result,
            CancellationToken cancellationToken);
    }
}
