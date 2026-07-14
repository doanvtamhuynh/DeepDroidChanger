using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class ProxyWorkflowService : IProxyWorkflowService
{
    private readonly IProxyService _proxyService;
    private readonly IDeviceLocationService _locationService;
    private readonly IDeviceTimezoneService _timezoneService;
    private readonly ILogger<ProxyWorkflowService> _logger;

    public ProxyWorkflowService(
        IProxyService proxyService,
        IDeviceLocationService locationService,
        IDeviceTimezoneService timezoneService,
        ILogger<ProxyWorkflowService> logger)
    {
        _proxyService = proxyService;
        _locationService = locationService;
        _timezoneService = timezoneService;
        _logger = logger;
    }

    public async Task<ProxyWorkflowResult> ApplyAsync(
        string serial,
        FakeProxyDialogResult configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        await _proxyService.StartProxyAsync(
                serial,
                configuration.Host,
                configuration.Port,
                configuration.Username,
                configuration.Password,
                configuration.ProxyType,
                cancellationToken)
            .ConfigureAwait(false);

        bool locationUpdateFailed = false;
        string appliedLatitude = string.Empty;
        string appliedLongitude = string.Empty;
        if (configuration.ProxyChangeLocationByIp)
        {
            try
            {
                (string latitude, string longitude) =
                    await _locationService.ResolveLocationByDeviceIpAsync(serial, cancellationToken).ConfigureAwait(false);
                await _locationService.ApplyLocationAsync(serial, latitude, longitude, cancellationToken).ConfigureAwait(false);
                appliedLatitude = latitude;
                appliedLongitude = longitude;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                locationUpdateFailed = true;
                _logger.LogError(exception, "Failed to update location after applying proxy for device {Serial}.", serial);
            }
        }

        bool timezoneUpdateFailed = false;
        string appliedTimezone = string.Empty;
        if (configuration.ProxyChangeTimezoneByIp)
        {
            try
            {
                string timezone = await _timezoneService
                    .ResolveTimezoneByDeviceIpAsync(serial, cancellationToken)
                    .ConfigureAwait(false);
                await _timezoneService.ApplyTimezoneAsync(serial, timezone, cancellationToken).ConfigureAwait(false);
                appliedTimezone = timezone;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                timezoneUpdateFailed = true;
                _logger.LogError(exception, "Failed to update timezone after applying proxy for device {Serial}.", serial);
            }
        }

        return new ProxyWorkflowResult(
            locationUpdateFailed,
            timezoneUpdateFailed,
            appliedLatitude,
            appliedLongitude,
            appliedTimezone);
    }
}
