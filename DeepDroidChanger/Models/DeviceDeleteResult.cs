namespace DeepDroidChanger.Models
{
    public sealed record DeviceDeleteResult(
        bool Removed,
        DeviceListSnapshot Snapshot);
}
