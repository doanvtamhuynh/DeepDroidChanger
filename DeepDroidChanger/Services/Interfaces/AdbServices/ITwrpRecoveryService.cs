using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface ITwrpRecoveryService
{
    string DefaultRescueDirectory { get; }
    string? GetDefaultImagePath(string deviceMarker);
    Task<string> GetDeviceMarkerAsync(string serial, CancellationToken cancellationToken);
    Task EnterRecoveryAsync(string serial, IProgress<string>? progress, CancellationToken cancellationToken,
        DeviceActionSource source = DeviceActionSource.SingleDevice);
    Task<TwrpPreflight> InspectAsync(string serial, CancellationToken cancellationToken);
    Task<TwrpSession> RunAsync(TwrpRequest request, IProgress<TwrpProgress>? progress, CancellationToken cancellationToken);
    Task<TwrpSession> RecoverAsync(Guid sessionId, IProgress<TwrpProgress>? progress, CancellationToken cancellationToken);
    Task<IReadOnlyList<TwrpSession>> ListAsync(string serial, CancellationToken cancellationToken);
    Task DeleteAsync(string serial, Guid sessionId, CancellationToken cancellationToken);
    Task<bool> RequestStopAsync(string serial, Guid operationId, CancellationToken cancellationToken);
    Task<TwrpSession> RetryExportAsync(Guid sessionId, IProgress<TwrpProgress>? progress, CancellationToken cancellationToken);
}
