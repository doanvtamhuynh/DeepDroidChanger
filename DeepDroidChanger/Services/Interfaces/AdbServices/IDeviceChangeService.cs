using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IDeviceChangeService
{
    Task ChangeSimAsync(
        string serial,
        SimProfile profile,
        CancellationToken cancellationToken);

    Task ChangeWithoutWipeAsync(
        string serial,
        DeviceInfoApiDevice profile,
        bool changeSim,
        DeviceChangeOptions options,
        IProgress<DeviceChangeStage>? progress,
        CancellationToken cancellationToken);

    Task WipeWithoutChangeAsync(
        string serial,
        DeviceChangeOptions options,
        IProgress<DeviceChangeStage>? progress,
        CancellationToken cancellationToken);

    Task ChangeAsync(
        string serial,
        DeviceInfoApiDevice profile,
        bool changeSim,
        DeviceChangeOptions options,
        IProgress<DeviceChangeStage>? progress,
        CancellationToken cancellationToken);
}
