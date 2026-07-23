using System.Text.Json;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
[DoNotParallelize]
public sealed class DeviceStoreServiceTests
{
    [TestMethod]
    public async Task LoadAsync_CorruptJson_IsQuarantinedBeforeDefaultsAreWritten()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "devices.json");
        await File.WriteAllTextAsync(path, "{not-json");
        var service = new DeviceStoreService(path, NullLogger<DeviceStoreService>.Instance);

        IReadOnlyList<StoredDeviceConfig> devices = await service.LoadAsync(CancellationToken.None);

        Assert.IsEmpty(devices);
        Assert.HasCount(1, Directory.GetFiles(fixture.Path, "devices.json.corrupt-*"));
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.AreEqual(JsonValueKind.Array, document.RootElement.GetProperty("devices").ValueKind);
    }

    [TestMethod]
    public async Task LoadAsync_MergesSerialsCaseInsensitivelyAndPreservesCarrierIdentity()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "devices.json");
        await File.WriteAllTextAsync(
            path,
            "{\"devices\":["
            + "{\"serial\":\"ABC\",\"carrier\":\"Shared Name\",\"carrierMcc\":\"310\",\"carrierMnc\":\"260\"},"
            + "{\"serial\":\"abc\",\"carrier\":\"Shared Name\",\"carrierMcc\":\"452\",\"carrierMnc\":\"04\"}]}");
        var service = new DeviceStoreService(path, NullLogger<DeviceStoreService>.Instance);

        IReadOnlyList<StoredDeviceConfig> devices = await service.LoadAsync(CancellationToken.None);

        Assert.HasCount(1, devices);
        Assert.AreEqual("310", devices[0].CarrierMcc);
        Assert.AreEqual("260", devices[0].CarrierMnc);
    }

    [TestMethod]
    public async Task LoadAsync_LegacyJson_UsesBackwardCompatibleProfileDefaults()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "devices.json");
        await File.WriteAllTextAsync(path, "{\"devices\":[{\"serial\":\"LEGACY\",\"name\":\"Old device\"}]}");
        var service = new DeviceStoreService(path, NullLogger<DeviceStoreService>.Instance);

        IReadOnlyList<StoredDeviceConfig> devices = await service.LoadAsync(CancellationToken.None);

        Assert.HasCount(1, devices);
        Assert.IsTrue(devices[0].ChangeSimEnabled);
        Assert.IsTrue(devices[0].UseIntegritySecurityPatch);
        Assert.AreEqual(string.Empty, devices[0].Brand);
        Assert.AreEqual(string.Empty, devices[0].AndroidVersion);
        Assert.AreEqual(string.Empty, devices[0].Timezone);
    }

    [TestMethod]
    public async Task MergeAsync_NewDevice_PreservesExistingDeviceProfileAndDialogConfig()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "devices.json");
        var service = new DeviceStoreService(path, NullLogger<DeviceStoreService>.Instance);
        await service.SaveAsync(
            [
                new StoredDeviceConfig
                {
                    Serial = "EXISTING",
                    Brand = "Samsung",
                    CountryIso = "vn",
                    CarrierMcc = "452",
                    CarrierMnc = "04",
                    LocationLatitude = "10.8231",
                    LocationLongitude = "106.6297",
                    Timezone = "Asia/Ho_Chi_Minh",
                    ProxyFullString = "127.0.0.1:1080"
                }
            ],
            CancellationToken.None);

        IReadOnlyList<StoredDeviceConfig> merged = await service.MergeAsync(
            [new StoredDeviceConfig { Serial = "NEW", Name = "New device", Type = "Phone" }],
            CancellationToken.None);

        Assert.HasCount(2, merged);
        StoredDeviceConfig existing = merged.Single(device => device.Serial == "EXISTING");
        Assert.AreEqual("Samsung", existing.Brand);
        Assert.AreEqual("vn", existing.CountryIso);
        Assert.AreEqual("452", existing.CarrierMcc);
        Assert.AreEqual("04", existing.CarrierMnc);
        Assert.AreEqual("10.8231", existing.LocationLatitude);
        Assert.AreEqual("106.6297", existing.LocationLongitude);
        Assert.AreEqual("Asia/Ho_Chi_Minh", existing.Timezone);
        Assert.AreEqual("127.0.0.1:1080", existing.ProxyFullString);
    }

    [TestMethod]
    public async Task SaveAndLoadAsync_PreservesAllowedMainAndDialogConfig()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "devices.json");
        var service = new DeviceStoreService(path, NullLogger<DeviceStoreService>.Instance);
        var config = new StoredDeviceConfig
        {
            Serial = "SERIAL",
            Name = "Primary device",
            Type = "Phone",
            Brand = "Google",
            AndroidVersion = "Android 14",
            ChangeSimEnabled = false,
            UseIntegritySecurityPatch = true,
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
                SelectedPackages = ["com.example.app"]
            },
            CountryIso = "us",
            CountryName = "United States",
            Carrier = "T-Mobile",
            CarrierMcc = "310",
            CarrierMnc = "260",
            UpdateIntegrityFromServer = false,
            UpdateIntegrityFile = "pif.json",
            UpdateKeyboxFile = "keybox.xml",
            UpdateIntegrityEnabled = false,
            UpdateKeyboxEnabled = true,
            LocationMode = nameof(ChangeLocationMode.Config),
            LocationLatitude = "38.9072",
            LocationLongitude = "-77.0369",
            LocationCountryCode = "US",
            LocationCityName = "Washington D.C.",
            TimezoneMode = nameof(ChangeTimezoneMode.Data),
            Timezone = "America/New_York",
            ProxyFullString = "proxy.example:1080:user:password",
            ProxyType = "SOCKS5",
            ProxyChangeLocationByIp = false,
            ProxyChangeTimezoneByIp = true
        };

        await service.SaveAsync([config], CancellationToken.None);
        IReadOnlyList<StoredDeviceConfig> loaded = await service.LoadAsync(CancellationToken.None);

        Assert.HasCount(1, loaded);
        StoredDeviceConfig restored = loaded[0];
        Assert.AreEqual("Primary device", restored.Name);
        Assert.AreEqual("Phone", restored.Type);
        Assert.AreEqual("Google", restored.Brand);
        Assert.AreEqual("Android 14", restored.AndroidVersion);
        Assert.IsFalse(restored.ChangeSimEnabled);
        Assert.IsTrue(restored.UseIntegritySecurityPatch);
        Assert.IsFalse(restored.ChangeOptions.UseDefaultMode);
        Assert.IsTrue(restored.ChangeOptions.ChangeAndroidId);
        Assert.IsTrue(restored.ChangeOptions.ClearSelectedPackages);
        Assert.IsFalse(restored.ChangeOptions.ChangeMacAddress);
        Assert.IsTrue(restored.ChangeOptions.UseRmRfForPackageCleanup);
        Assert.IsTrue(restored.ChangeOptions.ClearGooglePackages);
        Assert.IsTrue(restored.ChangeOptions.ClearGoogleAccounts);
        CollectionAssert.AreEqual(new[] { "com.example.app" }, restored.ChangeOptions.SelectedPackages);
        Assert.AreEqual("us", restored.CountryIso);
        Assert.AreEqual("United States", restored.CountryName);
        Assert.AreEqual("T-Mobile", restored.Carrier);
        Assert.AreEqual("310", restored.CarrierMcc);
        Assert.AreEqual("260", restored.CarrierMnc);
        Assert.IsFalse(restored.UpdateIntegrityFromServer);
        Assert.AreEqual("pif.json", restored.UpdateIntegrityFile);
        Assert.AreEqual("keybox.xml", restored.UpdateKeyboxFile);
        Assert.IsFalse(restored.UpdateIntegrityEnabled);
        Assert.IsTrue(restored.UpdateKeyboxEnabled);
        Assert.AreEqual(nameof(ChangeLocationMode.Config), restored.LocationMode);
        Assert.AreEqual("38.9072", restored.LocationLatitude);
        Assert.AreEqual("-77.0369", restored.LocationLongitude);
        Assert.AreEqual("US", restored.LocationCountryCode);
        Assert.AreEqual("Washington D.C.", restored.LocationCityName);
        Assert.AreEqual(nameof(ChangeTimezoneMode.Data), restored.TimezoneMode);
        Assert.AreEqual("America/New_York", restored.Timezone);
        Assert.AreEqual("proxy.example:1080:user:password", restored.ProxyFullString);
        Assert.AreEqual("SOCKS5", restored.ProxyType);
        Assert.IsFalse(restored.ProxyChangeLocationByIp);
        Assert.IsTrue(restored.ProxyChangeTimezoneByIp);
    }

    [TestMethod]
    public async Task SaveAndLoadAsync_ExplicitDisabledIntegrityPatch_PreservesFalse()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "devices.json");
        var service = new DeviceStoreService(path, NullLogger<DeviceStoreService>.Instance);

        await service.SaveAsync(
            [new StoredDeviceConfig { Serial = "SERIAL", UseIntegritySecurityPatch = false }],
            CancellationToken.None);
        IReadOnlyList<StoredDeviceConfig> loaded = await service.LoadAsync(CancellationToken.None);

        Assert.HasCount(1, loaded);
        Assert.IsFalse(loaded[0].UseIntegritySecurityPatch);
    }

    [TestMethod]
    public async Task LoadAsync_LegacyRandomDeviceValues_AreRemovedFromPersistedJson()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "devices.json");
        await File.WriteAllTextAsync(
            path,
            "{\"devices\":[{\"serial\":\"SERIAL\",\"brand\":\"Samsung\","
            + "\"deviceInfoModel\":\"SM-S928B\",\"deviceInfoImei\":\"123456789012345\"}]}");
        var service = new DeviceStoreService(path, NullLogger<DeviceStoreService>.Instance);

        IReadOnlyList<StoredDeviceConfig> loaded = await service.LoadAsync(CancellationToken.None);

        Assert.HasCount(1, loaded);
        Assert.AreEqual(string.Empty, loaded[0].Brand);
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.AreEqual(5, document.RootElement.GetProperty("version").GetInt32());
        JsonElement savedDevice = document.RootElement.GetProperty("devices")[0];
        Assert.AreEqual(string.Empty, savedDevice.GetProperty("brand").GetString());
        Assert.IsFalse(savedDevice.TryGetProperty("deviceInfoModel", out _));
        Assert.IsFalse(savedDevice.TryGetProperty("deviceInfoImei", out _));
    }

    [TestMethod]
    public async Task LoadAsync_Version4Document_PreservesAndroidIdAndDeepWipeOptions()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "devices.json");
        await File.WriteAllTextAsync(
            path,
            "{\"version\":4,\"devices\":[{\"serial\":\"SERIAL\",\"changeOptions\":{"
            + "\"useDefaultMode\":false,\"changeAndroidId\":true,"
            + "\"useRmRfForPackageCleanup\":true,\"clearAllPackages\":false}}]}");
        var service = new DeviceStoreService(path, NullLogger<DeviceStoreService>.Instance);

        IReadOnlyList<StoredDeviceConfig> loaded = await service.LoadAsync(CancellationToken.None);

        Assert.HasCount(1, loaded);
        Assert.IsTrue(loaded[0].ChangeOptions.ChangeAndroidId);
        Assert.IsTrue(loaded[0].ChangeOptions.UseRmRfForPackageCleanup);
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.AreEqual(5, document.RootElement.GetProperty("version").GetInt32());
        JsonElement options = document.RootElement
            .GetProperty("devices")[0]
            .GetProperty("changeOptions");
        Assert.IsTrue(options.GetProperty("changeAndroidId").GetBoolean());
        Assert.IsTrue(options.GetProperty("useRmRfForPackageCleanup").GetBoolean());
    }

    [TestMethod]
    public async Task LoadAsync_DocumentWithoutChangeAndroidId_DefaultsOptionToFalse()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "devices.json");
        await File.WriteAllTextAsync(
            path,
            "{\"version\":5,\"devices\":[{\"serial\":\"SERIAL\",\"changeOptions\":{\"useDefaultMode\":false}}]}");
        var service = new DeviceStoreService(path, NullLogger<DeviceStoreService>.Instance);

        IReadOnlyList<StoredDeviceConfig> loaded = await service.LoadAsync(CancellationToken.None);

        Assert.HasCount(1, loaded);
        Assert.IsFalse(loaded[0].ChangeOptions.ChangeAndroidId);
    }

    [TestMethod]
    public async Task LoadAsync_PreCompatibilityDocument_ClearsOldBrandAndAndroidVersionOnce()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "devices.json");
        await File.WriteAllTextAsync(
            path,
            "{\"devices\":[{\"serial\":\"SERIAL\",\"brand\":\"OPPO\",\"androidVersion\":\"Android 15\"}]}");
        var service = new DeviceStoreService(path, NullLogger<DeviceStoreService>.Instance);

        IReadOnlyList<StoredDeviceConfig> migrated = await service.LoadAsync(CancellationToken.None);
        Assert.AreEqual(string.Empty, migrated[0].Brand);
        Assert.AreEqual(string.Empty, migrated[0].AndroidVersion);

        migrated[0].Brand = "OPPO";
        migrated[0].AndroidVersion = "Android 14";
        await service.SaveAsync(migrated, CancellationToken.None);
        IReadOnlyList<StoredDeviceConfig> reloaded = await service.LoadAsync(CancellationToken.None);

        Assert.AreEqual("OPPO", reloaded[0].Brand);
        Assert.AreEqual("Android 14", reloaded[0].AndroidVersion);
    }
}
