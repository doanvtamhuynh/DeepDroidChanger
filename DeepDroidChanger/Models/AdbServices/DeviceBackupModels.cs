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