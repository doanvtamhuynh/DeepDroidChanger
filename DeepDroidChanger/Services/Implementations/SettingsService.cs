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
                    RuntimeDataPathConstants.AppSettingsDirectoryName,
                    RuntimeDataPathConstants.AppSettingsFileName),
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
            settings.DeviceTableColumnRatios ??= new Dictionary<string, double>();

            var validKeys = DeviceTableColumnSettings.DefaultRatios.Keys.ToHashSet(StringComparer.Ordinal);
            foreach (var key in settings.DeviceTableColumnRatios.Keys.ToArray())
            {
                if (!validKeys.Contains(key) || settings.DeviceTableColumnRatios[key] <= 0)
                    settings.DeviceTableColumnRatios.Remove(key);
            }

            if (settings.DeviceTableColumnRatios.Count == 0)
                settings.DeviceTableColumnRatios = new Dictionary<string, double>(DeviceTableColumnSettings.DefaultRatios);

            settings.SelectedDeviceSerial ??= string.Empty;
        }
    }
}
