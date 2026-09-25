using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class DeviceBackupService : IDeviceBackupService
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(25);
    private static readonly Regex ValidPackageName = new(
        @"^[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VersionCodePattern = new(
        @"\bversionCode=(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TargetSdkPattern = new(
        @"\btargetSdk=(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SourceUidPattern = new(
        @"\buserId=(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SigningDigestLabelPattern = new(
        @"(?im)\b(?:signingCertificateSha256|signing[ \t_-]*certificate[ \t_-]*sha[ \t_-]*256|signer(?:'s)?[ \t_-]*(?:certificate|cert)(?:'s)?[ \t_-]*sha[ \t_-]*256(?:[ \t_-]*digest)?|certificate[ \t_-]*sha[ \t_-]*256(?:[ \t_-]*digest)?|sha[ \t_-]*256[ \t_-]*(?:signing|certificate)[ \t_-]*digest)\b[ \t]*[:=][ \t]*(?<value>[^\r\n,;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly SettingDescriptor[] ManagedSettings =
    [
        new(DeviceSettingsInfoConstants.GlobalNamespace, DeviceSettingsInfoConstants.DeviceName),
        new(DeviceSettingsInfoConstants.SecureNamespace, DeviceSettingsInfoConstants.BluetoothName),
        new(DeviceSettingsInfoConstants.GlobalNamespace, DeviceSettingsInfoConstants.WifiP2pDeviceName),
        new(DeviceSettingsInfoConstants.SystemNamespace, DeviceSettingsInfoConstants.ScreenTimeout),
        new(DeviceSettingsInfoConstants.SecureNamespace, DeviceSettingsInfoConstants.BluetoothAddress),
        new(DeviceSettingsInfoConstants.SecureNamespace, DeviceSettingsInfoConstants.BluetoothAddressValid),
        new(DeviceSettingsInfoConstants.GlobalNamespace, DeviceSettingsInfoConstants.RandomMac),
        new(DeviceSettingsInfoConstants.GlobalNamespace, "auto_time_zone"),
        new(DeviceSettingsInfoConstants.SystemNamespace, "time_12_24")
    ];

    private static readonly string[] GooglePackages =
    [
        "com.google.android.gms",
        "com.google.android.gsf",
        "com.google.android.gsf.login",
        "com.android.vending"
    ];

    private static readonly AccountPathDescriptor[] AccountPaths =
    [
        new("/data/system_ce/0", "accounts_ce.db*", "account/system_ce/accounts_ce.tar", true),
        new("/data/system_de/0", "accounts_de.db*", "account/system_de/accounts_de.tar", true),
        new("/data/system/users/0", "accounts.db*", "account/users/accounts.tar", true),
        new("/data/system", "syncmanager.db*", "account/syncmanager/syncmanager.tar", true),
        new("/data/system", "sync", "account/sync/sync.tar", false),
        new("/data/system/users/0", "registered_services", "account/users/registered_services.tar", false)
    ];

    private readonly IAdbCommandService _adb;
    private readonly IAdbRootAccessService _rootAccessService;
    private readonly IDevicePackageService _devicePackageService;
    private readonly ILogger<DeviceBackupService> _logger;

    public DeviceBackupService(
        IAdbCommandService adb,
        IAdbRootAccessService rootAccessService,
        IDevicePackageService devicePackageService,
        ILogger<DeviceBackupService> logger)
    {
        _adb = adb;
        _rootAccessService = rootAccessService;
        _devicePackageService = devicePackageService;
        _logger = logger;
    }

    public async Task<DeviceBackupResult> BackupAsync(
        string serial,
        DeviceBackupOptions options,
        IProgress<DeviceBackupProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.HasSelectedComponent)
            throw new ArgumentException("At least one backup component must be selected.", nameof(options));

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new(DeviceBackupStage.Preparing));

        string destinationDirectory = string.IsNullOrWhiteSpace(options.DestinationDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "Backup")
            : Path.GetFullPath(options.DestinationDirectory.Trim());
        Directory.CreateDirectory(destinationDirectory);

        DateTime createdAtUtc = DateTime.UtcNow;
        string sessionId = Guid.NewGuid().ToString("N");
        string localTemporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "DeepDroidChanger",
            "Backup",
            sessionId);
        Directory.CreateDirectory(localTemporaryDirectory);

        string? partialPath = null;
        string? finalPath = null;
        try
        {
            using (FileStream output = CreatePartialArchive(
                       destinationDirectory,
                       serial,
                       createdAtUtc,
                       out finalPath,
                       out partialPath))
            {
                var writer = new ArchiveWriter(new ZipArchive(
                    output,
                    ZipArchiveMode.Create,
                    leaveOpen: true));
                ArchiveWriterSnapshot? snapshot = null;
                try
                {
                    var context = new BackupExecutionContext();
                    string remoteSessionPath = $"/data/local/tmp/deepdroid-backup/{sessionId}";

                    await _rootAccessService.ExecuteAsRootAsync(
                            serial,
                            rootToken => CollectBackupAsync(
                                serial,
                                options,
                                writer,
                                context,
                                remoteSessionPath,
                                localTemporaryDirectory,
                                progress,
                                rootToken),
                            cancellationToken)
                        .ConfigureAwait(false);

                    progress?.Report(new(DeviceBackupStage.Finalizing));
                    cancellationToken.ThrowIfCancellationRequested();

                    var manifest = new BackupArchiveManifest(
                        FormatVersion: 1,
                        CreatedAtUtc: createdAtUtc,
                        SourceSerial: serial,
                        SourceDeviceRole: context.SourceDeviceRole,
                        AndroidSdkVersion: context.AndroidSdkVersion,
                        SelectedComponents: new BackupSelectedComponents(
                            options.IncludeDeviceProperties,
                            options.IncludeManagedSystemSettings,
                            options.IncludeUserAppData,
                            options.IncludeKeybox,
                            options.IncludeSsaid,
                            options.IncludeGoogleAppData,
                            options.IncludeGoogleAccountState),
                        Keybox: context.KeyboxState,
                        PropertiesStatus: context.PropertiesStatus,
                        PropertyStatuses: context.PropertyStatuses,
                        SettingsStatus: context.SettingsStatus,
                        SettingsStatuses: context.SettingsStatuses,
                        Packages: context.Packages,
                        OptionalComponents: context.OptionalComponents,
                        Warnings: context.Warnings,
                        Checksums: writer.Checksums);
                    await writer.AddJsonEntryAsync(
                            "manifest.json",
                            manifest,
                            includeChecksum: false,
                            cancellationToken)
                        .ConfigureAwait(false);
                    snapshot = new ArchiveWriterSnapshot(
                        new Dictionary<string, string>(writer.Checksums, StringComparer.Ordinal));
                }
                finally
                {
                    writer.Dispose();
                }

                output.Flush(flushToDisk: true);
                output.Dispose();
                DeleteTemporaryDirectory(localTemporaryDirectory);
                await ValidateArchiveAsync(
                        partialPath!,
                        snapshot ?? throw new InvalidDataException("The backup manifest could not be finalized."),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partialPath!, finalPath!);
            progress?.Report(new(DeviceBackupStage.Completed));
            _logger.LogInformation(
                "Device backup completed for {Serial} as {ArchiveName}.",
                serial,
                Path.GetFileName(finalPath));
            return new DeviceBackupResult(finalPath!, createdAtUtc);
        }
        catch
        {
            TryDeleteFile(partialPath);
            TryDeleteTemporaryDirectory(localTemporaryDirectory);
            throw;
        }
    }

    private async Task CollectBackupAsync(
        string serial,
        DeviceBackupOptions options,
        ArchiveWriter writer,
        BackupExecutionContext context,
        string remoteSessionPath,
        string localTemporaryDirectory,
        IProgress<DeviceBackupProgress>? progress,
        CancellationToken cancellationToken)
    {
        bool debugGateTouched = false;
        Exception? backupFailure = null;
        try
        {
            await RunRequiredShellAsync(
                    serial,
                    $"mkdir -p {QuoteShellValue(remoteSessionPath)}",
                    "create device backup temporary directory",
                    cancellationToken)
                .ConfigureAwait(false);

            context.SourceDeviceRole = await ReadSourceDeviceRoleAsync(
                    serial,
                    context.Warnings,
                    cancellationToken)
                .ConfigureAwait(false);
            context.AndroidSdkVersion = await ReadAndroidSdkVersionAsync(
                    serial,
                    context.Warnings,
                    cancellationToken)
                .ConfigureAwait(false);

            bool captureProperties = options.IncludeDeviceProperties || options.IncludeKeybox;
            if (captureProperties)
            {
                if (options.IncludeDeviceProperties)
                    progress?.Report(new(DeviceBackupStage.ReadingProperties));
                context.PropertiesStatus = options.IncludeDeviceProperties
                    ? "backed_up"
                    : "keybox_property_only";
                var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
                string missingSentinel = $"__DDC_BACKUP_MISSING_{Guid.NewGuid():N}__";
                IEnumerable<string> propertyNames = options.IncludeDeviceProperties
                    ? ManagedDevicePropertyCatalog.Properties
                    : [PropertyConstants.Keybox.Enabled];

                bool propertyGateReady = true;
                debugGateTouched = true;
                try
                {
                    await _adb.SetPropertyAsync(
                            serial,
                            PropertyConstants.Debug.Enabled,
                            "true",
                            cancellationToken)
                        .ConfigureAwait(false);
                    string debugValue = await ReadPropertyValueAsync(
                            serial,
                            PropertyConstants.Debug.Enabled,
                            missingSentinel,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!string.Equals(debugValue, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "The persisted property debug gate could not be enabled and verified.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (!options.IncludeDeviceProperties && options.IncludeKeybox)
                {
                    propertyGateReady = false;
                    context.KeyboxEnabledPropertyPresent = null;
                    context.KeyboxEnabledPropertyState = "read_failed";
                    context.Warnings.Add("keybox_enabled_property_read_failed");
                    _logger.LogWarning(
                        "The Keybox enabled property could not be read on {Serial} ({ExceptionType}).",
                        serial,
                        exception.GetType().Name);
                }

                if (propertyGateReady)
                {
                    foreach (string propertyName in propertyNames)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string value;
                        try
                        {
                            value = await ReadPropertyValueAsync(
                                    serial,
                                    propertyName,
                                    missingSentinel,
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception exception) when (
                            options.IncludeKeybox
                            && string.Equals(
                                propertyName,
                                PropertyConstants.Keybox.Enabled,
                                StringComparison.Ordinal))
                        {
                            context.KeyboxEnabledPropertyPresent = null;
                            context.KeyboxEnabledPropertyState = "read_failed";
                            context.PropertyStatuses.Add(new(
                                propertyName,
                                "read_failed",
                                "property_read_failed"));
                            context.Warnings.Add("keybox_enabled_property_read_failed");
                            _logger.LogWarning(
                                "The Keybox enabled property could not be read on {Serial} ({ExceptionType}).",
                                serial,
                                exception.GetType().Name);
                            continue;
                        }

                        bool isKeyboxProperty = string.Equals(
                            propertyName,
                            PropertyConstants.Keybox.Enabled,
                            StringComparison.Ordinal);
                        if (value == missingSentinel)
                        {
                            context.PropertyStatuses.Add(new(propertyName, "skipped_missing", "property_not_present"));
                            if (options.IncludeKeybox && isKeyboxProperty)
                            {
                                context.KeyboxEnabledPropertyPresent = false;
                                context.KeyboxEnabledPropertyState = "missing";
                            }
                        }
                        else if (value.Length == 0)
                        {
                            context.PropertyStatuses.Add(new(
                                propertyName,
                                isKeyboxProperty ? "empty" : "skipped_empty",
                                "value_empty"));
                            if (options.IncludeKeybox && isKeyboxProperty)
                            {
                                context.KeyboxEnabledPropertyPresent = true;
                                context.KeyboxEnabledPropertyState = "empty";
                            }
                        }
                        else
                        {
                            values[propertyName] = value;
                            context.PropertyStatuses.Add(new(propertyName, "backed_up", null));
                            if (options.IncludeKeybox && isKeyboxProperty)
                            {
                                context.KeyboxEnabledPropertyPresent = true;
                                context.KeyboxEnabledPropertyState = "backed_up";
                            }
                        }
                    }
                }
                else
                {
                    foreach (string propertyName in propertyNames)
                    {
                        context.PropertyStatuses.Add(new(
                            propertyName,
                            "read_failed",
                            "debug_gate_unavailable"));
                    }
                }

                if (options.IncludeDeviceProperties)
                {
                    foreach (string propertyName in ManagedDevicePropertyCatalog.ExcludedProperties)
                    {
                        if (propertyGateReady
                            && propertyName.StartsWith("persist.props.config.sim2.", StringComparison.Ordinal))
                        {
                            string legacyValue = await ReadPropertyValueAsync(
                                    serial,
                                    propertyName,
                                    missingSentinel,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            if (legacyValue != missingSentinel && legacyValue.Length > 0)
                            {
                                _logger.LogDebug(
                                    "Legacy property {PropertyName} is present and excluded from backup.",
                                    propertyName);
                            }
                        }

                        context.PropertyStatuses.Add(new(
                            propertyName,
                            "excluded",
                            "control_or_legacy_property"));
                    }
                }

                await writer.AddJsonEntryAsync(
                        "properties.json",
                        new BackupPropertyPayload(values),
                        includeChecksum: true,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (options.IncludeManagedSystemSettings)
            {
                progress?.Report(new(DeviceBackupStage.ReadingSettings));
                context.SettingsStatus = "backed_up";
                var settingValues = new List<BackupSettingValue>();
                foreach (SettingDescriptor descriptor in ManagedSettings)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string displayName = $"{descriptor.Namespace}.{descriptor.Key}";
                    string value = await ReadSettingValueAsync(
                            serial,
                            descriptor,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (value == "null")
                    {
                        context.SettingsStatuses.Add(new(displayName, "skipped_missing", "setting_not_present"));
                    }
                    else if (value.Length == 0)
                    {
                        context.SettingsStatuses.Add(new(displayName, "skipped_empty", "value_empty"));
                    }
                    else
                    {
                        settingValues.Add(new(displayName, value));
                        context.SettingsStatuses.Add(new(displayName, "backed_up", null));
                    }
                }

                string missingSentinel = $"__DDC_BACKUP_MISSING_{Guid.NewGuid():N}__";
                string timezone = await ReadPropertyValueAsync(
                        serial,
                        PropertyConstants.Timezone,
                        missingSentinel,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (timezone == missingSentinel)
                {
                    context.SettingsStatuses.Add(new(
                        PropertyConstants.Timezone,
                        "skipped_missing",
                        "setting_not_present"));
                }
                else if (timezone.Length == 0)
                {
                    context.SettingsStatuses.Add(new(
                        PropertyConstants.Timezone,
                        "skipped_empty",
                        "value_empty"));
                }
                else
                {
                    settingValues.Add(new(PropertyConstants.Timezone, timezone));
                    context.SettingsStatuses.Add(new(PropertyConstants.Timezone, "backed_up", null));
                }

                await writer.AddJsonEntryAsync(
                        "settings.json",
                        new BackupSettingsPayload(settingValues),
                        includeChecksum: true,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (options.IncludeUserAppData)
            {
                progress?.Report(new(DeviceBackupStage.BackingUpApps));
                IReadOnlyList<string> packages = await _devicePackageService
                    .GetUserInstalledPackagesAsync(serial, cancellationToken)
                    .ConfigureAwait(false);
                foreach (string packageName in packages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsValidPackageName(packageName))
                    {
                        context.Warnings.Add("invalid_user_package_name_skipped");
                        continue;
                    }

                    progress?.Report(new(DeviceBackupStage.BackingUpApps, packageName));
                    PackageBackupManifest package = await BackupPackageAsync(
                            serial,
                            packageName,
                            "apps",
                            includeExternalData: true,
                            writer,
                            remoteSessionPath,
                            localTemporaryDirectory,
                            cancellationToken)
                        .ConfigureAwait(false);
                    context.Packages.Add(package);
                }
            }

            if (options.IncludeGoogleAppData)
            {
                progress?.Report(new(DeviceBackupStage.BackingUpOptionalData));
                IReadOnlyList<string> installedPackages = await _devicePackageService
                    .GetInstalledPackagesAsync(serial, cancellationToken)
                    .ConfigureAwait(false);
                var installed = new HashSet<string>(installedPackages, StringComparer.Ordinal);
                int backedUpGooglePackages = 0;
                foreach (string packageName in GooglePackages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!installed.Contains(packageName))
                    {
                        context.OptionalComponents.Add(new(
                            $"google_app:{packageName}",
                            "skipped_missing",
                            "package_not_installed"));
                        continue;
                    }

                    progress?.Report(new(DeviceBackupStage.BackingUpOptionalData, packageName));
                    PackageBackupManifest package = await BackupPackageAsync(
                            serial,
                            packageName,
                            "google",
                            includeExternalData: false,
                            writer,
                            remoteSessionPath,
                            localTemporaryDirectory,
                            cancellationToken)
                        .ConfigureAwait(false);
                    context.Packages.Add(package);
                    if (string.Equals(package.Status, "backed_up", StringComparison.Ordinal))
                        backedUpGooglePackages++;
                    context.OptionalComponents.Add(new(
                        $"google_app:{packageName}",
                        package.Status,
                        package.Reason));
                }

                context.OptionalComponents.Add(new(
                    "googleAppData",
                    backedUpGooglePackages > 0 ? "backed_up" : "skipped_missing",
                    "experimental_google_state"));
            }
            else
            {
                context.OptionalComponents.Add(new("googleAppData", "not_selected", null));
            }

            if (options.IncludeKeybox || options.IncludeSsaid || options.IncludeGoogleAccountState)
                progress?.Report(new(DeviceBackupStage.BackingUpOptionalData));

            if (options.IncludeKeybox)
            {
                const string keyboxPath = "/data/system/keybox.xml";
                string? identity = await ProbePathIdentityAsync(
                        serial,
                        keyboxPath,
                        cancellationToken)
                    .ConfigureAwait(false);
                bool filePresent = identity is not null;
                if (!filePresent)
                {
                    context.OptionalComponents.Add(new(
                        "keybox",
                        "skipped_missing",
                        "file_not_present"));
                }
                else
                {
                    await PullPayloadAsync(
                            serial,
                            keyboxPath,
                            "optional/keybox.xml",
                            "keybox",
                            writer,
                            localTemporaryDirectory,
                            cancellationToken)
                        .ConfigureAwait(false);
                    context.OptionalComponents.Add(new("keybox", "backed_up", null));
                }

                context.KeyboxState = new BackupKeyboxState(
                    filePresent,
                    PropertyConstants.Keybox.Enabled,
                    context.KeyboxEnabledPropertyPresent,
                    context.KeyboxEnabledPropertyState ?? "read_failed");
            }
            else
            {
                context.OptionalComponents.Add(new("keybox", "not_selected", null));
            }

            if (options.IncludeSsaid)
            {
                const string ssaidPath = "/data/system/users/0/settings_ssaid.xml";
                string? identity = await ProbePathIdentityAsync(
                        serial,
                        ssaidPath,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (identity is null)
                {
                    context.OptionalComponents.Add(new(
                        "ssaId",
                        "skipped_missing",
                        "file_not_present"));
                }
                else
                {
                    await PullPayloadAsync(
                            serial,
                            ssaidPath,
                            "experimental/settings_ssaid.xml",
                            "ssaId",
                            writer,
                            localTemporaryDirectory,
                            cancellationToken)
                        .ConfigureAwait(false);
                    context.OptionalComponents.Add(new(
                        "ssaId",
                        "backed_up",
                        "experimental_not_portable_by_default"));
                }
            }
            else
            {
                context.OptionalComponents.Add(new("ssaId", "not_selected", null));
            }

            if (options.IncludeGoogleAccountState)
            {
                bool anyAccountPayload = false;
                foreach (AccountPathDescriptor descriptor in AccountPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    IReadOnlyList<string> sourcePaths;
                    if (descriptor.IsPattern)
                    {
                        sourcePaths = await FindMatchingPathsAsync(
                                serial,
                                descriptor.Directory,
                                descriptor.NameOrPattern,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        string fullPath = $"{descriptor.Directory}/{descriptor.NameOrPattern}";
                        string? identity = await ProbePathIdentityAsync(
                                serial,
                                fullPath,
                                cancellationToken)
                            .ConfigureAwait(false);
                        sourcePaths = identity is null ? [] : [fullPath];
                    }

                    if (sourcePaths.Count == 0)
                    {
                        context.OptionalComponents.Add(new(
                            $"google_account:{descriptor.NameOrPattern}",
                            "skipped_missing",
                            "path_not_present"));
                        continue;
                    }

                    string[] names = sourcePaths.Select(GetUnixFileName).ToArray();
                    await AddTarFromNamesAsync(
                            serial,
                            descriptor.Directory,
                            names,
                            descriptor.ArchivePath,
                            "account",
                            writer,
                            remoteSessionPath,
                            localTemporaryDirectory,
                            cancellationToken)
                        .ConfigureAwait(false);
                    anyAccountPayload = true;
                    context.OptionalComponents.Add(new(
                        $"google_account:{descriptor.NameOrPattern}",
                        "backed_up",
                        "experimental_same_device_state"));
                }

                context.OptionalComponents.Add(new(
                    "googleAccountState",
                    anyAccountPayload ? "backed_up" : "skipped_missing",
                    "experimental_same_device_state"));
            }
            else
            {
                context.OptionalComponents.Add(new("googleAccountState", "not_selected", null));
            }
        }
        catch (Exception exception)
        {
            backupFailure = exception;
            throw;
        }
        finally
        {
            try
            {
                await CleanupRootOperationAsync(serial, remoteSessionPath, debugGateTouched)
                    .ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                if (backupFailure is null)
                    throw;

                backupFailure.Data["DeepDroidChanger.DeviceBackup.CleanupFailure"] = cleanupException;
                _logger.LogError(
                    cleanupException,
                    "Device backup cleanup failed for {Serial} after the primary backup failure ({ExceptionType}).",
                    serial,
                    backupFailure.GetType().Name);
            }
        }
    }


    private async Task<PackageBackupManifest> BackupPackageAsync(
        string serial,
        string packageName,
        string packageGroup,
        bool includeExternalData,
        ArchiveWriter writer,
        string remoteSessionPath,
        string localTemporaryDirectory,
        CancellationToken cancellationToken)
    {
        if (!IsValidPackageName(packageName))
            throw new ArgumentException("The package name is invalid.", nameof(packageName));

        PackageMetadata metadata = await ReadPackageMetadataAsync(
                serial,
                packageName,
                cancellationToken)
            .ConfigureAwait(false);
        string manifestPath = $"{packageGroup}/{packageName}/manifest.json";

        await _adb.ForceStopPackageAsync(serial, packageName, cancellationToken)
            .ConfigureAwait(false);
        var pathStatuses = new List<BackupDataPathStatus>();
        var logicalAliases = new List<string>();
        bool? sameUnderlyingData = null;

        string dataDataPath = $"/data/data/{packageName}";
        string cePath = $"/data/user/0/{packageName}";
        string? dataDataIdentity = await ProbePathIdentityAsync(
                serial,
                dataDataPath,
                cancellationToken)
            .ConfigureAwait(false);
        string? ceIdentity = await ProbePathIdentityAsync(serial, cePath, cancellationToken)
            .ConfigureAwait(false);

        if (dataDataIdentity is not null && ceIdentity is not null)
        {
            sameUnderlyingData = string.Equals(dataDataIdentity, ceIdentity, StringComparison.Ordinal);
            if (sameUnderlyingData.Value)
            {
                logicalAliases.Add(dataDataPath);
                logicalAliases.Add(cePath);
                await AddTarPayloadAsync(
                        serial,
                        cePath,
                        GetPackageArchivePath(packageGroup, packageName, "ce.tar"),
                        writer,
                        remoteSessionPath,
                        localTemporaryDirectory,
                        cancellationToken)
                    .ConfigureAwait(false);
                pathStatuses.Add(new(dataDataPath, "present_alias", "ce.tar"));
                pathStatuses.Add(new(cePath, "backed_up", "ce.tar"));
            }
            else
            {
                logicalAliases.Add(cePath);
                await AddTarPayloadAsync(
                        serial,
                        cePath,
                        GetPackageArchivePath(packageGroup, packageName, "ce.tar"),
                        writer,
                        remoteSessionPath,
                        localTemporaryDirectory,
                        cancellationToken)
                    .ConfigureAwait(false);
                await AddTarPayloadAsync(
                        serial,
                        dataDataPath,
                        GetPackageArchivePath(packageGroup, packageName, "data-data.tar"),
                        writer,
                        remoteSessionPath,
                        localTemporaryDirectory,
                        cancellationToken)
                    .ConfigureAwait(false);
                pathStatuses.Add(new(dataDataPath, "backed_up", "data-data.tar"));
                pathStatuses.Add(new(cePath, "backed_up", "ce.tar"));
            }
        }
        else if (ceIdentity is not null)
        {
            logicalAliases.Add(cePath);
            await AddTarPayloadAsync(
                    serial,
                    cePath,
                    GetPackageArchivePath(packageGroup, packageName, "ce.tar"),
                    writer,
                    remoteSessionPath,
                    localTemporaryDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
            pathStatuses.Add(new(dataDataPath, "skipped_missing", null));
            pathStatuses.Add(new(cePath, "backed_up", "ce.tar"));
        }
        else if (dataDataIdentity is not null)
        {
            logicalAliases.Add(dataDataPath);
            await AddTarPayloadAsync(
                    serial,
                    dataDataPath,
                    GetPackageArchivePath(packageGroup, packageName, "ce.tar"),
                    writer,
                    remoteSessionPath,
                    localTemporaryDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
            pathStatuses.Add(new(dataDataPath, "backed_up", "ce.tar"));
            pathStatuses.Add(new(cePath, "skipped_missing", null));
        }
        else
        {
            pathStatuses.Add(new(dataDataPath, "skipped_missing", null));
            pathStatuses.Add(new(cePath, "skipped_missing", null));
        }

        await BackupOptionalPathAsync(
                serial,
                $"/data/user_de/0/{packageName}",
                GetPackageArchivePath(packageGroup, packageName, "de.tar"),
                pathStatuses,
                writer,
                remoteSessionPath,
                localTemporaryDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        if (includeExternalData)
        {
            await BackupOptionalPathAsync(
                    serial,
                    $"/data/media/0/Android/data/{packageName}",
                    GetPackageArchivePath(packageGroup, packageName, "external.tar"),
                    pathStatuses,
                    writer,
                    remoteSessionPath,
                    localTemporaryDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
            await BackupOptionalPathAsync(
                    serial,
                    $"/data/media/0/Android/media/{packageName}",
                    GetPackageArchivePath(packageGroup, packageName, "media.tar"),
                    pathStatuses,
                    writer,
                    remoteSessionPath,
                    localTemporaryDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
            await BackupOptionalPathAsync(
                    serial,
                    $"/data/media/0/Android/obb/{packageName}",
                    GetPackageArchivePath(packageGroup, packageName, "obb.tar"),
                    pathStatuses,
                    writer,
                    remoteSessionPath,
                    localTemporaryDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var manifest = new PackageBackupManifest(
            packageGroup,
            packageName,
            "backed_up",
            null,
            metadata.VersionCode,
            metadata.SourceUid,
            metadata.TargetSdk,
            metadata.SigningCertificateSha256,
            logicalAliases,
            sameUnderlyingData,
            pathStatuses);
        await writer.AddJsonEntryAsync(
                manifestPath,
                manifest,
                includeChecksum: false,
                cancellationToken)
            .ConfigureAwait(false);
        return manifest;
    }

    private async Task BackupOptionalPathAsync(
        string serial,
        string devicePath,
        string archivePath,
        List<BackupDataPathStatus> statuses,
        ArchiveWriter writer,
        string remoteSessionPath,
        string localTemporaryDirectory,
        CancellationToken cancellationToken)
    {
        string? identity = await ProbePathIdentityAsync(serial, devicePath, cancellationToken)
            .ConfigureAwait(false);
        if (identity is null)
        {
            statuses.Add(new(devicePath, "skipped_missing", null));
            return;
        }

        await AddTarPayloadAsync(
                serial,
                devicePath,
                archivePath,
                writer,
                remoteSessionPath,
                localTemporaryDirectory,
                cancellationToken)
            .ConfigureAwait(false);
        statuses.Add(new(devicePath, "backed_up", archivePath[(archivePath.LastIndexOf('/') + 1)..]));
    }

    private async Task AddTarPayloadAsync(
        string serial,
        string sourcePath,
        string archivePath,
        ArchiveWriter writer,
        string remoteSessionPath,
        string localTemporaryDirectory,
        CancellationToken cancellationToken)
    {
        string parentDirectory = GetUnixDirectoryName(sourcePath);
        string name = GetUnixFileName(sourcePath);
        await AddTarFromNamesAsync(
                serial,
                parentDirectory,
                [name],
                archivePath,
                "app data",
                writer,
                remoteSessionPath,
                localTemporaryDirectory,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task AddTarFromNamesAsync(
        string serial,
        string parentDirectory,
        IReadOnlyList<string> names,
        string archivePath,
        string componentName,
        ArchiveWriter writer,
        string remoteSessionPath,
        string localTemporaryDirectory,
        CancellationToken cancellationToken)
    {
        if (names.Count == 0)
            throw new InvalidOperationException("A backup tar source was empty.");

        cancellationToken.ThrowIfCancellationRequested();
        int index = writer.NextTemporaryFileIndex();
        string remoteTarPath = $"{remoteSessionPath}/payload-{index:D5}.tar";
        string localTarPath = Path.Combine(localTemporaryDirectory, $"payload-{index:D5}.tar");
        string sourceArguments = string.Join(" ", names.Select(QuoteShellValue));
        string tarCommand =
            $"tar -cpf {QuoteShellValue(remoteTarPath)} -C {QuoteShellValue(parentDirectory)} {sourceArguments}";
        await RunRequiredShellAsync(
                serial,
                tarCommand,
                $"create {componentName} tar payload",
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            CommandResult pullResult = await _adb.PullFileAsync(
                    serial,
                    remoteTarPath,
                    localTarPath,
                    cancellationToken)
                .ConfigureAwait(false);
            EnsureCommandSuccess(pullResult, $"pull {componentName} tar payload");

            var file = new FileInfo(localTarPath);
            if (!file.Exists || file.Length == 0)
                throw new InvalidOperationException($"The {componentName} tar payload was empty.");

            await writer.AddFileAsync(
                    archivePath,
                    localTarPath,
                    includeChecksum: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(localTarPath);
        }
    }

    private async Task PullPayloadAsync(
        string serial,
        string sourcePath,
        string archivePath,
        string componentName,
        ArchiveWriter writer,
        string localTemporaryDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string localPath = Path.Combine(
            localTemporaryDirectory,
            $"payload-{Guid.NewGuid():N}.bin");
        try
        {
            CommandResult pullResult = await _adb.PullFileAsync(
                    serial,
                    sourcePath,
                    localPath,
                    cancellationToken)
                .ConfigureAwait(false);
            EnsureCommandSuccess(pullResult, $"pull {componentName} payload");
            if (!File.Exists(localPath))
                throw new InvalidOperationException($"The {componentName} payload was not created.");

            await writer.AddFileAsync(
                    archivePath,
                    localPath,
                    includeChecksum: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(localPath);
        }
    }


    private async Task<IReadOnlyList<string>> FindMatchingPathsAsync(
        string serial,
        string directory,
        string filePattern,
        CancellationToken cancellationToken)
    {
        string? directoryIdentity = await ProbePathIdentityAsync(
                serial,
                directory,
                cancellationToken)
            .ConfigureAwait(false);
        if (directoryIdentity is null)
            return [];

        CommandResult result = await _adb.RunAdbShellAsync(
                serial,
                $"find {QuoteShellValue(directory)} -maxdepth 1 -name {QuoteShellValue(filePattern)} -print",
                cancellationToken)
            .ConfigureAwait(false);
        EnsureCommandSuccess(result, "list experimental account state files");

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => path.StartsWith(directory + "/", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<PackageMetadata> ReadPackageMetadataAsync(
        string serial,
        string packageName,
        CancellationToken cancellationToken)
    {
        try
        {
            CommandResult result = await _adb.RunAdbShellAsync(
                    serial,
                    $"dumpsys package {QuoteShellValue(packageName)}",
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                _logger.LogDebug(
                    "Package metadata could not be read for {PackageName} on {Serial}.",
                    packageName,
                    serial);
                return new(null, null, null, null);
            }

            string output = result.StandardOutput;
            return new(
                ParseLong(VersionCodePattern, output),
                ParseLong(SourceUidPattern, output),
                ParseInt(TargetSdkPattern, output),
                ParseSigningCertificateSha256(output));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                "Package metadata query failed for {PackageName} on {Serial} ({ExceptionType}).",
                packageName,
                serial,
                exception.GetType().Name);
            return new(null, null, null, null);
        }
    }

    private async Task<string?> ReadSourceDeviceRoleAsync(
        string serial,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            string value = await _adb.GetPropertyAsync(
                    serial,
                    PropertyConstants.DeepDroidDevice,
                    cancellationToken)
                .ConfigureAwait(false);
            string normalized = value.Trim().ToLowerInvariant();
            if (normalized is "sargo" or "starlte" or "tissot")
                return normalized;

            warnings.Add(string.IsNullOrWhiteSpace(normalized)
                ? "source_device_role_unavailable"
                : "source_device_role_unrecognized");
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            warnings.Add("source_device_role_unavailable");
            _logger.LogWarning(
                "The source device role could not be read on {Serial} ({ExceptionType}).",
                serial,
                exception.GetType().Name);
            return null;
        }
    }

    private async Task<int?> ReadAndroidSdkVersionAsync(
        string serial,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            string value = await _adb.GetPropertyAsync(
                    serial,
                    PropertyConstants.Runtime.AndroidSdkVersion,
                    cancellationToken)
                .ConfigureAwait(false);
            if (int.TryParse(value, out int sdkVersion) && sdkVersion > 0)
                return sdkVersion;

            warnings.Add("android_sdk_version_unavailable");
            _logger.LogWarning(
                "The Android SDK version was unavailable on {Serial}.",
                serial);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            warnings.Add("android_sdk_version_unavailable");
            _logger.LogWarning(
                "The Android SDK version could not be read on {Serial} ({ExceptionType}).",
                serial,
                exception.GetType().Name);
            return null;
        }
    }

    private static string? ParseSigningCertificateSha256(string output)
    {
        MatchCollection matches = SigningDigestLabelPattern.Matches(output);
        if (matches.Count == 0)
            return null;

        var digests = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in matches)
        {
            if (!TryNormalizeSigningDigest(match.Groups["value"].Value, out string digest))
                return null;
            digests.Add(digest);
        }

        return digests.Count == 1 ? digests.First() : null;
    }

    private static bool TryNormalizeSigningDigest(string value, out string digest)
    {
        string trimmed = value.Trim().Trim('"', (char)39);
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[2..];

        string compactHex = string.Concat(trimmed.Where(character =>
            character != ':' && !char.IsWhiteSpace(character)));
        if (compactHex.Length == 64 && compactHex.All(Uri.IsHexDigit))
        {
            digest = compactHex.ToUpperInvariant();
            return true;
        }

        string compactBase64 = string.Concat(trimmed.Where(character => !char.IsWhiteSpace(character)));
        if (compactBase64.Length == 43)
            compactBase64 += "=";
        if (compactBase64.Length == 44)
        {
            try
            {
                byte[] decoded = Convert.FromBase64String(compactBase64);
                if (decoded.Length == 32)
                {
                    digest = Convert.ToHexString(decoded);
                    return true;
                }
            }
            catch (FormatException)
            {
            }
        }

        digest = string.Empty;
        return false;
    }

    private async Task<string?> ProbePathIdentityAsync(
        string serial,
        string devicePath,
        CancellationToken cancellationToken)
    {
        string missingSentinel = $"__DDC_PATH_MISSING_{Guid.NewGuid():N}__";
        string quotedPath = QuoteShellValue(devicePath);
        string command =
            $"if [ -e {quotedPath} ]; then stat -c '%d:%i' {quotedPath}; else printf '%s\\n' {QuoteShellValue(missingSentinel)}; fi";
        CommandResult result = await _adb.RunAdbShellAsync(serial, command, cancellationToken)
            .ConfigureAwait(false);
        EnsureCommandSuccess(result, $"inspect device path {devicePath}");
        string value = result.StandardOutput.Trim();
        if (value == missingSentinel)
            return null;
        if (value.Length == 0)
            throw new InvalidOperationException($"The device path {devicePath} could not be inspected.");
        return value;
    }

    private async Task<string> ReadPropertyValueAsync(
        string serial,
        string propertyName,
        string missingSentinel,
        CancellationToken cancellationToken)
    {
        CommandResult result = await _adb.RunAdbShellAsync(
                serial,
                $"getprop {propertyName} {QuoteShellValue(missingSentinel)}",
                cancellationToken)
            .ConfigureAwait(false);
        EnsureCommandSuccess(result, $"read property {propertyName}");
        return result.StandardOutput.Trim();
    }

    private async Task<string> ReadSettingValueAsync(
        string serial,
        SettingDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        CommandResult result = await _adb.RunAdbShellAsync(
                serial,
                $"settings get {descriptor.Namespace} {descriptor.Key}",
                cancellationToken)
            .ConfigureAwait(false);
        EnsureCommandSuccess(result, $"read setting {descriptor.Namespace}.{descriptor.Key}");
        return result.StandardOutput.Trim();
    }

    private async Task RunRequiredShellAsync(
        string serial,
        string command,
        string purpose,
        CancellationToken cancellationToken)
    {
        CommandResult result = await _adb.RunAdbShellAsync(serial, command, cancellationToken)
            .ConfigureAwait(false);
        EnsureCommandSuccess(result, purpose);
    }

    private async Task CleanupRootOperationAsync(
        string serial,
        string remoteSessionPath,
        bool debugGateTouched)
    {
        Exception? cleanupFailure = null;

        using (var remoteCleanup = new CancellationTokenSource(CleanupTimeout))
        {
            try
            {
                CommandResult result = await _adb.RunAdbShellAsync(
                        serial,
                        $"rm -rf {QuoteShellValue(remoteSessionPath)}",
                        remoteCleanup.Token)
                    .ConfigureAwait(false);
                EnsureCommandSuccess(result, "remove device backup temporary files");
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }
        }

        if (debugGateTouched)
        {
            using var gateCleanup = new CancellationTokenSource(CleanupTimeout);
            try
            {
                await _adb.SetPropertyAsync(
                        serial,
                        PropertyConstants.Debug.Enabled,
                        "false",
                        gateCleanup.Token)
                    .ConfigureAwait(false);
                string missingSentinel = $"__DDC_DEBUG_MISSING_{Guid.NewGuid():N}__";
                string debugValue = await ReadPropertyValueAsync(
                        serial,
                        PropertyConstants.Debug.Enabled,
                        missingSentinel,
                        gateCleanup.Token)
                    .ConfigureAwait(false);
                if (string.Equals(debugValue, "true", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The persisted property debug gate remained enabled after backup cleanup.");
                }
            }
            catch (Exception exception)
            {
                cleanupFailure = cleanupFailure is null
                    ? exception
                    : new AggregateException(cleanupFailure, exception);
            }
        }

        if (cleanupFailure is not null)
        {
            throw new InvalidOperationException(
                "Device backup cleanup did not complete successfully.",
                cleanupFailure);
        }
    }

    private static string GetPackageArchivePath(
        string packageGroup,
        string packageName,
        string fileName)
    {
        return $"{packageGroup}/{packageName}/{fileName}";
    }

    private static bool IsValidPackageName(string packageName)
    {
        return packageName.Length <= 255 && ValidPackageName.IsMatch(packageName);
    }

    private static long? ParseLong(Regex pattern, string output)
    {
        Match match = pattern.Match(output);
        return match.Success && long.TryParse(match.Groups[1].Value, out long value)
            ? value
            : null;
    }

    private static int? ParseInt(Regex pattern, string output)
    {
        Match match = pattern.Match(output);
        return match.Success && int.TryParse(match.Groups[1].Value, out int value)
            ? value
            : null;
    }

    private static string GetUnixDirectoryName(string path)
    {
        int lastSeparator = path.LastIndexOf('/');
        if (lastSeparator <= 0)
            throw new InvalidOperationException("The device path did not include a parent directory.");
        return path[..lastSeparator];
    }

    private static string GetUnixFileName(string path)
    {
        int lastSeparator = path.LastIndexOf('/');
        return lastSeparator < 0 ? path : path[(lastSeparator + 1)..];
    }

    private static string QuoteShellValue(string value)
    {
        return $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
    }

    private static void EnsureCommandSuccess(CommandResult result, string purpose)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ADB failed while attempting to {purpose} (exit code {result.ExitCode}).");
        }
    }


    private static FileStream CreatePartialArchive(
        string destinationDirectory,
        string serial,
        DateTime createdAtUtc,
        out string finalPath,
        out string partialPath)
    {
        string safeSerial = SanitizeFileNamePart(serial);
        string timestamp = createdAtUtc.ToString(
            "yyyyMMdd_HHmmss",
            System.Globalization.CultureInfo.InvariantCulture);

        for (int suffix = 0; suffix < 10_000; suffix++)
        {
            string suffixText = suffix == 0 ? string.Empty : $"_{suffix}";
            finalPath = Path.Combine(
                destinationDirectory,
                $"{safeSerial}_{timestamp}{suffixText}.ddcbak");
            partialPath = finalPath + ".partial";
            if (File.Exists(finalPath) || File.Exists(partialPath))
                continue;

            try
            {
                return new FileStream(
                    partialPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException) when (File.Exists(partialPath) || File.Exists(finalPath))
            {
            }
        }

        throw new IOException("Unable to allocate a unique device backup file.");
    }

    private static string SanitizeFileNamePart(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var result = new StringBuilder(Math.Min(value.Length, 72));
        foreach (char character in value)
        {
            if (char.IsControl(character) || invalid.Contains(character) || char.IsWhiteSpace(character))
                result.Append('_');
            else
                result.Append(character);

            if (result.Length >= 72)
                break;
        }

        string sanitized = result.ToString().Trim(' ', '.');
        if (sanitized.Length == 0)
            sanitized = "device";

        string stem = sanitized.Split('.')[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(
                stem,
                @"^(COM|LPT)[1-9]$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            sanitized = "device_" + sanitized;
        }

        return sanitized;
    }

    private static async Task ValidateArchiveAsync(
        string archivePath,
        ArchiveWriterSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        ZipArchiveEntry manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException("The backup manifest was not written.");
        await using Stream manifestStream = manifestEntry.Open();
        using JsonDocument manifest = await JsonDocument.ParseAsync(
                manifestStream,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        JsonElement manifestChecksumsElement = manifest.RootElement.GetProperty("checksums");
        var manifestChecksums = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty checksum in manifestChecksumsElement.EnumerateObject())
        {
            if (!manifestChecksums.TryAdd(checksum.Name, checksum.Value.GetString() ?? string.Empty))
                throw new InvalidDataException("The backup manifest contains a duplicate checksum entry.");
        }

        if (manifestChecksums.Count != snapshot.Checksums.Count
            || snapshot.Checksums.Any(checksum =>
                !manifestChecksums.TryGetValue(checksum.Key, out string? manifestHash)
                || !string.Equals(manifestHash, checksum.Value, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The backup manifest checksums did not match the archive contents.");
        }

        foreach ((string entryPath, string expectedHash) in snapshot.Checksums)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = archive.GetEntry(entryPath)
                ?? throw new InvalidDataException($"The backup payload {entryPath} is missing.");
            await using Stream stream = entry.Open();
            byte[] actualHash = await SHA256.HashDataAsync(stream, cancellationToken)
                .ConfigureAwait(false);
            string actualHashHex = Convert.ToHexString(actualHash);
            if (!string.Equals(actualHashHex, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The backup payload checksum did not match for {entryPath}.");
        }
    }
    private void DeleteTemporaryDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private void TryDeleteTemporaryDirectory(string path)
    {
        try
        {
            DeleteTemporaryDirectory(path);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not remove local backup temporary directory {TemporaryDirectory}.",
                path);
        }
    }

    private void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not remove backup temporary file {TemporaryFile}.", path);
        }
    }

    private sealed class ArchiveWriter : IDisposable
    {
        private readonly ZipArchive _archive;
        private int _temporaryFileIndex;

        public ArchiveWriter(ZipArchive archive)
        {
            _archive = archive;
        }

        public SortedDictionary<string, string> Checksums { get; } = new(StringComparer.Ordinal);

        public int NextTemporaryFileIndex()
        {
            return Interlocked.Increment(ref _temporaryFileIndex);
        }

        public async Task AddJsonEntryAsync(
            string archivePath,
            object value,
            bool includeChecksum,
            CancellationToken cancellationToken)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            await AddBytesAsync(archivePath, bytes, includeChecksum, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task AddBytesAsync(
            string archivePath,
            byte[] bytes,
            bool includeChecksum,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = _archive.CreateEntry(archivePath, CompressionLevel.Optimal);
            await using Stream destination = entry.Open();
            await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (includeChecksum)
                Checksums[archivePath] = Convert.ToHexString(SHA256.HashData(bytes));
        }

        public async Task AddFileAsync(
            string archivePath,
            string localPath,
            bool includeChecksum,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = _archive.CreateEntry(
                archivePath,
                archivePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)
                    ? CompressionLevel.NoCompression
                    : CompressionLevel.Optimal);
            await using Stream destination = entry.Open();
            await using var source = new FileStream(
                localPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);

            using IncrementalHash? hash = includeChecksum
                ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
                : null;
            byte[] buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(
                        buffer.AsMemory(0, bytesRead),
                        cancellationToken)
                    .ConfigureAwait(false);
                hash?.AppendData(buffer, 0, bytesRead);
            }

            if (hash is not null)
                Checksums[archivePath] = Convert.ToHexString(hash.GetHashAndReset());
        }

        public void Dispose()
        {
            _archive.Dispose();
        }
    }

    private sealed class BackupExecutionContext
    {
        public string? SourceDeviceRole { get; set; }
        public int? AndroidSdkVersion { get; set; }
        public string PropertiesStatus { get; set; } = "not_selected";
        public bool? KeyboxEnabledPropertyPresent { get; set; }
        public string? KeyboxEnabledPropertyState { get; set; }
        public BackupKeyboxState? KeyboxState { get; set; }
        public List<BackupPropertyStatus> PropertyStatuses { get; } = [];
        public string SettingsStatus { get; set; } = "not_selected";
        public List<BackupSettingStatus> SettingsStatuses { get; } = [];
        public List<PackageBackupManifest> Packages { get; } = [];
        public List<BackupComponentStatus> OptionalComponents { get; } = [];
        public List<string> Warnings { get; } = [];
    }

    private sealed record SettingDescriptor(string Namespace, string Key);
    private sealed record AccountPathDescriptor(
        string Directory,
        string NameOrPattern,
        string ArchivePath,
        bool IsPattern);
    private sealed record PackageMetadata(
        long? VersionCode,
        long? SourceUid,
        int? TargetSdk,
        string? SigningCertificateSha256);
    private sealed record BackupPropertyPayload(IReadOnlyDictionary<string, string> Values);
    private sealed record BackupSettingsPayload(IReadOnlyList<BackupSettingValue> Values);
    private sealed record BackupSettingValue(string Name, string Value);
    private sealed record BackupPropertyStatus(string PropertyName, string State, string? Reason);
    private sealed record BackupSettingStatus(string Name, string State, string? Reason);
    private sealed record BackupDataPathStatus(string DevicePath, string State, string? Payload);
    private sealed record BackupComponentStatus(string Component, string State, string? Reason);
    private sealed record BackupKeyboxState(
        bool FilePresent,
        string EnabledPropertyName,
        bool? EnabledPropertyPresent,
        string EnabledPropertyState);
    private sealed record BackupSelectedComponents(
        bool DeviceProperties,
        bool ManagedSystemSettings,
        bool UserAppData,
        bool Keybox,
        bool Ssaid,
        bool GoogleAppData,
        bool GoogleAccountState);
    private sealed record PackageBackupManifest(
        string Group,
        string PackageName,
        string Status,
        string? Reason,
        long? VersionCode,
        long? SourceUid,
        int? TargetSdk,
        string? SigningCertificateSha256,
        IReadOnlyList<string> CeLogicalAliases,
        bool? SameUnderlyingCeData,
        IReadOnlyList<BackupDataPathStatus> Paths);
    private sealed record BackupArchiveManifest(
        int FormatVersion,
        DateTime CreatedAtUtc,
        string SourceSerial,
        string? SourceDeviceRole,
        int? AndroidSdkVersion,
        BackupSelectedComponents SelectedComponents,
        BackupKeyboxState? Keybox,
        string PropertiesStatus,
        IReadOnlyList<BackupPropertyStatus> PropertyStatuses,
        string SettingsStatus,
        IReadOnlyList<BackupSettingStatus> SettingsStatuses,
        IReadOnlyList<PackageBackupManifest> Packages,
        IReadOnlyList<BackupComponentStatus> OptionalComponents,
        IReadOnlyList<string> Warnings,
        IReadOnlyDictionary<string, string> Checksums);
    private sealed record ArchiveWriterSnapshot(IReadOnlyDictionary<string, string> Checksums);
}
