using FFmpeg.AutoGen;
using ScrcpyNet;
using SharpAdbClient;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;

namespace DeepDroidChanger.ViewDevices.Runtime;

public sealed class ScrcpyNetClientFactory(ScrcpyNetRuntimeResolver runtimeResolver) : IScrcpyNetClientFactory
{
    public IScrcpyNetClient Create(DeviceData device, ViewDeviceLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(options);

        ScrcpyNetRuntimeInfo runtime = runtimeResolver.Resolve();
        ffmpeg.RootPath = runtime.RuntimeDirectory;

        Scrcpy client = new(device)
        {
            Bitrate = ParseBitrate(options.VideoBitRate),
            MaxSize = options.MaxSize,
            MaxFramerate = options.MaxFps,
            ScrcpyServerFile = runtime.ServerPath
        };
        return new ScrcpyNetClient(client);
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
