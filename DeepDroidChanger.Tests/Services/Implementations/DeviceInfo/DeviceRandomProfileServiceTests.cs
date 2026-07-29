using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using NSubstitute;
using System.Globalization;

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
            Manufacturer = "unknown",
            Brand = "unknown",
            Name = "unknown",
            Hardware = "tensor",
            Platform = "gs201",
            SecurityPatch = "2025-01-01",
        };
        api.GetRandomDeviceAsync(Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(apiDevice);
        var randomService = new DeterministicRandomService();
        DeviceRandomProfileService service = CreateService(api, randomService: randomService);
        var request = new RandomDeviceRequest
        {
            SelectedBrand = "SAMSUNG",
            SelectedAndroidVersion = "Android 14",
            Country = new CarrierCountryOption("VN", "84", "Vietnam"),
            Carrier = new CarrierOption("Viettel - Mobile", "452", "04"),
        };

        DeviceInfoApiDevice result = await service.CreateRandomProfileAsync(request, CancellationToken.None);

        Assert.AreSame(apiDevice, result);
        Assert.AreEqual("samsung", result.Manufacturer);
        Assert.AreEqual("samsung", result.Brand);
        Assert.AreEqual("husky", result.Name);
        Assert.AreEqual("husky", result.Product);
        Assert.AreEqual("husky", result.Code);
        Assert.AreEqual("14", result.Release);
        Assert.AreEqual("34", result.Sdk);
        Assert.AreEqual("AP1A", result.BuildId);
        Assert.AreEqual("123456", result.BuildIncremental);
        Assert.AreEqual("AP1A.123456", result.BuildDisplayId);
        Assert.AreEqual("husky-user", result.BuildFlavor);
        Assert.AreEqual("husky 14 AP1A 123456 release-keys", result.BuildDescription);
        Assert.AreEqual("android-husky", result.BuildUser);
        Assert.AreEqual("android-husky", result.BuildHost);
        Assert.AreEqual("123456", result.Bootloader);
        Assert.AreEqual("123456", result.Baseband);
        Assert.AreEqual("Sun Oct 05 00:00:00 UTC 2025", result.BuildDate);
        Assert.AreEqual(
            new DateTimeOffset(2025, 10, 5, 0, 0, 0, TimeSpan.Zero)
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture),
            result.BuildDateUtc);
        Assert.AreEqual("Pixel8_Robinson", result.SettingDeviceName);
        Assert.AreEqual("Pixel8_Simmons", result.SettingBluetoothName);
        Assert.AreEqual("66:77:88:99:aa:bb", result.BluetoothMacAddress);
        Assert.AreEqual("66:77:88:99:aa:bb", result.WifiBssid);
        Assert.AreEqual("Pixel8_Potter", result.WifiSsid);
        Assert.HasCount(64, result.VbmetaDigest);
        Assert.AreEqual("45204", result.SimOperatorNumeric);
        Assert.AreEqual("vn", result.SimOperatorCountry);
        Assert.AreEqual("Viettel", result.SimOperatorName);
        Assert.StartsWith("+84", result.SimPhoneNumber);
        Assert.AreEqual(15, result.Imei!.Length);
        Assert.AreEqual(15, result.Imei1!.Length);
        Assert.AreNotEqual(result.Imei, result.Imei1);
        Assert.AreEqual(result.Imei[..8], result.Imei1[..8]);
        CollectionAssert.AreEqual(new[] { "samsung", "samsung" }, randomService.ImeiBrands);
        await api.Received(1).GetRandomDeviceAsync(
            Arg.Is<RandomDeviceSelection>(selection => selection.Brand == "samsung" && selection.Sdk == 34),
            CancellationToken.None);
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_ValidServerImeis_ArePreservedWithoutFallbackGeneration()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        DeviceInfoApiDevice apiDevice = CreateApiDevice("Pixel");
        apiDevice.Imei = "355273350000000";
        apiDevice.Imei1 = "355273350000018";
        api.GetRandomDeviceAsync(Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(apiDevice);
        var randomService = new DeterministicRandomService();
        DeviceRandomProfileService service = CreateService(api, randomService: randomService);

        DeviceInfoApiDevice result = await service.CreateRandomProfileAsync(
            new RandomDeviceRequest { SelectedBrand = "google", SelectedAndroidVersion = "Android 13" },
            CancellationToken.None);

        Assert.AreEqual("355273350000000", result.Imei);
        Assert.AreEqual("355273350000018", result.Imei1);
        Assert.IsEmpty(randomService.ImeiBrands);
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_OnlyValidSecondaryServerImei_PreservesItAndGeneratesPrimary()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        DeviceInfoApiDevice apiDevice = CreateApiDevice("Pixel");
        apiDevice.Imei = null;
        apiDevice.Imei1 = "355273350000018";
        api.GetRandomDeviceAsync(Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(apiDevice);
        var randomService = new DeterministicRandomService();
        DeviceRandomProfileService service = CreateService(api, randomService: randomService);

        DeviceInfoApiDevice result = await service.CreateRandomProfileAsync(
            new RandomDeviceRequest { SelectedBrand = "google", SelectedAndroidVersion = "Android 13" },
            CancellationToken.None);

        Assert.AreEqual("355273350000000", result.Imei);
        Assert.AreEqual("355273350000018", result.Imei1);
        CollectionAssert.AreEqual(new[] { "google" }, randomService.ImeiBrands);
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_ValidServerBuildDateUtc_PreservesTimestampAndDerivesDisplayDate()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        DeviceInfoApiDevice apiDevice = CreateApiDevice("Pixel");
        apiDevice.BuildDateUtc = " 1760000000 ";
        api.GetRandomDeviceAsync(Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(apiDevice);
        DeviceRandomProfileService service = CreateService(api);

        DeviceInfoApiDevice result = await service.CreateRandomProfileAsync(
            new RandomDeviceRequest { SelectedBrand = "google", SelectedAndroidVersion = "Android 13" },
            CancellationToken.None);

        DateTimeOffset expected = DateTimeOffset.FromUnixTimeSeconds(1760000000);
        Assert.AreEqual("1760000000", result.BuildDateUtc);
        Assert.AreEqual(
            expected.ToString("ddd MMM dd HH:mm:ss 'UTC' yyyy", CultureInfo.InvariantCulture),
            result.BuildDate);
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_InvalidServerBuildDateUtc_FallsBackFromSecurityPatch()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        DeviceInfoApiDevice apiDevice = CreateApiDevice("Pixel", securityPatch: "2026-06-01");
        apiDevice.BuildDateUtc = "N/A";
        api.GetRandomDeviceAsync(Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(apiDevice);
        DeviceRandomProfileService service = CreateService(api);

        DeviceInfoApiDevice result = await service.CreateRandomProfileAsync(
            new RandomDeviceRequest { SelectedBrand = "google", SelectedAndroidVersion = "Android 13" },
            CancellationToken.None);

        DateTimeOffset expected = new(2026, 6, 4, 0, 0, 0, TimeSpan.Zero);
        Assert.AreEqual(expected.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), result.BuildDateUtc);
        Assert.AreEqual("Thu Jun 04 00:00:00 UTC 2026", result.BuildDate);
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_PlaceholderBootloaderAndBaseband_UseBuildIncremental()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        DeviceInfoApiDevice apiDevice = CreateApiDevice("Pixel");
        apiDevice.Bootloader = "N/A";
        apiDevice.Baseband = "null";
        api.GetRandomDeviceAsync(Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(apiDevice);
        DeviceRandomProfileService service = CreateService(api);

        DeviceInfoApiDevice result = await service.CreateRandomProfileAsync(
            new RandomDeviceRequest { SelectedBrand = "google", SelectedAndroidVersion = "Android 13" },
            CancellationToken.None);

        Assert.AreEqual("123456", result.Bootloader);
        Assert.AreEqual("123456", result.Baseband);
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_RandomBrandWithFixedSdk_UsesCompatibleBrandAndDefaults()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        api.GetRandomDeviceAsync(Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(CreateApiDevice("Model", release: "15"));
        DeviceRandomProfileService service = CreateService(api);

        DeviceInfoApiDevice result = await service.CreateRandomProfileAsync(
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
        api.GetRandomDeviceAsync(Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(CreateApiDevice("OnePlus", release: "13"));
        DeviceRandomProfileService service = CreateService(api);

        await service.CreateRandomProfileAsync(
            new RandomDeviceRequest { SelectedBrand = "oneplus", SelectedAndroidVersion = "Android 15" },
            CancellationToken.None);

        await api.Received(1).GetRandomDeviceAsync(
            Arg.Is<RandomDeviceSelection>(selection => selection.Brand == "OnePlus" && selection.Sdk == 33),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_ServerSdkDoesNotMatchRequest_RejectsInconsistentProfile()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        api.GetRandomDeviceAsync(Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(CreateApiDevice("Pixel", release: "14"));
        DeviceRandomProfileService service = CreateService(api);

        await Assert.ThrowsExactlyAsync<DeviceRandomApiException>(() => service.CreateRandomProfileAsync(
            new RandomDeviceRequest { SelectedBrand = "google", SelectedAndroidVersion = "Android 13" },
            CancellationToken.None));
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_MissingModel_ThrowsTypedApiException()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        api.GetRandomDeviceAsync(Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(new DeviceInfoApiDevice());
        DeviceRandomProfileService service = CreateService(api);

        await Assert.ThrowsExactlyAsync<DeviceRandomApiException>(() => service.CreateRandomProfileAsync(
            new RandomDeviceRequest(), CancellationToken.None));
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_MissingFingerprint_ThrowsTypedApiException()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        api.GetRandomDeviceAsync(Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(new DeviceInfoApiDevice { Model = "Pixel" });
        DeviceRandomProfileService service = CreateService(api);

        await Assert.ThrowsExactlyAsync<DeviceRandomApiException>(() => service.CreateRandomProfileAsync(
            new RandomDeviceRequest(), CancellationToken.None));
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_IntegrityPatchEnabled_OverridesApiSecurityPatch()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        api.GetRandomDeviceAsync(Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(CreateApiDevice("Pixel", securityPatch: "2024-01-01"));
        IDeviceIntegrityService integrity = Substitute.For<IDeviceIntegrityService>();
        integrity.TryGetRandomSecurityPatchAsync(Arg.Any<CancellationToken>()).Returns("2026-06-01");
        DeviceRandomProfileService service = CreateService(api, integrity);

        DeviceInfoApiDevice result = await service.CreateRandomProfileAsync(
            new RandomDeviceRequest { UseIntegritySecurityPatch = true },
            CancellationToken.None);

        Assert.AreEqual("2026-06-01", result.SecurityPatch);
        Assert.AreEqual("Thu Jun 04 00:00:00 UTC 2026", result.BuildDate);
        Assert.AreEqual(
            new DateTimeOffset(2026, 6, 4, 0, 0, 0, TimeSpan.Zero)
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture),
            result.BuildDateUtc);
        await integrity.Received(1).TryGetRandomSecurityPatchAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_IntegrityPatchUnavailable_KeepsApiSecurityPatch()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        api.GetRandomDeviceAsync(Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(CreateApiDevice("Pixel", securityPatch: "2024-01-01"));
        IDeviceIntegrityService integrity = Substitute.For<IDeviceIntegrityService>();
        integrity.TryGetRandomSecurityPatchAsync(Arg.Any<CancellationToken>()).Returns((string?)null);
        DeviceRandomProfileService service = CreateService(api, integrity);

        DeviceInfoApiDevice result = await service.CreateRandomProfileAsync(
            new RandomDeviceRequest { UseIntegritySecurityPatch = true },
            CancellationToken.None);

        Assert.AreEqual("2024-01-01", result.SecurityPatch);
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_NoSecurityPatchFromEitherSource_RejectsIncompleteProfile()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        api.GetRandomDeviceAsync(Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(CreateApiDevice("Pixel", securityPatch: string.Empty));
        IDeviceIntegrityService integrity = Substitute.For<IDeviceIntegrityService>();
        integrity.TryGetRandomSecurityPatchAsync(Arg.Any<CancellationToken>()).Returns((string?)null);
        DeviceRandomProfileService service = CreateService(api, integrity);

        await Assert.ThrowsExactlyAsync<DeviceRandomApiException>(() => service.CreateRandomProfileAsync(
            new RandomDeviceRequest { UseIntegritySecurityPatch = true },
            CancellationToken.None));
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_IntegrityPatchDisabled_DoesNotContactIntegrityServer()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        api.GetRandomDeviceAsync(Arg.Any<RandomDeviceSelection>(), Arg.Any<CancellationToken>())
            .Returns(CreateApiDevice("Pixel", securityPatch: "2024-01-01"));
        IDeviceIntegrityService integrity = Substitute.For<IDeviceIntegrityService>();
        DeviceRandomProfileService service = CreateService(api, integrity);

        DeviceInfoApiDevice result = await service.CreateRandomProfileAsync(
            new RandomDeviceRequest { UseIntegritySecurityPatch = false },
            CancellationToken.None);

        Assert.AreEqual("2024-01-01", result.SecurityPatch);
        await integrity.DidNotReceiveWithAnyArgs().TryGetRandomSecurityPatchAsync(default);
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_NullRequest_ThrowsBeforeApiCall()
    {
        IDeviceRandomApiService api = Substitute.For<IDeviceRandomApiService>();
        DeviceRandomProfileService service = CreateService(api);

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => service.CreateRandomProfileAsync(
            null!, CancellationToken.None));
        await api.DidNotReceiveWithAnyArgs().GetRandomDeviceAsync(default!, default);
    }

    private static DeviceInfoApiDevice CreateApiDevice(
        string model,
        string securityPatch = "2025-01-01",
        string release = "13")
    {
        return new DeviceInfoApiDevice
        {
            Model = model,
            Board = "board",
            Hardware = "hardware",
            Platform = "platform",
            Manufacturer = "google",
            Fingerprint = $"google/product/device:{release}/BUILD/123456:user/release-keys",
            SecurityPatch = securityPatch
        };
    }

    private static DeviceRandomProfileService CreateService(
        IDeviceRandomApiService api,
        IDeviceIntegrityService? integrity = null,
        IRandomService? randomService = null)
    {
        randomService ??= new DeterministicRandomService();
        return new DeviceRandomProfileService(
            api,
            integrity ?? Substitute.For<IDeviceIntegrityService>(),
            randomService,
            new SimProfileService(randomService));
    }

    private sealed class DeterministicRandomService : IRandomService
    {
        private readonly string[] _names = ["Robinson", "Simmons", "Potter"];
        private readonly string[] _imeis = ["355273350000000", "355273350000018"];
        private int _nameIndex;
        private int _imeiIndex;

        public List<string> ImeiBrands { get; } = [];

        public int RandomInRange(int minValue, int maxValue) => minValue;
        public long RandomInRange(long minValue, long maxValue) => minValue;
        public T PickRandom<T>(IReadOnlyList<T> values) => values[0];
        public string GetRandomLocalIp() => "192.168.20.20";
        public string GetRandomHexString(int minimumLength) => new('a', Math.Max(32, minimumLength));
        public string GenerateImsi(string mcc, string mnc) => string.Concat(mcc, mnc, "0000000000");
        public string GenerateIccid(string countryCode, string mnc) => string.Concat("89", countryCode, mnc, "0000000000000");
        public string GeneratePhoneNumber() => "100000000";
        public string GenerateName(bool requireSingle = false) => _names[_nameIndex++ % _names.Length];
        public string GenerateImei(string brand, string? preferredTac = null)
        {
            ImeiBrands.Add(brand);
            string imei = _imeis[_imeiIndex++ % _imeis.Length];
            return preferredTac is { Length: 8 }
                ? string.Concat(preferredTac, imei[8..])
                : imei;
        }
        public string GenerateMacAddress() => "00:11:22:33:44:55";
        public string GenerateWifiMacAddress(string brand) => "66:77:88:99:aa:bb";
    }
}
