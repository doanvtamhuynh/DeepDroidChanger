using DeepDroidChanger.Helpers;
using System.IO;
using System.Text.Json;

namespace DeepDroidChanger.Services
{
    public sealed class RandomService : IRandomService
    {
        private const string MacVendorsResourcePath = "Assets/Data/mac_vendors.json";
        private const string DefaultMacPrefix = "d4:88:90";
        private IReadOnlyDictionary<string, IReadOnlyList<string>>? _macPrefixes;

        public int RandomInRange(int minValue, int maxValue)
        {
            return Random.Shared.Next(minValue, maxValue);
        }

        public T PickRandom<T>(IReadOnlyList<T> values)
        {
            ArgumentNullException.ThrowIfNull(values);

            if (values.Count == 0)
                throw new ArgumentException("Collection must contain at least one item.", nameof(values));

            return values[RandomInRange(0, values.Count)];
        }

        public string GetRandomLocalIp()
        {
            return $"192.168.{RandomInRange(20, 200)}.{RandomInRange(20, 256)}";
        }

        public string GetRandomHexString(int minimumLength)
        {
            var bytes = new byte[Math.Max(8, minimumLength / 2 + 1)];
            Random.Shared.NextBytes(bytes);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        public string GenerateImsi(string mcc, string mnc)
        {
            var normalizedMcc = NormalizeDigits(mcc);
            var normalizedMnc = NormalizeDigits(mnc);
            var randomLength = normalizedMcc.Length + normalizedMnc.Length == 6 ? 9 : 10;
            return string.Concat(normalizedMcc, normalizedMnc, GenerateDigits(randomLength));
        }

        public string GenerateIccid(string countryCode, string mnc)
        {
            const string telecomIndustryIdentifier = "89";
            var body = string.Concat(
                telecomIndustryIdentifier,
                NormalizeDigits(countryCode),
                NormalizeDigits(mnc),
                GenerateDigits(12));
            return string.Concat(body, GenerateIccidChecksum(body));
        }

        public string GeneratePhoneNumber()
        {
            return RandomInRange(100000000, 999999999).ToString();
        }

        public string GenerateMacAddress()
        {
            return string.Join(":", Enumerable.Range(0, 6).Select(_ => RandomInRange(0, 256).ToString("x2")));
        }

        public string GenerateWifiMacAddress(string brand)
        {
            var prefix = PickMacPrefix(brand);
            var suffix = string.Join(":", Enumerable.Range(0, 3).Select(_ => RandomInRange(0, 253).ToString("x2")));
            return string.Concat(prefix, ":", suffix);
        }

        private string PickMacPrefix(string brand)
        {
            _macPrefixes ??= LoadMacPrefixes();
            var normalizedBrand = string.IsNullOrWhiteSpace(brand) ? "samsung" : brand.Trim().ToLowerInvariant();

            if (_macPrefixes.TryGetValue(normalizedBrand, out var prefixes) && prefixes.Count > 0)
                return PickRandom(prefixes);

            return DefaultMacPrefix;
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadMacPrefixes()
        {
            try
            {
                var json = AssetDataReader.ReadText(MacVendorsResourcePath);
                var prefixes = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json);
                return prefixes?.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<string>)pair.Value,
                    StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            }
            catch (IOException)
            {
                return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string GenerateDigits(int length)
        {
            return string.Concat(Enumerable.Range(0, length).Select(_ => Random.Shared.Next(0, 10).ToString()));
        }

        private static string NormalizeDigits(string value)
        {
            return string.Concat((value ?? string.Empty).Where(char.IsDigit));
        }

        private static int GenerateIccidChecksum(string number)
        {
            var sum = 0;
            for (var index = 0; index < number.Length; index++)
            {
                var digit = number[index] - '0';
                if (index % 2 != 0)
                {
                    digit *= 2;
                    if (digit >= 10)
                        digit = digit / 10 + digit % 10;
                }

                sum += digit;
            }

            return sum * 9 % 10;
        }
    }
}
