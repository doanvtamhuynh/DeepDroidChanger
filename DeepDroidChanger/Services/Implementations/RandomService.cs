using DeepDroidChanger.Constants;
using DeepDroidChanger.Helpers;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace DeepDroidChanger.Services
{
    public sealed class RandomService : IRandomService
    {
        private static readonly IReadOnlyList<string> FallbackNames =
            ["Alex", "Jordan", "Morgan", "Taylor"];
        internal static readonly IReadOnlyList<string> FallbackMacPrefixes =
        [
            "84:ab:1a", "bc:52:74", "a4:d9:90", "a8:05:56", "e4:b5:55",
            "fc:19:10", "bc:e1:43", "00:24:e9", "18:70:3b", "28:a0:2b",
            "28:bd:89", "94:0c:98", "00:0d:93", "80:04:5f", "64:44:7b",
            "d8:cf:9c", "bc:2e:f6", "30:66:d0", "8c:58:77", "e0:62:67"
        ];
        internal static readonly IReadOnlyList<string> FallbackImeiTacs =
        [
            "35527335", "35529153", "35531042", "35531436", "35536179", "35538821",
            "35547998", "35549649", "35549782", "35552257", "35167418", "35167506",
            "35167509", "35167530", "35167549", "35167564", "35167584", "35167656",
            "35167682", "35167709"
        ];
        private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<string>>> _imeiTacs = new(LoadImeiTacs);
        private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<string>>> _macPrefixes = new(LoadMacPrefixes);
        private readonly Lazy<IReadOnlyList<string>> _imeiPool;
        private readonly Lazy<IReadOnlyList<string>> _macPool;
        private readonly Lazy<IReadOnlyList<string>> _names = new(LoadNames);

        public RandomService()
        {
            _imeiPool = new(() => _imeiTacs.Value.Values.SelectMany(values => values).Distinct().ToArray());
            _macPool = new(() => _macPrefixes.Value.Values.SelectMany(values => values).Distinct().ToArray());
        }

        public int RandomInRange(int minValue, int maxValue)
        {
            return Random.Shared.Next(minValue, maxValue);
        }

        public long RandomInRange(long minValue, long maxValue)
        {
            return Random.Shared.NextInt64(minValue, maxValue);
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
            int length = Math.Max(1, minimumLength);
            var bytes = new byte[(length + 1) / 2];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToHexString(bytes).ToLowerInvariant()[..length];
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

        public string GenerateName(bool requireSingle = false)
        {
            IReadOnlyList<string> names = _names.Value;
            bool isSingleWord = RandomInRange(1, 7) % 4 != 0;
            if (isSingleWord || requireSingle)
                return PickRandom(names);

            return string.Concat(PickRandom(names), " ", PickRandom(names));
        }

        public string GenerateImei(string brand, string? preferredTac = null)
        {
            string tac = IsValidTac(preferredTac) ? preferredTac! : PickImeiTac(brand);
            string body = string.Concat(tac, GenerateDigits(6));
            return string.Concat(body, GenerateLuhnCheckDigit(body));
        }

        public string GenerateMacAddress()
        {
            return string.Join(":", Enumerable.Range(0, 6).Select(_ => RandomInRange(0, 256).ToString("x2")));
        }

        public string GenerateWifiMacAddress(string brand)
        {
            var prefix = PickMacPrefix(brand);
            var suffix = string.Join(":", Enumerable.Range(0, 3).Select(_ => RandomInRange(0, 256).ToString("x2")));
            return string.Concat(prefix, ":", suffix);
        }

        private string PickMacPrefix(string brand)
        {
            string normalizedBrand = brand?.Trim().ToLowerInvariant() ?? string.Empty;
            var prefixesByBrand = _macPrefixes.Value;

            if (prefixesByBrand.TryGetValue(normalizedBrand, out var prefixes) && prefixes.Count > 0)
                return PickRandom(prefixes);

            string alias = normalizedBrand switch
            {
                "poco" or "redmi" => "xiaomi",
                "oppo" => "oneplus",
                _ => normalizedBrand
            };
            if (prefixesByBrand.TryGetValue(alias, out prefixes) && prefixes.Count > 0)
                return PickRandom(prefixes);
            return PickRandom(_macPool.Value.Count > 0 ? _macPool.Value : FallbackMacPrefixes);
        }

        private string PickImeiTac(string brand)
        {
            string normalizedBrand = string.IsNullOrWhiteSpace(brand)
                ? string.Empty
                : brand.Trim().ToLowerInvariant();
            var tacsByBrand = _imeiTacs.Value;

            if (tacsByBrand.TryGetValue(normalizedBrand, out IReadOnlyList<string>? tacs) && tacs.Count > 0)
                return PickRandom(tacs);

            string alias = normalizedBrand is "poco" or "redmi" ? "xiaomi" : normalizedBrand;
            if (tacsByBrand.TryGetValue(alias, out tacs) && tacs.Count > 0)
                return PickRandom(tacs);
            return PickRandom(_imeiPool.Value.Count > 0 ? _imeiPool.Value : FallbackImeiTacs);
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadImeiTacs()
            => LoadIdentifierValues(AssetConstants.Data.ImeiTacsPath, IsValidTac);

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadMacPrefixes()
            => LoadIdentifierValues(AssetConstants.Data.MacVendorsPath, IsValidMacPrefix);

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadIdentifierValues(
            string path, Func<string?, bool> isValid)
        {
            try
            {
                string json = AssetDataReader.ReadText(path);
                var source = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json);
                return source?.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<string>)pair.Value.Where(isValid)
                        .Select(value => value.ToLowerInvariant())
                        .Distinct(StringComparer.Ordinal).ToArray(),
                    StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static IReadOnlyList<string> LoadNames()
        {
            try
            {
                string text = AssetDataReader.ReadText(AssetConstants.Data.NamesPath);
                string[] names = text
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(name => name.Length > 0)
                    .ToArray();
                return names.Length > 0 ? names : FallbackNames;
            }
            catch (IOException)
            {
                return FallbackNames;
            }
        }

        private static bool IsValidTac(string? value)
        {
            return value is { Length: 8 } && value.All(char.IsDigit);
        }

        private static bool IsValidMacPrefix(string? value)
        {
            if (value is not { Length: 8 })
                return false;
            string[] parts = value.Split(':');
            return parts.Length == 3 && parts.All(part => part.Length == 2 && part.All(Uri.IsHexDigit))
                && (Convert.ToByte(parts[0], 16) & 1) == 0;
        }

        private string GenerateDigits(int length)
        {
            return string.Concat(Enumerable.Range(0, length).Select(_ => RandomInRange(0, 10).ToString()));
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

        private static int GenerateLuhnCheckDigit(string body)
        {
            var sum = 0;
            for (var index = 0; index < body.Length; index++)
            {
                var digit = body[index] - '0';
                if ((index + 1) % 2 == 0)
                {
                    digit *= 2;
                    if (digit > 9)
                        digit -= 9;
                }

                sum += digit;
            }

            return (10 - sum % 10) % 10;
        }
    }
}
