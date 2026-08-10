using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class DeviceTimezoneService : IDeviceTimezoneService
    {
        private readonly IAdbCommandService _adbCommandService;
        private readonly IIpGeolocationService _adbIpGeolocationService;
        private readonly ILogger<DeviceTimezoneService> _logger;

        public DeviceTimezoneService(
            IAdbCommandService adbCommandService,
            IIpGeolocationService adbIpGeolocationService,
            ILogger<DeviceTimezoneService> logger)
        {
            _adbCommandService = adbCommandService;
            _adbIpGeolocationService = adbIpGeolocationService;
            _logger = logger;
        }

        public async Task ApplyTimezoneAsync(string serial, string timezone, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(timezone))
                throw new ArgumentException("Timezone cannot be empty.", nameof(timezone));

            timezone = timezone.Trim();
            _logger.LogInformation("Applying timezone {Timezone} to device {Serial}.", timezone, serial);

            await _adbCommandService.PutSettingAsync(serial, "global", "auto_time_zone", "0", cancellationToken).ConfigureAwait(false);
            await _adbCommandService.SetPropertyAsync(serial, PropertyConstants.Timezone, timezone, cancellationToken).ConfigureAwait(false);
            await _adbCommandService.BroadcastAsync(serial, "android.intent.action.TIMEZONE_CHANGED", cancellationToken).ConfigureAwait(false);
            await _adbCommandService.PutSettingAsync(serial, "system", "time_12_24", "24", cancellationToken).ConfigureAwait(false);
            await _adbCommandService.BroadcastAsync(serial, "android.intent.action.TIME_SET", cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Timezone applied {Timezone} to device {Serial}.", timezone, serial);
        }

        public async Task<string> ResolveTimezoneByDeviceIpAsync(string serial, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Resolving timezone by device IP for {Serial}.", serial);
            var info = await _adbIpGeolocationService.GetDeviceIpGeolocationAsync(serial, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(info.Timezone))
                throw new InvalidOperationException("Failed to resolve timezone by device IP.");

            return info.Timezone.Trim();
        }

        public async Task<string> ApplyAsync(
            string serial,
            ChangeTimezoneDialogResult result,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(result);
            string timezone = result.Mode == ChangeTimezoneMode.DeviceIp
                ? await ResolveTimezoneByDeviceIpAsync(serial, cancellationToken).ConfigureAwait(false)
                : result.Timezone?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(timezone))
                throw new InvalidOperationException("Timezone selection returned no value.");

            await ApplyTimezoneAsync(serial, timezone, cancellationToken).ConfigureAwait(false);
            return timezone;
        }
    }
}
