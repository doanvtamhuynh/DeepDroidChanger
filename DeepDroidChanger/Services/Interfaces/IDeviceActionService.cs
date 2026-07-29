using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IDeviceActionService
{
    Task RebootAsync(string serial, CancellationToken cancellationToken);

    Task<GooglePackageState> GetGooglePackageStateAsync(
        string serial,
        CancellationToken cancellationToken);

    Task SetGmsEnabledAsync(
        string serial,
        bool enabled,
        CancellationToken cancellationToken);

    Task SetPlayStoreEnabledAsync(
        string serial,
        bool enabled,
        CancellationToken cancellationToken);

    Task<bool> GetWifiEnabledAsync(
        string serial,
        CancellationToken cancellationToken);

    Task SetWifiEnabledAsync(
        string serial,
        bool enabled,
        CancellationToken cancellationToken);
}
