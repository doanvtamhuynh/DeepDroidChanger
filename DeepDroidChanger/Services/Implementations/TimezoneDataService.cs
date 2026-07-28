using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using DeepDroidChanger.Helpers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class TimezoneDataService : ITimezoneDataService
    {
        private const string CountryCodePropertyName = "country_code";
        private const string CountryNamePropertyName = "country_name";
        private const string TimezonePropertyName = "timezone";
        private const string GmtOffsetPropertyName = "gmt_offset";

        private readonly ILogger<TimezoneDataService> _logger;
        private IReadOnlyList<TimezoneOption>? _timezones;

        public TimezoneDataService(ILogger<TimezoneDataService> logger)
        {
            _logger = logger;
        }

        public Task<IReadOnlyList<TimezoneOption>> GetTimezonesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _timezones ??= LoadTimezones(cancellationToken);
            return Task.FromResult(_timezones);
        }

        private IReadOnlyList<TimezoneOption> LoadTimezones(CancellationToken cancellationToken)
        {
            try
            {
                var json = AssetDataReader.ReadText(AssetConstants.Data.LocationTimezonesPath);
                using var document = JsonDocument.Parse(json);
                var options = new List<TimezoneOption>();

                foreach (var element in document.RootElement.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var countryCode = GetStringValue(element, CountryCodePropertyName);
                    var countryName = GetStringValue(element, CountryNamePropertyName);
                    var timezone = GetStringValue(element, TimezonePropertyName);
                    var gmtOffset = GetStringValue(element, GmtOffsetPropertyName);

                    if (countryCode.Length == 0 || countryName.Length == 0 || timezone.Length == 0)
                        continue;

                    options.Add(new TimezoneOption(
                        countryCode,
                        countryName,
                        timezone,
                        gmtOffset));
                }

                return options
                    .OrderBy(option => option.CountryName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(option => option.Timezone, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Failed to load timezone data.");
                return Array.Empty<TimezoneOption>();
            }
        }

        private static string GetStringValue(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(propertyName, out var property))
                return string.Empty;

            return property.GetString()?.Trim() ?? string.Empty;
        }
    }
}
