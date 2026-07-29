namespace DeepDroidChanger.Services;

public interface IDevicePackageService
{
    Task<IReadOnlyList<string>> GetInstalledPackagesAsync(
        string serial,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetUserInstalledPackagesAsync(
        string serial,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetDisabledPackagesAsync(
        string serial,
        CancellationToken cancellationToken);

    Task SetPackageEnabledAsync(
        string serial,
        string packageName,
        bool enabled,
        CancellationToken cancellationToken);
}
