namespace DeepDroidChanger.Models;

public enum TwrpOperationKind { LegacyProbe = 0, Backup = 1, Restore = 2, Recover = 3 }
public enum TwrpSessionState
{
    Preparing, OriginalSaved, ImagePrepared, FlashIntent, TwrpFlashed, Rebooted,
    RecoveryRunning, RestoreBootIntent, BootRestored, Completed, Failed, NeedsRecovery
}

public sealed record TwrpPartition(string Id, string MountPoint, string BlockName, long Bytes, bool Sensitive = false);
public sealed record TwrpRecoveryImage(string Device, string FileName)
{
    public static IReadOnlyList<TwrpRecoveryImage> Available { get; } =
    [new("sargo", "twrp_sargo.img"), new("starlte", "twrp_starlte.img"), new("tissot", "twrp_tissot_treble.img")];
}
public static class TwrpPartitionCatalog
{
    public static IReadOnlyList<TwrpPartition> All { get; } =
    [new("super", "/super", "super", 0), new("data", "/data", "userdata", 0),
     new("persist", "/persist_image", "persist", 0, true), new("boot", "/boot", "boot", 0)];
    // Keep older archives/journals readable without showing their options in UI.
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
// BootBytes retains the journal schema name; new sessions use the local image
// container capacity here. No remote boot-size/readback command is issued.
public sealed record TwrpDeviceInfo(
    string Serial, string Codename, string Fingerprint, string VendorFingerprint, int AndroidSdk,
    string Slot, string BootDevice, long BootBytes, string LayoutHash, string Hardware,
    long DataBytes, IReadOnlyList<TwrpPartition> Partitions, IReadOnlyList<string> StagingCandidates);
public sealed record TwrpPreflight(TwrpDeviceInfo? Device, IReadOnlyList<string> Issues, IReadOnlyList<string>? EnginePartitions = null)
{
    public bool CanStart => Device is not null && Issues.Count == 0;
}
public sealed record TwrpRequest(
    string Serial, IReadOnlyList<string> Partitions, string StorageRoot,
    string RescueDirectory, string Confirmation, Guid? SourceBackupId = null, string? StagingRoot = null,
    TwrpBackupOptions? Options = null, string? BackupName = null, string? BackupDirectory = null, string? SourceArchivePath = null,
    string? RecoveryDevice = null, string? SourceImagePath = null);
public sealed record TwrpBackupOptions(bool Compression = true, bool GenerateDigest = false, bool SkipSpaceCheck = false, bool VerifyDigest = false);
public enum TwrpExportState { None, Downloading, Packaging, Completed, Failed, Canceled }
public sealed record TwrpArchivePayload(string Path, long Bytes, string? PartitionId);
public sealed record TwrpBackupManifest(int Version, Guid BackupId, string Name, DateTimeOffset CreatedAt,
    TwrpDeviceInfo Device, TwrpBackupOptions Options, string[] Partitions, TwrpArchivePayload[] Payload, bool NativeBoot = false);
// Portable Data archives contain identity and payload metadata, not the runtime inventory.
internal sealed record TwrpCliBackupManifest(int Version, Guid BackupId, string Name, DateTimeOffset CreatedAt,
    string DeviceMarker, string Serial, string Slot, string LayoutHash, string RecoveryDevice,
    string[] Partitions, TwrpArchivePayload[] Payload);
// DeviceMarker is the captured ro.deepdroid.device value, never a product/build property.
public sealed record TwrpBackupType(int Version, Guid BackupId, string Name, DateTimeOffset CreatedAt,
    string DeviceMarker, string Serial, string Slot, string RecoveryDevice, string[] Partitions);
public sealed record TwrpDialogResult(TwrpRequest? Request = null, Guid? RecoverSessionId = null, Guid? RetryExportSessionId = null);
public sealed record TwrpArtifact(string Name, long Bytes, string Sha256 = "");
public sealed record TwrpProgress(Guid OperationId, TwrpSessionState State, string MessageKey, string? RescuePath = null, string? Detail = null);

public sealed class TwrpSession
{
    public int FormatVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public TwrpOperationKind Kind { get; set; }
    public TwrpSessionState State { get; set; } = TwrpSessionState.Preparing;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public TwrpDeviceInfo Device { get; set; } = null!;
    public string OriginalHash { get; set; } = "";
    public string RuntimeImageHash { get; set; } = "";
    public string SourceImageHash { get; set; } = "82133ef23effdc4787bc1f08a7854c96638b5d074421c226c02602630f231665";
    public string GuardVersion { get; set; } = "sargo-guard-9";
    public string StagingRoot { get; set; } = "";
    public string StorageRoot { get; set; } = "";
    public string RescueDirectory { get; set; } = "";
    public string NativeFolder { get; set; } = "";
    public string[] Partitions { get; set; } = [];
    public string[] RequestedPartitions { get; set; } = [];
    public string[] SkippedPartitions { get; set; } = [];
    public string? RecoveryDevice { get; set; }
    public string? SourceImagePath { get; set; }
    public bool CliBackup { get; set; }
    public bool CliRestore { get; set; }
    public string? CacheDirectory { get; set; }
    public int CacheLayoutVersion { get; set; }
    public bool NativeBackupCompleted { get; set; }
    public bool PayloadDownloaded { get; set; }
    public bool CleanupCompleted { get; set; }
    public Guid? SourceBackupId { get; set; }
    public TwrpArtifact[] Artifacts { get; set; } = [];
    public bool NativeBoot { get; set; }
    public string? WorkflowError { get; set; }
    public string? BootRestoreError { get; set; }
    public string RecoveryLog { get; set; } = "";
    public TwrpBackupOptions Options { get; set; } = new();
    public string? ArchivePath { get; set; }
    public TwrpExportState ExportState { get; set; }
    public string? ExportError { get; set; }
    public bool StopRequested { get; set; }
    // Retain the original JSON field for journals already written by version 1.
    [System.Text.Json.Serialization.JsonPropertyName("bootVerified")]
    public bool BootRestored { get; set; }
    public bool IsTerminal => State is TwrpSessionState.Completed or TwrpSessionState.Failed;
    public long BackupBytes => Artifacts.Sum(item => item.Bytes);
    public string PartitionSummary => string.Join(", ", Partitions);
}

public sealed class TwrpWorkflowException(string messageKey) : InvalidOperationException(messageKey)
{
    public string MessageKey { get; } = messageKey;
}
