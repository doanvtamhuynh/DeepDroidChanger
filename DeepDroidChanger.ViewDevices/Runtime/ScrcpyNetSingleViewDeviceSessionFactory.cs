using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewDevices.Runtime;

public sealed class ScrcpyNetSingleViewDeviceSessionFactory(
    ISharpAdbDeviceResolver deviceResolver,
    IScrcpyNetClientFactory clientFactory,
    ILogger<ScrcpyNetSingleViewDeviceSession> logger) : ISingleViewDeviceSessionFactory
{
    public ISingleViewDeviceSession Create(ViewDeviceLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ScrcpyNetSingleViewDeviceSession(options, deviceResolver, clientFactory, logger);
    }
}
