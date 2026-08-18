using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

/// <summary>
/// Immutable, serial-scoped presentation state for the latest device process message.
/// </summary>
public sealed record DeviceProcessSnapshot(
    string Serial,
    string Message,
    string ResourceKey,
    DeviceProcessState State);

/// <summary>
/// Application-lifetime source of transient device process presentation state.
/// Action ownership remains the responsibility of <see cref="IDeviceActionCoordinatorService"/>.
/// </summary>
public interface IDeviceProcessStateService
{
    event Action<DeviceProcessSnapshot>? ProcessChanged;

    DeviceProcessSnapshot? Get(string serial);

    void SetProcess(string serial, string message, string resourceKey);

    /// <summary>
    /// Shows a short-lived presentation message without replacing the
    /// authoritative process state.
    /// </summary>
    void ShowTemporaryProcess(
        string serial,
        string message,
        string resourceKey,
        TimeSpan duration);
}
