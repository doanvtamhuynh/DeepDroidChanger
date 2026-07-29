using DeepDroidChanger.Models;
using DeepDroidChanger.Authentication;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        private readonly DeviceInfoApiOptions _options;
        private readonly IAuthenticationSessionService _authenticationSessionService;
        private readonly ILogger<DeviceRandomApiService> _logger;
        private bool _disposed;

        public DeviceRandomApiService(
            IOptions<DeviceInfoApiOptions> options,
            IAuthenticationSessionService authenticationSessionService,
            ILogger<DeviceRandomApiService> logger)
            : this(
                new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
                options.Value,
                authenticationSessionService,
                logger,
                disposeHttpClient: true)
        {
        }

        internal DeviceRandomApiService(
            HttpClient httpClient,
            DeviceInfoApiOptions options,
            IAuthenticationSessionService authenticationSessionService,
            ILogger<DeviceRandomApiService> logger)
            : this(
                httpClient,
                options,
                authenticationSessionService,
                logger,
                disposeHttpClient: false)
        {
        }

        private DeviceRandomApiService(
            HttpClient httpClient,
            DeviceInfoApiOptions options,
            IAuthenticationSessionService authenticationSessionService,
            ILogger<DeviceRandomApiService> logger,
            bool disposeHttpClient)
        {
            _httpClient = httpClient;
            _options = options;
            _authenticationSessionService = authenticationSessionService;
            _logger = logger;
            _disposeHttpClient = disposeHttpClient;
        }

        public async Task<DeviceInfoApiDevice> GetRandomDeviceAsync(
            RandomDeviceSelection selection,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(selection);
            string? idToken = _authenticationSessionService.CurrentSession?.IdToken;
            if (string.IsNullOrWhiteSpace(idToken))
                throw new DeviceRandomApiException("Authentication is required.");

            for (int attempt = 1; attempt <= 4; attempt++)
            {
                DeviceInfoApiDevice? device = await SendRandomDeviceQueryAsync(
                        idToken,
                        selection,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (device != null)
                    return device;

                _logger.LogDebug(
                    "Random device API returned null on attempt {Attempt}/{MaximumAttempts} for brand {Brand} and SDK {Sdk}.",
                    attempt,
                    4,
                    selection.Brand,
                    selection.Sdk);
            }

            throw new DeviceRandomApiException("Random device request returned no result.");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_disposeHttpClient)
                _httpClient.Dispose();
        }

        private async Task<DeviceInfoApiDevice?> SendRandomDeviceQueryAsync(
            string idToken,
            RandomDeviceSelection selection,
            CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var endpoint))
            {
                _logger.LogWarning("Random device request could not be sent due to invalid API endpoint.");
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
            if (!request.Headers.TryAddWithoutValidation(_options.AuthorizationHeaderName, idToken))
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
