using DeepDroidChanger.Models;

namespace DeepDroidChanger.Helpers;

public static class StoredDeviceConfigSnapshot
{
    public static StoredDeviceConfig Create(StoredDeviceConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new StoredDeviceConfig
        {
            Serial = source.Serial,
            Name = source.Name,
            Type = source.Type,
            CountryIso = source.CountryIso,
            CountryName = source.CountryName,
            Carrier = source.Carrier,
            CarrierMcc = source.CarrierMcc,
            CarrierMnc = source.CarrierMnc,
            Brand = source.Brand,
            AndroidVersion = source.AndroidVersion,
            ChangeSimEnabled = source.ChangeSimEnabled,
            UseIntegritySecurityPatch = source.UseIntegritySecurityPatch,
            ChangeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(source.ChangeOptions),
            UpdateIntegrityFromServer = source.UpdateIntegrityFromServer,
            UpdateIntegrityFile = source.UpdateIntegrityFile,
            UpdateKeyboxFile = source.UpdateKeyboxFile,
            UpdateIntegrityEnabled = source.UpdateIntegrityEnabled,
            UpdateKeyboxEnabled = source.UpdateKeyboxEnabled,
            LocationMode = source.LocationMode,
            LocationLatitude = source.LocationLatitude,
            LocationLongitude = source.LocationLongitude,
            LocationCountryCode = source.LocationCountryCode,
            LocationCityName = source.LocationCityName,
            TimezoneMode = source.TimezoneMode,
            Timezone = source.Timezone,
            ProxyFullString = source.ProxyFullString,
            ProxyType = source.ProxyType,
            ProxyChangeLocationByIp = source.ProxyChangeLocationByIp,
            ProxyChangeTimezoneByIp = source.ProxyChangeTimezoneByIp
        };
    }

    public static List<StoredDeviceConfig> CreateMany(IEnumerable<StoredDeviceConfig> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Select(Create).ToList();
    }
}
