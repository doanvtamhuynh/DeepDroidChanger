using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using DeepDroidChanger.Constants;
using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class DeviceStoreService : IDeviceStoreService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly ILogger<DeviceStoreService> _logger;
    private readonly string _applicationRootDirectory;
    private readonly string _deviceManagerDirectory;
    private readonly string _devicesPath;

    public DeviceStoreService(ILogger<DeviceStoreService> logger)
        : this(
            Path.Combine(
                AppContext.BaseDirectory,
                RuntimeDataPathConstants.DeviceManagerDirectoryName,
                RuntimeDataPathConstants.DevicesFileName),
            logger)
    {
    }

    internal DeviceStoreService(string devicesPath, ILogger<DeviceStoreService> logger)
    {
        _devicesPath = Path.GetFullPath(devicesPath);
        _deviceManagerDirectory = Path.GetDirectoryName(_devicesPath)
            ?? throw new ArgumentException("Device store path must include a directory.", nameof(devicesPath));
        _applicationRootDirectory = string.Equals(
            Path.GetFileName(_deviceManagerDirectory),
            RuntimeDataPathConstants.DeviceManagerDirectoryName,
            StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(_deviceManagerDirectory) ?? _deviceManagerDirectory
            : _deviceManagerDirectory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StoredDeviceConfig>> LoadAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_deviceManagerDirectory);
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!File.Exists(_devicesPath))
                return await WriteAndReturnAsync([], cancellationToken).ConfigureAwait(false);

            return await ReadCurrentDevicesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Failed to load stored devices.");
            QuarantineFile(_devicesPath, "device index");
            return await WriteAndReturnAsync([], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(
        IEnumerable<StoredDeviceConfig> devices,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(devices);
        Directory.CreateDirectory(_deviceManagerDirectory);
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await WriteStoreAsync(Normalize(devices), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<bool> UpdateAsync(
        string serial,
        Action<StoredDeviceConfig> update,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(update);
        Directory.CreateDirectory(_deviceManagerDirectory);
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            List<StoredDeviceConfig> devices =
                await ReadCurrentDevicesForMutationAsync(cancellationToken).ConfigureAwait(false);
            StoredDeviceConfig? target = devices.FirstOrDefault(device =>
                string.Equals(device.Serial, serial, StringComparison.OrdinalIgnoreCase));
            if (target is null)
                return false;

            update(target);
            await WriteStoreAsync(devices, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<IReadOnlyList<StoredDeviceConfig>> MergeAsync(
        IEnumerable<StoredDeviceConfig> devices,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(devices);
        Directory.CreateDirectory(_deviceManagerDirectory);
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            List<StoredDeviceConfig> current =
                await ReadCurrentDevicesForMutationAsync(cancellationToken).ConfigureAwait(false);
            DeviceRowFactory.MergeSelectedDevices(current, devices);
            IReadOnlyList<StoredDeviceConfig> normalized = Normalize(current);
            await WriteStoreAsync(normalized, cancellationToken).ConfigureAwait(false);
            return normalized;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<bool> RemoveAsync(string serial, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        Directory.CreateDirectory(_deviceManagerDirectory);
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            List<StoredDeviceConfig> current =
                await ReadCurrentDevicesForMutationAsync(cancellationToken).ConfigureAwait(false);
            StoredDeviceConfig? removedDevice = current.FirstOrDefault(device =>
                string.Equals(device.Serial, serial, StringComparison.OrdinalIgnoreCase));
            if (removedDevice is null)
                return false;

            current.Remove(removedDevice);
            await WriteStoreAsync(current, cancellationToken).ConfigureAwait(false);
            DeleteDeviceDirectory(removedDevice.Serial);
            return true;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<List<StoredDeviceConfig>> ReadCurrentDevicesForMutationAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_devicesPath))
            return [];

        try
        {
            return (await ReadCurrentDevicesAsync(cancellationToken).ConfigureAwait(false)).ToList();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Failed to read stored devices for a mutation.");
            QuarantineFile(_devicesPath, "device index");
            await WriteStoreAsync([], cancellationToken).ConfigureAwait(false);
            return [];
        }
    }

    private async Task<IReadOnlyList<StoredDeviceConfig>> ReadCurrentDevicesAsync(
        CancellationToken cancellationToken)
    {
        string json = await File.ReadAllTextAsync(_devicesPath, cancellationToken).ConfigureAwait(false);
        using JsonDocument parsed = JsonDocument.Parse(json);
        if (parsed.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("Device index must be a JSON array.");

        List<DeviceIndexEntry> index =
            JsonSerializer.Deserialize<List<DeviceIndexEntry>>(json, JsonOptions) ?? [];
        IReadOnlyList<StoredDeviceConfig> devices =
            await ReadIndexedDevicesAsync(index, cancellationToken).ConfigureAwait(false);
        await WriteStoreAsync(devices, cancellationToken).ConfigureAwait(false);
        return devices;
    }

    private async Task<IReadOnlyList<StoredDeviceConfig>> ReadIndexedDevicesAsync(
        IEnumerable<DeviceIndexEntry> index,
        CancellationToken cancellationToken)
    {
        var devices = new List<StoredDeviceConfig>();
        var seenSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (DeviceIndexEntry entry in index)
        {
            string serial = NormalizeValue(entry.Serial);
            if (serial.Length == 0 || !seenSerials.Add(serial))
                continue;

            devices.Add(await ReadDeviceFilesAsync(
                    new DeviceIndexEntry
                    {
                        Serial = serial,
                        Name = NormalizeValue(entry.Name),
                        Type = NormalizeValue(entry.Type),
                        DataPath = GetRelativeDeviceDataPath(serial)
                    },
                    cancellationToken)
                .ConfigureAwait(false));
        }

        return Normalize(devices);
    }

    private async Task<StoredDeviceConfig> ReadDeviceFilesAsync(
        DeviceIndexEntry entry,
        CancellationToken cancellationToken)
    {
        string directory = GetDeviceDirectory(entry.Serial);
        DeviceRandomConfig randomConfig = await ReadJsonOrDefaultAsync<DeviceRandomConfig>(
            Path.Combine(directory, RuntimeDataPathConstants.RandomConfigFileName),
            cancellationToken).ConfigureAwait(false);

        DeviceChangeOptions changeOptions = await ReadJsonOrDefaultAsync<DeviceChangeOptions>(
            Path.Combine(directory, RuntimeDataPathConstants.ChangeOptionsConfigFileName),
            cancellationToken).ConfigureAwait(false);
        DeviceUpdateIntegrityConfig updateIntegrity =
            await ReadJsonOrDefaultAsync<DeviceUpdateIntegrityConfig>(
                Path.Combine(directory, RuntimeDataPathConstants.UpdateIntegrityConfigFileName),
                cancellationToken).ConfigureAwait(false);
        DeviceLocationConfig location = await ReadJsonOrDefaultAsync<DeviceLocationConfig>(
            Path.Combine(directory, RuntimeDataPathConstants.LocationConfigFileName),
            cancellationToken).ConfigureAwait(false);
        DeviceTimezoneConfig timezone = await ReadJsonOrDefaultAsync<DeviceTimezoneConfig>(
            Path.Combine(directory, RuntimeDataPathConstants.TimezoneConfigFileName),
            cancellationToken).ConfigureAwait(false);
        DeviceProxyConfig proxy = await ReadJsonOrDefaultAsync<DeviceProxyConfig>(
            Path.Combine(directory, RuntimeDataPathConstants.ProxyConfigFileName),
            cancellationToken).ConfigureAwait(false);

        return new StoredDeviceConfig
        {
            Serial = entry.Serial,
            Name = entry.Name,
            Type = entry.Type,
            CountryIso = randomConfig.CountryIso,
            CountryName = randomConfig.CountryName,
            Carrier = randomConfig.Carrier,
            CarrierMcc = randomConfig.CarrierMcc,
            CarrierMnc = randomConfig.CarrierMnc,
            Brand = randomConfig.Brand,
            AndroidVersion = randomConfig.AndroidVersion,
            ChangeSimEnabled = randomConfig.ChangeSimEnabled,
            UseIntegritySecurityPatch = randomConfig.UseIntegritySecurityPatch,
            ChangeOptions = changeOptions,
            UpdateIntegrityFromServer = updateIntegrity.FromServer,
            UpdateIntegrityFile = updateIntegrity.IntegrityFile,
            UpdateKeyboxFile = updateIntegrity.KeyboxFile,
            UpdateIntegrityEnabled = updateIntegrity.IntegrityEnabled,
            UpdateKeyboxEnabled = updateIntegrity.KeyboxEnabled,
            LocationMode = location.Mode,
            LocationLatitude = location.Latitude,
            LocationLongitude = location.Longitude,
            LocationCountryCode = location.CountryCode,
            LocationCityName = location.CityName,
            TimezoneMode = timezone.Mode,
            Timezone = timezone.Timezone,
            ProxyFullString = proxy.FullString,
            ProxyType = proxy.Type,
            ProxyChangeLocationByIp = proxy.ChangeLocationByIp,
            ProxyChangeTimezoneByIp = proxy.ChangeTimezoneByIp
        };
    }

    private async Task<T> ReadJsonOrDefaultAsync<T>(
        string path,
        CancellationToken cancellationToken)
        where T : new()
    {
        if (!File.Exists(path))
            return new T();

        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Failed to read device config file {FileName}.", Path.GetFileName(path));
            QuarantineFile(path, "device config");
            return new T();
        }
    }

    private async Task<IReadOnlyList<StoredDeviceConfig>> WriteAndReturnAsync(
        IEnumerable<StoredDeviceConfig> devices,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredDeviceConfig> normalized = Normalize(devices);
        await WriteStoreAsync(normalized, cancellationToken).ConfigureAwait(false);
        return normalized;
    }

    private async Task WriteStoreAsync(
        IEnumerable<StoredDeviceConfig> devices,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredDeviceConfig> normalized = Normalize(devices);
        foreach (StoredDeviceConfig device in normalized)
            await WriteDeviceFilesAsync(device, cancellationToken).ConfigureAwait(false);

        List<DeviceIndexEntry> index = normalized
            .Select(device => new DeviceIndexEntry
            {
                Serial = device.Serial,
                Name = device.Name,
                Type = device.Type,
                DataPath = GetRelativeDeviceDataPath(device.Serial)
            })
            .ToList();
        await WriteJsonAsync(_devicesPath, index, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteDeviceFilesAsync(
        StoredDeviceConfig device,
        CancellationToken cancellationToken)
    {
        string directory = GetDeviceDirectory(device.Serial);
        Directory.CreateDirectory(directory);

        await WriteJsonAsync(
            Path.Combine(directory, RuntimeDataPathConstants.RandomConfigFileName),
            new DeviceRandomConfig
            {
                CountryIso = device.CountryIso,
                CountryName = device.CountryName,
                Carrier = device.Carrier,
                CarrierMcc = device.CarrierMcc,
                CarrierMnc = device.CarrierMnc,
                Brand = device.Brand,
                AndroidVersion = device.AndroidVersion,
                ChangeSimEnabled = device.ChangeSimEnabled,
                UseIntegritySecurityPatch = device.UseIntegritySecurityPatch
            },
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(directory, RuntimeDataPathConstants.ChangeOptionsConfigFileName),
            DeviceChangeOptionsHelper.CreateNormalizedCopy(device.ChangeOptions),
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(directory, RuntimeDataPathConstants.UpdateIntegrityConfigFileName),
            new DeviceUpdateIntegrityConfig
            {
                FromServer = device.UpdateIntegrityFromServer,
                IntegrityFile = device.UpdateIntegrityFile,
                KeyboxFile = device.UpdateKeyboxFile,
                IntegrityEnabled = device.UpdateIntegrityEnabled,
                KeyboxEnabled = device.UpdateKeyboxEnabled
            },
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(directory, RuntimeDataPathConstants.LocationConfigFileName),
            new DeviceLocationConfig
            {
                Mode = device.LocationMode,
                Latitude = device.LocationLatitude,
                Longitude = device.LocationLongitude,
                CountryCode = device.LocationCountryCode,
                CityName = device.LocationCityName
            },
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(directory, RuntimeDataPathConstants.TimezoneConfigFileName),
            new DeviceTimezoneConfig
            {
                Mode = device.TimezoneMode,
                Timezone = device.Timezone
            },
            cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(
            Path.Combine(directory, RuntimeDataPathConstants.ProxyConfigFileName),
            new DeviceProxyConfig
            {
                FullString = device.ProxyFullString,
                Type = device.ProxyType,
                ChangeLocationByIp = device.ProxyChangeLocationByIp,
                ChangeTimezoneByIp = device.ProxyChangeTimezoneByIp
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        return AtomicFileWriter.WriteAllTextAsync(path, json, cancellationToken);
    }

    private string GetRelativeDeviceDataPath(string serial)
    {
        return Path.GetRelativePath(_applicationRootDirectory, GetDeviceDirectory(serial))
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    private string GetDeviceDirectory(string serial)
    {
        return Path.Combine(_deviceManagerDirectory, CreateSafeSerialDirectoryName(serial));
    }

    private static string CreateSafeSerialDirectoryName(string serial)
    {
        var builder = new StringBuilder(serial.Length);
        foreach (char character in serial)
        {
            bool isAsciiLetterOrDigit =
                character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9';
            if (isAsciiLetterOrDigit || character == '-')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('_');
                builder.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private void DeleteDeviceDirectory(string serial)
    {
        string directory = GetDeviceDirectory(serial);
        if (!Directory.Exists(directory))
            return;

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Removed device index entry but could not delete its data directory.");
        }
    }

    private void QuarantineFile(string path, string description)
    {
        if (!File.Exists(path))
            return;

        string quarantinePath = string.Concat(
            path,
            ".corrupt-",
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture),
            "-",
            Guid.NewGuid().ToString("N"));
        try
        {
            File.Move(path, quarantinePath);
            _logger.LogWarning("Moved corrupt {Description} to quarantine.", description);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Failed to quarantine corrupt {Description}.", description);
        }
    }

    private static IReadOnlyList<StoredDeviceConfig> Normalize(
        IEnumerable<StoredDeviceConfig>? devices)
    {
        var result = new List<StoredDeviceConfig>();
        var seenSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (StoredDeviceConfig device in devices ?? [])
        {
            string serial = NormalizeValue(device.Serial);
            if (serial.Length == 0 || !seenSerials.Add(serial))
                continue;

            result.Add(new StoredDeviceConfig
            {
                Serial = serial,
                Name = NormalizeValue(device.Name),
                Type = NormalizeValue(device.Type),
                CountryIso = NormalizeValue(device.CountryIso).ToLowerInvariant(),
                CountryName = NormalizeValue(device.CountryName),
                Carrier = NormalizeValue(device.Carrier),
                CarrierMcc = NormalizeValue(device.CarrierMcc),
                CarrierMnc = NormalizeValue(device.CarrierMnc),
                Brand = NormalizeValue(device.Brand),
                AndroidVersion = NormalizeValue(device.AndroidVersion),
                ChangeSimEnabled = device.ChangeSimEnabled,
                UseIntegritySecurityPatch = device.UseIntegritySecurityPatch,
                ChangeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(device.ChangeOptions),
                UpdateIntegrityFromServer = device.UpdateIntegrityFromServer,
                UpdateIntegrityFile = NormalizeValue(device.UpdateIntegrityFile),
                UpdateKeyboxFile = NormalizeValue(device.UpdateKeyboxFile),
                UpdateIntegrityEnabled = device.UpdateIntegrityEnabled,
                UpdateKeyboxEnabled = device.UpdateKeyboxEnabled,
                LocationMode = NormalizeValue(device.LocationMode),
                LocationLatitude = NormalizeValue(device.LocationLatitude),
                LocationLongitude = NormalizeValue(device.LocationLongitude),
                LocationCountryCode = NormalizeValue(device.LocationCountryCode),
                LocationCityName = NormalizeValue(device.LocationCityName),
                TimezoneMode = NormalizeValue(device.TimezoneMode),
                Timezone = NormalizeValue(device.Timezone),
                ProxyFullString = NormalizeValue(device.ProxyFullString),
                ProxyType = NormalizeValue(device.ProxyType),
                ProxyChangeLocationByIp = device.ProxyChangeLocationByIp,
                ProxyChangeTimezoneByIp = device.ProxyChangeTimezoneByIp
            });
        }

        return result;
    }

    private static string NormalizeValue(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
