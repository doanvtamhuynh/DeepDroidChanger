using System.Text.Json.Serialization;

namespace DeepDroidChanger.Models;

public sealed record DeviceRestoreOptions(
    string ArchivePath,
    bool RestoreDeviceProperties = true,
    bool RestoreManagedSystemSettings = true,
    bool RestoreUserAppData = true,
    bool RestoreKeybox = false,
    bool RestoreSsaid = false,
    bool RestoreGoogleAppData = false,
    bool RestoreGoogleAccountState = false)
{
    [JsonIgnore]
    public bool HasSelectedComponent =>
        RestoreDeviceProperties
        || RestoreManagedSystemSettings
        || RestoreUserAppData
        || RestoreKeybox
        || RestoreSsaid
        || RestoreGoogleAppData
        || RestoreGoogleAccountState;

    public override string ToString()
    {
        return $"{nameof(DeviceRestoreOptions)}(format=3,passwordless=true)";
    }
}

public sealed record DeviceRestoreBatchOptions(
    IReadOnlyList<string> ArchivePaths,
    bool RestoreDeviceProperties = true,
    bool RestoreManagedSystemSettings = true,
    bool RestoreUserAppData = true,
    bool RestoreKeybox = false,
    bool RestoreSsaid = false,
    bool RestoreGoogleAppData = false,
    bool RestoreGoogleAccountState = false)
{
    [JsonIgnore]
    public IReadOnlyList<DeviceRestoreInspection> Inspections { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyList<DeviceRestoreInspectionFailure> FailedInspections { get; init; } = [];

    [JsonIgnore]
    public bool HasSelectedComponent =>
        RestoreDeviceProperties
        || RestoreManagedSystemSettings
        || RestoreUserAppData
        || RestoreKeybox
        || RestoreSsaid
        || RestoreGoogleAppData
        || RestoreGoogleAccountState;

    public override string ToString()
    {
        return $"{nameof(DeviceRestoreBatchOptions)}(format=3,passwordless=true,archives={ArchivePaths.Count})";
    }
}

public enum DeviceRestoreStage
{
    Preparing,
    ValidatingArchive,
    CheckingCompatibility,
    RestoringProperties,
    RestoringSettings,
    RestoringApps,
    RestoringOptionalData,
    Finalizing,
    Rebooting,
    Completed
}

public sealed record DeviceRestoreProgress(
    DeviceRestoreStage Stage,
    string? PackageName = null,
    string? Component = null);

public enum DeviceRestoreOutcome
{
    Succeeded,
    Partial,
    Failed,
    NotSelected,
    Skipped
}

public sealed record DeviceRestorePackageCompatibility(
    string PackageName,
    DeviceRestoreOutcome Outcome,
    string? Reason,
    long? SourceVersionCode,
    long? TargetVersionCode,
    int? TargetUid,
    string? Warning = null);

public sealed record DeviceRestoreCompatibility(
    bool IsCompatible,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<DeviceRestorePackageCompatibility> Packages);

public sealed record DeviceRestoreInspection(
    string ArchivePath,
    DateTime CreatedAtUtc,
    string SourceSerial,
    string? SourceDeviceRole,
    int? AndroidSdkVersion,
    int FormatVersion,
    int EncryptionFormatVersion,
    BackupSelectedComponents SelectedComponents,
    BackupKeyboxState? Keybox,
    IReadOnlyList<BackupComponentStatus> OptionalComponents,
    IReadOnlyList<PackageBackupManifest> Packages,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> ValidationWarnings)
{
    public bool HasDeviceProperties =>
        SelectedComponents.DeviceProperties
        && PropertiesStatus is "backed_up" or "keybox_property_only"
        && PropertyStatuses.Count > 0;

    public bool HasManagedSystemSettings =>
        SelectedComponents.ManagedSystemSettings
        && SettingsStatus == "backed_up"
        && SettingsStatuses.Count > 0;

    public bool HasUserAppData =>
        SelectedComponents.UserAppData
        && Packages.Any(package =>
            string.Equals(package.Group, "apps", StringComparison.Ordinal)
            && string.Equals(package.Status, "backed_up", StringComparison.Ordinal)
            && package.Paths.Any(path =>
                path.State is "backed_up" or "present_alias"
                && !string.IsNullOrWhiteSpace(path.Payload)));

    public bool HasKeybox => SelectedComponents.Keybox
        && (FindOptionalComponent("keybox")?.State == "backed_up"
            || (FindOptionalComponent("keybox")?.State is "partial" or "skipped_missing"
                && HasKeyboxEnabledPropertyState));
    public bool HasSsaid => SelectedComponents.Ssaid
        && FindOptionalComponent("ssaId")?.State == "backed_up";
    public bool HasGoogleAppData =>
        SelectedComponents.GoogleAppData
        && HasAvailableOptionalComponent("googleAppData")
        && Packages.Any(package =>
            string.Equals(package.Group, "google", StringComparison.Ordinal)
            && string.Equals(package.Status, "backed_up", StringComparison.Ordinal)
            && package.Paths.Any(path =>
                path.State is "backed_up" or "present_alias"
                && !string.IsNullOrWhiteSpace(path.Payload)));

    public bool HasGoogleAccountState => SelectedComponents.GoogleAccountState
        && HasAvailableOptionalComponent("googleAccountState")
        && OptionalComponents.Any(item =>
            item.Component.StartsWith("account.", StringComparison.Ordinal)
            && string.Equals(item.State, "backed_up", StringComparison.Ordinal));

    public string PropertiesStatus { get; init; } = "not_selected";
    public IReadOnlyList<BackupPropertyStatus> PropertyStatuses { get; init; } = [];
    public string SettingsStatus { get; init; } = "not_selected";
    public IReadOnlyList<BackupSettingStatus> SettingsStatuses { get; init; } = [];

    public bool HasKeyboxEnabledPropertyState =>
        PropertyStatuses.Any(item =>
            string.Equals(
                item.PropertyName,
                DeepDroidChanger.Constants.PropertyConstants.Keybox.Enabled,
                StringComparison.Ordinal)
            && item.State is "backed_up" or "skipped_missing" or "skipped_empty" or "empty");

    public bool HasAvailableOptionalComponent(string component)
    {
        return OptionalComponents.Any(item =>
            string.Equals(item.Component, component, StringComparison.Ordinal)
            && item.State is ("backed_up" or "partial"));
    }

    public BackupComponentStatus? FindOptionalComponent(string component)
    {
        return OptionalComponents.FirstOrDefault(item =>
            string.Equals(item.Component, component, StringComparison.Ordinal));
    }
}

public sealed record DeviceRestoreInspectionFailure(
    string ArchivePath,
    string Reason);

public sealed record DeviceRestorePackageResult(
    string PackageName,
    string Group,
    DeviceRestoreOutcome Outcome,
    string? Reason,
    int RestoredPayloadCount = 0,
    int SkippedPayloadCount = 0,
    string? Warning = null);

public sealed record DeviceRestoreComponentResult(
    string Component,
    DeviceRestoreOutcome Outcome,
    string? Reason,
    int RestoredCount = 0,
    int SkippedCount = 0,
    int FailedCount = 0,
    bool Experimental = false);

public sealed record DeviceRestoreResult(
    DeviceRestoreOutcome Outcome,
    IReadOnlyList<DeviceRestoreComponentResult> Components,
    IReadOnlyList<DeviceRestorePackageResult> Packages,
    IReadOnlyList<string> Warnings,
    bool Rebooted,
    string? FailureReason = null);
