using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface IPackageInstallService
    {
        Task<InstallPackageResult> InstallAsync(
            string serial,
            string filePath,
            InstallPackageOptions options,
            CancellationToken cancellationToken);

        Task<InstallPackageSetResult> InstallManyAsync(
            string serial,
            IReadOnlyList<string> filePaths,
            InstallPackageOptions options,
            CancellationToken cancellationToken);
    }
}
