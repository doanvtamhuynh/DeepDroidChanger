namespace DeepDroidChanger.Models
{
    public sealed class AdbDevice
    {
        public AdbDevice(string serial, AdbDeviceStatus status)
        {
            Serial = serial;
            Status = status;
        }

        public string Serial { get; }
        public AdbDeviceStatus Status { get; }
    }
}
