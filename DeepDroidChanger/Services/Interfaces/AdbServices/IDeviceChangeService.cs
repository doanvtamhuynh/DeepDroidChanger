using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IDeviceChangeService
{
    Task ChangeAsync(
        string serial,
        DeviceInfoApiDevice profile,
        bool changeSim,
        DeviceChangeOptions options,
        IProgress<DeviceChangeStage>? progress,
        CancellationToken cancellationToken);
}
