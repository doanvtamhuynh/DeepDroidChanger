using System.Text.Json;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
[DoNotParallelize]
public sealed class MultipleDeviceConfigServiceTests
{
    [TestMethod]
    public async Task LoadAsync_MissingFiles_CreatesDedicatedDirectoryAndDefaults()
    {
        using var fixture = new TestTempDirectory();
        string directory = Path.Combine(fixture.Path, "ChangeMultipleDevices");
        var service = CreateService(directory);

        MultipleDeviceConfiguration configuration =
            await service.LoadAsync(CancellationToken.None);

        Assert.AreEqual("Random", configuration.ChangeConfig.Brand);
        Assert.IsTrue(configuration.ChangeConfig.ChangeSimEnabled);
        Assert.IsTrue(configuration.ChangeConfig.UseIntegritySecurityPatch);
        Assert.IsTrue(configuration.ChangeOptions.UseDefaultMode);
        Assert.IsTrue(File.Exists(Path.Combine(directory, "change_config.json")));
        Assert.IsTrue(File.Exists(Path.Combine(directory, "change_options_config.json")));
    }

    [TestMethod]
    public async Task SaveLoadAsync_RoundTripsNormalizedConfigurationAsCamelCaseJson()
    {
        using var fixture = new TestTempDirectory();
        string directory = Path.Combine(fixture.Path, "multiple_devices");
        var service = CreateService(directory);
        var expected = new MultipleDeviceConfiguration
        {
            ChangeConfig = new MultipleDeviceChangeConfig
            {
                Brand = " Samsung ",
                AndroidVersion = "Android 15",
                Model = " SM-S918B ",
                CountryIso = " VN ",
                CountryName = " Vietnam ",
                Carrier = " Viettel ",
                CarrierMcc = " 452 ",
                CarrierMnc = " 04 ",
                ChangeSimEnabled = false,
                UseIntegritySecurityPatch = false
            },
            ChangeOptions = new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ChangeAndroidId = true,
                ClearAllPackages = false,
                ClearSelectedPackages = true,
                SelectedPackages = ["com.example.two", "com.example.one", "com.example.two"]
            }
        };

        await service.SaveAsync(expected, CancellationToken.None);
        MultipleDeviceConfiguration loaded = await service.LoadAsync(CancellationToken.None);

        Assert.AreEqual("Samsung", loaded.ChangeConfig.Brand);
        Assert.AreEqual("SM-S918B", loaded.ChangeConfig.Model);
        Assert.AreEqual("vn", loaded.ChangeConfig.CountryIso);
        Assert.AreEqual("Viettel", loaded.ChangeConfig.Carrier);
        Assert.IsFalse(loaded.ChangeConfig.ChangeSimEnabled);
        Assert.IsFalse(loaded.ChangeOptions.UseDefaultMode);
        Assert.IsTrue(loaded.ChangeOptions.ChangeAndroidId);
        CollectionAssert.AreEqual(
            new[] { "com.example.one", "com.example.two" },
            loaded.ChangeOptions.SelectedPackages);

        using JsonDocument changeConfig = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(directory, "change_config.json")));
        Assert.IsTrue(changeConfig.RootElement.TryGetProperty("brand", out _));
        Assert.AreEqual(
            "SM-S918B",
            changeConfig.RootElement.GetProperty("model").GetString());
        Assert.IsFalse(changeConfig.RootElement.TryGetProperty("Brand", out _));
    }

    [TestMethod]
    public async Task SaveAsync_ConcurrentCalls_LeaveTwoValidFilesWithoutTemporaryArtifacts()
    {
        using var fixture = new TestTempDirectory();
        string directory = Path.Combine(fixture.Path, "multiple_devices");
        var service = CreateService(directory);
        Task[] saves = Enumerable.Range(0, 12)
            .Select(index => service.SaveAsync(
                new MultipleDeviceConfiguration
                {
                    ChangeConfig = new MultipleDeviceChangeConfig
                    {
                        Brand = $"Brand {index}",
                        AndroidVersion = $"Android {index}"
                    },
                    ChangeOptions = new DeviceChangeOptions
                    {
                        UseDefaultMode = index % 2 == 0
                    }
                },
                CancellationToken.None))
            .ToArray();

        await Task.WhenAll(saves);

        using JsonDocument changeConfig = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(directory, "change_config.json")));
        using JsonDocument changeOptions = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(directory, "change_options_config.json")));
        Assert.AreEqual(JsonValueKind.Object, changeConfig.RootElement.ValueKind);
        Assert.AreEqual(JsonValueKind.Object, changeOptions.RootElement.ValueKind);
        Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
    }

    [TestMethod]
    public async Task SaveAsync_PreCanceledToken_DoesNotCreateConfigFiles()
    {
        using var fixture = new TestTempDirectory();
        string directory = Path.Combine(fixture.Path, "multiple_devices");
        var service = CreateService(directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.SaveAsync(new MultipleDeviceConfiguration(), cancellation.Token));

        Assert.IsFalse(File.Exists(Path.Combine(directory, "change_config.json")));
        Assert.IsFalse(File.Exists(Path.Combine(directory, "change_options_config.json")));
        Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp"));
    }

    [TestMethod]
    public async Task LoadAsync_CorruptFile_QuarantinesItAndRestoresDefaultJson()
    {
        using var fixture = new TestTempDirectory();
        string directory = Path.Combine(fixture.Path, "multiple_devices");
        Directory.CreateDirectory(directory);
        string corruptPath = Path.Combine(directory, "change_config.json");
        await File.WriteAllTextAsync(corruptPath, "not-json");
        var service = CreateService(directory);

        MultipleDeviceConfiguration configuration =
            await service.LoadAsync(CancellationToken.None);

        Assert.AreEqual("Random", configuration.ChangeConfig.Brand);
        Assert.HasCount(1, Directory.GetFiles(directory, "change_config.json.corrupt-*"));
        using JsonDocument replacement = JsonDocument.Parse(await File.ReadAllTextAsync(corruptPath));
        Assert.AreEqual(JsonValueKind.Object, replacement.RootElement.ValueKind);
        Assert.IsTrue(File.Exists(Path.Combine(directory, "change_options_config.json")));
    }

    private static MultipleDeviceConfigService CreateService(string directory)
    {
        return new MultipleDeviceConfigService(
            directory,
            NullLogger<MultipleDeviceConfigService>.Instance);
    }
}
