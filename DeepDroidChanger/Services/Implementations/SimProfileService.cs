using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed class SimProfileService : ISimProfileService
{
    private const string DefaultMcc = "310";
    private const string DefaultMnc = "260";
    private const string DefaultCountryCode = "1";
    private const string DefaultCountryIso = "us";
    private const string DefaultCarrierName = "T-Mobile";

    private readonly IRandomService _randomService;

    public SimProfileService(IRandomService randomService)
    {
        _randomService = randomService;
    }

    public SimProfile CreateRandomProfile(CarrierCountryOption? country, CarrierOption? carrier)
    {
        string mcc = NormalizeDigits(carrier?.Mcc, DefaultMcc);
        string mnc = NormalizeDigits(carrier?.Mnc, DefaultMnc);
        string countryCode = NormalizeDigits(country?.CountryCode, DefaultCountryCode);
        string countryIso = string.IsNullOrWhiteSpace(country?.CountryIso)
            ? DefaultCountryIso
            : country.CountryIso.Trim().ToLowerInvariant();
        string carrierName = string.IsNullOrWhiteSpace(carrier?.CarrierName)
            ? DefaultCarrierName
            : carrier.CarrierName.Trim();

        return new SimProfile
        {
            Imsi = _randomService.GenerateImsi(mcc, mnc),
            Iccid = _randomService.GenerateIccid(countryCode, mnc),
            PhoneNumber = string.Concat("+", countryCode, _randomService.GeneratePhoneNumber()),
            OperatorNumeric = string.Concat(mcc, mnc),
            OperatorCountry = countryIso,
            OperatorName = carrierName
        };
    }

    private static string NormalizeDigits(string? value, string fallback)
    {
        string digits = string.Concat((value ?? string.Empty).Where(char.IsDigit));
        return digits.Length == 0 ? fallback : digits;
    }
}
