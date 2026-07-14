using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class DeviceRandomApiService : IDeviceRandomApiService, IDisposable
    {
        private const string GraphQlQuery = """
            query getDeviceV4($brand: String, $model: String, $sdkMin: Int, $sdkMax: Int) {
                GetDeviceV4: getDeviceV4(brand: $brand, model: $model, sdkMin: $sdkMin, sdkMax: $sdkMax) {
                    model
                    gaid
                    board
                    baseband
                    securityPath
                    name
                    fingerprint
                    buildDisplayId
                    manufacturer
                    buildDateUtc
                    hardware
                    imei
                    gpu
                    imei1
                    buildHost
                    gsf
                    platform
                    bootloader
                }
            }
            """;

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _httpClient;
        private readonly bool _disposeHttpClient;
        private readonly ILogger<DeviceRandomApiService> _logger;
        private bool _disposed;

        public DeviceRandomApiService(ILogger<DeviceRandomApiService> logger)
            : this(
                new HttpClient { Timeout = TimeSpan.FromSeconds(DeviceInfoApiConstants.RequestTimeoutSeconds) },
                logger,
                disposeHttpClient: true)
        {
        }

        internal DeviceRandomApiService(HttpClient httpClient, ILogger<DeviceRandomApiService> logger)
            : this(httpClient, logger, disposeHttpClient: false)
        {
        }

        private DeviceRandomApiService(
            HttpClient httpClient,
            ILogger<DeviceRandomApiService> logger,
            bool disposeHttpClient)
        {
            _httpClient = httpClient;
            _logger = logger;
            _disposeHttpClient = disposeHttpClient;
        }

        public async Task<DeviceInfoApiDevice> GetRandomDeviceAsync(AccountSession session, RandomDeviceSelection selection, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(selection);

            var device = await SendRandomDeviceQueryAsync(session, selection, cancellationToken).ConfigureAwait(false);
            if (device != null)
                return device;

            _logger.LogDebug("Random device API returned null. Retrying once for brand {Brand} and SDK {Sdk}.", selection.Brand, selection.Sdk);
            device = await SendRandomDeviceQueryAsync(session, selection, cancellationToken).ConfigureAwait(false);
            if (device == null)
                throw new DeviceRandomApiException("Random device request returned no result.");

            return device;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_disposeHttpClient)
                _httpClient.Dispose();
        }

        private async Task<DeviceInfoApiDevice?> SendRandomDeviceQueryAsync(AccountSession session, RandomDeviceSelection selection, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(session.Endpoint, UriKind.Absolute, out var endpoint))
            {
                _logger.LogWarning("Random device request could not be sent due to invalid session endpoint.");
                throw new DeviceRandomApiException("Random device request could not be sent.");
            }

            var payload = new
            {
                query = GraphQlQuery,
                variables = new
                {
                    brand = NormalizeBrand(selection.Brand),
                    model = string.Empty,
                    sdkMin = selection.Sdk,
                    sdkMax = selection.Sdk
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!request.Headers.TryAddWithoutValidation(session.AuthenticationHeaderName, session.IdToken))
            {
                _logger.LogWarning("Random device request could not be prepared: authentication header rejected.");
                throw new DeviceRandomApiException("Random device request could not be prepared.");
            }

            request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Random device request failed with status {StatusCode}.", (int)response.StatusCode);
                    throw new DeviceRandomApiException("Random device request failed.");
                }

                return ParseDevice(content);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Random device request timed out.");
                throw new DeviceRandomApiException("Random device request timed out.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                _logger.LogWarning("Random device network request failed.");
                throw new DeviceRandomApiException("Random device request failed.");
            }
            catch (DeviceRandomApiException)
            {
                throw;
            }
            catch (Exception)
            {
                _logger.LogWarning("Unexpected random device request failure.");
                throw new DeviceRandomApiException("Random device request failed.");
            }
        }

        private static DeviceInfoApiDevice? ParseDevice(string content)
        {
            try
            {
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                    throw new DeviceRandomApiException("Random device request returned errors.");

                if (!document.RootElement.TryGetProperty("data", out var data)
                    || !data.TryGetProperty("GetDeviceV4", out var deviceElement)
                    || deviceElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    return null;
                }

                return deviceElement.Deserialize<DeviceInfoApiDevice>(JsonOptions);
            }
            catch (JsonException exception)
            {
                throw new DeviceRandomApiException("Random device response could not be parsed.", exception);
            }
        }

        private static string NormalizeBrand(string brand)
        {
            return brand.Trim().ToLowerInvariant() switch
            {
                "xiaomi" => "Xiaomi",
                "oneplus" => "OnePlus",
                "oppo" => "OPPO",
                "samsung" => "samsung",
                "vivo" => "vivo",
                "google" => "google",
                _ => brand
            };
        }
    }
}
