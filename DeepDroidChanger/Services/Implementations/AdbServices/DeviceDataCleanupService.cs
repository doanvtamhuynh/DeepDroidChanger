using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class DeviceDataCleanupService : IDeviceDataCleanupService
{
    private static readonly HashSet<string> ProtectedPackages = new(StringComparer.Ordinal)
    {
        "com.android.shell"
    };

    internal static readonly string[] ProtectedDirectoryPaths =
    [
        "/data/misc/apexdata/com.android.wifi"
    ];

    internal static readonly string[] AccountDirectoryPatterns =
    [
        "/data/system_ce",
        "/data/system_de"
    ];

    internal static readonly string[] AccountFilePatterns =
    [
        "/data/system/users/*/accounts.db*",
        "/data/system/sync/accounts.xml",
        "/data/system/syncmanager.db*"
    ];

    internal static readonly string[] DefaultModeFilePatterns =
    [
        "/data/system/*.db*"
    ];

    internal static readonly string[] ResidualDirectoryPatterns =
    [
        "/data/anr",
        "/data/tombstones",
        "/data/backup",
        "/data/cache",
        "/data/dalvik-cache",
        "/data/drm",
        "/data/incremental",
        "/data/local",
        "/data/mediadrm",
        "/data/system/battery-history",
        "/data/system/battery-saver",
        "/data/system/battery-usage-stats",
        "/data/system/blobstore",
        "/data/system/appops",
        "/data/system/device_config",
        "/data/system/deviceidle",
        "/data/system/dropbox",
        "/data/system/graphicsstats",
        "/data/system/ifw",
        "/data/system/install_sessions*",
        "/data/system/integrity_rules*",
        "/data/system/integrity_staging*",
        "/data/system/job",
        "/data/system/netstats",
        "/data/system/package_cache",
        "/data/system/procexitstore",
        "/data/system/procstats",
        "/data/system/recoverablekeystore",
        "/data/system/sensorservice",
        "/data/system/slice",
        "/data/system/stats-service",
        "/data/system/storage",
        "/data/system/sync",
        "/data/system/syncmanager-log",
        "/data/system/usagestats",
        "/data/system/users/*/registered_services",
        "/data/apex",
        "/data/misc/apexdata/com.android.*",
        "/data/misc/apexdata/com.google.*",
        "/data/misc/apns",
        "/data/misc/bluedroid",
        "/data/misc/bluetooth",
        "/data/misc/bootstat",
        "/data/misc/carrierid",
        "/data/misc/credstore",
        "/data/misc/dhcp",
        "/data/misc/ethernet",
        "/data/misc/gatekeeper",
        "/data/misc/keychain",
        "/data/misc/keystore",
        "/data/misc/installd",
        "/data/misc/logd",
        "/data/misc/media",
        "/data/misc/net",
        "/data/misc/network_watchlist",
        "/data/misc/nfc",
        "/data/misc/perfetto-traces",
        "/data/misc/profiles",
        "/data/misc/profman",
        "/data/misc/radio",
        "/data/misc/recovery",
        "/data/misc/stats-active-metric",
        "/data/misc/stats-data",
        "/data/misc/stats-metadata",
        "/data/misc/stats-service",
        "/data/misc/trace",
        "/data/misc/update_engine",
        "/data/misc/update_engine_log",
        "/data/misc/user",
        "/data/misc/vpn",
        "/data/misc_ce",
        "/data/misc_de",
        "/data/per_boot",
        "/data/vendor",
        "/data/vendor_ce",
        "/data/vendor_de"
    ];

    internal static readonly string[] ResidualFilePatterns =
    [
        "/data/system/appops.xml*",
        "/data/system/cachequota.xml*",
        "/data/system/device_owner_2.xml*",
        "/data/system/device_policies.xml*",
        "/data/system/display-manager-state.xml*",
        "/data/system/diskstats_cache.json*",
        "/data/system/netpolicy.xml*",
        "/data/system/notification-log*",
        "/data/system/notification_policy.xml*",
        "/data/system/overlays.xml*",
        "/data/system/profiles.xml*",
        "/data/system/recoverablekeystore.db*",
        "/data/system/sensor_privacy.xml*",
        "/data/system/shortcut_service.xml*",
        "/data/system/users/*/app_idle_stats.xml*",
        "/data/system/users/*/appwidgets.xml*",
        "/data/system/users/*/package-restrictions.xml*",
        "/data/system/users/*/runtime-permissions.xml*",
        "/data/system/users/*/settings_config.xml*",
        "/data/system/users/*/settings_ssaid.xml*",
        "/data/system/users/*/wallpaper*",
        "/data/system/watchlist*"
    ];

    internal static readonly HashSet<string> GoogleDataPackages = new(StringComparer.Ordinal)
    {
        "com.android.chrome",
        "com.android.htmlviewer",
        "com.android.vending",
        "com.android.webview",
        "com.android.settings",
        "com.google.android.backuptransport",
        "com.google.android.ext.services",
        "com.google.android.ext.shared",
        "com.google.android.gm",
        "com.google.android.gms.location.history",
        "com.google.android.gsf",
        "com.google.android.gsf.login",
        "com.google.android.ims",
        "com.google.android.onetimeinitializer",
        "com.google.android.play.games",
        "com.google.android.syncadapters.contacts",
        "com.google.android.webview",
        "org.lineageos.jelly",
        "tugapower.codeaurora.browser"
    };

    internal static readonly HashSet<string> GoogleAccountPackages = new(StringComparer.Ordinal)
    {
        "com.google.android.gms",
        "com.google.android.gsf",
        "com.google.android.gsf.login"
    };

    private readonly IDevicePackageService _packageService;
    private readonly IAdbCommandService _adb;
    private readonly ILogger<DeviceDataCleanupService> _logger;

    public DeviceDataCleanupService(
        IDevicePackageService packageService,
        IAdbCommandService adb,
        ILogger<DeviceDataCleanupService> logger)
    {
        _packageService = packageService;
        _adb = adb;
        _logger = logger;
    }

    public async Task CleanAsync(
        string serial,
        DeviceChangeOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(options);

        bool clearAllPackages = options.UseDefaultMode || options.ClearAllPackages;
        bool clearGoogleAccounts = options.UseDefaultMode || options.ClearGoogleAccounts;
        IReadOnlyList<string> packagesToClear = await ResolvePackagesToClearAsync(
                serial,
                options,
                clearAllPackages,
                clearGoogleAccounts,
                cancellationToken)
            .ConfigureAwait(false);
        bool useRmRfForPackageCleanup = !options.UseDefaultMode
            && options.UseRmRfForPackageCleanup;
        string cleanupScript = CreateCleanupScript(
            packagesToClear,
            useRmRfForPackageCleanup,
            clearGoogleAccounts,
            options.UseDefaultMode);
        await RunRequiredScriptAsync(serial, cleanupScript, cancellationToken).ConfigureAwait(false);

        _logger.LogWarning(
            "Cleared device data on {Serial}. Package stores: {PackageCount}; clear all packages: {ClearAllPackages}; clear Google accounts and CE/DE state: {ClearGoogleAccounts}; residual directory patterns: {DirectoryPatternCount}; residual file patterns: {FilePatternCount}.",
            serial,
            packagesToClear.Count,
            clearAllPackages,
            clearGoogleAccounts,
            ResidualDirectoryPatterns.Length,
            ResidualFilePatterns.Length);
    }

    internal static bool IsGooglePackage(string packageName)
    {
        return packageName.StartsWith("com.google.", StringComparison.Ordinal)
            || GoogleDataPackages.Contains(packageName);
    }

    internal static string CreateCleanupScript(
        IReadOnlyCollection<string> packagesToClear,
        bool useRmRfForPackageCleanup,
        bool clearGoogleAccounts,
        bool useDefaultMode)
    {
        ArgumentNullException.ThrowIfNull(packagesToClear);
        List<string> commands =
        [
            "cmd activity force-stop-all >/dev/null 2>&1 || true",
            AdbCleanupCommandBuilder.CreatePackageCleanupCommand(
                packagesToClear,
                useRmRfForPackageCleanup)
        ];

        if (clearGoogleAccounts)
        {
            commands.Add(AdbCleanupCommandBuilder.CreatePreserveDirectoryCommand(
                string.Join(' ', AccountDirectoryPatterns)));
            commands.Add(AdbCleanupCommandBuilder.CreateRemoveFilesCommand(AccountFilePatterns));
        }

        if (useDefaultMode)
        {
            commands.Add(AdbCleanupCommandBuilder.CreateProtectedSystemFilesCommand(
                string.Join(' ', DefaultModeFilePatterns)));
        }

        commands.Add(AdbCleanupCommandBuilder.CreatePreserveDirectoryCommand(
            string.Join(' ', ResidualDirectoryPatterns),
            ProtectedDirectoryPaths));
        commands.Add(AdbCleanupCommandBuilder.CreateRemoveFilesCommand(ResidualFilePatterns));
        commands.Add("pm trim-caches 999G || exit $?");
        commands.Add("sync || exit $?");

        return string.Join(
            '\n',
            commands.Where(command => !string.IsNullOrWhiteSpace(command)));
    }

    private async Task<IReadOnlyList<string>> ResolvePackagesToClearAsync(
        string serial,
        DeviceChangeOptions options,
        bool clearAllPackages,
        bool clearGoogleAccounts,
        CancellationToken cancellationToken)
    {
        if (!DeviceChangeOptionsHelper.HasPackageCleanup(options))
            return [];

        IReadOnlyList<string> installedPackages = await _packageService
            .GetInstalledPackagesAsync(serial, cancellationToken)
            .ConfigureAwait(false);
        var installedSet = installedPackages.ToHashSet(StringComparer.Ordinal);
        var packagesToClear = new HashSet<string>(StringComparer.Ordinal);

        if (clearAllPackages)
            packagesToClear.UnionWith(installedPackages);
        else if (options.ClearSelectedPackages)
            packagesToClear.UnionWith(options.SelectedPackages ?? []);

        if (!clearAllPackages && options.ClearGooglePackages)
        {
            packagesToClear.UnionWith(GoogleDataPackages);
            packagesToClear.UnionWith(installedPackages.Where(IsGooglePackage));
        }

        if (clearGoogleAccounts)
        {
            packagesToClear.UnionWith(GoogleDataPackages);
            packagesToClear.UnionWith(GoogleAccountPackages);
        }

        packagesToClear.IntersectWith(installedSet);
        packagesToClear.ExceptWith(ProtectedPackages);
        return packagesToClear
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task RunRequiredScriptAsync(
        string serial,
        string script,
        CancellationToken cancellationToken)
    {
        CommandResult result = await _adb
            .RunAdbShellScriptAsync(serial, script, cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"Unable to execute the consolidated cleanup script on device {serial}.");
    }

}
