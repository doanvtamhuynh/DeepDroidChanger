using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using DeepDroidChanger.Helpers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class CarrierDataService : ICarrierDataService
    {
        private const string CountryIsoPropertyName = "country_iso";
        private const string CountryCodePropertyName = "country_code";
        private const string CountryNamePropertyName = "country_name";
        private const string CarrierNamePropertyName = "carrier_name";
        private const string CarrierAttributePropertyName = "carrier_attribute";
        private const string MccPropertyName = "mcc";
        private const string MncPropertyName = "mnc";

        private readonly ILogger<CarrierDataService> _logger;
        private IReadOnlyList<CarrierProfile>? _carrierProfiles;

        public CarrierDataService(ILogger<CarrierDataService> logger)
        {
            _logger = logger;
        }

        public Task<IReadOnlyList<CarrierProfile>> GetCarrierProfilesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _carrierProfiles ??= LoadCarrierProfiles(cancellationToken);
            return Task.FromResult(_carrierProfiles);
        }

        private IReadOnlyList<CarrierProfile> LoadCarrierProfiles(CancellationToken cancellationToken)
        {
            try
            {
                var json = AssetDataReader.ReadText(AssetConstants.Data.CarriersPath);
                using var document = JsonDocument.Parse(json);
                var profiles = new List<CarrierProfile>();
                var seenCarriers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var carrierElement in document.RootElement.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var countryIso = GetStringValue(carrierElement, CountryIsoPropertyName).ToLowerInvariant();
                    var countryCode = GetStringValue(carrierElement, CountryCodePropertyName);
                    var countryName = GetStringValue(carrierElement, CountryNamePropertyName);
                    var carrierName = GetStringValue(carrierElement, CarrierNamePropertyName);

                    if (countryIso.Length == 0 || countryName.Length == 0 || carrierName.Length == 0)
                        continue;

                    var carrierAttribute = GetObjectValue(carrierElement, CarrierAttributePropertyName);
                    var mcc = GetStringValue(carrierAttribute, MccPropertyName);
                    var mnc = GetStringValue(carrierAttribute, MncPropertyName);
                    var dedupeKey = string.Concat(countryIso, "|", carrierName, "|", mcc, "|", mnc);
                    if (!seenCarriers.Add(dedupeKey))
                        continue;

                    profiles.Add(new CarrierProfile(
                        countryIso,
                        countryCode,
                        countryName,
                        carrierName,
                        mcc,
                        mnc));
                }

                return profiles
                    .OrderBy(profile => profile.CountryName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(profile => profile.CountryIso, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(profile => profile.CarrierName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(profile => profile.Mcc, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(profile => profile.Mnc, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Failed to load carrier profiles.");
                return Array.Empty<CarrierProfile>();
            }
        }

        private static JsonElement GetObjectValue(JsonElement element, string propertyName)
        {
            return element.ValueKind != JsonValueKind.Undefined
                && element.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.Object
                ? property
                : default;
        }

        private static string GetStringValue(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(propertyName, out var property))
                return string.Empty;

            return property.GetString()?.Trim() ?? string.Empty;
        }
    }
}
