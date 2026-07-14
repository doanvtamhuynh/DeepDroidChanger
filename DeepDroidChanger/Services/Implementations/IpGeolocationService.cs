using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
using DeepDroidChanger.Helpers;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class IpGeolocationService : IIpGeolocationService
    {
        private readonly IAdbCommandService _adbCommandService;
        private readonly ILogger<IpGeolocationService> _logger;

        public IpGeolocationService(
            IAdbCommandService adbCommandService,
            ILogger<IpGeolocationService> logger)
        {
            _adbCommandService = adbCommandService;
            _logger = logger;
        }

        public async Task<IpGeolocationInfo> GetDeviceIpGeolocationAsync(
            string serial,
            CancellationToken cancellationToken)
        {
            _logger.LogDebug("Resolving IP geolocation through device network for {Serial}.", serial);

            var primaryInfo = await TryGetIpGeolocationAsync(serial, IpGeolocationConstants.Endpoint, cancellationToken).ConfigureAwait(false);
            if (primaryInfo != null)
                return primaryInfo;

            throw new InvalidOperationException("Failed to resolve IP geolocation from device network.");
        }

        private async Task<IpGeolocationInfo?> TryGetIpGeolocationAsync(
            string serial,
            string endpoint,
            CancellationToken cancellationToken)
        {
            try
            {
                var output = await _adbCommandService.CurlAsync(serial, endpoint, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(output))
                    return null;

                if (!IpGeolocationResponseParser.TryParse(output, out IpGeolocationInfo info))
                    return null;

                _logger.LogDebug("Resolved device IP geolocation for {Serial}.", serial);
                return info;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogDebug(exception, "IP geolocation lookup failed for {Serial}.", serial);
                _logger.LogWarning("IP geolocation lookup failed.");
                return null;
            }
        }
    }
}
