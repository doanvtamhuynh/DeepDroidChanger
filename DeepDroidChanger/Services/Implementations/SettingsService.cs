using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
using DeepDroidChanger.Helpers;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class SettingsService : ISettingsService
    {
        private readonly SemaphoreSlim _fileLock = new(1, 1);
        private readonly IThemeService _themeService;
        private readonly ILogger<SettingsService> _logger;
        private readonly string _settingsDirectory;
        private readonly string _settingsPath;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public SettingsService(IThemeService themeService, ILogger<SettingsService> logger)
            : this(
                Path.Combine(
                    AppContext.BaseDirectory,
                    AssetConstants.RuntimeData.AppSettingsDirectoryName,
                    AssetConstants.RuntimeData.AppSettingsFileName),
                themeService,
                logger)
        {
        }

        internal SettingsService(
            string settingsPath,
            IThemeService themeService,
            ILogger<SettingsService> logger)
        {
            _settingsPath = Path.GetFullPath(settingsPath);
            _settingsDirectory = Path.GetDirectoryName(_settingsPath)
                ?? throw new ArgumentException("Settings path must include a directory.", nameof(settingsPath));
            _themeService = themeService;
            _logger = logger;
        }

        public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_settingsDirectory);
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (!File.Exists(_settingsPath))
                {
                    var defaults = new AppSettings();
                    Normalize(defaults);
                    await WriteSettingsAsync(defaults, cancellationToken).ConfigureAwait(false);
                    return defaults;
                }

                var json = await File.ReadAllTextAsync(_settingsPath, cancellationToken).ConfigureAwait(false);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
                ApplyLegacySettings(json, settings);
                Normalize(settings);
                return settings;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Failed to load application settings.");
                QuarantineCorruptFile();
                var defaults = new AppSettings();
                Normalize(defaults);
                await WriteSettingsAsync(defaults, cancellationToken).ConfigureAwait(false);
                return defaults;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_settingsDirectory);
            await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                Normalize(settings);
                await WriteSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _fileLock.Release();
            }
        }

        private async Task WriteSettingsAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            Normalize(settings);
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            await AtomicFileWriter.WriteAllTextAsync(_settingsPath, json, cancellationToken).ConfigureAwait(false);
        }

        private void QuarantineCorruptFile()
        {
            if (!File.Exists(_settingsPath))
                return;

            string quarantinePath = string.Concat(
                _settingsPath,
                ".corrupt-",
                DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture),
                "-",
                Guid.NewGuid().ToString("N"));
            try
            {
                File.Move(_settingsPath, quarantinePath);
                _logger.LogWarning("Moved corrupt settings to quarantine.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "Failed to quarantine corrupt settings.");
            }
        }

        private void Normalize(AppSettings settings)
        {
            settings.Theme = _themeService.NormalizeTheme(settings.Theme);
            settings.DeviceTableColumnRatios = NormalizeColumnRatios(
                settings.DeviceTableColumnRatios,
                selectedRatio: 0.55,
                processRatio: 1.95);
            settings.SelectedSingleDeviceSerial =
                settings.SelectedSingleDeviceSerial?.Trim() ?? string.Empty;
            settings.SelectedMultipleDeviceSerials = (settings.SelectedMultipleDeviceSerials ?? [])
                .Where(serial => !string.IsNullOrWhiteSpace(serial))
                .Select(serial => serial.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void ApplyLegacySettings(string json, AppSettings settings)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty(nameof(AppSettings.DeviceTableColumnRatios), out _)
                && (root.TryGetProperty("SingleDeviceTableColumnRatios", out JsonElement legacyRatios)
                    || root.TryGetProperty("MultipleDeviceTableColumnRatios", out legacyRatios)))
            {
                settings.DeviceTableColumnRatios =
                    JsonSerializer.Deserialize<Dictionary<string, double>>(
                        legacyRatios.GetRawText(),
                        _jsonOptions) ?? [];
            }

            if (!root.TryGetProperty(nameof(AppSettings.SelectedSingleDeviceSerial), out _)
                && root.TryGetProperty("SelectedDeviceSerial", out JsonElement legacySerial)
                && legacySerial.ValueKind == JsonValueKind.String)
            {
                settings.SelectedSingleDeviceSerial = legacySerial.GetString() ?? string.Empty;
            }
        }

        private static Dictionary<string, double> NormalizeColumnRatios(
            Dictionary<string, double>? ratios,
            double selectedRatio,
            double processRatio)
        {
            var defaults = new Dictionary<string, double>
            {
                ["Index"] = 0.55,
                ["Selected"] = selectedRatio,
                ["Serial"] = 1.05,
                ["Name"] = 1.05,
                ["Type"] = 0.9,
                ["Active"] = 1.05,
                ["Status"] = 1.0,
                ["Process"] = processRatio
            };
            if (ratios == null)
                return defaults;

            foreach (string key in ratios.Keys.ToArray())
            {
                double ratio = ratios[key];
                if (!defaults.ContainsKey(key)
                    || !double.IsFinite(ratio)
                    || ratio <= 0)
                    ratios.Remove(key);
            }

            foreach ((string key, double ratio) in defaults)
                ratios.TryAdd(key, ratio);

            return ratios;
        }

    }
}
