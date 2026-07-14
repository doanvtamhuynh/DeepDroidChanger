using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface IDeviceTimezoneService
    {
        Task ApplyTimezoneAsync(string serial, string timezone, CancellationToken cancellationToken);
        Task<string> ResolveTimezoneByDeviceIpAsync(string serial, CancellationToken cancellationToken);
        Task<string> ApplyAsync(string serial, ChangeTimezoneDialogResult result, CancellationToken cancellationToken);
    }
}
