namespace DeepDroidChanger.Services;

/// <summary>
/// Service that prevents concurrent actions from running on the same device.
/// </summary>
public interface IDeviceActionGuardService
{
    /// <summary>
    /// Event raised when the busy state of a device serial changes.
    /// </summary>
    event Action<string, bool>? BusyStateChanged;

    /// <summary>
    /// Checks whether an action is currently running for the specified device serial.
    /// </summary>
    /// <param name="serial">The device serial number.</param>
    /// <returns><c>true</c> if an action is in progress; otherwise, <c>false</c>.</returns>
    bool IsBusy(string serial);

    /// <summary>
    /// Attempts to acquire an execution lease for the specified device serial.
    /// </summary>
    /// <param name="serial">The device serial number.</param>
    /// <returns>An <see cref="IDisposable"/> lease if acquired successfully; otherwise, <c>null</c>.</returns>
    IDisposable? TryAcquire(string serial);
}

