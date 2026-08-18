using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface IChangeLocationDialogService
    {
        Task<ChangeLocationDialogResult?> ShowChangeLocationAsync(
            string deviceSerial,
            string deviceName,
            CancellationToken cancellationToken);

        Task<ChangeLocationDialogResult?> ShowChangeLocationAsync(
            string deviceSerial,
            string deviceName,
            StoredDeviceConfig? configurationSnapshot,
            CancellationToken cancellationToken)
            => ShowChangeLocationAsync(deviceSerial, deviceName, cancellationToken);

        Task<ChangeLocationDialogResult?> ShowChangeLocationBatchAsync(
            int targetCount,
            CancellationToken cancellationToken);
    }
}
