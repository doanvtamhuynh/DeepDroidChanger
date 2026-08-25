using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
using DeepDroidChanger.Helpers;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class IpGeolocationService : IIpGeolocationService
    {
        private const int MaximumLookupAttempts = 2;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

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

            for (int attempt = 1; attempt <= MaximumLookupAttempts; attempt++)
            {
                (IpGeolocationInfo? information, bool shouldRetry) =
                    await TryGetIpGeolocationAsync(serial, UrlConstants.IpGeolocation, cancellationToken)
                        .ConfigureAwait(false);
                if (information != null)
                    return information;

                if (!shouldRetry || attempt == MaximumLookupAttempts)
                    break;

                _logger.LogWarning(
                    "Device IP geolocation request failed for {Serial}; retrying once.",
                    serial);
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException("Failed to resolve IP geolocation from device network.");
        }

        private async Task<(IpGeolocationInfo? Information, bool ShouldRetry)> TryGetIpGeolocationAsync(
            string serial,
            string endpoint,
            CancellationToken cancellationToken)
        {
            try
            {
                var output = await _adbCommandService.CurlAsync(serial, endpoint, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(output))
                    return (null, true);

                if (!IpGeolocationResponseParser.TryParse(output, out IpGeolocationInfo info))
                    return (null, false);

                _logger.LogDebug("Resolved device IP geolocation for {Serial}.", serial);
                return (info, false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogDebug(exception, "IP geolocation lookup failed for {Serial}.", serial);
                _logger.LogWarning("IP geolocation lookup failed.");
                return (null, true);
            }
        }
    }
}
