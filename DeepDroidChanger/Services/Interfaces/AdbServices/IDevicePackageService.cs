namespace DeepDroidChanger.Services;

public interface IDevicePackageService
{
    Task<IReadOnlyList<string>> GetInstalledPackagesAsync(
        string serial,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetUserInstalledPackagesAsync(
        string serial,
        CancellationToken cancellationToken);
}
