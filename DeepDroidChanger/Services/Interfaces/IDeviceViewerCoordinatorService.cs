namespace DeepDroidChanger.Services;

public interface IDeviceViewerCoordinatorService
{
    Task<double> QueryDeviceAspectRatioAsync(string serial, CancellationToken cancellationToken);
}
