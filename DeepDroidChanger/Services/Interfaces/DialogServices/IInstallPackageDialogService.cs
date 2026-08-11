using DeepDroidChanger.Models;
namespace DeepDroidChanger.Services
{
    public interface IInstallPackageDialogService
    {
        Task<InstallPackageRequest?> ShowInstallPackageAsync(
            string deviceSerial,
            string deviceName,
            CancellationToken cancellationToken);

        Task<InstallPackageBatchRequest?> ShowInstallPackageBatchAsync(
            int targetCount,
            CancellationToken cancellationToken);
    }
}
