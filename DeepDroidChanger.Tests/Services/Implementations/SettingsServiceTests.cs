using System.Text.Json;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
[DoNotParallelize]
public sealed class SettingsServiceTests
{
    [TestMethod]
    public async Task LoadAsync_MissingFile_WritesDefaultSettings()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "app_settings.json");
        IThemeService themes = Substitute.For<IThemeService>();
        themes.NormalizeTheme(Arg.Any<string>()).Returns("Dark");
        var service = new SettingsService(path, themes, NullLogger<SettingsService>.Instance);

        AppSettings settings = await service.LoadAsync(CancellationToken.None);

        Assert.AreEqual("en", settings.Language);
        Assert.AreEqual("Dark", settings.Theme);
        Assert.IsFalse(settings.SidebarCollapsed);
        Assert.AreEqual(string.Empty, settings.SelectedSingleDeviceSerial);
        Assert.IsEmpty(settings.SelectedMultipleDeviceSerials);

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.AreEqual("Dark", document.RootElement.GetProperty("Theme").GetString());
        Assert.AreEqual("en", document.RootElement.GetProperty("Language").GetString());
    }

    [TestMethod]
    public async Task SaveLoadAsync_PreservesAppSettingsProperties()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "app_settings.json");
        IThemeService themes = Substitute.For<IThemeService>();
        themes.NormalizeTheme("light").Returns("light");
        var service = new SettingsService(path, themes, NullLogger<SettingsService>.Instance);
        var settings = new AppSettings
        {
            Language = "vi",
            Theme = "light",
            SidebarCollapsed = true,
            SelectedSingleDeviceSerial = "DEVICE_123",
            SelectedMultipleDeviceSerials = [" DEVICE_A ", "device_a", "DEVICE_B"]
        };

        await service.SaveAsync(settings, CancellationToken.None);
        using (JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
        {
            Assert.AreEqual("light", document.RootElement.GetProperty("Theme").GetString());
            Assert.AreEqual("vi", document.RootElement.GetProperty("Language").GetString());
            Assert.IsTrue(document.RootElement.GetProperty("SidebarCollapsed").GetBoolean());
            Assert.AreEqual(
                "DEVICE_123",
                document.RootElement.GetProperty("SelectedSingleDeviceSerial").GetString());
            Assert.IsFalse(document.RootElement.TryGetProperty("SelectedDeviceSerial", out _));
        }

        AppSettings loaded = await service.LoadAsync(CancellationToken.None);

        Assert.AreEqual("vi", loaded.Language);
        Assert.AreEqual("light", loaded.Theme);
        Assert.IsTrue(loaded.SidebarCollapsed);
        Assert.AreEqual("DEVICE_123", loaded.SelectedSingleDeviceSerial);
        CollectionAssert.AreEqual(
            new[] { "DEVICE_A", "DEVICE_B" },
            loaded.SelectedMultipleDeviceSerials);
    }

    [TestMethod]
    public async Task LoadAsync_CorruptJson_IsQuarantinedAndReplacedWithNormalizedDefaults()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "app_settings.json");
        await File.WriteAllTextAsync(path, "not-json");
        IThemeService themes = Substitute.For<IThemeService>();
        themes.NormalizeTheme(Arg.Any<string>()).Returns("Dark");
        var service = new SettingsService(path, themes, NullLogger<SettingsService>.Instance);

        AppSettings settings = await service.LoadAsync(CancellationToken.None);

        Assert.AreEqual("Dark", settings.Theme);
        Assert.HasCount(1, Directory.GetFiles(fixture.Path, "app_settings.json.corrupt-*"));
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.AreEqual(JsonValueKind.Object, document.RootElement.ValueKind);
    }

    [TestMethod]
    public async Task LoadAsync_LegacySingleDeviceKeys_MigratesWithoutOverwritingMultipleState()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "app_settings.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "Language": "vi",
              "Theme": "Dark",
              "SelectedDeviceSerial": " LEGACY_SINGLE ",
              "DeviceTableColumnRatios": {
                "Index": 0.4,
                "Selected": 0.5,
                "Serial": 0.6,
                "Name": 0.7,
                "Type": 0.8,
                "Active": 0.9,
                "Status": 1.0,
                "Process": 1.1
              },
              "SelectedMultipleDeviceSerials": [" A ", "a", "", "B"],
              "MultipleDeviceTableColumnRatios": {
                "Index": 1.1,
                "Selected": 1.2,
                "Serial": 1.3,
                "Name": 1.4,
                "Type": 1.5,
                "Active": 1.6,
                "Status": 1.7,
                "Process": 1.8
              }
            }
            """);
        IThemeService themes = Substitute.For<IThemeService>();
        themes.NormalizeTheme(Arg.Any<string>()).Returns("Dark");
        var service = new SettingsService(path, themes, NullLogger<SettingsService>.Instance);

        AppSettings settings = await service.LoadAsync(CancellationToken.None);

        Assert.AreEqual("LEGACY_SINGLE", settings.SelectedSingleDeviceSerial);
        Assert.AreEqual(0.7, settings.SingleDeviceTableColumnRatios["Name"]);
        Assert.AreEqual(1.4, settings.MultipleDeviceTableColumnRatios["Name"]);
        CollectionAssert.AreEqual(new[] { "A", "B" }, settings.SelectedMultipleDeviceSerials);

        await service.SaveAsync(settings, CancellationToken.None);
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.IsFalse(document.RootElement.TryGetProperty("SelectedDeviceSerial", out _));
        Assert.IsFalse(document.RootElement.TryGetProperty("DeviceTableColumnRatios", out _));
        Assert.IsTrue(document.RootElement.TryGetProperty("SelectedSingleDeviceSerial", out _));
        Assert.IsTrue(document.RootElement.TryGetProperty("SingleDeviceTableColumnRatios", out _));
        Assert.IsTrue(document.RootElement.TryGetProperty("MultipleDeviceTableColumnRatios", out _));
    }

    [TestMethod]
    public async Task LoadAsync_InitialMultipleLayoutRatios_MigratesToSingleDeviceProportions()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "app_settings.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "MultipleDeviceTableColumnRatios": {
                "Index": 0.55,
                "Selected": 0.7,
                "Serial": 1.05,
                "Name": 1.05,
                "Type": 0.9,
                "Active": 1.05,
                "Status": 1.0,
                "Process": 1.8
              }
            }
            """);
        IThemeService themes = Substitute.For<IThemeService>();
        themes.NormalizeTheme(Arg.Any<string>()).Returns("Dark");
        var service = new SettingsService(path, themes, NullLogger<SettingsService>.Instance);

        AppSettings settings = await service.LoadAsync(CancellationToken.None);

        Assert.AreEqual(0.55, settings.MultipleDeviceTableColumnRatios["Selected"]);
        Assert.AreEqual(1.95, settings.MultipleDeviceTableColumnRatios["Process"]);
        Assert.AreEqual(
            settings.SingleDeviceTableColumnRatios["Selected"],
            settings.MultipleDeviceTableColumnRatios["Selected"]);
        Assert.AreEqual(
            settings.SingleDeviceTableColumnRatios["Process"],
            settings.MultipleDeviceTableColumnRatios["Process"]);
    }

    [TestMethod]
    public async Task LoadAsync_NormalizedInitialMultipleLayoutRatios_MigratesWithoutChangingScale()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "app_settings.json");
        const double oldTotal = 8.1;
        string json = JsonSerializer.Serialize(new
        {
            MultipleDeviceTableColumnRatios = new Dictionary<string, double>
            {
                ["Index"] = 0.55 / oldTotal,
                ["Selected"] = 0.7 / oldTotal,
                ["Serial"] = 1.05 / oldTotal,
                ["Name"] = 1.05 / oldTotal,
                ["Type"] = 0.9 / oldTotal,
                ["Active"] = 1.05 / oldTotal,
                ["Status"] = 1.0 / oldTotal,
                ["Process"] = 1.8 / oldTotal
            }
        });
        await File.WriteAllTextAsync(
            path,
            json);
        IThemeService themes = Substitute.For<IThemeService>();
        themes.NormalizeTheme(Arg.Any<string>()).Returns("Dark");
        var service = new SettingsService(path, themes, NullLogger<SettingsService>.Instance);

        AppSettings settings = await service.LoadAsync(CancellationToken.None);

        Assert.AreEqual(0.55 / oldTotal, settings.MultipleDeviceTableColumnRatios["Selected"], 0.000001);
        Assert.AreEqual(1.95 / oldTotal, settings.MultipleDeviceTableColumnRatios["Process"], 0.000001);
        Assert.AreEqual(1.0, settings.MultipleDeviceTableColumnRatios.Values.Sum(), 0.000001);
    }

    [TestMethod]
    public async Task SaveAsync_PartialOrInvalidColumnRatios_RepairsEachLayoutIndependently()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "app_settings.json");
        IThemeService themes = Substitute.For<IThemeService>();
        themes.NormalizeTheme(Arg.Any<string>()).Returns("Dark");
        var service = new SettingsService(path, themes, NullLogger<SettingsService>.Instance);
        var settings = new AppSettings
        {
            SingleDeviceTableColumnRatios = new Dictionary<string, double>
            {
                ["Name"] = 0.4,
                ["Process"] = double.NaN,
                ["Unknown"] = 2
            },
            MultipleDeviceTableColumnRatios = new Dictionary<string, double>
            {
                ["Selected"] = 0.25,
                ["Status"] = double.PositiveInfinity
            }
        };

        await service.SaveAsync(settings, CancellationToken.None);

        Assert.HasCount(8, settings.SingleDeviceTableColumnRatios);
        Assert.HasCount(8, settings.MultipleDeviceTableColumnRatios);
        Assert.AreEqual(0.4, settings.SingleDeviceTableColumnRatios["Name"]);
        Assert.AreEqual(1.95, settings.SingleDeviceTableColumnRatios["Process"]);
        Assert.IsFalse(settings.SingleDeviceTableColumnRatios.ContainsKey("Unknown"));
        Assert.AreEqual(0.25, settings.MultipleDeviceTableColumnRatios["Selected"]);
        Assert.AreEqual(1.0, settings.MultipleDeviceTableColumnRatios["Status"]);
        Assert.IsTrue(settings.SingleDeviceTableColumnRatios.Values.All(double.IsFinite));
        Assert.IsTrue(settings.MultipleDeviceTableColumnRatios.Values.All(double.IsFinite));

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.IsTrue(document.RootElement.TryGetProperty("SingleDeviceTableColumnRatios", out _));
        Assert.IsTrue(document.RootElement.TryGetProperty("MultipleDeviceTableColumnRatios", out _));
    }

}
