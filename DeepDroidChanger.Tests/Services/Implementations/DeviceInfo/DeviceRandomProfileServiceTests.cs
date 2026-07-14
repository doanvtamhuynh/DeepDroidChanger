using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.DeviceInfo;

[TestClass]
public sealed class DeviceRandomProfileServiceTests
{
    [TestMethod]
    public async Task CreateRandomProfileAsync_ExplicitSelection_NormalizesAndGeneratesProfile()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        var apiDevice = new DeviceInfoApiDevice
        {
            Model = "Pixel 8",
            Board = "husky",
            Fingerprint = "google/husky/husky:14/AP1A/123456:user/release-keys",
            Manufacturer = " ",
            Name = "unknown",
        };
        api.GetRandomDeviceAsync(Arg.Any<AccountSession>(), Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(apiDevice);
        var service = new DeviceRandomProfileService(api, new DeterministicRandomService());
        var session = CreateSession();
        var request = new RandomDeviceRequest
        {
            SelectedBrand = "SAMSUNG",
            SelectedAndroidVersion = "Android 14",
            Country = new CarrierCountryOption("VN", "84", "Vietnam"),
            Carrier = new CarrierOption("Viettel - Mobile", "452", "04"),
        };

        DeviceInfoApiDevice result = await service.CreateRandomProfileAsync(session, request, CancellationToken.None);

        Assert.AreSame(apiDevice, result);
        Assert.AreEqual("samsung", result.Manufacturer);
        Assert.AreEqual("samsung", result.Brand);
        Assert.AreEqual("husky", result.Name);
        Assert.AreEqual("husky", result.Product);
        Assert.AreEqual("husky", result.Code);
        Assert.AreEqual("14", result.Release);
        Assert.AreEqual("34", result.Sdk);
        Assert.AreEqual("45204", result.SimOperatorNumeric);
        Assert.AreEqual("vn", result.SimOperatorCountry);
        Assert.AreEqual("Viettel", result.SimOperatorName);
        Assert.StartsWith("+84", result.SimPhoneNumber);
        Assert.AreEqual(15, result.Imei!.Length);
        Assert.AreEqual(15, result.Imei1!.Length);
        await api.Received(1).GetRandomDeviceAsync(
            session,
            Arg.Is<RandomDeviceSelection>(selection => selection.Brand == "samsung" && selection.Sdk == 34),
            CancellationToken.None);
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_RandomBrandWithFixedSdk_UsesCompatibleBrandAndDefaults()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        api.GetRandomDeviceAsync(Arg.Any<AccountSession>(), Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(new DeviceInfoApiDevice { Model = "Model" });
        var service = new DeviceRandomProfileService(api, new DeterministicRandomService());

        DeviceInfoApiDevice result = await service.CreateRandomProfileAsync(
            CreateSession(),
            new RandomDeviceRequest { SelectedBrand = "Random", SelectedAndroidVersion = "35" },
            CancellationToken.None);

        Assert.AreEqual("google", result.Brand);
        Assert.AreEqual("35", result.Sdk);
        Assert.AreEqual("310260", result.SimOperatorNumeric);
        Assert.AreEqual("us", result.SimOperatorCountry);
        Assert.AreEqual("T-Mobile", result.SimOperatorName);
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_UnsupportedSdkForBrand_FallsBackToValidSdk()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        api.GetRandomDeviceAsync(Arg.Any<AccountSession>(), Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(new DeviceInfoApiDevice { Model = "OnePlus", Fingerprint = "short/value" });
        var service = new DeviceRandomProfileService(api, new DeterministicRandomService());

        await service.CreateRandomProfileAsync(
            CreateSession(),
            new RandomDeviceRequest { SelectedBrand = "oneplus", SelectedAndroidVersion = "Android 15" },
            CancellationToken.None);

        await api.Received(1).GetRandomDeviceAsync(
            Arg.Any<AccountSession>(),
            Arg.Is<RandomDeviceSelection>(selection => selection.Brand == "OnePlus" && selection.Sdk == 33),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_MissingModel_ThrowsTypedApiException()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        api.GetRandomDeviceAsync(Arg.Any<AccountSession>(), Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(new DeviceInfoApiDevice());
        var service = new DeviceRandomProfileService(api, new DeterministicRandomService());

        await Assert.ThrowsExactlyAsync<DeviceRandomApiException>(() => service.CreateRandomProfileAsync(
            CreateSession(), new RandomDeviceRequest(), CancellationToken.None));
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_NullRequest_ThrowsBeforeApiCall()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        var service = new DeviceRandomProfileService(api, new DeterministicRandomService());

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => service.CreateRandomProfileAsync(
            CreateSession(), null!, CancellationToken.None));
        await api.DidNotReceiveWithAnyArgs().GetRandomDeviceAsync(default!, default!, default);
    }

    private static AccountSession CreateSession() => new("https://example.test/graphql", "authorization", "token");

    private sealed class DeterministicRandomService : IRandomService
    {
        public int RandomInRange(int minValue, int maxValue) => minValue;
        public T PickRandom<T>(IReadOnlyList<T> values) => values[0];
        public string GetRandomLocalIp() => "192.168.20.20";
        public string GetRandomHexString(int minimumLength) => new('a', Math.Max(32, minimumLength));
        public string GenerateImsi(string mcc, string mnc) => string.Concat(mcc, mnc, "0000000000");
        public string GenerateIccid(string countryCode, string mnc) => string.Concat("89", countryCode, mnc, "0000000000000");
        public string GeneratePhoneNumber() => "100000000";
        public string GenerateMacAddress() => "00:11:22:33:44:55";
        public string GenerateWifiMacAddress(string brand) => "66:77:88:99:aa:bb";
    }
}
