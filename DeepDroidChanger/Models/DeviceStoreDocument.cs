namespace DeepDroidChanger.Models
{
    public sealed class DeviceStoreDocument
    {
        public int Version { get; set; }
        public List<StoredDeviceConfig> Devices { get; set; } = new();
    }
}
