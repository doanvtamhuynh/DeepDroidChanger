namespace DeepDroidChanger.Models;

public enum TwrpOperationKind { Backup, Restore }
public enum TwrpOperationState
{
    Preparing, EnteringRecovery, RecoveryReady, NativeBackupRunning, NativeRestoreRunning,
    NativeCompleted, Downloading, Packaging, Verifying, Cleaning, Completed, Failed, Stopping, Stopped
}

public sealed record TwrpPartition(string Id, string MountPoint, string BlockName, long Bytes, bool Sensitive = false);
public sealed record TwrpRecoveryImage(string Device, string FileName)
{
    public static IReadOnlyList<TwrpRecoveryImage> Available { get; } =
    [new("sargo", "twrp_sargo.img"), new("starlte", "twrp_starlte.img"), new("tissot", "twrp_tissot_treble.img")];
}
public static class TwrpPartitionCatalog
{
    // Keep older archives readable without showing their options in UI.
    public static IReadOnlyList<TwrpPartition> Known { get; } =
    [
        new("data", "/data", "userdata", 0), new("boot", "/boot", "boot", 0),
        new("system", "/system", "system", 0), new("vendor", "/vendor", "vendor", 0),
        new("system_image", "/system_image", "system", 0), new("vendor_image", "/vendor_image", "vendor", 0),
        new("super", "/super", "super", 0), new("recovery", "/recovery", "recovery", 0), new("cache", "/cache", "cache", 0),
        new("efs", "/efs", "efs", 0, true), new("efs1", "/ddc_efs1", "modemst1", 0, true),
        new("efs2", "/ddc_efs2", "modemst2", 0, true), new("fsg", "/ddc_fsg", "fsg", 0, true),
        new("modem", "/ddc_modem", "modem", 0, true), new("persist", "/persist_image", "persist", 0, true)
    ];
}
// Retained device fields describe portable archive formats 1-6. No live inventory is collected.
public sealed record TwrpDeviceInfo(
    string Serial, string Codename, string Fingerprint, string VendorFingerprint, int AndroidSdk,
    string Slot, string BootDevice, long BootBytes, string LayoutHash, string Hardware,
    long DataBytes, IReadOnlyList<TwrpPartition> Partitions, IReadOnlyList<string> StagingCandidates);
public sealed record TwrpRequest(string Serial, string Confirmation,
    string? BackupName = null, string? BackupDirectory = null, string? SourceArchivePath = null,
    string? RecoveryDevice = null, string? SourceImagePath = null);
public sealed record TwrpBackupOptions(bool Compression = true, bool GenerateDigest = false, bool SkipSpaceCheck = false, bool VerifyDigest = false);
public sealed record TwrpArchivePayload(string Path, long Bytes, string? PartitionId, string? Sha256 = null);
public sealed record TwrpBackupManifest(int Version, Guid BackupId, string Name, DateTimeOffset CreatedAt,
    TwrpDeviceInfo Device, TwrpBackupOptions Options, string[] Partitions, TwrpArchivePayload[] Payload, bool NativeBoot = false);
// Portable Data archives contain identity and payload metadata, not the runtime inventory.
internal sealed record TwrpCliBackupManifest(int Version, Guid BackupId, string Name, DateTimeOffset CreatedAt,
    string DeviceMarker, string Serial, string Slot, string LayoutHash, string RecoveryDevice,
    string[] Partitions, TwrpArchivePayload[] Payload);
internal sealed record TwrpDataBackupManifest(int Version, Guid BackupId, string Name, DateTimeOffset CreatedAt,
    string DeviceMarker, string Serial, string RecoveryDevice, string[] Partitions, TwrpArchivePayload[] Payload);
// DeviceMarker is the captured ro.deepdroid.device value, never a product/build property.
public sealed record TwrpBackupType(int Version, Guid BackupId, string Name, DateTimeOffset CreatedAt,
    string DeviceMarker, string Serial, string Slot, string RecoveryDevice, string[] Partitions);
public sealed record TwrpDialogResult(TwrpRequest Request);
public sealed record TwrpArtifact(string Name, long Bytes);
public sealed record TwrpProgress(Guid OperationId, TwrpOperationState State, string MessageKey,
    string? Detail = null, double? Percent = null, TimeSpan? Elapsed = null, string? ProcessStatus = null);

/// <summary>Operation state exists only in memory and is never deserialized.</summary>
public sealed class TwrpOperation
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public TwrpOperationKind Kind { get; init; }
    public TwrpOperationState State { get; set; } = TwrpOperationState.Preparing;
    public TwrpDeviceInfo Device { get; init; } = null!;
    public string NativeFolder { get; set; } = "";
    public string? RecoveryDevice { get; init; }
    public bool CliBackup => Kind == TwrpOperationKind.Backup;
    public bool CliRestore => Kind == TwrpOperationKind.Restore;
    public string? CacheDirectory { get; set; }
    internal List<string> PreparationLog { get; } = [];
    public bool NativeStarted { get; set; }
    public bool NativeCompleted { get; set; }
    public bool NativeResultVerified { get; set; }
    public bool RemoteLockOwned { get; set; }
    public bool RemotePayloadOwned { get; set; }
    public bool NativeStateUnknown { get; set; }
    internal int? RecoveryScreenTimeout { get; set; }
    public TwrpArtifact[] Artifacts { get; set; } = [];
    public string? WorkflowError { get; set; }
    public TwrpBackupOptions Options { get; set; } = new();
    public string? ArchivePath { get; init; }
    public bool StopRequested { get; set; }
}

public sealed class TwrpWorkflowException(string messageKey) : InvalidOperationException(messageKey)
{
    public string MessageKey { get; } = messageKey;
}
