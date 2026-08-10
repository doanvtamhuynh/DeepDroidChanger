using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface IChangeLocationDialogService
    {
        Task<ChangeLocationDialogResult?> ShowChangeLocationAsync(
            string deviceSerial,
            string deviceName,
            CancellationToken cancellationToken);

        Task<ChangeLocationDialogResult?> ShowChangeLocationBatchAsync(
            int targetCount,
            CancellationToken cancellationToken);
    }
}
