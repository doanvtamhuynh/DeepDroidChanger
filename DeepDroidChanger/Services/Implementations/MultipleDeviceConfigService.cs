using System.Globalization;
using System.IO;
using System.Text.Json;
using DeepDroidChanger.Constants;
using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class MultipleDeviceConfigService : IMultipleDeviceConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly ILogger<MultipleDeviceConfigService> _logger;
    private readonly string _directory;
    private readonly string _changeConfigPath;
    private readonly string _changeOptionsPath;
    private readonly string _proxyConfigPath;

    public MultipleDeviceConfigService(ILogger<MultipleDeviceConfigService> logger)
        : this(
            Path.Combine(
                AppContext.BaseDirectory,
                AssetConstants.RuntimeData.ChangeMultipleDevicesDirectoryName),
            logger)
    {
    }

    internal MultipleDeviceConfigService(
        string directory,
        ILogger<MultipleDeviceConfigService> logger)
    {
        _directory = Path.GetFullPath(directory);
        _changeConfigPath = Path.Combine(
            _directory,
            AssetConstants.RuntimeData.MultipleDeviceChangeConfigFileName);
        _changeOptionsPath = Path.Combine(
            _directory,
            AssetConstants.RuntimeData.ChangeOptionsConfigFileName);
        _proxyConfigPath = Path.Combine(
            _directory,
            AssetConstants.RuntimeData.MultipleDeviceProxyConfigFileName);
        _logger = logger;
    }

    public async Task<MultipleDeviceConfiguration> LoadAsync(
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            MultipleDeviceChangeConfig changeConfig =
                await ReadJsonOrDefaultAsync<MultipleDeviceChangeConfig>(
                        _changeConfigPath,
                        cancellationToken)
                    .ConfigureAwait(false);
            DeviceChangeOptions changeOptions =
                await ReadJsonOrDefaultAsync<DeviceChangeOptions>(
                        _changeOptionsPath,
                        cancellationToken)
                    .ConfigureAwait(false);
            MultipleDeviceConfiguration configuration = Normalize(
                new MultipleDeviceConfiguration
                {
                    ChangeConfig = changeConfig,
                    ChangeOptions = changeOptions
                });
            await WriteConfigurationAsync(configuration, cancellationToken).ConfigureAwait(false);
            return configuration;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(
        MultipleDeviceConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Directory.CreateDirectory(_directory);
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await WriteConfigurationAsync(
                    Normalize(configuration),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<MultipleDeviceProxyConfig> LoadProxyConfigAsync(
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            MultipleDeviceProxyConfig configuration =
                await ReadJsonOrDefaultAsync<MultipleDeviceProxyConfig>(
                        _proxyConfigPath,
                        cancellationToken)
                    .ConfigureAwait(false);
            return NormalizeProxyConfig(configuration);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveProxyConfigAsync(
        MultipleDeviceProxyConfig configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Directory.CreateDirectory(_directory);
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await WriteJsonAsync(
                    _proxyConfigPath,
                    NormalizeProxyConfig(configuration),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
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
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "Failed to read Multiple Device config file {FileName}.",
                Path.GetFileName(path));
            QuarantineFile(path);
            return new T();
        }
    }

    private async Task WriteConfigurationAsync(
        MultipleDeviceConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await WriteJsonAsync(
                _changeConfigPath,
                configuration.ChangeConfig,
                cancellationToken)
            .ConfigureAwait(false);
        await WriteJsonAsync(
                _changeOptionsPath,
                configuration.ChangeOptions,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        return AtomicFileWriter.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static MultipleDeviceConfiguration Normalize(
        MultipleDeviceConfiguration configuration)
    {
        MultipleDeviceChangeConfig source = configuration.ChangeConfig ?? new();
        return new MultipleDeviceConfiguration
        {
            ChangeConfig = new MultipleDeviceChangeConfig
            {
                Brand = NormalizeValue(source.Brand, "Random"),
                AndroidVersion = NormalizeValue(source.AndroidVersion, "Random"),
                Model = NormalizeValue(source.Model),
                CountryIso = NormalizeValue(source.CountryIso).ToLowerInvariant(),
                CountryName = NormalizeValue(source.CountryName),
                Carrier = NormalizeValue(source.Carrier),
                CarrierMcc = NormalizeValue(source.CarrierMcc),
                CarrierMnc = NormalizeValue(source.CarrierMnc),
                ChangeSimEnabled = source.ChangeSimEnabled,
                UseIntegritySecurityPatch = source.UseIntegritySecurityPatch
            },
            ChangeOptions = DeviceChangeOptionsHelper.CreateNormalizedCopy(
                configuration.ChangeOptions)
        };
    }

    private static MultipleDeviceProxyConfig NormalizeProxyConfig(
        MultipleDeviceProxyConfig configuration)
    {
        var proxies = new List<string>();
        foreach (string proxyText in configuration.Proxies ?? [])
        {
            if (ProxyEndpoint.TryParse(proxyText, out ProxyEndpoint? proxy))
                proxies.Add(proxy!.NormalizedText);
        }

        return new MultipleDeviceProxyConfig
        {
            Proxies = proxies,
            ProxyType = ProxyEndpoint.DefaultProxyType,
            ChangeLocationByIp = configuration.ChangeLocationByIp,
            ChangeTimezoneByIp = configuration.ChangeTimezoneByIp,
            AssignmentMode = Enum.IsDefined(configuration.AssignmentMode)
                ? configuration.AssignmentMode
                : ProxyAssignmentMode.OneToOne,
            RepeatCount = Math.Max(1, configuration.RepeatCount),
            RepeatPattern = Enum.IsDefined(configuration.RepeatPattern)
                ? configuration.RepeatPattern
                : ProxyRepeatPattern.Interleaved
        };
    }

    private static string NormalizeValue(string? value, string fallback = "")
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length == 0 ? fallback : normalized;
    }

    private void QuarantineFile(string path)
    {
        if (!File.Exists(path))
            return;

        string quarantinePath = string.Concat(
            path,
            ".corrupt-",
            DateTimeOffset.UtcNow.ToString(
                "yyyyMMddHHmmssfff",
                CultureInfo.InvariantCulture),
            "-",
            Guid.NewGuid().ToString("N"));
        try
        {
            File.Move(path, quarantinePath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "Failed to quarantine Multiple Device config file {FileName}.",
                Path.GetFileName(path));
        }
    }
}
