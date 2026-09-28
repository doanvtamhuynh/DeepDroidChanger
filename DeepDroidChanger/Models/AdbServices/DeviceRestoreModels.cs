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
    public const int MinimumRestorePasswordLength = DeviceBackupOptions.MinimumBackupPasswordLength;

    [JsonIgnore]
    public string RestorePassword { get; init; } = string.Empty;

    [JsonIgnore]
    public bool HasSelectedComponent =>
        RestoreDeviceProperties
        || RestoreManagedSystemSettings
        || RestoreUserAppData
        || RestoreKeybox
        || RestoreSsaid
        || RestoreGoogleAppData
        || RestoreGoogleAccountState;

    [JsonIgnore]
    public bool HasValidRestorePassword =>
        !string.IsNullOrWhiteSpace(RestorePassword)
        && RestorePassword.Length >= MinimumRestorePasswordLength;

    public override string ToString()
    {
        return $"{nameof(DeviceRestoreOptions)}(encryptedArchive=true)";
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
    public const int MinimumRestorePasswordLength = DeviceRestoreOptions.MinimumRestorePasswordLength;

    [JsonIgnore]
    public string RestorePassword { get; init; } = string.Empty;

    [JsonIgnore]
    public IReadOnlyList<DeviceRestoreInspection> Inspections { get; init; } = [];

    [JsonIgnore]
    public bool HasSelectedComponent =>
        RestoreDeviceProperties
        || RestoreManagedSystemSettings
        || RestoreUserAppData
        || RestoreKeybox
        || RestoreSsaid
        || RestoreGoogleAppData
        || RestoreGoogleAccountState;

    [JsonIgnore]
    public bool HasValidRestorePassword =>
        !string.IsNullOrWhiteSpace(RestorePassword)
        && RestorePassword.Length >= MinimumRestorePasswordLength;

    public override string ToString()
    {
        return $"{nameof(DeviceRestoreBatchOptions)}(encryptedArchives={ArchivePaths.Count})";
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
        && !string.Equals(PropertiesStatus, "not_selected", StringComparison.Ordinal);

    public bool HasManagedSystemSettings =>
        SelectedComponents.ManagedSystemSettings
        && !string.Equals(SettingsStatus, "not_selected", StringComparison.Ordinal);

    public bool HasUserAppData =>
        SelectedComponents.UserAppData
        && Packages.Any(package => string.Equals(package.Group, "apps", StringComparison.Ordinal));

    public bool HasKeybox => SelectedComponents.Keybox && HasAvailableOptionalComponent("keybox");
    public bool HasSsaid => SelectedComponents.Ssaid && HasAvailableOptionalComponent("ssaId");
    public bool HasGoogleAppData => SelectedComponents.GoogleAppData && HasAvailableOptionalComponent("googleAppData");

    public bool HasGoogleAccountState => SelectedComponents.GoogleAccountState
        && HasAvailableOptionalComponent("googleAccountState");

    public string PropertiesStatus { get; init; } = "not_selected";
    public IReadOnlyList<BackupPropertyStatus> PropertyStatuses { get; init; } = [];
    public string SettingsStatus { get; init; } = "not_selected";
    public IReadOnlyList<BackupSettingStatus> SettingsStatuses { get; init; } = [];

    public bool HasAvailableOptionalComponent(string component)
    {
        return OptionalComponents.Any(item =>
            string.Equals(item.Component, component, StringComparison.Ordinal)
            && item.State is "backed_up" or "partial");
    }

    public BackupComponentStatus? FindOptionalComponent(string component)
    {
        return OptionalComponents.FirstOrDefault(item =>
            string.Equals(item.Component, component, StringComparison.Ordinal));
    }
}

public sealed record DeviceRestorePackageResult(
    string PackageName,
    string Group,
    DeviceRestoreOutcome Outcome,
    string? Reason,
    int RestoredPayloadCount = 0,
    int SkippedPayloadCount = 0);

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
