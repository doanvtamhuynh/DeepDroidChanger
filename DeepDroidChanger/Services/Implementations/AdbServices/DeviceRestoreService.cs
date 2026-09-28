using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

/// <summary>
/// Restores a validated Device Backup archive. Archive validation is kept in
/// this service so DeviceBackupService remains responsible only for writing
/// the existing format.
/// </summary>
public sealed class DeviceRestoreService : IDeviceRestoreService
{
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(25);
    private static readonly Regex ValidPackageName = new(
        @"^[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SigningDigestLabelPattern = new(
        @"(?im)\b(?:signingCertificateSha256|signing[ \t_-]*certificate[ \t_-]*sha[ \t_-]*256|signer(?:'s)?[ \t_-]*(?:certificate|cert)(?:'s)?[ \t_-]*sha[ \t_-]*256(?:[ \t_-]*digest)?|certificate[ \t_-]*sha[ \t_-]*256(?:[ \t_-]*digest)?|sha[ \t_-]*256[ \t_-]*(?:signing|certificate)[ \t_-]*digest)\b[ \t]*[:=][ \t]*(?<value>[^\r\n,;]+)",
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
    private static readonly Regex FileMetadataPattern = new(
        @"^(?<uid>\d+):(?<gid>\d+):(?<mode>[0-7]{3,4})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
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

    private static readonly AccountRestoreDescriptor[] AccountPaths =
    [
        new("account.accounts_ce", "/data/system_ce/0", "accounts_ce.db", "account/system_ce/accounts_ce.tar", true),
        new("account.accounts_de", "/data/system_de/0", "accounts_de.db", "account/system_de/accounts_de.tar", true),
        new("account.accounts_legacy", "/data/system/users/0", "accounts.db", "account/users/accounts.tar", true),
        new("account.syncmanager", "/data/system", "syncmanager.db", "account/syncmanager/syncmanager.tar", true),
        new("account.sync", "/data/system", "sync", "account/sync/sync.tar", false),
        new("account.registered_services", "/data/system/users/0", "registered_services", "account/users/registered_services.tar", false)
    ];

    private static readonly string[] IntegrityProperties =
    [
        PropertyConstants.Integrity.Brand,
        PropertyConstants.Integrity.Device,
        PropertyConstants.Integrity.DeviceInitialSdkInt,
        PropertyConstants.Integrity.Fingerprint,
        PropertyConstants.Integrity.Id,
        PropertyConstants.Integrity.Manufacturer,
        PropertyConstants.Integrity.Model,
        PropertyConstants.Integrity.Product,
        PropertyConstants.Integrity.Release,
        PropertyConstants.Integrity.SecurityPatch,
        PropertyConstants.Integrity.Tags,
        PropertyConstants.Integrity.Type,
        PropertyConstants.Integrity.SdkInt
    ];

    private static readonly string[] SimProperties =
    [
        PropertyConstants.Spoof.SimIccid,
        PropertyConstants.Spoof.SimImsi,
        PropertyConstants.Spoof.SimPhoneNumber,
        PropertyConstants.Spoof.SimOperatorName,
        PropertyConstants.Spoof.SimOperatorCountry,
        PropertyConstants.Spoof.SimOperatorNumeric
    ];

    private readonly IAdbCommandService _adb;
    private readonly IAdbRootAccessService _rootAccessService;
    private readonly IDevicePackageService _devicePackageService;
    private readonly ILogger<DeviceRestoreService> _logger;

    public DeviceRestoreService(
        IAdbCommandService adb,
        IAdbRootAccessService rootAccessService,
        IDevicePackageService devicePackageService,
        ILogger<DeviceRestoreService> logger)
    {
        _adb = adb;
        _rootAccessService = rootAccessService;
        _devicePackageService = devicePackageService;
        _logger = logger;
    }

    public async Task<DeviceRestoreInspection> InspectAsync(
        string archivePath,
        string password,
        CancellationToken cancellationToken)
    {
        ValidateArchiveArguments(archivePath, password);
        cancellationToken.ThrowIfCancellationRequested();

        string sessionDirectory = CreateLocalSessionDirectory();
        string plaintextPath = Path.Combine(sessionDirectory, "payload.zip");
        try
        {
            await BackupEnvelopeCrypto.VerifyAndDecryptAsync(
                    archivePath,
                    plaintextPath,
                    password,
                    cancellationToken)
                .ConfigureAwait(false);
            BackupArchiveManifest manifest = await LoadAndValidateArchiveAsync(
                    plaintextPath,
                    cancellationToken)
                .ConfigureAwait(false);
            return CreateInspection(archivePath, manifest);
        }
        finally
        {
            TryDeleteDirectory(sessionDirectory);
        }
    }

    public async Task<DeviceRestoreResult> RestoreAsync(
        string serial,
        DeviceRestoreOptions options,
        IProgress<DeviceRestoreProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(options);
        ValidateArchiveArguments(options.ArchivePath, options.RestorePassword);
        if (!options.HasSelectedComponent)
            throw new ArgumentException("At least one restore component must be selected.", nameof(options));

        progress?.Report(new(DeviceRestoreStage.Preparing));
        string localSessionDirectory = CreateLocalSessionDirectory();
        string plaintextPath = Path.Combine(localSessionDirectory, "payload.zip");
        string sessionId = Guid.NewGuid().ToString("N");
        string remoteSessionPath = $"/data/local/tmp/deepdroid-restore/{sessionId}";
        var execution = new RestoreExecution();
        bool archiveValidated = false;

        try
        {
            progress?.Report(new(DeviceRestoreStage.ValidatingArchive));
            await BackupEnvelopeCrypto.VerifyAndDecryptAsync(
                    options.ArchivePath,
                    plaintextPath,
                    options.RestorePassword,
                    cancellationToken)
                .ConfigureAwait(false);
            BackupArchiveManifest manifest = await LoadAndValidateArchiveAsync(
                    plaintextPath,
                    cancellationToken)
                .ConfigureAwait(false);
            archiveValidated = true;

            progress?.Report(new(DeviceRestoreStage.CheckingCompatibility));
            RestorePlan plan = await BuildRestorePlanAsync(
                    serial,
                    options,
                    manifest,
                    cancellationToken)
                .ConfigureAwait(false);
            execution.Warnings.AddRange(plan.Warnings);

            await _rootAccessService.ExecuteAsRootAsync(
                    serial,
                    rootCancellationToken => RestoreUnderRootAsync(
                        serial,
                        options,
                        plan,
                        plaintextPath,
                        localSessionDirectory,
                        remoteSessionPath,
                        progress,
                        execution,
                        rootCancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            if (execution.MutationStarted)
            {
                progress?.Report(new(DeviceRestoreStage.Rebooting));
                try
                {
                    using var rebootCancellation = new CancellationTokenSource(CleanupTimeout);
                    await _adb.RebootAsync(serial, rebootCancellation.Token).ConfigureAwait(false);
                    execution.Rebooted = true;
                }
                catch (Exception exception)
                {
                    execution.Warnings.Add("final_reboot_failed");
                    execution.FailureReason ??= exception.Message;
                    _logger.LogError(
                        exception,
                        "Restore changed device {Serial} but the final reboot failed.",
                        serial);
                }
            }

            progress?.Report(new(DeviceRestoreStage.Completed));
            return execution.CreateResult();
        }
        catch (OperationCanceledException)
        {
            if (execution.MutationStarted && !execution.Rebooted)
                await TryRecoverWithRebootAsync(serial).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            if (execution.MutationStarted && !execution.Rebooted)
                await TryRecoverWithRebootAsync(serial).ConfigureAwait(false);

            if (!archiveValidated)
                throw;

            execution.FailureReason ??= exception.Message;
            execution.Warnings.Add("restore_failed");
            _logger.LogError(exception, "Restore failed for device {Serial}.", serial);
            return execution.CreateResult(forceFailed: true);
        }
        finally
        {
            TryDeleteDirectory(localSessionDirectory);
        }
    }

    private async Task<RestoreExecution> RestoreUnderRootAsync(
        string serial,
        DeviceRestoreOptions options,
        RestorePlan plan,
        string plaintextPath,
        string localSessionDirectory,
        string remoteSessionPath,
        IProgress<DeviceRestoreProgress>? progress,
        RestoreExecution execution,
        CancellationToken cancellationToken)
    {
        Exception? primaryFailure = null;
        try
        {
            await RunRequiredShellAsync(
                    serial,
                    $"mkdir -p {QuoteShellValue(remoteSessionPath)}",
                    "create device restore staging directory",
                    cancellationToken)
                .ConfigureAwait(false);

            using ZipArchive archive = ZipFile.OpenRead(plaintextPath);

            if (options.RestoreDeviceProperties)
            {
                progress?.Report(new(DeviceRestoreStage.RestoringProperties, Component: "deviceProperties"));
                try
                {
                    await RestorePropertiesAsync(
                            serial,
                            archive,
                            plan.Manifest,
                            execution,
                            cancellationToken)
                        .ConfigureAwait(false);
                    execution.AddComponent(new(
                        "deviceProperties",
                        DeviceRestoreOutcome.Succeeded,
                        null));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    execution.AddComponent(new(
                        "deviceProperties",
                        DeviceRestoreOutcome.Failed,
                        exception.Message));
                    primaryFailure = exception;
                }
            }

            if (primaryFailure is null && options.RestoreManagedSystemSettings)
            {
                progress?.Report(new(DeviceRestoreStage.RestoringSettings, Component: "managedSystemSettings"));
                try
                {
                    await RestoreSettingsAsync(
                            serial,
                            archive,
                            plan.Manifest,
                            execution,
                            cancellationToken)
                        .ConfigureAwait(false);
                    execution.AddComponent(new(
                        "managedSystemSettings",
                        DeviceRestoreOutcome.Succeeded,
                        null));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    execution.AddComponent(new(
                        "managedSystemSettings",
                        DeviceRestoreOutcome.Failed,
                        exception.Message));
                    primaryFailure = exception;
                }
            }

            bool restoreApps = options.RestoreUserAppData || options.RestoreGoogleAppData;
            if (primaryFailure is null && restoreApps)
            {
                progress?.Report(new(DeviceRestoreStage.RestoringApps));
                try
                {
                    await RestorePackagesAsync(
                            serial,
                            archive,
                            plan,
                            localSessionDirectory,
                            remoteSessionPath,
                            options,
                            progress,
                            execution,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    execution.AddComponent(new(
                        "applications",
                        DeviceRestoreOutcome.Failed,
                        exception.Message));
                    primaryFailure = exception;
                }
            }

            if (primaryFailure is null &&
                (options.RestoreKeybox || options.RestoreSsaid || options.RestoreGoogleAccountState))
            {
                progress?.Report(new(DeviceRestoreStage.RestoringOptionalData));

                if (options.RestoreKeybox)
                {
                    try
                    {
                        await RestoreKeyboxAsync(
                                serial,
                                archive,
                                plan.Manifest,
                                remoteSessionPath,
                                localSessionDirectory,
                                execution,
                                cancellationToken)
                            .ConfigureAwait(false);
                        execution.AddComponent(new(
                            "keybox",
                            DeviceRestoreOutcome.Succeeded,
                            null,
                            Experimental: true));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        execution.AddComponent(new(
                            "keybox",
                            DeviceRestoreOutcome.Failed,
                            exception.Message,
                            Experimental: true));
                        primaryFailure = exception;
                    }
                }

                if (primaryFailure is null && options.RestoreSsaid)
                {
                    try
                    {
                        await RestoreSsaidAsync(
                                serial,
                                archive,
                                remoteSessionPath,
                                localSessionDirectory,
                                execution,
                                cancellationToken)
                            .ConfigureAwait(false);
                        execution.AddComponent(new(
                            "ssaId",
                            DeviceRestoreOutcome.Succeeded,
                            null,
                            Experimental: true));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        execution.AddComponent(new(
                            "ssaId",
                            DeviceRestoreOutcome.Failed,
                            exception.Message,
                            Experimental: true));
                        primaryFailure = exception;
                    }
                }

                if (primaryFailure is null && options.RestoreGoogleAccountState)
                {
                    try
                    {
                        if (!plan.AccountStateAllowed)
                        {
                            execution.AddComponent(new(
                                "googleAccountState",
                                DeviceRestoreOutcome.Skipped,
                                "incompatible_source",
                                Experimental: true));
                        }
                        else
                        {
                            await RestoreGoogleAccountStateAsync(
                                    serial,
                                    archive,
                                    plan.Manifest,
                                    remoteSessionPath,
                                    localSessionDirectory,
                                    execution,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            execution.AddComponent(new(
                                "googleAccountState",
                                plan.AccountStatePartial
                                    ? DeviceRestoreOutcome.Partial
                                    : DeviceRestoreOutcome.Succeeded,
                                plan.AccountStatePartial
                                    ? "partial_archive"
                                    : null,
                                Experimental: true));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        execution.AddComponent(new(
                            "googleAccountState",
                            DeviceRestoreOutcome.Failed,
                            exception.Message,
                            Experimental: true));
                        primaryFailure = exception;
                    }
                }
            }

            if (execution.MutationStarted)
            {
                try
                {
                    await FinalizePropertyStatesAsync(
                            serial,
                            execution,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    execution.Warnings.Add("final_property_states_failed");
                    primaryFailure ??= exception;
                }
            }

            progress?.Report(new(DeviceRestoreStage.Finalizing));
            execution.FailureReason ??= primaryFailure?.Message;
        }
        catch (OperationCanceledException)
        {
            execution.FailureReason ??= "restore_cancelled";
            throw;
        }
        catch (Exception exception)
        {
            execution.FailureReason ??= exception.Message;
            primaryFailure ??= exception;
        }
        finally
        {
            try
            {
                using var cleanupCancellation = new CancellationTokenSource(CleanupTimeout);
                CommandResult cleanupResult = await _adb.RunAdbShellAsync(
                        serial,
                        $"rm -rf {QuoteShellValue(remoteSessionPath)}",
                        cleanupCancellation.Token)
                    .ConfigureAwait(false);
                EnsureCommandSuccess(cleanupResult, "remove device restore temporary files");
            }
            catch (Exception cleanupException)
            {
                execution.Warnings.Add("remote_restore_cleanup_failed");
                _logger.LogWarning(
                    cleanupException,
                    "Could not remove restore staging files for {Serial}.",
                    serial);
            }
        }

        if (primaryFailure is not null)
            execution.FailureReason ??= primaryFailure.Message;
        return execution;
    }

    private async Task RestorePropertiesAsync(
        string serial,
        ZipArchive archive,
        BackupArchiveManifest manifest,
        RestoreExecution execution,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(manifest.PropertiesStatus, "keybox_property_only", StringComparison.Ordinal)
            && !manifest.SelectedComponents.DeviceProperties)
        {
            throw new InvalidOperationException("Device properties are not available in this backup.");
        }

        BackupPropertyPayload payload = await ReadJsonEntryAsync<BackupPropertyPayload>(
                archive,
                "properties.json",
                cancellationToken)
            .ConfigureAwait(false);
        Dictionary<string, string> values = new(payload.Values, StringComparer.Ordinal);
        Dictionary<string, BackupPropertyStatus> statuses = CreateUniqueStatusMap(
            manifest.PropertyStatuses,
            item => item.PropertyName,
            "property status");
        HashSet<string> allowed = new(
            ManagedDevicePropertyCatalog.Properties,
            StringComparer.Ordinal);

        HashSet<string> explicitlyExcluded = new(
            ManagedDevicePropertyCatalog.ExcludedProperties,
            StringComparer.Ordinal);
        foreach (string propertyName in statuses.Keys)
        {
            if (!allowed.Contains(propertyName)
                && !explicitlyExcluded.Contains(propertyName)
                && !string.Equals(propertyName, PropertyConstants.Keybox.Enabled, StringComparison.Ordinal))
            {
                execution.Warnings.Add($"property_not_whitelisted:{propertyName}");
            }
        }

        // Keybox is handled after its payload has been installed, so it can
        // never be enabled before the backing file is ready.
        allowed.Remove(PropertyConstants.Keybox.Enabled);

        bool needsDebugGate = allowed.Any(statuses.ContainsKey);
        if (!needsDebugGate)
            return;

        execution.MutationStarted = true;
        bool debugGateTouched = false;
        Exception? primaryFailure = null;
        try
        {
            await _adb.SetPropertyAsync(
                    serial,
                    PropertyConstants.Debug.Enabled,
                    "true",
                    cancellationToken)
                .ConfigureAwait(false);
            debugGateTouched = true;
            string debugValue = await _adb.GetPropertyAsync(
                    serial,
                    PropertyConstants.Debug.Enabled,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(debugValue.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The persisted property debug gate could not be enabled and verified.");

            var handled = new HashSet<string>(StringComparer.Ordinal);
            if (statuses.ContainsKey(PropertyConstants.Spoof.SimEnabled))
            {
                await ApplyPropertyStatusAsync(
                        serial,
                        PropertyConstants.Spoof.SimEnabled,
                        values,
                        statuses,
                        cancellationToken,
                        forceValue: "0")
                    .ConfigureAwait(false);
                handled.Add(PropertyConstants.Spoof.SimEnabled);
            }

            if (statuses.ContainsKey(PropertyConstants.Integrity.Enabled))
            {
                await ApplyPropertyStatusAsync(
                        serial,
                        PropertyConstants.Integrity.Enabled,
                        values,
                        statuses,
                        cancellationToken,
                        forceValue: "false")
                    .ConfigureAwait(false);
                handled.Add(PropertyConstants.Integrity.Enabled);
            }

            if (statuses.ContainsKey(PropertyConstants.Integrity.DroidGuardSdk))
            {
                await ApplyPropertyStatusAsync(
                        serial,
                        PropertyConstants.Integrity.DroidGuardSdk,
                        values,
                        statuses,
                        cancellationToken,
                        forceValue: "false")
                    .ConfigureAwait(false);
                handled.Add(PropertyConstants.Integrity.DroidGuardSdk);
            }

            foreach (string propertyName in ManagedDevicePropertyCatalog.Properties)
            {
                if (handled.Contains(propertyName)
                    || string.Equals(propertyName, PropertyConstants.Keybox.Enabled, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!statuses.ContainsKey(propertyName))
                    continue;

                await ApplyPropertyStatusAsync(
                        serial,
                        propertyName,
                        values,
                        statuses,
                        cancellationToken)
                    .ConfigureAwait(false);
                handled.Add(propertyName);
            }

            execution.PendingPropertyValues = values;
            execution.PendingPropertyStatuses = statuses;
            execution.PropertiesPrepared = true;
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            if (debugGateTouched)
            {
                try
                {
                    using var cleanupCancellation = new CancellationTokenSource(CleanupTimeout);
                    await _adb.SetPropertyAsync(
                            serial,
                            PropertyConstants.Debug.Enabled,
                            "false",
                            cleanupCancellation.Token)
                        .ConfigureAwait(false);
                    string debugValue = await _adb.GetPropertyAsync(
                            serial,
                            PropertyConstants.Debug.Enabled,
                            cleanupCancellation.Token)
                        .ConfigureAwait(false);
                    if (string.Equals(debugValue.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The persisted property debug gate remained enabled after restore cleanup.");
                }
                catch (Exception cleanupException)
                {
                    execution.Warnings.Add("property_debug_cleanup_failed");
                    _logger.LogError(
                        cleanupException,
                        "Property debug cleanup failed for {Serial}.",
                        serial);
                    if (primaryFailure is null)
                        throw;
                }
            }
        }
    }

    private async Task ApplyPropertyStatusAsync(
        string serial,
        string propertyName,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, BackupPropertyStatus> statuses,
        CancellationToken cancellationToken,
        string? forceValue = null)
    {
        BackupPropertyStatus status = statuses[propertyName];
        if (string.Equals(status.State, "excluded", StringComparison.Ordinal))
            return;

        string value;
        if (forceValue is not null)
        {
            value = forceValue;
        }
        else if (string.Equals(status.State, "backed_up", StringComparison.Ordinal))
        {
            if (!values.TryGetValue(propertyName, out value!))
            {
                throw new InvalidDataException(
                    $"The backup property {propertyName} is marked backed_up but has no value.");
            }
        }
        else if (status.State is "skipped_missing" or "skipped_empty" or "empty")
        {
            value = string.Empty;
        }
        else
        {
            return;
        }

        await _adb.SetPropertyAsync(serial, propertyName, value, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task FinalizePropertyStatesAsync(
        string serial,
        RestoreExecution execution,
        CancellationToken cancellationToken)
    {
        bool hasPendingPropertyStates = execution.PendingPropertyValues is not null
            && execution.PendingPropertyStatuses is not null;
        bool debugGateTouched = false;
        Exception? primaryFailure = null;

        try
        {
            if (hasPendingPropertyStates || execution.PendingKeyboxEnabledValue is not null)
            {
                await _adb.SetPropertyAsync(
                        serial,
                        PropertyConstants.Debug.Enabled,
                        "true",
                        cancellationToken)
                    .ConfigureAwait(false);
                debugGateTouched = true;
                string debugValue = await _adb.GetPropertyAsync(
                        serial,
                        PropertyConstants.Debug.Enabled,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!string.Equals(debugValue.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The persisted property debug gate could not be enabled for final state restoration.");
                }
            }

            if (hasPendingPropertyStates)
            {
                IReadOnlyDictionary<string, string> values = execution.PendingPropertyValues!;
                IReadOnlyDictionary<string, BackupPropertyStatus> statuses = execution.PendingPropertyStatuses!;

                // These gates are deliberately restored after all payloads.
                if (statuses.ContainsKey(PropertyConstants.Spoof.SimEnabled))
                {
                    await ApplyPropertyStatusAsync(
                            serial,
                            PropertyConstants.Spoof.SimEnabled,
                            values,
                            statuses,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (statuses.ContainsKey(PropertyConstants.Integrity.DroidGuardSdk))
                {
                    await ApplyPropertyStatusAsync(
                            serial,
                            PropertyConstants.Integrity.DroidGuardSdk,
                            values,
                            statuses,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (statuses.ContainsKey(PropertyConstants.Integrity.Enabled))
                {
                    await ApplyPropertyStatusAsync(
                            serial,
                            PropertyConstants.Integrity.Enabled,
                            values,
                            statuses,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (execution.PendingKeyboxEnabledValue is not null)
            {
                await _adb.SetPropertyAsync(
                        serial,
                        PropertyConstants.Keybox.Enabled,
                        execution.PendingKeyboxEnabledValue,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await RunRequiredShellAsync(
                    serial,
                    "sync",
                    "flush restored device state",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            if (debugGateTouched)
            {
                try
                {
                    using var cleanupCancellation = new CancellationTokenSource(CleanupTimeout);
                    await _adb.SetPropertyAsync(
                            serial,
                            PropertyConstants.Debug.Enabled,
                            "false",
                            cleanupCancellation.Token)
                        .ConfigureAwait(false);
                    string debugValue = await _adb.GetPropertyAsync(
                            serial,
                            PropertyConstants.Debug.Enabled,
                            cleanupCancellation.Token)
                        .ConfigureAwait(false);
                    if (string.Equals(debugValue.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "The persisted property debug gate remained enabled after finalization.");
                    }
                }
                catch (Exception cleanupException)
                {
                    execution.Warnings.Add("property_debug_cleanup_failed");
                    _logger.LogError(
                        cleanupException,
                        "Final property debug cleanup failed for {Serial}.",
                        serial);
                    if (primaryFailure is null)
                        throw;
                }
            }
        }
    }

    private async Task RestoreSettingsAsync(
        string serial,
        ZipArchive archive,
        BackupArchiveManifest manifest,
        RestoreExecution execution,
        CancellationToken cancellationToken)
    {
        if (!manifest.SelectedComponents.ManagedSystemSettings
            || string.Equals(manifest.SettingsStatus, "not_selected", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Managed system settings are not available in this backup.");
        }

        BackupSettingsPayload payload = await ReadJsonEntryAsync<BackupSettingsPayload>(
                archive,
                "settings.json",
                cancellationToken)
            .ConfigureAwait(false);
        Dictionary<string, string> values = payload.Values
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Single().Value, StringComparer.Ordinal);
        Dictionary<string, BackupSettingStatus> statuses = CreateUniqueStatusMap(
            manifest.SettingsStatuses,
            item => item.Name,
            "setting status");
        HashSet<string> knownSettings = ManagedSettings
            .Select(descriptor => $"{descriptor.Namespace}.{descriptor.Key}")
            .Append(PropertyConstants.Timezone)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string settingName in statuses.Keys)
        {
            if (!knownSettings.Contains(settingName))
                execution.Warnings.Add($"setting_not_whitelisted:{settingName}");
        }

        execution.MutationStarted = true;
        foreach (SettingDescriptor descriptor in ManagedSettings.Where(
                     descriptor => !IsTimezoneControl(descriptor)))
        {
            await RestoreManagedSettingAsync(
                    serial,
                    descriptor,
                    values,
                    statuses,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (statuses.TryGetValue(PropertyConstants.Timezone, out BackupSettingStatus? timezoneStatus))
        {
            if (string.Equals(timezoneStatus.State, "backed_up", StringComparison.Ordinal))
            {
                if (!values.TryGetValue(PropertyConstants.Timezone, out string? timezone))
                    throw new InvalidDataException("The backup timezone is marked backed_up but has no value.");

                await _adb.SetPropertyAsync(
                        serial,
                        PropertyConstants.Timezone,
                        timezone,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (timezoneStatus.State is "skipped_missing" or "skipped_empty" or "empty")
            {
                await _adb.SetPropertyAsync(
                        serial,
                        PropertyConstants.Timezone,
                        string.Empty,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await _adb.BroadcastAsync(
                    serial,
                    "android.intent.action.TIMEZONE_CHANGED",
                    cancellationToken)
                .ConfigureAwait(false);
            await _adb.BroadcastAsync(
                    serial,
                    "android.intent.action.TIME_SET",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (SettingDescriptor descriptor in ManagedSettings.Where(IsTimezoneControl))
        {
            await RestoreManagedSettingAsync(
                    serial,
                    descriptor,
                    values,
                    statuses,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task RestoreManagedSettingAsync(
        string serial,
        SettingDescriptor descriptor,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, BackupSettingStatus> statuses,
        CancellationToken cancellationToken)
    {
        string name = $"{descriptor.Namespace}.{descriptor.Key}";
        if (!statuses.TryGetValue(name, out BackupSettingStatus? status))
            return;

        if (string.Equals(status.State, "backed_up", StringComparison.Ordinal))
        {
            if (!values.TryGetValue(name, out string? value))
                throw new InvalidDataException($"The backup setting {name} is marked backed_up but has no value.");

            await _adb.PutSettingAsync(
                    serial,
                    descriptor.Namespace,
                    descriptor.Key,
                    value,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (status.State is "skipped_missing" or "skipped_empty" or "empty")
        {
            await _adb.DeleteSettingAsync(
                    serial,
                    descriptor.Namespace,
                    descriptor.Key,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task RestorePackagesAsync(
        string serial,
        ZipArchive archive,
        RestorePlan plan,
        string localSessionDirectory,
        string remoteSessionPath,
        DeviceRestoreOptions options,
        IProgress<DeviceRestoreProgress>? progress,
        RestoreExecution execution,
        CancellationToken cancellationToken)
    {
        foreach (PackagePlan packagePlan in plan.Packages)
        {
            bool selected = packagePlan.Manifest.Group switch
            {
                "apps" => options.RestoreUserAppData,
                "google" => options.RestoreGoogleAppData,
                _ => false
            };
            if (!selected)
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(
                DeviceRestoreStage.RestoringApps,
                packagePlan.Manifest.PackageName,
                packagePlan.Manifest.Group));

            if (packagePlan.Compatibility.Outcome != DeviceRestoreOutcome.Succeeded)
            {
                execution.AddPackage(new(
                    packagePlan.Manifest.PackageName,
                    packagePlan.Manifest.Group,
                    packagePlan.Compatibility.Outcome,
                    packagePlan.Compatibility.Reason));
                continue;
            }

            try
            {
                int restoredPayloads = await RestorePackageAsync(
                        serial,
                        archive,
                        packagePlan.Manifest,
                        packagePlan.Compatibility.TargetUid!.Value,
                        localSessionDirectory,
                        remoteSessionPath,
                        execution,
                        cancellationToken)
                    .ConfigureAwait(false);
                execution.AddPackage(new(
                    packagePlan.Manifest.PackageName,
                    packagePlan.Manifest.Group,
                    DeviceRestoreOutcome.Succeeded,
                    null,
                    restoredPayloads));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                execution.AddPackage(new(
                    packagePlan.Manifest.PackageName,
                    packagePlan.Manifest.Group,
                    DeviceRestoreOutcome.Failed,
                    exception.Message));
                execution.Warnings.Add($"package_restore_failed:{packagePlan.Manifest.PackageName}");
            }
        }

        if (options.RestoreUserAppData)
        {
            int appCount = plan.Packages.Count(item =>
                string.Equals(item.Manifest.Group, "apps", StringComparison.Ordinal));
            if (appCount > 0)
            {
                int restored = execution.Packages.Count(item =>
                    string.Equals(item.Group, "apps", StringComparison.Ordinal)
                    && item.Outcome == DeviceRestoreOutcome.Succeeded);
                int skipped = execution.Packages.Count(item =>
                    string.Equals(item.Group, "apps", StringComparison.Ordinal)
                    && item.Outcome == DeviceRestoreOutcome.Skipped);
                int failed = execution.Packages.Count(item =>
                    string.Equals(item.Group, "apps", StringComparison.Ordinal)
                    && item.Outcome == DeviceRestoreOutcome.Failed);
                execution.AddComponent(new(
                    "userAppData",
                    failed > 0 && restored == 0
                        ? DeviceRestoreOutcome.Failed
                        : failed > 0 || skipped > 0
                            ? DeviceRestoreOutcome.Partial
                            : DeviceRestoreOutcome.Succeeded,
                    null,
                    restored,
                    skipped,
                    failed));
            }
            else
            {
                execution.AddComponent(new(
                    "userAppData",
                    DeviceRestoreOutcome.Skipped,
                    "no_payload"));
            }
        }

        if (options.RestoreGoogleAppData)
        {
            int googleCount = plan.Packages.Count(item =>
                string.Equals(item.Manifest.Group, "google", StringComparison.Ordinal));
            int googleRestored = execution.Packages.Count(item =>
                string.Equals(item.Group, "google", StringComparison.Ordinal)
                && item.Outcome == DeviceRestoreOutcome.Succeeded);
            int googleSkipped = execution.Packages.Count(item =>
                string.Equals(item.Group, "google", StringComparison.Ordinal)
                && item.Outcome == DeviceRestoreOutcome.Skipped);
            int googleFailed = execution.Packages.Count(item =>
                string.Equals(item.Group, "google", StringComparison.Ordinal)
                && item.Outcome == DeviceRestoreOutcome.Failed);
            execution.AddComponent(new(
                "googleAppData",
                googleCount == 0
                    ? DeviceRestoreOutcome.Skipped
                    : googleFailed > 0 && googleRestored == 0
                        ? DeviceRestoreOutcome.Failed
                        : googleFailed > 0 || googleSkipped > 0
                            ? DeviceRestoreOutcome.Partial
                            : DeviceRestoreOutcome.Succeeded,
                googleCount == 0 ? "no_payload" : "experimental_google_state",
                googleRestored,
                googleSkipped,
                googleFailed,
                Experimental: true));
        }
    }

    private async Task<int> RestorePackageAsync(
        string serial,
        ZipArchive archive,
        PackageBackupManifest package,
        int targetUid,
        string localSessionDirectory,
        string remoteSessionPath,
        RestoreExecution execution,
        CancellationToken cancellationToken)
    {
        execution.MutationStarted = true;
        await _adb.ForceStopPackageAsync(serial, package.PackageName, cancellationToken)
            .ConfigureAwait(false);
        await _adb.ClearPackageAsync(serial, package.PackageName, cancellationToken)
            .ConfigureAwait(false);

        int payloadIndex = 0;
        var restoredPayloads = new HashSet<string>(StringComparer.Ordinal);
        foreach (BackupDataPathStatus pathStatus in package.Paths)
        {
            if (pathStatus.State is "skipped_missing" or "skipped_empty")
                continue;
            if (pathStatus.State is not ("backed_up" or "present_alias")
                || string.IsNullOrWhiteSpace(pathStatus.Payload))
            {
                continue;
            }

            string destinationPath = GetPackageRestorePath(package.PackageName, pathStatus);
            string payloadName = pathStatus.Payload!;
            if (!restoredPayloads.Add(payloadName))
                continue;

            string archiveEntryPath = $"{package.Group}/{package.PackageName}/{payloadName}";
            ZipArchiveEntry entry = GetRequiredEntry(archive, archiveEntryPath);
            string remoteTarPath = $"{remoteSessionPath}/package-{payloadIndex:D5}.tar";
            payloadIndex++;
            await PushZipEntryAsync(
                    serial,
                    entry,
                    localSessionDirectory,
                    remoteTarPath,
                    $"restore {package.PackageName} payload",
                    cancellationToken)
                .ConfigureAwait(false);

            string parentPath = GetUnixDirectoryName(destinationPath);
            await RunRequiredShellAsync(
                    serial,
                    $"mkdir -p {QuoteShellValue(parentPath)} && tar -xpf {QuoteShellValue(remoteTarPath)} -C {QuoteShellValue(parentPath)}",
                    $"extract {package.PackageName} payload",
                    cancellationToken)
                .ConfigureAwait(false);
            await RunRequiredShellAsync(
                    serial,
                    $"rm -f {QuoteShellValue(remoteTarPath)}",
                    "remove extracted package payload",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        string targetGroup = await ReadTargetGroupAsync(
                serial,
                $"/data/user/0/{package.PackageName}",
                targetUid,
                cancellationToken)
            .ConfigureAwait(false);
        string owner = $"{targetUid}:{targetGroup}";
        var targetOwnedPaths = new List<string>();
        var externalPaths = new List<string>();
        foreach (string path in new[]
                 {
                     $"/data/user/0/{package.PackageName}",
                     $"/data/data/{package.PackageName}",
                     $"/data/user_de/0/{package.PackageName}"
                 })
        {
            if (package.Paths.Any(item =>
                    (item.State is "backed_up" or "present_alias")
                    && GetPackageRestorePath(package.PackageName, item) == path))
            {
                targetOwnedPaths.Add(path);
            }
        }

        foreach (string path in targetOwnedPaths.Distinct(StringComparer.Ordinal))
        {
            await RunRequiredShellAsync(
                    serial,
                    $"if [ -e {QuoteShellValue(path)} ]; then chown -R {QuoteShellValue(owner)} {QuoteShellValue(path)} && restorecon -RF {QuoteShellValue(path)}; fi",
                    $"apply ownership to {package.PackageName} data",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (string path in new[]
                 {
                     $"/data/media/0/Android/data/{package.PackageName}",
                     $"/data/media/0/Android/media/{package.PackageName}",
                     $"/data/media/0/Android/obb/{package.PackageName}"
                 })
        {
            if (!package.Paths.Any(item =>
                    (item.State is "backed_up" or "present_alias")
                    && GetPackageRestorePath(package.PackageName, item) == path))
            {
                continue;
            }

            externalPaths.Add(path);
        }

        foreach (string path in externalPaths.Distinct(StringComparer.Ordinal))
        {
            await RunRequiredShellAsync(
                    serial,
                    $"if [ -e {QuoteShellValue(path)} ]; then restorecon -RF {QuoteShellValue(path)}; fi",
                    $"restore SELinux context for {package.PackageName} external data",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return restoredPayloads.Count;
    }

    private async Task RestoreKeyboxAsync(
        string serial,
        ZipArchive archive,
        BackupArchiveManifest manifest,
        string remoteSessionPath,
        string localSessionDirectory,
        RestoreExecution execution,
        CancellationToken cancellationToken)
    {
        BackupComponentStatus status = FindRequiredOptionalStatus(manifest, "keybox");
        if (status.State != "backed_up")
            throw new InvalidOperationException("The Keybox payload is not available in this backup.");

        ZipArchiveEntry entry = GetRequiredEntry(archive, "optional/keybox.xml");
        await ValidateXmlEntryAsync(entry, "Keybox", cancellationToken).ConfigureAwait(false);
        BackupPropertyPayload properties = await ReadJsonEntryAsync<BackupPropertyPayload>(
                archive,
                "properties.json",
                cancellationToken)
            .ConfigureAwait(false);
        Dictionary<string, string> values = new(properties.Values, StringComparer.Ordinal);
        Dictionary<string, BackupPropertyStatus> statuses = CreateUniqueStatusMap(
            manifest.PropertyStatuses,
            item => item.PropertyName,
            "property status");

        execution.MutationStarted = true;
        await _adb.SetPropertyAsync(
                serial,
                PropertyConstants.Keybox.Enabled,
                "false",
                cancellationToken)
            .ConfigureAwait(false);

        const string targetPath = "/data/system/keybox.xml";
        string metadata = await ReadFileMetadataAsync(serial, targetPath, cancellationToken)
            .ConfigureAwait(false);
        string remotePayloadPath = $"{remoteSessionPath}/keybox.xml";
        await PushZipEntryAsync(
                serial,
                entry,
                localSessionDirectory,
                remotePayloadPath,
                "restore Keybox payload",
                cancellationToken)
            .ConfigureAwait(false);
        await RunRequiredShellAsync(
                serial,
                $"mkdir -p /data/system && cp {QuoteShellValue(remotePayloadPath)} {QuoteShellValue(targetPath)}",
                "install Keybox payload",
                cancellationToken)
            .ConfigureAwait(false);
        await ApplyFileMetadataAsync(serial, targetPath, metadata, defaultMode: "600", cancellationToken)
            .ConfigureAwait(false);
        await RunRequiredShellAsync(
                serial,
                $"restorecon {QuoteShellValue(targetPath)}",
                "restore Keybox SELinux context",
                cancellationToken)
            .ConfigureAwait(false);

        if (statuses.TryGetValue(PropertyConstants.Keybox.Enabled, out BackupPropertyStatus? keyboxStatus))
        {
            execution.PendingKeyboxEnabledValue = keyboxStatus.State == "backed_up"
                ? values.TryGetValue(PropertyConstants.Keybox.Enabled, out string? savedValue)
                    ? savedValue
                    : throw new InvalidDataException("The Keybox enabled property is missing from the backup.")
                : keyboxStatus.State is "skipped_missing" or "skipped_empty" or "empty"
                    ? string.Empty
                    : "false";
        }
    }

    private async Task RestoreSsaidAsync(
        string serial,
        ZipArchive archive,
        string remoteSessionPath,
        string localSessionDirectory,
        RestoreExecution execution,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = GetRequiredEntry(archive, "experimental/settings_ssaid.xml");
        await ValidateXmlEntryAsync(entry, "SSAID", cancellationToken).ConfigureAwait(false);

        const string targetPath = "/data/system/users/0/settings_ssaid.xml";
        string metadata = await ReadFileMetadataAsync(serial, targetPath, cancellationToken)
            .ConfigureAwait(false);
        string remotePayloadPath = $"{remoteSessionPath}/settings_ssaid.xml";
        string remoteRollbackPath = $"{remoteSessionPath}/rollback-settings_ssaid.xml";
        bool rollbackAvailable = !string.IsNullOrWhiteSpace(metadata);
        execution.MutationStarted = true;

        try
        {
            if (rollbackAvailable)
            {
                await RunRequiredShellAsync(
                        serial,
                        $"cp {QuoteShellValue(targetPath)} {QuoteShellValue(remoteRollbackPath)}",
                        "save the current SSAID state",
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await PushZipEntryAsync(
                    serial,
                    entry,
                    localSessionDirectory,
                    remotePayloadPath,
                    "restore SSAID payload",
                    cancellationToken)
                .ConfigureAwait(false);
            await _adb.ForceStopPackageAsync(
                    serial,
                    "com.android.providers.settings",
                    cancellationToken)
                .ConfigureAwait(false);
            await RunRequiredShellAsync(
                    serial,
                    $"mkdir -p /data/system/users/0 && cp {QuoteShellValue(remotePayloadPath)} {QuoteShellValue(targetPath)}",
                    "install SSAID state",
                    cancellationToken)
                .ConfigureAwait(false);
            await ApplyFileMetadataAsync(serial, targetPath, metadata, defaultMode: "600", cancellationToken)
                .ConfigureAwait(false);
            await RunRequiredShellAsync(
                    serial,
                    $"restorecon {QuoteShellValue(targetPath)} && sync",
                    "restore SSAID SELinux context",
                    cancellationToken)
                .ConfigureAwait(false);
            // SettingsProvider owns an in-memory SettingsState. Restarting its
            // process plus the final device reboot prevents stale SSAID state
            // from surviving this operation.
            await _adb.ForceStopPackageAsync(
                    serial,
                    "com.android.providers.settings",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            if (rollbackAvailable)
            {
                try
                {
                    using var rollbackCancellation = new CancellationTokenSource(CleanupTimeout);
                    await RunRequiredShellAsync(
                            serial,
                            $"cp {QuoteShellValue(remoteRollbackPath)} {QuoteShellValue(targetPath)} && restorecon {QuoteShellValue(targetPath)}",
                            "rollback SSAID state",
                            rollbackCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(
                        rollbackException,
                        "SSAID restore rollback failed for {Serial}.",
                        serial);
                }
            }

            throw;
        }
    }

    private async Task RestoreGoogleAccountStateAsync(
        string serial,
        ZipArchive archive,
        BackupArchiveManifest manifest,
        string remoteSessionPath,
        string localSessionDirectory,
        RestoreExecution execution,
        CancellationToken cancellationToken)
    {
        var staged = new List<StagedAccountPayload>();
        int index = 0;
        foreach (AccountRestoreDescriptor descriptor in AccountPaths)
        {
            BackupComponentStatus? status = FindOptionalStatus(manifest, descriptor.Component);
            if (status is null || status.State != "backed_up")
                continue;

            ZipArchiveEntry entry = GetRequiredEntry(archive, descriptor.ArchivePath);
            string remoteTarPath = $"{remoteSessionPath}/account-{index:D5}.tar";
            string stageDirectory = $"{remoteSessionPath}/account-stage/{SanitizeShellPart(descriptor.Component)}";
            index++;
            await PushZipEntryAsync(
                    serial,
                    entry,
                    localSessionDirectory,
                    remoteTarPath,
                    $"stage {descriptor.Component}",
                    cancellationToken)
                .ConfigureAwait(false);
            await RunRequiredShellAsync(
                    serial,
                    $"mkdir -p {QuoteShellValue(stageDirectory)} && tar -xpf {QuoteShellValue(remoteTarPath)} -C {QuoteShellValue(stageDirectory)} && rm -f {QuoteShellValue(remoteTarPath)}",
                    $"stage {descriptor.Component} payload",
                    cancellationToken)
                .ConfigureAwait(false);

            string stagedPath = $"{stageDirectory}/{descriptor.FileName}";
            if (descriptor.IsDatabase)
            {
                string quickCheck = await RunShellOutputAsync(
                        serial,
                        $"if command -v sqlite3 >/dev/null 2>&1; then if [ \"$(sqlite3 {QuoteShellValue(stagedPath)} 'PRAGMA quick_check;' 2>/dev/null)\" = \"ok\" ]; then printf '%s\\n' __DDC_SQLITE_OK__; else printf '%s\\n' __DDC_SQLITE_INVALID__; fi; elif [ \"$(dd if={QuoteShellValue(stagedPath)} bs=1 count=16 2>/dev/null | od -An -tx1 | tr -d ' \\n')\" = \"53514c69746520666f726d6174203300\" ]; then printf '%s\\n' __DDC_SQLITE_HEADER_OK__; else printf '%s\\n' __DDC_SQLITE_INVALID__; fi",
                        $"validate {descriptor.Component} SQLite snapshot",
                        cancellationToken)
                    .ConfigureAwait(false);
                if (quickCheck is not ("__DDC_SQLITE_OK__" or "__DDC_SQLITE_HEADER_OK__"))
                {
                    throw new InvalidDataException(
                        $"The {descriptor.Component} SQLite snapshot could not be validated ({quickCheck}).");
                }
            }

            string livePath = $"{descriptor.Directory}/{descriptor.FileName}";
            string metadata = await ReadFileMetadataAsync(serial, livePath, cancellationToken)
                .ConfigureAwait(false);
            staged.Add(new StagedAccountPayload(descriptor, stagedPath, livePath, metadata));
        }

        if (staged.Count == 0)
            throw new InvalidOperationException("No restorable Google Account State payload is available.");

        execution.MutationStarted = true;
        bool frameworkStopped = false;
        try
        {
            await RunRequiredShellAsync(
                    serial,
                    "setprop ctl.stop system_server",
                    "stop Android framework for account-state replacement",
                    cancellationToken)
                .ConfigureAwait(false);
            frameworkStopped = true;
            await RunRequiredShellAsync(
                    serial,
                    "for i in 1 2 3 4 5 6 7 8 9 10; do if ! pidof system_server >/dev/null 2>&1; then break; fi; sleep 1; done; if pidof system_server >/dev/null 2>&1; then exit 1; fi",
                    "wait for Android framework shutdown",
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (StagedAccountPayload payload in staged)
            {
                string parent = GetUnixDirectoryName(payload.LivePath);
                await RunRequiredShellAsync(
                        serial,
                        $"mkdir -p {QuoteShellValue(parent)} && cp -f {QuoteShellValue(payload.StagedPath)} {QuoteShellValue(payload.LivePath)}",
                        $"replace {payload.Descriptor.Component} state",
                        cancellationToken)
                    .ConfigureAwait(false);
                await ApplyFileMetadataAsync(
                        serial,
                        payload.LivePath,
                        payload.Metadata,
                        defaultMode: payload.Descriptor.IsDatabase ? "600" : "600",
                        cancellationToken)
                    .ConfigureAwait(false);
                await RunRequiredShellAsync(
                        serial,
                        $"restorecon {QuoteShellValue(payload.LivePath)}",
                        $"restore {payload.Descriptor.Component} SELinux context",
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await RunRequiredShellAsync(serial, "sync", "flush restored account state", cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            if (frameworkStopped)
            {
                try
                {
                    using var recoveryCancellation = new CancellationTokenSource(CleanupTimeout);
                    await RunRequiredShellAsync(
                            serial,
                            "setprop ctl.start system_server",
                            "recover Android framework after account restore failure",
                            recoveryCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception recoveryException)
                {
                    _logger.LogError(
                        recoveryException,
                        "Android framework recovery failed after account restore failure on {Serial}.",
                        serial);
                }
            }

            throw;
        }
    }

    private async Task<RestorePlan> BuildRestorePlanAsync(
        string serial,
        DeviceRestoreOptions options,
        BackupArchiveManifest manifest,
        CancellationToken cancellationToken)
    {
        ValidateSelectedComponents(options, manifest);
        string? targetRole = await TryReadPropertyAsync(
                serial,
                PropertyConstants.DeepDroidDevice,
                cancellationToken)
            .ConfigureAwait(false);
        int? targetSdk = ParsePositiveInt(await TryReadPropertyAsync(
                serial,
                PropertyConstants.Runtime.AndroidSdkVersion,
                cancellationToken).ConfigureAwait(false));

        var warnings = new List<string>(manifest.Warnings ?? []);
        if (!string.Equals(manifest.SourceSerial, serial, StringComparison.OrdinalIgnoreCase))
            warnings.Add("source_serial_differs_from_target");
        if (!string.IsNullOrWhiteSpace(manifest.SourceDeviceRole)
            && !string.Equals(manifest.SourceDeviceRole, targetRole, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("source_device_role_differs_from_target");
        }
        if (options.RestoreKeybox)
            warnings.Add("keybox_sensitive_restore");
        if (options.RestoreSsaid
            && !string.Equals(manifest.SourceSerial, serial, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("ssaId_source_serial_differs");
        }
        if (options.RestoreGoogleAppData
            && (!string.Equals(manifest.SourceSerial, serial, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(manifest.SourceDeviceRole, targetRole, StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("google_app_data_source_differs");
        }

        bool accountStateAllowed = true;
        bool accountStatePartial = string.Equals(
            FindOptionalStatus(manifest, "googleAccountState")?.State,
            "partial",
            StringComparison.Ordinal);
        if (options.RestoreGoogleAccountState)
        {
            accountStateAllowed = string.Equals(
                    manifest.SourceSerial,
                    serial,
                    StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(manifest.SourceDeviceRole)
                && string.Equals(
                    manifest.SourceDeviceRole,
                    targetRole,
                    StringComparison.OrdinalIgnoreCase)
                && manifest.AndroidSdkVersion.HasValue
                && targetSdk.HasValue
                && manifest.AndroidSdkVersion.Value == targetSdk.Value;
            if (!accountStateAllowed)
                warnings.Add("google_account_state_incompatible");
            if (accountStatePartial)
                warnings.Add("google_account_state_partial");
        }

        var packagePlans = new List<PackagePlan>();
        foreach (PackageBackupManifest package in manifest.Packages ?? [])
        {
            if (package.Group is not ("apps" or "google"))
                continue;
            if (!IsValidPackageName(package.PackageName))
                throw new InvalidDataException("The archive contains an invalid package name.");

            DeviceRestorePackageCompatibility compatibility =
                await CheckPackageCompatibilityAsync(serial, package, cancellationToken)
                    .ConfigureAwait(false);
            packagePlans.Add(new PackagePlan(package, compatibility));
        }

        return new RestorePlan(
            manifest,
            packagePlans,
            warnings,
            accountStateAllowed,
            accountStatePartial,
            targetRole,
            targetSdk);
    }

    private async Task<DeviceRestorePackageCompatibility> CheckPackageCompatibilityAsync(
        string serial,
        PackageBackupManifest package,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(package.Status, "backed_up", StringComparison.Ordinal))
        {
            return new(
                package.PackageName,
                DeviceRestoreOutcome.Skipped,
                package.Reason ?? "source_package_unavailable",
                package.VersionCode,
                null,
                null);
        }

        bool installed = await _devicePackageService
            .IsPackageInstalledAsync(serial, package.PackageName, cancellationToken)
            .ConfigureAwait(false);
        if (!installed)
        {
            return new(
                package.PackageName,
                DeviceRestoreOutcome.Skipped,
                "package_not_installed",
                package.VersionCode,
                null,
                null);
        }

        CommandResult metadataResult = await _adb.RunAdbShellAsync(
                serial,
                $"dumpsys package {QuoteShellValue(package.PackageName)}",
                cancellationToken)
            .ConfigureAwait(false);
        if (metadataResult.ExitCode != 0)
        {
            return new(
                package.PackageName,
                DeviceRestoreOutcome.Skipped,
                "package_metadata_unavailable",
                package.VersionCode,
                null,
                null);
        }

        string output = metadataResult.StandardOutput;
        long? targetVersion = ParseLong(VersionCodePattern, output);
        long? targetUidLong = ParseLong(SourceUidPattern, output);
        int? targetUid = targetUidLong is >= 0 and <= int.MaxValue
            ? (int)targetUidLong.Value
            : null;
        string? targetDigest = ParseSigningCertificateSha256(output);

        if (package.SigningCertificateSha256 is null || targetDigest is null)
        {
            return new(
                package.PackageName,
                DeviceRestoreOutcome.Skipped,
                "signature_unavailable",
                package.VersionCode,
                targetVersion,
                targetUid);
        }

        if (!string.Equals(
                NormalizeDigest(package.SigningCertificateSha256),
                NormalizeDigest(targetDigest),
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                package.PackageName,
                DeviceRestoreOutcome.Skipped,
                "signature_mismatch",
                package.VersionCode,
                targetVersion,
                targetUid);
        }

        if (!package.VersionCode.HasValue || !targetVersion.HasValue)
        {
            return new(
                package.PackageName,
                DeviceRestoreOutcome.Skipped,
                "version_unavailable",
                package.VersionCode,
                targetVersion,
                targetUid);
        }

        if (targetVersion.Value < package.VersionCode.Value)
        {
            return new(
                package.PackageName,
                DeviceRestoreOutcome.Skipped,
                "target_version_older",
                package.VersionCode,
                targetVersion,
                targetUid);
        }

        if (!targetUid.HasValue)
        {
            return new(
                package.PackageName,
                DeviceRestoreOutcome.Skipped,
                "target_uid_unavailable",
                package.VersionCode,
                targetVersion,
                null);
        }

        bool hasPayload = package.Paths.Any(path =>
            path.State is "backed_up" or "present_alias"
            && !string.IsNullOrWhiteSpace(path.Payload));
        if (!hasPayload)
        {
            return new(
                package.PackageName,
                DeviceRestoreOutcome.Skipped,
                "payload_missing",
                package.VersionCode,
                targetVersion,
                targetUid);
        }

        return new(
            package.PackageName,
            DeviceRestoreOutcome.Succeeded,
            null,
            package.VersionCode,
            targetVersion,
            targetUid,
            targetVersion > package.VersionCode ? "newer_target_version" : null);
    }

    private async Task<BackupArchiveManifest> LoadAndValidateArchiveAsync(
        string plaintextPath,
        CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.OpenRead(plaintextPath);
        var normalizedEntries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string normalized = NormalizeArchivePath(entry.FullName);
            if (!normalizedEntries.TryAdd(normalized, entry))
                throw new InvalidDataException($"The backup ZIP contains a duplicate entry: {normalized}.");
        }

        ZipArchiveEntry manifestEntry = GetRequiredEntry(normalizedEntries, "manifest.json");
        byte[] manifestBytes;
        await using (Stream manifestStream = manifestEntry.Open())
        await using (var buffer = new MemoryStream())
        {
            await manifestStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            manifestBytes = buffer.ToArray();
        }

        using JsonDocument manifestDocument = JsonDocument.Parse(manifestBytes);
        if (manifestDocument.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The backup manifest is not a JSON object.");
        JsonElement checksumsElement = manifestDocument.RootElement.TryGetProperty(
                "checksums",
                out JsonElement checksumProperty)
            ? checksumProperty
            : throw new InvalidDataException("The backup manifest did not contain checksums.");
        if (checksumsElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The backup manifest checksums were invalid.");

        var checksumNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty checksum in checksumsElement.EnumerateObject())
        {
            string path = NormalizeArchivePath(checksum.Name);
            if (!checksumNames.Add(path))
                throw new InvalidDataException("The backup manifest contains duplicate checksum entries.");
            if (checksum.Value.ValueKind != JsonValueKind.String
                || !IsSha256(checksum.Value.GetString()))
            {
                throw new InvalidDataException($"The backup checksum for {path} was invalid.");
            }
        }

        BackupArchiveManifest manifest = JsonSerializer.Deserialize<BackupArchiveManifest>(
                manifestBytes,
                JsonOptions)
            ?? throw new InvalidDataException("The backup manifest could not be parsed.");
        ValidateManifestShape(manifest);

        if (manifest.Checksums.Count != checksumNames.Count)
            throw new InvalidDataException("The backup manifest checksum table was inconsistent.");

        foreach (string entryPath in normalizedEntries.Keys)
        {
            if (entryPath != "manifest.json" && !checksumNames.Contains(entryPath))
            {
                throw new InvalidDataException(
                    $"The backup ZIP contains an unlisted payload entry: {entryPath}.");
            }
        }

        foreach ((string path, string expectedHash) in manifest.Checksums)
        {
            string normalized = NormalizeArchivePath(path);
            if (!normalizedEntries.TryGetValue(normalized, out ZipArchiveEntry? entry))
                throw new InvalidDataException($"The backup payload {normalized} is missing.");

            await using Stream payloadStream = entry.Open();
            byte[] actualHash = await SHA256.HashDataAsync(payloadStream, cancellationToken)
                .ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(
                    actualHash,
                    Convert.FromHexString(expectedHash)))
            {
                throw new InvalidDataException($"The backup payload checksum did not match for {normalized}.");
            }
        }

        var requiredEntries = new HashSet<string>(StringComparer.Ordinal) { "manifest.json" };
        if (manifest.PropertiesStatus != "not_selected")
            requiredEntries.Add("properties.json");
        if (manifest.SettingsStatus != "not_selected")
            requiredEntries.Add("settings.json");

        var tarRoots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (PackageBackupManifest package in manifest.Packages)
        {
            string packageManifest = $"{package.Group}/{package.PackageName}/manifest.json";
            requiredEntries.Add(NormalizeArchivePath(packageManifest));
            foreach (BackupDataPathStatus path in package.Paths)
            {
                if (path.State is not ("backed_up" or "present_alias")
                    || string.IsNullOrWhiteSpace(path.Payload))
                {
                    continue;
                }

                string payloadPath = NormalizeArchivePath(
                    $"{package.Group}/{package.PackageName}/{path.Payload}");
                requiredEntries.Add(payloadPath);
                tarRoots.TryAdd(
                    payloadPath,
                    GetUnixFileName(GetPackageRestorePath(package.PackageName, path)));
            }
        }

        foreach (BackupComponentStatus component in manifest.OptionalComponents)
        {
            if (component.State != "backed_up")
                continue;

            string? payloadPath = component.Component switch
            {
                "keybox" => "optional/keybox.xml",
                "ssaId" => "experimental/settings_ssaid.xml",
                _ => AccountPaths.FirstOrDefault(item =>
                        string.Equals(item.Component, component.Component, StringComparison.Ordinal))
                    ?.ArchivePath
            };
            if (payloadPath is null)
                continue;

            string normalizedPayloadPath = NormalizeArchivePath(payloadPath);
            requiredEntries.Add(normalizedPayloadPath);
            if (normalizedPayloadPath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            {
                AccountRestoreDescriptor descriptor = AccountPaths.First(item =>
                    string.Equals(item.ArchivePath, normalizedPayloadPath, StringComparison.Ordinal));
                tarRoots.TryAdd(normalizedPayloadPath, descriptor.FileName);
            }
        }

        foreach (string requiredEntry in requiredEntries)
        {
            if (!normalizedEntries.ContainsKey(requiredEntry))
                throw new InvalidDataException($"The required backup entry {requiredEntry} is missing.");
        }

        foreach ((string tarPath, string expectedRoot) in tarRoots)
        {
            await ValidateTarEntryAsync(
                    normalizedEntries[tarPath],
                    expectedRoot,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (FindOptionalStatus(manifest, "keybox")?.State == "backed_up")
        {
            await ValidateXmlEntryAsync(
                    normalizedEntries["optional/keybox.xml"],
                    "Keybox",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (FindOptionalStatus(manifest, "ssaId")?.State == "backed_up")
        {
            await ValidateXmlEntryAsync(
                    normalizedEntries["experimental/settings_ssaid.xml"],
                    "SSAID",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (manifest.PropertiesStatus != "not_selected")
        {
            await ValidatePropertiesEntryAsync(
                    normalizedEntries["properties.json"],
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (manifest.SettingsStatus != "not_selected")
        {
            await ValidateSettingsEntryAsync(
                    normalizedEntries["settings.json"],
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (PackageBackupManifest package in manifest.Packages)
        {
            await ValidatePackageManifestEntryAsync(
                    normalizedEntries[$"{package.Group}/{package.PackageName}/manifest.json"],
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return manifest;
    }

    private static void ValidateManifestShape(BackupArchiveManifest manifest)
    {
        if (manifest.FormatVersion != 2
            || !manifest.Encrypted
            || manifest.EncryptionFormatVersion != BackupEnvelopeCrypto.FormatVersion)
        {
            throw new InvalidDataException("The backup manifest format is not supported.");
        }

        if (string.IsNullOrWhiteSpace(manifest.SourceSerial))
            throw new InvalidDataException("The backup manifest did not contain a source serial.");
        if (manifest.SelectedComponents is null)
            throw new InvalidDataException("The backup manifest component selection was missing.");
        if (manifest.Checksums is null)
            throw new InvalidDataException("The backup manifest checksums were missing.");

        CreateUniqueStatusMap(
            manifest.PropertyStatuses ?? [],
            item => item.PropertyName,
            "property status");
        CreateUniqueStatusMap(
            manifest.SettingsStatuses ?? [],
            item => item.Name,
            "setting status");
        CreateUniqueStatusMap(
            manifest.OptionalComponents ?? [],
            item => item.Component,
            "optional component");

        var packageNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (PackageBackupManifest package in manifest.Packages ?? [])
        {
            if (!IsValidPackageName(package.PackageName)
                || package.Group is not ("apps" or "google"))
            {
                throw new InvalidDataException("The backup manifest contains an invalid package entry.");
            }

            if (!packageNames.Add($"{package.Group}:{package.PackageName}"))
                throw new InvalidDataException("The backup manifest contains a duplicate package entry.");

            CreateUniqueStatusMap(
                package.Paths ?? [],
                item => item.DevicePath,
                $"package data path for {package.PackageName}");
        }
    }

    private static async Task ValidateTarEntryAsync(
        ZipArchiveEntry zipEntry,
        string expectedRoot,
        CancellationToken cancellationToken)
    {
        await using Stream tarStream = zipEntry.Open();
        using var reader = new TarReader(tarStream, leaveOpen: false);
        bool sawEntry = false;
        TarEntry? entry;
        while ((entry = reader.GetNextEntry(copyData: false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sawEntry = true;
            string name = NormalizeTarPath(entry.Name);
            if (name.Length == 0 || name == ".")
                throw new InvalidDataException("A tar payload contained an empty path.");
            if (name != expectedRoot
                && !name.StartsWith(expectedRoot + "/", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"A tar payload contained an entry outside its expected subtree: {name}.");
            }

            if (entry.EntryType is not (TarEntryType.V7RegularFile
                or TarEntryType.RegularFile
                or TarEntryType.ContiguousFile
                or TarEntryType.Directory))
            {
                throw new InvalidDataException($"The tar payload contains an unsafe entry type for {name}.");
            }

            if (entry.DataStream is not null)
                await entry.DataStream.CopyToAsync(Stream.Null, cancellationToken).ConfigureAwait(false);
        }

        if (!sawEntry)
            throw new InvalidDataException("A tar payload was empty.");
    }

    private static async Task ValidateXmlEntryAsync(
        ZipArchiveEntry entry,
        string componentName,
        CancellationToken cancellationToken)
    {
        await using Stream stream = entry.Open();
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 4 * 1024 * 1024,
            MaxCharactersFromEntities = 0
        };
        using XmlReader reader = XmlReader.Create(stream, settings);
        bool hasRoot = false;
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element && reader.Depth == 0)
                hasRoot = true;
        }

        if (!hasRoot)
            throw new InvalidDataException($"The {componentName} XML payload has no root element.");
    }

    private static async Task ValidatePropertiesEntryAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using Stream stream = entry.Open();
        using JsonDocument document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        JsonElement root = RequireJsonObject(document.RootElement, "properties.json");
        JsonElement values = RequireJsonProperty(root, "values", "properties.json");
        if (values.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The properties payload values were invalid.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in values.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw new InvalidDataException(
                    $"The properties payload contains a duplicate property: {property.Name}.");
            if (property.Value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException(
                    $"The property payload value for {property.Name} was not a string.");
        }
    }

    private static async Task ValidateSettingsEntryAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using Stream stream = entry.Open();
        using JsonDocument document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        JsonElement root = RequireJsonObject(document.RootElement, "settings.json");
        JsonElement values = RequireJsonProperty(root, "values", "settings.json");
        if (values.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The settings payload values were invalid.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement value in values.EnumerateArray())
        {
            JsonElement item = RequireJsonObject(value, "settings.json value");
            JsonElement name = RequireJsonProperty(item, "name", "settings.json value name");
            JsonElement settingValue = RequireJsonProperty(item, "value", "settings.json value");
            if (name.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(name.GetString()))
            {
                throw new InvalidDataException("A settings payload name was invalid.");
            }

            string settingName = name.GetString()!;
            if (!names.Add(settingName))
                throw new InvalidDataException(
                    $"The settings payload contains a duplicate setting: {settingName}.");
            if (settingValue.ValueKind != JsonValueKind.String)
                throw new InvalidDataException(
                    $"The settings payload value for {settingName} was not a string.");
        }
    }

    private static async Task ValidatePackageManifestEntryAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using Stream stream = entry.Open();
        using JsonDocument document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        JsonElement root = RequireJsonObject(document.RootElement, entry.FullName);
        JsonElement paths = RequireJsonProperty(root, "paths", entry.FullName);
        if (paths.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"The package manifest paths were invalid: {entry.FullName}.");
        }

        foreach (JsonElement value in paths.EnumerateArray())
        {
            JsonElement item = RequireJsonObject(value, $"{entry.FullName} path");
            JsonElement devicePath = RequireJsonProperty(
                item,
                "devicePath",
                $"{entry.FullName} path devicePath");
            JsonElement state = RequireJsonProperty(
                item,
                "state",
                $"{entry.FullName} path state");
            if (devicePath.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(devicePath.GetString())
                || state.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(state.GetString()))
            {
                throw new InvalidDataException(
                    $"A package manifest path was invalid: {entry.FullName}.");
            }
        }
    }

    private static JsonElement RequireJsonObject(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"The JSON payload was not an object: {path}.");
        return element;
    }

    private static JsonElement RequireJsonProperty(
        JsonElement objectElement,
        string propertyName,
        string path)
    {
        if (!objectElement.TryGetProperty(propertyName, out JsonElement property))
            throw new InvalidDataException($"The JSON payload was missing {propertyName}: {path}.");
        return property;
    }

    private static async Task<T> ReadJsonEntryAsync<T>(
        ZipArchive archive,
        string path,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = GetRequiredEntry(archive, path);
        await using Stream stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException($"The backup JSON entry {path} was empty.");
    }

    private static DeviceRestoreInspection CreateInspection(
        string archivePath,
        BackupArchiveManifest manifest)
    {
        return new DeviceRestoreInspection(
            archivePath,
            manifest.CreatedAtUtc,
            manifest.SourceSerial,
            manifest.SourceDeviceRole,
            manifest.AndroidSdkVersion,
            manifest.FormatVersion,
            manifest.EncryptionFormatVersion,
            manifest.SelectedComponents,
            manifest.Keybox,
            manifest.OptionalComponents,
            manifest.Packages,
            manifest.Warnings,
            [])
        {
            PropertiesStatus = manifest.PropertiesStatus,
            PropertyStatuses = manifest.PropertyStatuses,
            SettingsStatus = manifest.SettingsStatus,
            SettingsStatuses = manifest.SettingsStatuses
        };
    }

    private static void ValidateSelectedComponents(
        DeviceRestoreOptions options,
        BackupArchiveManifest manifest)
    {
        if (options.RestoreDeviceProperties
            && (!manifest.SelectedComponents.DeviceProperties
                || manifest.PropertiesStatus == "not_selected"))
        {
            throw new InvalidOperationException("Device properties are not available in this backup.");
        }

        if (options.RestoreManagedSystemSettings
            && (!manifest.SelectedComponents.ManagedSystemSettings
                || manifest.SettingsStatus == "not_selected"))
        {
            throw new InvalidOperationException("Managed system settings are not available in this backup.");
        }

        if (options.RestoreUserAppData && !manifest.SelectedComponents.UserAppData)
            throw new InvalidOperationException("User app data is not available in this backup.");

        ValidateOptionalSelection(options.RestoreKeybox, manifest, "keybox");
        ValidateOptionalSelection(options.RestoreSsaid, manifest, "ssaId");
        ValidateOptionalSelection(options.RestoreGoogleAppData, manifest, "googleAppData");
        ValidateOptionalSelection(options.RestoreGoogleAccountState, manifest, "googleAccountState");
    }

    private static void ValidateOptionalSelection(
        bool selected,
        BackupArchiveManifest manifest,
        string component)
    {
        if (!selected)
            return;

        BackupComponentStatus? status = FindOptionalStatus(manifest, component);
        if (status is null || status.State is not ("backed_up" or "partial"))
        {
            throw new InvalidOperationException(
                $"The selected restore component {component} is not available in this backup.");
        }
    }

    private static BackupComponentStatus FindRequiredOptionalStatus(
        BackupArchiveManifest manifest,
        string component)
    {
        return FindOptionalStatus(manifest, component)
            ?? throw new InvalidDataException($"The backup component {component} was not described.");
    }

    private static BackupComponentStatus? FindOptionalStatus(
        BackupArchiveManifest manifest,
        string component)
    {
        return manifest.OptionalComponents.FirstOrDefault(item =>
            string.Equals(item.Component, component, StringComparison.Ordinal));
    }

    private static Dictionary<string, T> CreateUniqueStatusMap<T>(
        IEnumerable<T> statuses,
        Func<T, string> keySelector,
        string description)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (T status in statuses)
        {
            string key = keySelector(status);
            if (string.IsNullOrWhiteSpace(key) || !result.TryAdd(key, status))
                throw new InvalidDataException($"The backup contains duplicate or empty {description} entries.");
        }

        return result;
    }

    private static string NormalizeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("The backup contains an empty archive path.");

        string normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/')
            || normalized.StartsWith("//", StringComparison.Ordinal)
            || Regex.IsMatch(normalized, @"^[A-Za-z]:/", RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException($"The backup contains an unsafe absolute path: {path}.");
        }

        string[] segments = normalized.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw new InvalidDataException($"The backup contains an unsafe path: {path}.");
        return string.Join('/', segments);
    }

    private static string NormalizeTarPath(string path)
    {
        return NormalizeArchivePath(path);
    }

    private static ZipArchiveEntry GetRequiredEntry(ZipArchive archive, string path)
    {
        return archive.GetEntry(NormalizeArchivePath(path))
            ?? throw new InvalidDataException($"The backup entry {path} is missing.");
    }

    private static ZipArchiveEntry GetRequiredEntry(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        string path)
    {
        string normalized = NormalizeArchivePath(path);
        return entries.TryGetValue(normalized, out ZipArchiveEntry? entry)
            ? entry
            : throw new InvalidDataException($"The backup entry {normalized} is missing.");
    }

    private async Task PushZipEntryAsync(
        string serial,
        ZipArchiveEntry entry,
        string localSessionDirectory,
        string remotePath,
        string purpose,
        CancellationToken cancellationToken)
    {
        string localPath = Path.Combine(
            localSessionDirectory,
            $"payload-{Guid.NewGuid():N}.tar");
        try
        {
            await using (Stream source = entry.Open())
            await using (FileStream destination = new(
                               localPath,
                               FileMode.CreateNew,
                               FileAccess.Write,
                               FileShare.None,
                               bufferSize: 81920,
                               useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            CommandResult result = await _adb.PushFileAsync(
                    serial,
                    localPath,
                    remotePath,
                    cancellationToken)
                .ConfigureAwait(false);
            EnsureCommandSuccess(result, purpose);
        }
        finally
        {
            TryDeleteFile(localPath);
        }
    }

    private async Task<string> ReadTargetGroupAsync(
        string serial,
        string path,
        int fallbackUid,
        CancellationToken cancellationToken)
    {
        string metadata = await ReadFileMetadataAsync(serial, path, cancellationToken).ConfigureAwait(false);
        Match match = FileMetadataPattern.Match(metadata);
        return match.Success ? match.Groups["gid"].Value : fallbackUid.ToString();
    }

    private async Task<string> ReadFileMetadataAsync(
        string serial,
        string path,
        CancellationToken cancellationToken)
    {
        const string missing = "__DDC_FILE_MISSING__";
        string output = await RunShellOutputAsync(
                serial,
                $"if [ -e {QuoteShellValue(path)} ]; then stat -c '%u:%g:%a' {QuoteShellValue(path)}; else printf '%s\\n' {missing}; fi",
                $"inspect metadata for {path}",
                cancellationToken)
            .ConfigureAwait(false);
        return output == missing ? string.Empty : output;
    }

    private async Task ApplyFileMetadataAsync(
        string serial,
        string path,
        string metadata,
        string defaultMode,
        CancellationToken cancellationToken)
    {
        Match match = FileMetadataPattern.Match(metadata);
        string owner = match.Success
            ? $"{match.Groups["uid"].Value}:{match.Groups["gid"].Value}"
            : "0:0";
        string mode = match.Success ? match.Groups["mode"].Value : defaultMode;
        await RunRequiredShellAsync(
                serial,
                $"chown {QuoteShellValue(owner)} {QuoteShellValue(path)} && chmod {QuoteShellValue(mode)} {QuoteShellValue(path)}",
                $"apply metadata to {path}",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> RunShellOutputAsync(
        string serial,
        string command,
        string purpose,
        CancellationToken cancellationToken)
    {
        CommandResult result = await _adb.RunAdbShellAsync(serial, command, cancellationToken)
            .ConfigureAwait(false);
        EnsureCommandSuccess(result, purpose);
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

    private async Task<string?> TryReadPropertyAsync(
        string serial,
        string propertyName,
        CancellationToken cancellationToken)
    {
        try
        {
            string value = await _adb.GetPropertyAsync(serial, propertyName, cancellationToken)
                .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Could not read target property {PropertyName} during restore preflight.",
                propertyName);
            return null;
        }
    }

    private static string GetPackageRestorePath(
        string packageName,
        BackupDataPathStatus pathStatus)
    {
        string expectedCe = $"/data/user/0/{packageName}";
        string legacyCe = $"/data/data/{packageName}";
        if (pathStatus.DevicePath == legacyCe && pathStatus.Payload == "ce.tar")
            return expectedCe;
        return pathStatus.DevicePath switch
        {
            var path when path == expectedCe => expectedCe,
            var path when path == legacyCe && pathStatus.Payload == "data-data.tar" => legacyCe,
            var path when path == $"/data/user_de/0/{packageName}" => path,
            var path when path == $"/data/media/0/Android/data/{packageName}" => path,
            var path when path == $"/data/media/0/Android/media/{packageName}" => path,
            var path when path == $"/data/media/0/Android/obb/{packageName}" => path,
            _ => throw new InvalidDataException($"The backup package path is not supported: {pathStatus.DevicePath}.")
        };
    }

    private static bool IsTimezoneControl(SettingDescriptor descriptor)
    {
        return descriptor.Namespace == DeviceSettingsInfoConstants.GlobalNamespace
            && descriptor.Key == "auto_time_zone"
            || descriptor.Namespace == DeviceSettingsInfoConstants.SystemNamespace
            && descriptor.Key == "time_12_24";
    }

    private static string GetUnixDirectoryName(string path)
    {
        int separator = path.LastIndexOf('/');
        if (separator <= 0)
            throw new InvalidDataException($"The device path has no safe parent: {path}.");
        return path[..separator];
    }

    private static string GetUnixFileName(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator < 0 ? path : path[(separator + 1)..];
    }

    private static string QuoteShellValue(string value)
    {
        return $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
    }

    private static string SanitizeShellPart(string value)
    {
        return string.Concat(value.Where(character =>
            character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_'
                or '.'));
    }

    private static bool IsValidPackageName(string packageName)
    {
        return packageName.Length <= 255 && ValidPackageName.IsMatch(packageName);
    }

    private static bool IsSha256(string? value)
    {
        return value is { Length: 64 } && value.All(Uri.IsHexDigit);
    }

    private static long? ParseLong(Regex pattern, string output)
    {
        Match match = pattern.Match(output);
        return match.Success && long.TryParse(match.Groups[1].Value, out long value)
            ? value
            : null;
    }

    private static int? ParsePositiveInt(string? value)
    {
        return int.TryParse(value, out int parsed) && parsed > 0 ? parsed : null;
    }

    private static string? ParseSigningCertificateSha256(string output)
    {
        MatchCollection matches = SigningDigestLabelPattern.Matches(output);
        if (matches.Count == 0)
            return null;

        var digests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in matches)
        {
            string normalized = NormalizeDigest(match.Groups["value"].Value);
            if (normalized.Length == 0)
                return null;
            digests.Add(normalized);
        }

        return digests.Count == 1 ? digests.First() : null;
    }

    private static string NormalizeDigest(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        string trimmed = value.Trim().Trim('"', '\'');
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[2..];
        string compactHex = string.Concat(trimmed.Where(character =>
            character != ':' && !char.IsWhiteSpace(character)));
        if (compactHex.Length == 64 && compactHex.All(Uri.IsHexDigit))
            return compactHex.ToUpperInvariant();

        string compactBase64 = string.Concat(trimmed.Where(character => !char.IsWhiteSpace(character)));
        if (compactBase64.Length == 43)
            compactBase64 += "=";
        if (compactBase64.Length == 44)
        {
            try
            {
                byte[] decoded = Convert.FromBase64String(compactBase64);
                if (decoded.Length == 32)
                    return Convert.ToHexString(decoded);
            }
            catch (FormatException)
            {
            }
        }

        return string.Empty;
    }

    private static string CreateLocalSessionDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DeepDroidChanger",
            "Restore",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void ValidateArchiveArguments(string archivePath, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("The selected backup archive was not found.", archivePath);
        if (string.IsNullOrWhiteSpace(password)
            || password.Length < DeviceRestoreOptions.MinimumRestorePasswordLength)
        {
            throw new ArgumentException(
                $"A restore password of at least {DeviceRestoreOptions.MinimumRestorePasswordLength} characters is required.",
                nameof(password));
        }
    }

    private async Task TryRecoverWithRebootAsync(string serial)
    {
        try
        {
            using var rebootCancellation = new CancellationTokenSource(CleanupTimeout);
            await _adb.RebootAsync(serial, rebootCancellation.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Best-effort restore recovery reboot failed for {Serial}.",
                serial);
        }
    }

    private static void EnsureCommandSuccess(CommandResult result, string purpose)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ADB failed while attempting to {purpose} (exit code {result.ExitCode}).");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed record SettingDescriptor(string Namespace, string Key);

    private sealed record AccountRestoreDescriptor(
        string Component,
        string Directory,
        string FileName,
        string ArchivePath,
        bool IsDatabase);

    private sealed record PackagePlan(
        PackageBackupManifest Manifest,
        DeviceRestorePackageCompatibility Compatibility);

    private sealed record RestorePlan(
        BackupArchiveManifest Manifest,
        IReadOnlyList<PackagePlan> Packages,
        IReadOnlyList<string> Warnings,
        bool AccountStateAllowed,
        bool AccountStatePartial,
        string? TargetDeviceRole,
        int? TargetSdk);

    private sealed record StagedAccountPayload(
        AccountRestoreDescriptor Descriptor,
        string StagedPath,
        string LivePath,
        string Metadata);

    private sealed class RestoreExecution
    {
        public List<DeviceRestoreComponentResult> Components { get; } = [];
        public List<DeviceRestorePackageResult> Packages { get; } = [];
        public List<string> Warnings { get; } = [];
        public bool MutationStarted { get; set; }
        public bool Rebooted { get; set; }
        public bool PropertiesPrepared { get; set; }
        public IReadOnlyDictionary<string, string>? PendingPropertyValues { get; set; }
        public IReadOnlyDictionary<string, BackupPropertyStatus>? PendingPropertyStatuses { get; set; }
        public string? PendingKeyboxEnabledValue { get; set; }
        public string? FailureReason { get; set; }

        public void AddComponent(DeviceRestoreComponentResult component)
        {
            Components.RemoveAll(item =>
                string.Equals(item.Component, component.Component, StringComparison.Ordinal));
            Components.Add(component);
        }

        public void AddPackage(DeviceRestorePackageResult package)
        {
            Packages.Add(package);
        }

        public DeviceRestoreResult CreateResult(bool forceFailed = false)
        {
            bool hasSuccessfulWork = Components.Any(component =>
                    component.Outcome == DeviceRestoreOutcome.Succeeded)
                || Packages.Any(package => package.Outcome == DeviceRestoreOutcome.Succeeded);
            bool hasFailureReason = !string.IsNullOrWhiteSpace(FailureReason);
            DeviceRestoreOutcome outcome = forceFailed
                ? DeviceRestoreOutcome.Failed
                : hasFailureReason
                    ? hasSuccessfulWork
                        ? DeviceRestoreOutcome.Partial
                        : DeviceRestoreOutcome.Failed
                : Components.Any(component => component.Outcome == DeviceRestoreOutcome.Failed)
                    ? Components.Any(component => component.Outcome == DeviceRestoreOutcome.Succeeded)
                        ? DeviceRestoreOutcome.Partial
                        : DeviceRestoreOutcome.Failed
                    : Components.Any(component => component.Outcome is DeviceRestoreOutcome.Partial
                        or DeviceRestoreOutcome.Skipped)
                        || Packages.Any(package => package.Outcome is DeviceRestoreOutcome.Failed or DeviceRestoreOutcome.Skipped)
                        ? DeviceRestoreOutcome.Partial
                        : DeviceRestoreOutcome.Succeeded;

            return new DeviceRestoreResult(
                outcome,
                Components,
                Packages,
                Warnings,
                Rebooted,
                FailureReason);
        }
    }
}
