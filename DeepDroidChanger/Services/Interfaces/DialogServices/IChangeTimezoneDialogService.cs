using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface IChangeTimezoneDialogService
    {
        Task<ChangeTimezoneDialogResult?> ShowChangeTimezoneAsync(
            string deviceSerial,
            string deviceName,
            CancellationToken cancellationToken);

        Task<ChangeTimezoneDialogResult?> ShowChangeTimezoneBatchAsync(
            int targetCount,
            CancellationToken cancellationToken);
    }
}
