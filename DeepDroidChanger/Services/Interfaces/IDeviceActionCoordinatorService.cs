namespace DeepDroidChanger.Services;

/// <summary>
/// Identifies an exclusive device workflow owned by the action coordinator.
/// </summary>
public enum DeviceActionKind
{
    RandomDevice,
    RandomChangeAndWipe,
    ChangeDevice,
    ChangeWithoutWipe,
    Wipe,
    RandomSim,
    ChangeSim,
    ChangeLocation,
    ChangeTimezone,
    InstallPackages,
    DeleteDevice,
    UpdateIntegrity,
    FakeProxy,
    StopFakeProxy,
    ViewRandomDeviceInfo,
    BatchRandomDevice,
    BatchRandomChangeAndWipe,
    BatchChangeDevice,
    BatchChangeWithoutWipe,
    BatchWipe,
    BatchRandomSim,
    BatchChangeSim,
    BatchChangeLocation,
    BatchChangeTimezone,
    BatchInstallPackages
}

public static class DeviceActionKindExtensions
{
    public static bool IsBatchAction(this DeviceActionKind kind)
    {
        return kind is DeviceActionKind.BatchRandomDevice
            or DeviceActionKind.BatchRandomChangeAndWipe
            or DeviceActionKind.BatchChangeDevice
            or DeviceActionKind.BatchChangeWithoutWipe
            or DeviceActionKind.BatchWipe
            or DeviceActionKind.BatchRandomSim
            or DeviceActionKind.BatchChangeSim
            or DeviceActionKind.BatchChangeLocation
            or DeviceActionKind.BatchChangeTimezone
            or DeviceActionKind.BatchInstallPackages;
    }

    /// <summary>
    /// Maps coordinator ownership kinds to the logical action shown across screens.
    /// This does not change coordinator ownership or cancellation authority.
    /// </summary>
    public static DeviceActionKind ToLogicalActionKind(this DeviceActionKind kind)
    {
        return kind switch
        {
            DeviceActionKind.BatchRandomDevice => DeviceActionKind.RandomDevice,
            DeviceActionKind.BatchRandomChangeAndWipe => DeviceActionKind.RandomChangeAndWipe,
            DeviceActionKind.BatchChangeDevice => DeviceActionKind.ChangeDevice,
            DeviceActionKind.BatchChangeWithoutWipe => DeviceActionKind.ChangeWithoutWipe,
            DeviceActionKind.BatchWipe => DeviceActionKind.Wipe,
            DeviceActionKind.BatchRandomSim => DeviceActionKind.RandomSim,
            DeviceActionKind.BatchChangeSim => DeviceActionKind.ChangeSim,
            DeviceActionKind.BatchChangeLocation => DeviceActionKind.ChangeLocation,
            DeviceActionKind.BatchChangeTimezone => DeviceActionKind.ChangeTimezone,
            DeviceActionKind.BatchInstallPackages => DeviceActionKind.InstallPackages,
            _ => kind
        };
    }

    /// <summary>
    /// Maps a logical Multiple Device action to the coordinator kind used by
    /// each serial-scoped batch target.
    /// </summary>
    public static DeviceActionKind ToBatchActionKind(this DeviceActionKind kind)
    {
        if (kind.IsBatchAction())
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A logical action kind is required.");

        return kind switch
        {
            DeviceActionKind.RandomDevice => DeviceActionKind.BatchRandomDevice,
            DeviceActionKind.RandomChangeAndWipe => DeviceActionKind.BatchRandomChangeAndWipe,
            DeviceActionKind.ChangeDevice => DeviceActionKind.BatchChangeDevice,
            DeviceActionKind.ChangeWithoutWipe => DeviceActionKind.BatchChangeWithoutWipe,
            DeviceActionKind.Wipe => DeviceActionKind.BatchWipe,
            DeviceActionKind.RandomSim => DeviceActionKind.BatchRandomSim,
            DeviceActionKind.ChangeSim => DeviceActionKind.BatchChangeSim,
            DeviceActionKind.ChangeLocation => DeviceActionKind.BatchChangeLocation,
            DeviceActionKind.ChangeTimezone => DeviceActionKind.BatchChangeTimezone,
            DeviceActionKind.InstallPackages => DeviceActionKind.BatchInstallPackages,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    public static string GetDisplayResourceKey(this DeviceActionKind kind)
    {
        return kind.ToLogicalActionKind() switch
        {
            DeviceActionKind.RandomDevice => "DeviceAction_Name_RandomDevice",
            DeviceActionKind.RandomChangeAndWipe => "DeviceAction_Name_RandomChangeAndWipe",
            DeviceActionKind.ChangeDevice => "DeviceAction_Name_ChangeDevice",
            DeviceActionKind.ChangeWithoutWipe => "DeviceAction_Name_ChangeWithoutWipe",
            DeviceActionKind.Wipe => "DeviceAction_Name_Wipe",
            DeviceActionKind.RandomSim => "DeviceAction_Name_RandomSim",
            DeviceActionKind.ChangeSim => "DeviceAction_Name_ChangeSim",
            DeviceActionKind.ChangeLocation => "DeviceAction_Name_ChangeLocation",
            DeviceActionKind.ChangeTimezone => "DeviceAction_Name_ChangeTimezone",
            DeviceActionKind.InstallPackages => "DeviceAction_Name_InstallPackages",
            DeviceActionKind.DeleteDevice => "DeviceAction_Name_DeleteDevice",
            DeviceActionKind.UpdateIntegrity => "DeviceAction_Name_UpdateIntegrity",
            DeviceActionKind.FakeProxy => "DeviceAction_Name_FakeProxy",
            DeviceActionKind.StopFakeProxy => "DeviceAction_Name_StopFakeProxy",
            DeviceActionKind.ViewRandomDeviceInfo => "DeviceAction_Name_ViewRandomDeviceInfo",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }
}

/// <summary>
/// Transient runtime state for an exclusive device operation.
/// </summary>
public enum DeviceActionRuntimeState
{
    Idle,
    Running,
    Stopping
}

public enum DeviceActionCancellationReason
{
    None,
    UserStop,
    External
}

/// <summary>
/// Identifies the workspace that initiated a coordinated action.
/// </summary>
public enum DeviceActionSource
{
    Unknown,
    SingleDevice,
    MultipleDevices
}

public static class DeviceActionSourceExtensions
{
    public static string GetDisplayResourceKey(this DeviceActionSource source)
    {
        return source switch
        {
            DeviceActionSource.SingleDevice => "DeviceAction_Source_SingleDevice",
            DeviceActionSource.MultipleDevices => "DeviceAction_Source_MultipleDevices",
            _ => "DeviceAction_Source_Unknown"
        };
    }
}

/// <summary>
/// Immutable presentation data for a currently owned device operation.
/// </summary>
public sealed record DeviceActionOperationSnapshot(
    string Serial,
    DeviceActionKind Kind,
    Guid OperationId,
    DeviceActionRuntimeState State,
    bool CanCancel,
    DeviceActionCancellationReason CancellationReason,
    Guid SessionId,
    DeviceActionSource Source);

/// <summary>
/// Immutable presentation data for one logical action invocation.
/// </summary>
public sealed record DeviceActionSessionSnapshot(
    Guid SessionId,
    DeviceActionKind Kind,
    DeviceActionRuntimeState State,
    bool CanCancel,
    DeviceActionSource Source,
    IReadOnlyList<DeviceActionOperationSnapshot> Operations);

/// <summary>
/// Handle owned by the workflow that acquired a device operation.
/// </summary>
public interface IDeviceActionOperation : IDisposable
{
    string Serial { get; }
    DeviceActionKind Kind { get; }
    Guid OperationId { get; }
    Guid SessionId { get; }
    DeviceActionSource Source { get; }
    DeviceActionRuntimeState State { get; }
    bool CanCancel { get; }
    DeviceActionCancellationReason CancellationReason { get; }
    CancellationToken CancellationToken { get; }
    bool IsCancellationRequested { get; }
    DeviceActionOperationSnapshot Snapshot { get; }
}

/// <summary>
/// Central owner of exclusive long-running device actions.
/// </summary>
public interface IDeviceActionCoordinatorService
{
    /// <summary>
    /// Raised outside the coordinator synchronization boundary.
    /// </summary>
    event Action<DeviceActionOperationSnapshot>? OperationStateChanged;

    bool IsBusy(string serial);

    DeviceActionOperationSnapshot? GetOperation(string serial);

    /// <summary>
    /// Returns all currently active logical action sessions.
    /// </summary>
    IReadOnlyList<DeviceActionSessionSnapshot> GetActiveSessions() => [];

    IDeviceActionOperation? TryStart(
        string serial,
        DeviceActionKind kind,
        bool canCancel,
        CancellationToken externalCancellationToken = default,
        Guid? sessionId = null,
        DeviceActionSource source = DeviceActionSource.Unknown);

    bool TryRequestCancellation(string serial);

    /// <summary>
    /// Requests cancellation for every cancellable operation in a session.
    /// </summary>
    bool TryRequestSessionCancellation(Guid sessionId) => false;

    /// <summary>
    /// Requests cancellation for every active operation, including operations
    /// whose normal UI command does not expose Stop.
    /// </summary>
    void RequestShutdownCancellation() { }

    /// <summary>
    /// Completes once the coordinator has no active device operations.
    /// </summary>
    Task WaitForIdleAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
