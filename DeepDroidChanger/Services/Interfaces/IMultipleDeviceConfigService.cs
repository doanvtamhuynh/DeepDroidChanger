using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IMultipleDeviceConfigService
{
    Task<MultipleDeviceConfiguration> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(
        MultipleDeviceConfiguration configuration,
        CancellationToken cancellationToken);

    Task<MultipleDeviceProxyConfig> LoadProxyConfigAsync(
        CancellationToken cancellationToken);

    Task SaveProxyConfigAsync(
        MultipleDeviceProxyConfig configuration,
        CancellationToken cancellationToken);
}
