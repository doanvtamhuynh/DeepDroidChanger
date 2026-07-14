using DeepDroidChanger.Services;

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
        string mac = service.GenerateMacAddress();
        string wifiMac = service.GenerateWifiMacAddress("unknown-vendor");

        Assert.IsGreaterThanOrEqualTo(16, hex.Length);
        Assert.IsTrue(hex.All(Uri.IsHexDigit));
        Assert.HasCount(15, imsi);
        Assert.HasCount(15, shortMncImsi);
        Assert.StartsWith("310260", imsi);
        Assert.StartsWith("898404", iccid);
        Assert.IsTrue(iccid.All(char.IsDigit));
        Assert.HasCount(9, phone);
        Assert.HasCount(6, mac.Split(':'));
        Assert.StartsWith("d4:88:90:", wifiMac);
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
}
