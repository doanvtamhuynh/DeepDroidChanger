namespace DeepDroidChanger.Models;

public sealed record DeviceBackupOptions(
    bool IncludeDeviceProperties = true,
    bool IncludeManagedSystemSettings = true,
    bool IncludeUserAppData = true,
    bool IncludeKeybox = false,
    bool IncludeSsaid = false,
    bool IncludeGoogleAppData = false,
    bool IncludeGoogleAccountState = false,
    string? DestinationDirectory = null)
{
    public bool HasSelectedComponent =>
        IncludeDeviceProperties
        || IncludeManagedSystemSettings
        || IncludeUserAppData
        || IncludeKeybox
        || IncludeSsaid
        || IncludeGoogleAppData
        || IncludeGoogleAccountState;

    public override string ToString()
    {
        return $"{nameof(DeviceBackupOptions)}(format=3,passwordless=true)";
    }
}

public enum DeviceBackupStage
{
    Preparing,
    ReadingProperties,
    ReadingSettings,
    BackingUpApps,
    BackingUpOptionalData,
    Finalizing,
    Completed
}

public sealed record DeviceBackupProgress(
    DeviceBackupStage Stage,
    string? PackageName = null);

public sealed record DeviceBackupResult(
    string ArchivePath,
    DateTime CreatedAtUtc);

// These archive contracts are intentionally shared by backup and restore.
// Keep their member names and ordering stable because the ZIP payload format
// is a persisted interchange contract.
public sealed record BackupPropertyPayload(IReadOnlyDictionary<string, string> Values);

public sealed record BackupSettingsPayload(IReadOnlyList<BackupSettingValue> Values);

public sealed record BackupSettingValue(string Name, string Value);

public sealed record BackupPropertyStatus(string PropertyName, string State, string? Reason);

public sealed record BackupSettingStatus(string Name, string State, string? Reason);

public sealed record BackupDataPathStatus(string DevicePath, string State, string? Payload);

public sealed record BackupComponentStatus(
    string Component,
    string State,
    string? Reason,
    bool? Experimental = null,
    bool? ConsistencyGuaranteed = null,
    string? SnapshotMethod = null);

public sealed record BackupKeyboxState(
    bool FilePresent,
    string EnabledPropertyName,
    bool? EnabledPropertyPresent,
    string EnabledPropertyState);

public sealed record BackupSelectedComponents(
    bool DeviceProperties,
    bool ManagedSystemSettings,
    bool UserAppData,
    bool Keybox,
    bool Ssaid,
    bool GoogleAppData,
    bool GoogleAccountState);

public sealed record PackageBackupManifest(
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

public sealed record BackupArchiveManifest(
    int FormatVersion,
    bool Encrypted,
    int EncryptionFormatVersion,
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
