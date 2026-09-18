using FFmpeg.AutoGen;
using System.IO;
using ScrcpyNet;
using SharpAdbClient;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;

namespace DeepDroidChanger.ViewDevices.Runtime;

public sealed class ScrcpyNetClientFactory : IScrcpyNetClientFactory
{
    private static readonly object FfmpegRootGate = new();
    private static string? _configuredFfmpegRoot;

    private readonly IScrcpyNetDeviceLeaseCoordinator _leaseCoordinator;

    public ScrcpyNetClientFactory(
        ScrcpyNetRuntimeResolver runtimeResolver,
        IScrcpyNetDeviceLeaseCoordinator leaseCoordinator)
    {
        ArgumentNullException.ThrowIfNull(runtimeResolver);
        _leaseCoordinator = leaseCoordinator ?? throw new ArgumentNullException(nameof(leaseCoordinator));
        RuntimeResolver = runtimeResolver;
    }

    private ScrcpyNetRuntimeResolver RuntimeResolver { get; }

    public IScrcpyNetClient Create(DeviceData device, ViewDeviceLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(options);

        IDisposable? lease = _leaseCoordinator.Acquire(device.Serial);
        Scrcpy? client = null;
        try
        {
            ScrcpyNetRuntimeInfo runtime = RuntimeResolver.Resolve();
            ConfigureFfmpegRoot(runtime.RuntimeDirectory);

            client = new Scrcpy(device)
            {
                Bitrate = ParseBitrate(options.VideoBitRate),
                MaxSize = options.MaxSize,
                MaxFramerate = options.MaxFps,
                ScrcpyServerFile = runtime.ServerPath
            };
            ScrcpyNetClient wrapper = new(client, lease);
            lease = null;
            return wrapper;
        }
        catch
        {
            client?.Dispose();
            lease?.Dispose();
            throw;
        }
    }

    private static void ConfigureFfmpegRoot(string runtimeDirectory)
    {
        string canonicalRoot = Path.GetFullPath(runtimeDirectory);
        lock (FfmpegRootGate)
        {
            if (_configuredFfmpegRoot is null)
            {
                _configuredFfmpegRoot = canonicalRoot;
                ffmpeg.RootPath = canonicalRoot;
                return;
            }

            if (!string.Equals(
                    _configuredFfmpegRoot,
                    canonicalRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"ScrcpyNet FFmpeg root is already configured as '{_configuredFfmpegRoot}', " +
                    $"but runtime '{canonicalRoot}' was requested.");
            }

            ffmpeg.RootPath = _configuredFfmpegRoot;
        }
    }

    internal static long ParseBitrate(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        string normalized = value.Trim();
        long multiplier = 1;
        if (normalized.EndsWith("M", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1_000_000;
            normalized = normalized[..^1];
        }
        else if (normalized.EndsWith("K", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1_000;
            normalized = normalized[..^1];
        }

        if (!long.TryParse(normalized, out long number) || number <= 0)
            throw new FormatException($"The ScrcpyNet bitrate '{value}' is invalid.");

        return checked(number * multiplier);
    }
}
