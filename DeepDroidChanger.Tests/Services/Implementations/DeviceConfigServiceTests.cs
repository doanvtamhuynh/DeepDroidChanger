using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations
{
    [TestClass]
    public sealed class DeviceConfigServiceTests
    {
        [TestMethod]
        public async Task SaveDeviceRowAsync_PreservesSelectedCarrierMccMnc()
        {
            var store = Substitute.For<IDeviceStoreService>();
            var settingsService = Substitute.For<ISettingsService>();
            var service = new DeviceConfigService(store, settingsService, new AppSettings());
            var storedDevices = new List<StoredDeviceConfig>
            {
                new() { Serial = "SERIAL", Name = "Old", Type = "Phone" }
            };
            ConfigureUpdates(store, storedDevices);
            var country = new CarrierCountryOption("us", "1", "United States");
            var carrier = new CarrierOption("AT&T Wireless Inc.", "310", "410");

            var saved = await service.SaveDeviceRowAsync(
                storedDevices,
                "serial",
                "Pixel",
                "sargo",
                country,
                carrier,
                includeSelectedCarrierConfig: true,
                CancellationToken.None);

            Assert.IsTrue(saved);
            Assert.AreEqual("Pixel", storedDevices[0].Name);
            Assert.AreEqual("sargo", storedDevices[0].Type);
            Assert.AreEqual("us", storedDevices[0].CountryIso);
            Assert.AreEqual("AT&T Wireless Inc.", storedDevices[0].Carrier);
            Assert.AreEqual("310", storedDevices[0].CarrierMcc);
            Assert.AreEqual("410", storedDevices[0].CarrierMnc);
            await store.Received(1).UpdateAsync(
                "serial",
                Arg.Any<Action<StoredDeviceConfig>>(),
                CancellationToken.None);
        }

        [TestMethod]
        public async Task SaveDeviceProfileAsync_PersistsOnlyAllowedMainConfigFields()
        {
            var store = Substitute.For<IDeviceStoreService>();
            var service = new DeviceConfigService(store, Substitute.For<ISettingsService>(), new AppSettings());
            var storedDevices = new List<StoredDeviceConfig> { new() { Serial = "SERIAL" } };
            ConfigureUpdates(store, storedDevices);
            var profile = new DeviceProfileConfig
            {
                Brand = "Samsung",
                AndroidVersion = "Android 15",
                ChangeSimEnabled = false,
                UseIntegritySecurityPatch = true,
                CountryIso = "vn",
                CountryName = "Vietnam",
                Carrier = "Viettel",
                CarrierMcc = "452",
                CarrierMnc = "04",
                ChangeOptions = new DeviceChangeOptions
                {
                    UseDefaultMode = false,
                    ChangeAndroidId = true,
                    ClearAllPackages = false,
                    ClearSelectedPackages = true,
                    ChangeMacAddress = false,
                    UseRmRfForPackageCleanup = true,
                    ClearGooglePackages = true,
                    ClearGoogleAccounts = true,
                    SelectedPackages = ["com.example.two", "com.example.one", "com.example.two"]
                }
            };

            bool saved = await service.SaveDeviceProfileAsync(
                storedDevices,
                "serial",
                profile,
                CancellationToken.None);

            Assert.IsTrue(saved);
            StoredDeviceConfig config = storedDevices[0];
            Assert.AreEqual("Samsung", config.Brand);
            Assert.AreEqual("Android 15", config.AndroidVersion);
            Assert.IsFalse(config.ChangeSimEnabled);
            Assert.IsTrue(config.UseIntegritySecurityPatch);
            Assert.AreEqual("vn", config.CountryIso);
            Assert.AreEqual("Viettel", config.Carrier);
            Assert.AreEqual("452", config.CarrierMcc);
            Assert.AreEqual("04", config.CarrierMnc);
            Assert.IsFalse(config.ChangeOptions.UseDefaultMode);
            Assert.IsTrue(config.ChangeOptions.ChangeAndroidId);
            Assert.IsTrue(config.ChangeOptions.ClearSelectedPackages);
            Assert.IsFalse(config.ChangeOptions.ChangeMacAddress);
            Assert.IsTrue(config.ChangeOptions.UseRmRfForPackageCleanup);
            Assert.IsTrue(config.ChangeOptions.ClearGooglePackages);
            Assert.IsTrue(config.ChangeOptions.ClearGoogleAccounts);
            CollectionAssert.AreEqual(
                new[] { "com.example.one", "com.example.two" },
                config.ChangeOptions.SelectedPackages);
            await store.Received(1).UpdateAsync(
                "serial",
                Arg.Any<Action<StoredDeviceConfig>>(),
                CancellationToken.None);
        }

        [TestMethod]
        public async Task SaveTimezoneConfigAsync_PersistsResolvedTimezoneValue()
        {
            var store = Substitute.For<IDeviceStoreService>();
            var service = new DeviceConfigService(store, Substitute.For<ISettingsService>(), new AppSettings());
            var storedDevices = new List<StoredDeviceConfig> { new() { Serial = "SERIAL" } };
            ConfigureUpdates(store, storedDevices);

            bool saved = await service.SaveTimezoneConfigAsync(
                storedDevices,
                "serial",
                ChangeTimezoneMode.DeviceIp,
                "Asia/Ho_Chi_Minh",
                CancellationToken.None);

            Assert.IsTrue(saved);
            Assert.AreEqual(nameof(ChangeTimezoneMode.DeviceIp), storedDevices[0].TimezoneMode);
            Assert.AreEqual("Asia/Ho_Chi_Minh", storedDevices[0].Timezone);
            await store.Received(1).UpdateAsync(
                "serial",
                Arg.Any<Action<StoredDeviceConfig>>(),
                CancellationToken.None);
        }

        [TestMethod]
        public async Task SaveLocationConfigAsync_PersistsResolvedCoordinates()
        {
            var store = Substitute.For<IDeviceStoreService>();
            var service = new DeviceConfigService(store, Substitute.For<ISettingsService>(), new AppSettings());
            var storedDevices = new List<StoredDeviceConfig> { new() { Serial = "SERIAL" } };
            ConfigureUpdates(store, storedDevices);

            bool saved = await service.SaveLocationConfigAsync(
                storedDevices,
                "serial",
                ChangeLocationMode.DeviceIp,
                "10.7626",
                "106.6602",
                CancellationToken.None);

            Assert.IsTrue(saved);
            Assert.AreEqual(nameof(ChangeLocationMode.DeviceIp), storedDevices[0].LocationMode);
            Assert.AreEqual("10.7626", storedDevices[0].LocationLatitude);
            Assert.AreEqual("106.6602", storedDevices[0].LocationLongitude);
            await store.Received(1).UpdateAsync(
                "serial",
                Arg.Any<Action<StoredDeviceConfig>>(),
                CancellationToken.None);
        }

        [TestMethod]
        public async Task SaveLocationConfigAsync_WithMetadata_ReplacesStaleMetadata()
        {
            var store = Substitute.For<IDeviceStoreService>();
            var service = new DeviceConfigService(store, Substitute.For<ISettingsService>(), new AppSettings());
            var storedDevices = new List<StoredDeviceConfig>
            {
                new()
                {
                    Serial = "SERIAL",
                    LocationCountryCode = "FR",
                    LocationCityName = "Paris"
                }
            };
            ConfigureUpdates(store, storedDevices);

            bool saved = await service.SaveLocationConfigAsync(
                storedDevices,
                "SERIAL",
                ChangeLocationMode.DeviceIp,
                "10.7626",
                "106.6602",
                "VN",
                string.Empty,
                CancellationToken.None);

            Assert.IsTrue(saved);
            Assert.AreEqual("VN", storedDevices[0].LocationCountryCode);
            Assert.AreEqual(string.Empty, storedDevices[0].LocationCityName);
        }

        [TestMethod]
        public async Task SaveLocationConfigAsync_LegacyOverload_PreservesExistingMetadata()
        {
            var store = Substitute.For<IDeviceStoreService>();
            var service = new DeviceConfigService(store, Substitute.For<ISettingsService>(), new AppSettings());
            var storedDevices = new List<StoredDeviceConfig>
            {
                new()
                {
                    Serial = "SERIAL",
                    LocationCountryCode = "FR",
                    LocationCityName = "Paris"
                }
            };
            ConfigureUpdates(store, storedDevices);

            bool saved = await service.SaveLocationConfigAsync(
                storedDevices,
                "SERIAL",
                ChangeLocationMode.Config,
                "10.7626",
                "106.6602",
                CancellationToken.None);

            Assert.IsTrue(saved);
            Assert.AreEqual("FR", storedDevices[0].LocationCountryCode);
            Assert.AreEqual("Paris", storedDevices[0].LocationCityName);
        }

        [TestMethod]
        public async Task SaveDeviceProfileAsync_DoesNotOverwriteNewerDialogConfigFromStaleCache()
        {
            using var fixture = new TestTempDirectory();
            string path = Path.Combine(fixture.Path, "ChangeSingleDevice", "devices.json");
            var store = new DeviceStoreService(path, NullLogger<DeviceStoreService>.Instance);
            await store.SaveAsync(
                [new StoredDeviceConfig
                {
                    Serial = "SERIAL",
                    LocationMode = nameof(ChangeLocationMode.Config),
                    LocationLatitude = "10.7626",
                    LocationLongitude = "106.6602",
                    ProxyFullString = "proxy.example:1080"
                }],
                CancellationToken.None);
            var staleCache = new List<StoredDeviceConfig> { new() { Serial = "SERIAL" } };
            var service = new DeviceConfigService(store, Substitute.For<ISettingsService>(), new AppSettings());

            bool saved = await service.SaveDeviceProfileAsync(
                staleCache,
                "SERIAL",
                new DeviceProfileConfig { Brand = "Samsung", AndroidVersion = "Android 15" },
                CancellationToken.None);
            IReadOnlyList<StoredDeviceConfig> reloaded = await store.LoadAsync(CancellationToken.None);

            Assert.IsTrue(saved);
            Assert.AreEqual("Samsung", reloaded[0].Brand);
            Assert.AreEqual("Android 15", reloaded[0].AndroidVersion);
            Assert.AreEqual(nameof(ChangeLocationMode.Config), reloaded[0].LocationMode);
            Assert.AreEqual("10.7626", reloaded[0].LocationLatitude);
            Assert.AreEqual("106.6602", reloaded[0].LocationLongitude);
            Assert.AreEqual("proxy.example:1080", reloaded[0].ProxyFullString);
        }

        private static void ConfigureUpdates(
            IDeviceStoreService store,
            IList<StoredDeviceConfig> devices)
        {
            store.UpdateAsync(
                    Arg.Any<string>(),
                    Arg.Any<Action<StoredDeviceConfig>>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    string serial = callInfo.ArgAt<string>(0);
                    StoredDeviceConfig? device = devices.FirstOrDefault(item =>
                        string.Equals(item.Serial, serial, StringComparison.OrdinalIgnoreCase));
                    if (device == null)
                        return false;

                    callInfo.ArgAt<Action<StoredDeviceConfig>>(1)(device);
                    return true;
                });
        }
    }
}
