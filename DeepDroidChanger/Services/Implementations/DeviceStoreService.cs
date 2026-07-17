using DeepDroidChanger.Models;
using DeepDroidChanger.Helpers;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class DeviceStoreService : IDeviceStoreService
    {
        private const int CurrentDocumentVersion = 4;
        private const string SettingsDirectoryName = "Settings";
        private const string DevicesFileName = "devices.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private readonly SemaphoreSlim _fileLock = new(1, 1);
        private readonly ILogger<DeviceStoreService> _logger;
        private readonly string _settingsDirectory;
        private readonly string _devicesPath;

        public DeviceStoreService(ILogger<DeviceStoreService> logger)
            : this(Path.Combine(AppContext.BaseDirectory, SettingsDirectoryName, DevicesFileName), logger)
        {
        }

        internal DeviceStoreService(string devicesPath, ILogger<DeviceStoreService> logger)
        {
            _devicesPath = Path.GetFullPath(devicesPath);
            _settingsDirectory = Path.GetDirectoryName(_devicesPath)
                ?? throw new ArgumentException("Device store path must include a directory.", nameof(devicesPath));
            _logger = logger;
        }

        public async Task<IReadOnlyList<StoredDeviceConfig>> LoadAsync(CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_settingsDirectory);
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (!File.Exists(_devicesPath))
                    return await WriteAndReturnAsync(Array.Empty<StoredDeviceConfig>(), cancellationToken).ConfigureAwait(false);

                var json = await File.ReadAllTextAsync(_devicesPath, cancellationToken).ConfigureAwait(false);
                var document = JsonSerializer.Deserialize<DeviceStoreDocument>(json, JsonOptions) ?? new DeviceStoreDocument();
                var devices = Normalize(
                    document.Devices,
                    clearLegacyDeviceProfile: document.Version < 2);
                await WriteDocumentAsync(devices, cancellationToken).ConfigureAwait(false);
                return devices;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Failed to load stored devices.");
                QuarantineCorruptFile();
                return await WriteAndReturnAsync(Array.Empty<StoredDeviceConfig>(), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task SaveAsync(IEnumerable<StoredDeviceConfig> devices, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_settingsDirectory);
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await WriteDocumentAsync(Normalize(devices), cancellationToken).ConfigureAwait(false);
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
            Directory.CreateDirectory(_settingsDirectory);
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                List<StoredDeviceConfig> devices = await ReadCurrentDevicesAsync(cancellationToken).ConfigureAwait(false);
                StoredDeviceConfig? target = devices.FirstOrDefault(device =>
                    string.Equals(device.Serial, serial, StringComparison.OrdinalIgnoreCase));
                if (target == null)
                    return false;

                update(target);
                await WriteDocumentAsync(devices, cancellationToken).ConfigureAwait(false);
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
            Directory.CreateDirectory(_settingsDirectory);
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                List<StoredDeviceConfig> current = await ReadCurrentDevicesAsync(cancellationToken).ConfigureAwait(false);
                DeviceRowFactory.MergeSelectedDevices(current, devices);
                IReadOnlyList<StoredDeviceConfig> normalized = Normalize(current);
                await WriteDocumentAsync(normalized, cancellationToken).ConfigureAwait(false);
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
            Directory.CreateDirectory(_settingsDirectory);
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                List<StoredDeviceConfig> current = await ReadCurrentDevicesAsync(cancellationToken).ConfigureAwait(false);
                bool removed = current.RemoveAll(device =>
                    string.Equals(device.Serial, serial, StringComparison.OrdinalIgnoreCase)) > 0;
                if (removed)
                    await WriteDocumentAsync(current, cancellationToken).ConfigureAwait(false);

                return removed;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        private async Task<List<StoredDeviceConfig>> ReadCurrentDevicesAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_devicesPath))
                return new List<StoredDeviceConfig>();

            try
            {
                string json = await File.ReadAllTextAsync(_devicesPath, cancellationToken).ConfigureAwait(false);
                DeviceStoreDocument document = JsonSerializer.Deserialize<DeviceStoreDocument>(json, JsonOptions)
                    ?? new DeviceStoreDocument();
                return Normalize(document.Devices).ToList();
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                _logger.LogError(exception, "Failed to read stored devices for a mutation.");
                QuarantineCorruptFile();
                await WriteDocumentAsync(Array.Empty<StoredDeviceConfig>(), cancellationToken).ConfigureAwait(false);
                return new List<StoredDeviceConfig>();
            }
        }

        private async Task<IReadOnlyList<StoredDeviceConfig>> WriteAndReturnAsync(
            IEnumerable<StoredDeviceConfig> devices,
            CancellationToken cancellationToken)
        {
            var normalizedDevices = Normalize(devices);
            await WriteDocumentAsync(normalizedDevices, cancellationToken).ConfigureAwait(false);
            return normalizedDevices;
        }

        private async Task WriteDocumentAsync(IEnumerable<StoredDeviceConfig> devices, CancellationToken cancellationToken)
        {
            var document = new DeviceStoreDocument
            {
                Version = CurrentDocumentVersion,
                Devices = Normalize(devices).ToList()
            };

            var json = JsonSerializer.Serialize(document, JsonOptions);
            await AtomicFileWriter.WriteAllTextAsync(_devicesPath, json, cancellationToken).ConfigureAwait(false);
        }

        private void QuarantineCorruptFile()
        {
            if (!File.Exists(_devicesPath))
                return;

            string quarantinePath = string.Concat(
                _devicesPath,
                ".corrupt-",
                DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture),
                "-",
                Guid.NewGuid().ToString("N"));
            try
            {
                File.Move(_devicesPath, quarantinePath);
                _logger.LogWarning("Moved corrupt device store to quarantine.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "Failed to quarantine corrupt device store.");
            }
        }

        private static IReadOnlyList<StoredDeviceConfig> Normalize(
            IEnumerable<StoredDeviceConfig>? devices,
            bool clearLegacyDeviceProfile = false)
        {
            var result = new List<StoredDeviceConfig>();
            var seenSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var device in devices ?? Array.Empty<StoredDeviceConfig>())
            {
                var serial = NormalizeValue(device.Serial);
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
                    Brand = clearLegacyDeviceProfile ? string.Empty : NormalizeValue(device.Brand),
                    AndroidVersion = clearLegacyDeviceProfile ? string.Empty : NormalizeValue(device.AndroidVersion),
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
}
