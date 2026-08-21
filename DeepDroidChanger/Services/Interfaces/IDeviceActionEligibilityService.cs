namespace DeepDroidChanger.Services;

[Flags]
public enum DeviceActionRequirement
{
    None = 0,
    Online = 1,
    Idle = 2
}

public enum DeviceActionEligibilityFailure
{
    None,
    Offline,
    Busy
}

public interface IDeviceActionEligibilityService
{
    Task<DeviceActionEligibilityFailure> CheckAsync(
        string serial,
        DeviceActionRequirement requirements,
        CancellationToken cancellationToken);
}
