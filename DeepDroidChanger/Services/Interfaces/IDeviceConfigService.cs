using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IDeviceConfigService
{
    Task SaveSettingsAsync(CancellationToken cancellationToken);

    Task<bool> SaveDeviceRowAsync(
        IList<StoredDeviceConfig> storedDevices,
        string serial,
        string name,
        string type,
        CarrierCountryOption? selectedCountry,
        CarrierOption? selectedCarrier,
        bool includeSelectedCarrierConfig,
        CancellationToken cancellationToken);

    Task<bool> SaveUpdateIntegrityConfigAsync(
        IList<StoredDeviceConfig> storedDevices,
        string serial,
        UpdateIntegrityDialogResult result,
        CancellationToken cancellationToken);

    Task<bool> SaveDeviceProfileAsync(
        IList<StoredDeviceConfig> storedDevices,
        string serial,
        DeviceProfileConfig profile,
        CancellationToken cancellationToken);

    Task<bool> SaveTimezoneConfigAsync(
        string serial,
        ChangeTimezoneMode mode,
        string timezone,
        CancellationToken cancellationToken);

    Task<bool> SaveTimezoneConfigAsync(
        IList<StoredDeviceConfig> storedDevices,
        string serial,
        ChangeTimezoneMode mode,
        string timezone,
        CancellationToken cancellationToken);

    Task<bool> SaveLocationConfigAsync(
        IList<StoredDeviceConfig> storedDevices,
        string serial,
        ChangeLocationMode mode,
        string latitude,
        string longitude,
        CancellationToken cancellationToken);

    Task<bool> SaveLocationConfigAsync(
        string serial,
        ChangeLocationMode mode,
        string latitude,
        string longitude,
        CancellationToken cancellationToken);

    Task<bool> SaveLocationConfigAsync(
        IList<StoredDeviceConfig> storedDevices,
        string serial,
        ChangeLocationMode mode,
        string latitude,
        string longitude,
        string countryCode,
        string cityName,
        CancellationToken cancellationToken);

    Task<bool> SaveProxyConfigAsync(
        IList<StoredDeviceConfig> storedDevices,
        string serial,
        FakeProxyDialogResult configuration,
        CancellationToken cancellationToken);

    Task<bool> SaveProxyConfigAsync(
        string serial,
        FakeProxyDialogResult configuration,
        CancellationToken cancellationToken);
}
