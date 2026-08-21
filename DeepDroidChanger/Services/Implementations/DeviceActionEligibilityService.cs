using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class DeviceActionEligibilityService : IDeviceActionEligibilityService
{
    internal const int MaxConcurrentLiveChecks = 10;

    private readonly IDeviceListService _deviceListService;
    private readonly IDeviceActionCoordinatorService _deviceActionCoordinatorService;
    private readonly ILogger<DeviceActionEligibilityService> _logger;
    private readonly SemaphoreSlim _liveCheckLimiter = new(MaxConcurrentLiveChecks, MaxConcurrentLiveChecks);

    public DeviceActionEligibilityService(
        IDeviceListService deviceListService,
        IDeviceActionCoordinatorService deviceActionCoordinatorService,
        ILogger<DeviceActionEligibilityService> logger)
    {
        _deviceListService = deviceListService;
        _deviceActionCoordinatorService = deviceActionCoordinatorService;
        _logger = logger;
    }

    public async Task<DeviceActionEligibilityFailure> CheckAsync(
        string serial,
        DeviceActionRequirement requirements,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        cancellationToken.ThrowIfCancellationRequested();

        if (requirements.HasFlag(DeviceActionRequirement.Idle)
            && _deviceActionCoordinatorService.IsBusy(serial))
        {
            return DeviceActionEligibilityFailure.Busy;
        }

        if (!requirements.HasFlag(DeviceActionRequirement.Online))
            return DeviceActionEligibilityFailure.None;

        await _liveCheckLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool isOnline = await _deviceListService
                .IsDeviceOnlineAsync(serial, cancellationToken)
                .ConfigureAwait(false);
            return isOnline
                ? DeviceActionEligibilityFailure.None
                : DeviceActionEligibilityFailure.Offline;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to verify live ADB eligibility for device {Serial}.",
                serial);
            return DeviceActionEligibilityFailure.Offline;
        }
        finally
        {
            _liveCheckLimiter.Release();
        }
    }
}
