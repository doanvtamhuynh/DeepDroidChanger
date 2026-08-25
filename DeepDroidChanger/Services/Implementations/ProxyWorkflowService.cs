using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class ProxyWorkflowService : IProxyWorkflowService
{
    private readonly IProxyService _proxyService;
    private readonly IDeviceLocationService _locationService;
    private readonly IDeviceTimezoneService _timezoneService;
    private readonly IDeviceConfigService _deviceConfigService;
    private readonly ILogger<ProxyWorkflowService> _logger;

    public ProxyWorkflowService(
        IProxyService proxyService,
        IDeviceLocationService locationService,
        IDeviceTimezoneService timezoneService,
        IDeviceConfigService deviceConfigService,
        ILogger<ProxyWorkflowService> logger)
    {
        _proxyService = proxyService;
        _locationService = locationService;
        _timezoneService = timezoneService;
        _deviceConfigService = deviceConfigService;
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

        await TryPersistProxyAsync(serial, configuration).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        await _proxyService
            .WaitForInternetAndOpenBrowserLeaksAsync(serial, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        bool locationUpdateFailed = false;
        string appliedLatitude = string.Empty;
        string appliedLongitude = string.Empty;
        if (configuration.ProxyChangeLocationByIp)
        {
            try
            {
                DeviceLocationResult location =
                    await _locationService.ResolveLocationByDeviceIpAsync(serial, cancellationToken).ConfigureAwait(false);
                await _locationService
                    .ApplyLocationAsync(serial, location.Latitude, location.Longitude, cancellationToken)
                    .ConfigureAwait(false);
                appliedLatitude = location.Latitude;
                appliedLongitude = location.Longitude;
                await TryPersistLocationAsync(
                        serial,
                        location.Latitude,
                        location.Longitude)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
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
                await TryPersistTimezoneAsync(serial, timezone).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
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

        cancellationToken.ThrowIfCancellationRequested();

        return new ProxyWorkflowResult(
            locationUpdateFailed,
            timezoneUpdateFailed,
            appliedLatitude,
            appliedLongitude,
            appliedTimezone);
    }

    private async Task TryPersistProxyAsync(
        string serial,
        FakeProxyDialogResult configuration)
    {
        try
        {
            bool saved = await _deviceConfigService
                .SaveProxyConfigAsync(serial, configuration, CancellationToken.None)
                .ConfigureAwait(false);
            if (!saved)
            {
                _logger.LogError(
                    "Fake Proxy was applied but proxy configuration could not be stored for device {Serial}.",
                    serial);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Fake Proxy proxy configuration persistence failed for device {Serial}.",
                serial);
        }
    }

    private async Task TryPersistLocationAsync(
        string serial,
        string latitude,
        string longitude)
    {
        try
        {
            bool saved = await _deviceConfigService
                .SaveLocationConfigAsync(
                    serial,
                    ChangeLocationMode.DeviceIp,
                    latitude,
                    longitude,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!saved)
            {
                _logger.LogError(
                    "Fake Proxy was applied but location configuration could not be stored for device {Serial}.",
                    serial);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Fake Proxy location configuration persistence failed for device {Serial}.",
                serial);
        }
    }

    private async Task TryPersistTimezoneAsync(
        string serial,
        string timezone)
    {
        try
        {
            bool saved = await _deviceConfigService
                .SaveTimezoneConfigAsync(
                    serial,
                    ChangeTimezoneMode.DeviceIp,
                    timezone,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!saved)
            {
                _logger.LogError(
                    "Fake Proxy was applied but timezone configuration could not be stored for device {Serial}.",
                    serial);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Fake Proxy timezone configuration persistence failed for device {Serial}.",
                serial);
        }
    }
}
