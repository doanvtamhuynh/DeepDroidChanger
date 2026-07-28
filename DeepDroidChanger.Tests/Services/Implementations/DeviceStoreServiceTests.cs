using System.Text.Json;
using DeepDroidChanger.Constants;
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
    public async Task LoadAsync_MissingIndex_WritesEmptyArray()
    {
        using var fixture = new TestTempDirectory();
        string path = GetDeviceIndexPath(fixture.Path);
        var service = CreateService(path);

        IReadOnlyList<StoredDeviceConfig> devices = await service.LoadAsync(CancellationToken.None);

        Assert.IsEmpty(devices);
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.AreEqual(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.AreEqual(0, document.RootElement.GetArrayLength());
    }

    [TestMethod]
    public async Task LoadAsync_CorruptIndex_IsQuarantinedBeforeDefaultsAreWritten()
    {
        using var fixture = new TestTempDirectory();
        string path = GetDeviceIndexPath(fixture.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{not-json");
        var service = CreateService(path);

        IReadOnlyList<StoredDeviceConfig> devices = await service.LoadAsync(CancellationToken.None);

        Assert.IsEmpty(devices);
        Assert.HasCount(
            1,
            Directory.GetFiles(Path.GetDirectoryName(path)!, "devices.json.corrupt-*"));
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.AreEqual(JsonValueKind.Array, document.RootElement.ValueKind);
    }

    [TestMethod]
    public async Task SaveAndLoadAsync_PreservesAllMainAndDialogConfig()
    {
        using var fixture = new TestTempDirectory();
        string path = GetDeviceIndexPath(fixture.Path);
        var service = CreateService(path);
        var config = CreateCompleteConfig();

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
        CollectionAssert.AreEqual(
            new[] { "com.example.app" },
            restored.ChangeOptions.SelectedPackages);
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

        AssertIndexContainsOnlyIdentityAndDataPath(path, "DeviceManager/SERIAL");
        string deviceDirectory = GetDeviceDirectory(fixture.Path, "SERIAL");
        AssertAllDeviceFilesExist(deviceDirectory);
        using JsonDocument randomConfig = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(
                deviceDirectory,
                RuntimeDataPathConstants.RandomConfigFileName)));
        Assert.AreEqual(
            "Google",
            randomConfig.RootElement.GetProperty("brand").GetString());
        Assert.AreEqual(
            "310",
            randomConfig.RootElement.GetProperty("carrierMcc").GetString());
        Assert.IsFalse(
            randomConfig.RootElement.GetProperty("changeSimEnabled").GetBoolean());
    }

    [TestMethod]
    public async Task MergeAsync_NewDevice_CreatesDirectoryAndPreservesExistingConfig()
    {
        using var fixture = new TestTempDirectory();
        string path = GetDeviceIndexPath(fixture.Path);
        var service = CreateService(path);
        await service.SaveAsync(
            [
                new StoredDeviceConfig
                {
                    Serial = "EXISTING",
                    Brand = "Samsung",
                    LocationLatitude = "10.8231",
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
        Assert.AreEqual("10.8231", existing.LocationLatitude);
        Assert.AreEqual("Asia/Ho_Chi_Minh", existing.Timezone);
        Assert.AreEqual("127.0.0.1:1080", existing.ProxyFullString);
        AssertAllDeviceFilesExist(GetDeviceDirectory(fixture.Path, "NEW"));
    }

    [TestMethod]
    public async Task RemoveAsync_ExistingDevice_RemovesIndexAndDeviceDirectory()
    {
        using var fixture = new TestTempDirectory();
        string path = GetDeviceIndexPath(fixture.Path);
        var service = CreateService(path);
        await service.SaveAsync(
            [new StoredDeviceConfig { Serial = "SERIAL", Name = "Device" }],
            CancellationToken.None);
        string directory = GetDeviceDirectory(fixture.Path, "SERIAL");

        bool removed = await service.RemoveAsync("serial", CancellationToken.None);
        IReadOnlyList<StoredDeviceConfig> reloaded = await service.LoadAsync(CancellationToken.None);

        Assert.IsTrue(removed);
        Assert.IsEmpty(reloaded);
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public async Task LoadAsync_CorruptDialogConfig_QuarantinesOnlyThatFile()
    {
        using var fixture = new TestTempDirectory();
        string path = GetDeviceIndexPath(fixture.Path);
        var service = CreateService(path);
        await service.SaveAsync([CreateCompleteConfig()], CancellationToken.None);
        string directory = GetDeviceDirectory(fixture.Path, "SERIAL");
        string locationPath = Path.Combine(
            directory,
            RuntimeDataPathConstants.LocationConfigFileName);
        await File.WriteAllTextAsync(locationPath, "{not-json");

        IReadOnlyList<StoredDeviceConfig> loaded = await service.LoadAsync(CancellationToken.None);

        Assert.HasCount(1, loaded);
        Assert.AreEqual("Google", loaded[0].Brand);
        Assert.AreEqual(string.Empty, loaded[0].LocationLatitude);
        Assert.HasCount(
            1,
            Directory.GetFiles(directory, "location_config.json.corrupt-*"));
        Assert.IsTrue(File.Exists(locationPath));
    }

    [TestMethod]
    public async Task SaveAsync_SerialContainsPathCharacters_UsesSafeDirectory()
    {
        using var fixture = new TestTempDirectory();
        string path = GetDeviceIndexPath(fixture.Path);
        var service = CreateService(path);
        const string serial = "192.168.0.2:5555";

        await service.SaveAsync(
            [new StoredDeviceConfig { Serial = serial }],
            CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        string dataPath = document.RootElement[0].GetProperty("dataPath").GetString()!;
        Assert.StartsWith("DeviceManager/", dataPath, StringComparison.Ordinal);
        Assert.DoesNotContain(":", dataPath, StringComparison.Ordinal);
        Assert.IsTrue(Directory.Exists(Path.Combine(
            fixture.Path,
            dataPath.Replace('/', Path.DirectorySeparatorChar))));
    }

    private static DeviceStoreService CreateService(string path)
    {
        return new DeviceStoreService(path, NullLogger<DeviceStoreService>.Instance);
    }

    private static string GetDeviceIndexPath(string rootPath)
    {
        return Path.Combine(
            rootPath,
            RuntimeDataPathConstants.DeviceManagerDirectoryName,
            RuntimeDataPathConstants.DevicesFileName);
    }

    private static string GetDeviceDirectory(string rootPath, string serial)
    {
        return Path.Combine(
            rootPath,
            RuntimeDataPathConstants.DeviceManagerDirectoryName,
            serial);
    }

    private static void AssertIndexContainsOnlyIdentityAndDataPath(
        string indexPath,
        string expectedDataPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(indexPath));
        Assert.AreEqual(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.AreEqual(1, document.RootElement.GetArrayLength());
        JsonElement entry = document.RootElement[0];
        CollectionAssert.AreEquivalent(
            new[] { "serial", "name", "type", "dataPath" },
            entry.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual(expectedDataPath, entry.GetProperty("dataPath").GetString());
    }

    private static void AssertAllDeviceFilesExist(string directory)
    {
        string[] expectedFiles =
        [
            RuntimeDataPathConstants.RandomConfigFileName,
            RuntimeDataPathConstants.ChangeOptionsConfigFileName,
            RuntimeDataPathConstants.UpdateIntegrityConfigFileName,
            RuntimeDataPathConstants.LocationConfigFileName,
            RuntimeDataPathConstants.TimezoneConfigFileName,
            RuntimeDataPathConstants.ProxyConfigFileName
        ];

        Assert.IsTrue(Directory.Exists(directory));
        CollectionAssert.AreEquivalent(
            expectedFiles,
            Directory.GetFiles(directory).Select(Path.GetFileName).ToArray());
    }

    private static StoredDeviceConfig CreateCompleteConfig()
    {
        return new StoredDeviceConfig
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
    }
}
