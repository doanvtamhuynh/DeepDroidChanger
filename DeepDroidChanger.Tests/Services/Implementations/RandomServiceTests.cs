using DeepDroidChanger.Helpers;
using DeepDroidChanger.Services;
using System.Text.Json;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
public sealed class RandomServiceTests
{
    [TestMethod]
    public void PickRandom_ValidAndInvalidCollections_BehavesPredictably()
    {
        var service = new RandomService();

        Assert.AreEqual("only", service.PickRandom(new[] { "only" }));
        Assert.ThrowsExactly<ArgumentNullException>(() => service.PickRandom<string>(null!));
        Assert.ThrowsExactly<ArgumentException>(() => service.PickRandom(Array.Empty<string>()));
    }

    [TestMethod]
    public void GeneratedIdentifiers_HaveExpectedShapeAndChecksums()
    {
        var service = new RandomService();

        string hex = service.GetRandomHexString(16);
        string imsi = service.GenerateImsi("310", "260");
        string shortMncImsi = service.GenerateImsi("452", "04");
        string iccid = service.GenerateIccid("84", "04");
        string phone = service.GeneratePhoneNumber();
        string name = service.GenerateName(requireSingle: true);
        string imei = service.GenerateImei("google");
        string mac = service.GenerateMacAddress();
        string wifiMac = service.GenerateWifiMacAddress("unknown-vendor");

        Assert.HasCount(16, hex);
        Assert.IsTrue(hex.All(Uri.IsHexDigit));
        Assert.HasCount(15, imsi);
        Assert.HasCount(15, shortMncImsi);
        Assert.StartsWith("310260", imsi);
        Assert.StartsWith("898404", iccid);
        Assert.IsTrue(iccid.All(char.IsDigit));
        Assert.IsTrue(IsValidIccidChecksum(iccid));
        Assert.HasCount(9, phone);
        Assert.IsFalse(string.IsNullOrWhiteSpace(name));
        Assert.IsFalse(name.Any(char.IsWhiteSpace));
        Assert.HasCount(15, imei);
        Assert.IsTrue(imei.All(char.IsDigit));
        Assert.IsTrue(IsValidLuhn(imei));
        Assert.HasCount(6, mac.Split(':'));
        Assert.IsTrue(FallbackPrefixMatches(wifiMac));
    }

    [TestMethod]
    public void GenerateWifiMacAddress_KnownAndUnknownBrands_UseDatabaseAndFallbackPrefixes()
    {
        const string resourcePath = "Assets/Data/mac_vendors.json";
        Dictionary<string, string[]> database = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            AssetDataReader.ReadText(resourcePath))!;
        var service = new RandomService();

        Assert.HasCount(8, database);
        Assert.AreEqual(3_241, database.Values.Sum(values => values.Length));
        Assert.AreEqual(1_352, database["apple"].Length);
        Assert.IsTrue(database.Values.SelectMany(values => values).All(IsValidMacPrefix));
        Assert.HasCount(20, RandomService.FallbackMacPrefixes);

        var allPrefixes = database.Values.SelectMany(values => values).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var applePrefixes = database["apple"].ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.IsTrue(RandomService.FallbackMacPrefixes.All(allPrefixes.Contains));

        for (var iteration = 0; iteration < 100; iteration++)
        {
            string appleMac = service.GenerateWifiMacAddress("ApPlE");
            string fallbackMac = service.GenerateWifiMacAddress("brand-not-in-database");

            Assert.IsTrue(applePrefixes.Contains(appleMac[..8]));
            Assert.IsTrue(FallbackPrefixMatches(fallbackMac));
        }
    }

    [TestMethod]
    public void GenerateImei_KnownAndUnknownBrands_UseDatabaseAndFallbackTacs()
    {
        const string resourcePath = "Assets/Data/imei_tacs.json";
        Dictionary<string, string[]> database = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            AssetDataReader.ReadText(resourcePath))!;
        var service = new RandomService();

        Assert.HasCount(8, database);
        Assert.AreEqual(37_435, database.Values.Sum(values => values.Length));
        Assert.IsTrue(database.Values.SelectMany(values => values).All(tac => tac.Length == 8 && tac.All(char.IsDigit)));
        Assert.HasCount(20, RandomService.FallbackImeiTacs);

        var googleTacs = database["google"].ToHashSet(StringComparer.Ordinal);
        var fallbackTacs = RandomService.FallbackImeiTacs.ToHashSet(StringComparer.Ordinal);
        string firstImei = service.GenerateImei("google");
        string pairedImei = service.GenerateImei("google", firstImei[..8]);
        Assert.AreEqual(firstImei[..8], pairedImei[..8]);
        Assert.IsTrue(IsValidLuhn(pairedImei));

        for (var iteration = 0; iteration < 100; iteration++)
        {
            string googleImei = service.GenerateImei("GoOgLe");
            string fallbackImei = service.GenerateImei("brand-not-in-database");

            Assert.IsTrue(googleTacs.Contains(googleImei[..8]));
            Assert.IsTrue(fallbackTacs.Contains(fallbackImei[..8]));
            Assert.IsTrue(IsValidLuhn(googleImei));
            Assert.IsTrue(IsValidLuhn(fallbackImei));
        }
    }

    [TestMethod]
    public void GetRandomLocalIp_RepeatedValues_AreValidIpv4Addresses()
    {
        var service = new RandomService();

        for (var iteration = 0; iteration < 1_000; iteration++)
        {
            string ipAddress = service.GetRandomLocalIp();
            string[] octets = ipAddress.Split('.');

            Assert.HasCount(4, octets);
            Assert.IsTrue(octets.All(octet =>
                int.TryParse(octet, out int value) && value is >= 0 and <= 255));
        }
    }

    private static bool IsValidLuhn(string value)
    {
        var sum = 0;
        for (var index = 0; index < value.Length - 1; index++)
        {
            int digit = value[index] - '0';
            if ((index + 1) % 2 == 0)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }

            sum += digit;
        }

        return (10 - sum % 10) % 10 == value[^1] - '0';
    }

    private static bool IsValidIccidChecksum(string value)
    {
        string body = value[..^1];
        var sum = 0;
        for (var index = 0; index < body.Length; index++)
        {
            int digit = body[index] - '0';
            if (index % 2 != 0)
            {
                digit *= 2;
                if (digit >= 10)
                    digit = digit / 10 + digit % 10;
            }

            sum += digit;
        }

        return sum * 9 % 10 == value[^1] - '0';
    }

    private static bool FallbackPrefixMatches(string macAddress)
    {
        return RandomService.FallbackMacPrefixes.Contains(macAddress[..8], StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsValidMacPrefix(string value)
    {
        string[] octets = value.Split(':');
        return octets.Length == 3
            && octets.All(octet => octet.Length == 2 && octet.All(Uri.IsHexDigit));
    }
}
