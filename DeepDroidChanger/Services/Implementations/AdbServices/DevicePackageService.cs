using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed class DevicePackageService : IDevicePackageService
{
    private readonly IAdbCommandService _adb;

    public DevicePackageService(IAdbCommandService adb)
    {
        _adb = adb;
    }

    public async Task<IReadOnlyList<string>> GetInstalledPackagesAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        return await ListPackagesAsync(serial, "pm list packages", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetUserInstalledPackagesAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        return await ListPackagesAsync(serial, "pm list packages -3", cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> ListPackagesAsync(
        string serial,
        string command,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        CommandResult result = await _adb
            .RunAdbShellAsync(serial, command, cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Unable to list packages on device {serial}.");

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParsePackageName)
            .Where(packageName => packageName.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(packageName => packageName, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ParsePackageName(string value)
    {
        const string prefix = "package:";
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
            return string.Empty;

        string packageName = value[prefix.Length..].Trim();
        return packageName.EndsWith('_') ? string.Empty : packageName;
    }
}
