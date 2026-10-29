using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface ITwrpRecoveryService
{
    string? GetDefaultImagePath(string deviceMarker);
    Task<string> GetDeviceMarkerAsync(string serial, CancellationToken cancellationToken);
    Task<TwrpOperation> FactoryResetAsync(string serial, string expectedMarker, IProgress<TwrpProgress>? progress,
        CancellationToken cancellationToken, DeviceActionSource source = DeviceActionSource.SingleDevice);
    Task EnterRecoveryAsync(string serial, IProgress<string>? progress, CancellationToken cancellationToken,
        DeviceActionSource source = DeviceActionSource.SingleDevice);
    Task<TwrpOperation> RunAsync(TwrpRequest request, IProgress<TwrpProgress>? progress, CancellationToken cancellationToken);
    Task<bool> RequestStopAsync(string serial, Guid operationId, CancellationToken cancellationToken);
}
