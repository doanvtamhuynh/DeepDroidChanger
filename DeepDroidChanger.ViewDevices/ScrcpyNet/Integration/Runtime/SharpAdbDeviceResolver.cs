using System.Linq;
using SharpAdbClient;
using DeepDroidChanger.ViewDevices.Contracts;

namespace DeepDroidChanger.ViewDevices.Runtime;

public sealed class SharpAdbDeviceResolver(string canonicalAdbPath) : ISharpAdbDeviceResolver
{
    public DeviceData Resolve(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        AdbServer server = new();
        if (!server.GetStatus().IsRunning)
            server.StartServer(canonicalAdbPath, false);

        DeviceData? device = new AdbClient()
            .GetDevices()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Serial, serial, StringComparison.OrdinalIgnoreCase));
        return device ?? throw new InvalidOperationException(
            $"ADB did not return an attached device for serial '{serial}'.");
    }
}
