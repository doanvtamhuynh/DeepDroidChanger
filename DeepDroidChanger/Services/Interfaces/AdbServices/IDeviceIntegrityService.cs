using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface IDeviceIntegrityService
    {
        Task UpdateIntegrityAsync(string serial, bool fromServer, string? jsonPath, CancellationToken cancellationToken);
        Task UpdateKeyboxAsync(string serial, bool fromServer, string? keyboxPath, CancellationToken cancellationToken);
        Task ApplyAsync(string serial, UpdateIntegrityDialogResult result, CancellationToken cancellationToken);
    }
}
