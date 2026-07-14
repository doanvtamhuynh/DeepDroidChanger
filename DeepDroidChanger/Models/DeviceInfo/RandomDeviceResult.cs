namespace DeepDroidChanger.Models;

public sealed record RandomDeviceResult(
    RandomDeviceStatus Status,
    DeviceInfoApiDevice? Profile);

public enum RandomDeviceStatus
{
    LoginRequired,
    Created,
    Failed
}
