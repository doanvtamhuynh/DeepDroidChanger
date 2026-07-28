using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class DeviceDataCleanupService : IDeviceDataCleanupService
{
    internal enum CleanupTargetKind
    {
        FilePattern,
        DirectoryContents
    }

    internal readonly record struct CleanupTarget(
        string Path,
        CleanupTargetKind Kind);

    internal static readonly TimeSpan CleanupCommandTimeout = TimeSpan.FromSeconds(60);

    internal const string SsaidFilePattern = "/data/system/users/0/settings_ssaid.xml*";
    internal const string DropBoxDirectoryPath = "/data/system/dropbox";

    internal static readonly string[] DeepWipePackagePathTemplates =
    [
        "/data/user/0/{package}",
        "/data/user_de/0/{package}",
        "/data/media/0/Android/data/{package}",
        "/data/media/0/Android/media/{package}",
        "/data/misc/profiles/cur/0/{package}",
        "/data/misc/profiles/ref/{package}"
    ];

    internal static readonly string[] IdentityDirectoryPaths =
    [
        "/data/misc/bluedroid",
        "/data/misc/bluetooth",
        "/data/misc/dhcp",
        "/data/misc/net",
        "/data/misc/network_watchlist",
        "/data/misc/radio",
        "/data/misc/carrierid",
        "/data/misc/apns",
        "/data/system/netstats",
        "/data/system/procexitstore",
        "/data/system/procstats",
        "/data/system/graphicsstats",
        "/data/system/usagestats",
        "/data/system_ce/0/usagestats",
        "/data/anr",
        "/data/tombstones"
    ];

    internal static readonly string[] PreservedWifiDataRoots =
    [
        "/data/misc/wifi",
        "/data/misc/apexdata/com.android.wifi",
        "/data/misc_ce/0/apexdata/com.android.wifi",
        "/data/misc_de/0/apexdata/com.android.wifi"
    ];

    internal static readonly CleanupTarget[] AccountCleanupTargets =
    [
        new("/data/system_ce/0/accounts_ce.db*", CleanupTargetKind.FilePattern),
        new("/data/system_de/0/accounts_de.db*", CleanupTargetKind.FilePattern),
        new("/data/system/users/0/accounts.db*", CleanupTargetKind.FilePattern),
        new("/data/system/syncmanager.db*", CleanupTargetKind.FilePattern),
        new("/data/system/sync", CleanupTargetKind.DirectoryContents),
        new("/data/system/users/0/registered_services", CleanupTargetKind.DirectoryContents)
    ];

    private static readonly HashSet<string> ProtectedPackages = new(StringComparer.Ordinal)
    {
        "android",
        "com.android.shell",
        "com.android.wifi"
    };

    private static readonly HashSet<string> ProtectedRemovalRoots = new(StringComparer.Ordinal)
    {
        "/data",
        "/data/apex",
        "/data/app",
        "/data/local/tmp",
        "/data/misc",
        "/data/misc_ce",
        "/data/misc_de",
        "/data/property",
        "/data/system",
        "/data/system_ce",
        "/data/system_de",
        "/data/vendor",
        "/data/vendor_ce",
        "/data/vendor_de"
    };

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

    public Task CleanAsync(
        string serial,
        DeviceChangeOptions options,
        CancellationToken cancellationToken)
    {
        return CleanAsync(
            serial,
            options,
            resetSharedIdentityState: true,
            preserveSsaid: false,
            cancellationToken);
    }

    public Task CleanPreservingSsaidAsync(
        string serial,
        DeviceChangeOptions options,
        CancellationToken cancellationToken)
    {
        return CleanAsync(
            serial,
            options,
            resetSharedIdentityState: false,
            preserveSsaid: true,
            cancellationToken);
    }

    public async Task CleanPostRebootAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        string[] commands =
        [
            CreateDeleteDirectoryContentsCommand(DropBoxDirectoryPath),
            "sync"
        ];
        await RunCleanupCommandsAsync(serial, commands, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Deleted post-reboot DropBox files while preserving its directory on {Serial}.", serial);
    }

    public async Task DeleteSsaidAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        string[] commands =
        [
            CreateRemoveFileCommand(SsaidFilePattern),
            "sync"
        ];
        await RunCleanupCommandsAsync(serial, commands, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Deleted stored SSAID files on {Serial} so Android can regenerate them.", serial);
    }

    internal static bool IsGooglePackage(string packageName)
    {
        return packageName.StartsWith("com.google.", StringComparison.Ordinal)
            || GoogleDataPackages.Contains(packageName);
    }

    internal static string CreateRemoveFileCommand(string pattern)
    {
        ValidateDataPathPattern(pattern, allowWildcards: true);
        if (IsProtectedRemovalPath(pattern))
            throw new ArgumentException("Cleanup target is a protected data path.", nameof(pattern));

        int wildcardIndex = pattern.IndexOfAny(['*', '?', '[', ']']);
        if (wildcardIndex >= 0)
        {
            int fileNameStart = pattern.LastIndexOf('/') + 1;
            bool hasOneTrailingAsterisk = wildcardIndex > fileNameStart
                && pattern[wildcardIndex] == '*'
                && wildcardIndex == pattern.Length - 1
                && pattern.LastIndexOf('*') == wildcardIndex;
            if (!hasOneTrailingAsterisk)
            {
                throw new ArgumentException(
                    "File cleanup allows only one trailing filename wildcard.",
                    nameof(pattern));
            }
        }

        return $"rm -f {pattern}";
    }

    internal static string CreateDeleteDirectoryContentsCommand(string path)
    {
        ValidateDataPathPattern(path, allowWildcards: false);
        if (IsProtectedRemovalPath(path))
            throw new ArgumentException("Cleanup target is a protected data path.", nameof(path));

        return $"find '{path}' -mindepth 1 -not -type d -delete";
    }

    internal static IReadOnlyList<string> CreatePackageCleanupCommands(
        IEnumerable<string> packageNames,
        bool useDeepPackageWipe)
    {
        string[] packages = packageNames
            .Select(NormalizePackageName)
            .Distinct(StringComparer.Ordinal)
            .Where(packageName => !ProtectedPackages.Contains(packageName))
            .OrderBy(packageName => packageName, StringComparer.Ordinal)
            .ToArray();
        var commands = new List<string>();
        foreach (string packageName in packages)
        {
            commands.Add($"am force-stop \"{packageName}\" >/dev/null 2>&1");
            commands.Add($"pm clear --user 0 \"{packageName}\" >/dev/null 2>&1");

            if (!useDeepPackageWipe)
                continue;

            foreach (string pathTemplate in DeepWipePackagePathTemplates)
            {
                string path = pathTemplate.Replace(
                    "{package}",
                    packageName,
                    StringComparison.Ordinal);
                commands.Add(CreateDeleteDirectoryContentsCommand(path));
            }
        }

        return commands;
    }

    internal static IReadOnlyList<string> CreateCleanupCommands(
        IReadOnlyCollection<string> packagesToClear,
        bool useDeepPackageWipe,
        bool clearGoogleAccounts,
        bool preserveSsaid = false,
        bool clearAllPackages = false,
        bool resetSharedIdentityState = true)
    {
        ArgumentNullException.ThrowIfNull(packagesToClear);

        var commands = new List<string>();

        if (clearAllPackages)
            commands.Add("cmd activity force-stop-all >/dev/null 2>&1");

        commands.AddRange(CreatePackageCleanupCommands(
            packagesToClear,
            useDeepPackageWipe));

        if (clearGoogleAccounts)
            AddAccountCleanupCommands(commands);

        if (resetSharedIdentityState)
            AddIdentityCleanupCommands(commands, preserveSsaid);

        if (clearAllPackages)
        {
            commands.Add("pm reset-permissions");
            commands.Add("cmd appops reset --user 0");
            commands.Add("pm trim-caches 999G");
        }

        commands.Add("sync");
        return commands;
    }

    private async Task CleanAsync(
        string serial,
        DeviceChangeOptions options,
        bool resetSharedIdentityState,
        bool preserveSsaid,
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
        bool useDeepPackageWipe = !options.UseDefaultMode
            && options.UseRmRfForPackageCleanup;
        IReadOnlyList<string> cleanupCommands = CreateCleanupCommands(
            packagesToClear,
            useDeepPackageWipe,
            clearGoogleAccounts,
            preserveSsaid,
            clearAllPackages,
            resetSharedIdentityState);
        await RunCleanupCommandsAsync(serial, cleanupCommands, cancellationToken).ConfigureAwait(false);

        _logger.LogWarning(
            "Cleared device data on {Serial}. Packages: {PackageCount}; deep package file cleanup: {DeepPackageFileCleanup}; clear all packages: {ClearAllPackages}; clear Google accounts: {ClearGoogleAccounts}; reset shared identity state: {ResetSharedIdentityState}; preserve SSAID: {PreserveSsaid}.",
            serial,
            packagesToClear.Count,
            useDeepPackageWipe,
            clearAllPackages,
            clearGoogleAccounts,
            resetSharedIdentityState,
            preserveSsaid);
    }

    private static void AddAccountCleanupCommands(List<string> commands)
    {
        foreach (CleanupTarget target in AccountCleanupTargets)
            AddCleanupTargetCommand(commands, target.Path, target.Kind);
    }

    private static void AddIdentityCleanupCommands(
        List<string> commands,
        bool preserveSsaid)
    {
        foreach (string path in IdentityDirectoryPaths)
            commands.Add(CreateDeleteDirectoryContentsCommand(path));
        if (!preserveSsaid)
            commands.Add(CreateRemoveFileCommand(SsaidFilePattern));
    }

    private static void AddCleanupTargetCommand(
        List<string> commands,
        string path,
        CleanupTargetKind kind)
    {
        commands.Add(kind switch
        {
            CleanupTargetKind.FilePattern =>
                CreateRemoveFileCommand(path),
            CleanupTargetKind.DirectoryContents =>
                CreateDeleteDirectoryContentsCommand(path),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        });
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
        var packagesToClear = clearAllPackages
            ? installedSet
            : new HashSet<string>(StringComparer.Ordinal);

        if (!clearAllPackages && options.ClearSelectedPackages)
            packagesToClear.UnionWith(options.SelectedPackages ?? []);

        if (!clearAllPackages && options.ClearGooglePackages)
        {
            packagesToClear.UnionWith(GoogleDataPackages);
            packagesToClear.UnionWith(installedPackages.Where(IsGooglePackage));
        }

        if (!clearAllPackages && clearGoogleAccounts)
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

    private async Task RunCleanupCommandsAsync(
        string serial,
        IReadOnlyList<string> commands,
        CancellationToken cancellationToken)
    {
        foreach (string command in commands)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(CleanupCommandTimeout);
            try
            {
                _ = await _adb
                    .RunAdbShellScriptAsync(serial, command, timeoutSource.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Match AccountCreator's ignored "PROCESS TIMEOUT" result and continue.
            }
        }
    }

    private static string NormalizePackageName(string packageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        string normalized = packageName.Trim();
        if (!normalized.All(character => char.IsLetterOrDigit(character) || character is '.' or '_'))
            throw new ArgumentException("Package name contains unsupported characters.", nameof(packageName));

        return normalized;
    }

    private static bool IsProtectedRemovalPath(string path)
    {
        string normalized = path.TrimEnd('/');
        return ProtectedRemovalRoots.Contains(normalized)
            || PreservedWifiDataRoots.Any(root =>
                string.Equals(normalized, root, StringComparison.Ordinal)
                || normalized.StartsWith($"{root}/", StringComparison.Ordinal));
    }

    private static void ValidateDataPathPattern(string pattern, bool allowWildcards)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        if (!pattern.StartsWith("/data/", StringComparison.Ordinal))
            throw new ArgumentException("Cleanup target must stay under /data.", nameof(pattern));
        if (pattern.Any(char.IsWhiteSpace) || pattern.Contains('\'') || pattern.Contains('"'))
            throw new ArgumentException("Only one safe path pattern is allowed.", nameof(pattern));
        if (!allowWildcards && pattern.IndexOfAny(['*', '?', '[', ']']) >= 0)
            throw new ArgumentException("Wildcards are not allowed for this cleanup command.", nameof(pattern));
        if (!pattern.All(character =>
                char.IsLetterOrDigit(character)
                || character is '/' or '.' or '_' or '-' or '*' or '?' or '[' or ']'))
        {
            throw new ArgumentException("Cleanup target contains unsupported characters.", nameof(pattern));
        }
    }
}
