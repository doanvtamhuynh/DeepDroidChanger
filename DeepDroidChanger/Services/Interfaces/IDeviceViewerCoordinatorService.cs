namespace DeepDroidChanger.Services;

public interface IDeviceViewerCoordinatorService
{
    Task MonitorStreamAsync(DeviceViewerMonitorContext context);

    Task EnsureStreamAsync(DeviceViewerStartContext context);

    Task<bool> IsDeviceConnectedAsync(string serial, CancellationToken cancellationToken);

    Task<double> QueryDeviceAspectRatioAsync(string serial, CancellationToken cancellationToken);
}
