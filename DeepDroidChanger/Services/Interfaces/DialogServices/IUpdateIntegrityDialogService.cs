using DeepDroidChanger.Models;
namespace DeepDroidChanger.Services
{
    public interface IUpdateIntegrityDialogService
    {
        Task<UpdateIntegrityDialogResult?> ShowUpdateIntegrityAsync(
            string deviceSerial,
            string deviceName,
            StoredDeviceConfig currentConfig,
            Func<UpdateIntegrityDialogResult, CancellationToken, Task>? settingsChangedAsync,
            CancellationToken cancellationToken);

        Task<UpdateIntegrityDialogResult?> ShowUpdateIntegrityBatchAsync(
            int targetCount,
            DeviceUpdateIntegrityConfig currentConfig,
            Func<UpdateIntegrityDialogResult, CancellationToken, Task> settingsChangedAsync,
            CancellationToken cancellationToken);
    }
}
