using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface IDeviceIntegrityService
    {
        Task<string?> TryGetRandomSecurityPatchAsync(CancellationToken cancellationToken);
        Task UpdateIntegrityAsync(string serial, bool fromServer, string? jsonPath, CancellationToken cancellationToken);
        Task UpdateKeyboxAsync(string serial, bool fromServer, string? keyboxPath, CancellationToken cancellationToken);
        Task<PreparedIntegrityData> PrepareAsync(
            UpdateIntegrityDialogResult result,
            CancellationToken cancellationToken);
        Task ApplyPreparedAsync(
            string serial,
            PreparedIntegrityData preparedData,
            CancellationToken cancellationToken);
        Task ApplyAsync(string serial, UpdateIntegrityDialogResult result, CancellationToken cancellationToken);
    }
}
