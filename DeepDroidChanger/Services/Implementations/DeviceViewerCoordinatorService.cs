using System.Globalization;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class DeviceViewerCoordinatorService : IDeviceViewerCoordinatorService
{
    private const string DeviceSizeCommand = "shell wm size";
    private const double FallbackDeviceAspectRatio = 9.0 / 20.0;

    private readonly IAdbCommandService _adbCommandService;
    private readonly ILogger<DeviceViewerCoordinatorService> _logger;

    public DeviceViewerCoordinatorService(
        IAdbCommandService adbCommandService,
        ILogger<DeviceViewerCoordinatorService> logger)
    {
        _adbCommandService = adbCommandService;
        _logger = logger;
    }

    public async Task<double> QueryDeviceAspectRatioAsync(string serial, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _adbCommandService
                .RunAdbAsync(serial, DeviceSizeCommand, cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode == 0)
                return ParseDeviceAspectRatio(result.StandardOutput);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to query device size for {Serial}.", serial);
        }

        return FallbackDeviceAspectRatio;
    }

    internal static double ParseDeviceAspectRatio(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return FallbackDeviceAspectRatio;

        (int Width, int Height)? physical = null;
        (int Width, int Height)? overrideSize = null;

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = rawLine.IndexOf(':');
            if (separatorIndex < 0)
                continue;

            var label = rawLine[..separatorIndex].Trim();
            var dimensions = rawLine[(separatorIndex + 1)..].Trim();
            if (!TryParseDimensions(dimensions, out var size))
                continue;

            if (label.Equals("Override size", StringComparison.OrdinalIgnoreCase))
                overrideSize = size;
            else if (label.Equals("Physical size", StringComparison.OrdinalIgnoreCase))
                physical = size;
        }

        var selected = overrideSize ?? physical;
        return selected is { Height: > 0 }
            ? (double)selected.Value.Width / selected.Value.Height
            : FallbackDeviceAspectRatio;
    }

    private static bool TryParseDimensions(string value, out (int Width, int Height) dimensions)
    {
        dimensions = default;
        var parts = value.Split('x', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height) ||
            width <= 0 ||
            height <= 0)
        {
            return false;
        }

        dimensions = (width, height);
        return true;
    }
}
