using System.Text.Json;
using DeepDroidChanger.Constants;
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
        string path = Path.Combine(fixture.Path, "settings.json");
        IThemeService themes = Substitute.For<IThemeService>();
        themes.NormalizeTheme(Arg.Any<string>()).Returns(ThemeConstants.Dark);
        var service = new SettingsService(path, themes, NullLogger<SettingsService>.Instance);

        AppSettings settings = await service.LoadAsync(CancellationToken.None);

        Assert.AreEqual(LanguageConstants.English, settings.Language);
        Assert.AreEqual(ThemeConstants.Dark, settings.Theme);
        Assert.IsFalse(settings.SidebarCollapsed);
        Assert.AreEqual("Settings/devices.json", settings.DeviceDataFilePath);
        Assert.AreEqual(string.Empty, settings.SelectedDeviceSerial);

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.AreEqual(ThemeConstants.Dark, document.RootElement.GetProperty("Theme").GetString());
        Assert.AreEqual(LanguageConstants.English, document.RootElement.GetProperty("Language").GetString());
    }

    [TestMethod]
    public async Task SaveLoadAsync_PreservesAppSettingsProperties()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "settings.json");
        IThemeService themes = Substitute.For<IThemeService>();
        themes.NormalizeTheme("light").Returns("light");
        var service = new SettingsService(path, themes, NullLogger<SettingsService>.Instance);
        var settings = new AppSettings
        {
            Language = LanguageConstants.Vietnamese,
            Theme = "light",
            SidebarCollapsed = true,
            SelectedDeviceSerial = "DEVICE_123"
        };

        await service.SaveAsync(settings, CancellationToken.None);
        using (JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
        {
            Assert.AreEqual("light", document.RootElement.GetProperty("Theme").GetString());
            Assert.AreEqual(LanguageConstants.Vietnamese, document.RootElement.GetProperty("Language").GetString());
            Assert.IsTrue(document.RootElement.GetProperty("SidebarCollapsed").GetBoolean());
            Assert.AreEqual("DEVICE_123", document.RootElement.GetProperty("SelectedDeviceSerial").GetString());
        }

        AppSettings loaded = await service.LoadAsync(CancellationToken.None);

        Assert.AreEqual(LanguageConstants.Vietnamese, loaded.Language);
        Assert.AreEqual("light", loaded.Theme);
        Assert.IsTrue(loaded.SidebarCollapsed);
        Assert.AreEqual("DEVICE_123", loaded.SelectedDeviceSerial);
    }

    [TestMethod]
    public async Task LoadAsync_CorruptJson_IsQuarantinedAndReplacedWithNormalizedDefaults()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "settings.json");
        await File.WriteAllTextAsync(path, "not-json");
        IThemeService themes = Substitute.For<IThemeService>();
        themes.NormalizeTheme(Arg.Any<string>()).Returns(ThemeConstants.Dark);
        var service = new SettingsService(path, themes, NullLogger<SettingsService>.Instance);

        AppSettings settings = await service.LoadAsync(CancellationToken.None);

        Assert.AreEqual(ThemeConstants.Dark, settings.Theme);
        Assert.HasCount(1, Directory.GetFiles(fixture.Path, "settings.json.corrupt-*"));
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.AreEqual(JsonValueKind.Object, document.RootElement.ValueKind);
    }
}
