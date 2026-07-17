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
    public async Task LoadAsync_MissingFile_WritesDefaultChangeDeviceConfiguration()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "settings.json");
        IThemeService themes = Substitute.For<IThemeService>();
        themes.NormalizeTheme(Arg.Any<string>()).Returns(ThemeConstants.Dark);
        var service = new SettingsService(path, themes, NullLogger<SettingsService>.Instance);

        AppSettings settings = await service.LoadAsync(CancellationToken.None);

        Assert.IsTrue(settings.ChangeOptions.UseDefaultMode);
        Assert.IsTrue(settings.ChangeOptions.ChangeMacAddress);
        Assert.IsTrue(settings.ChangeOptions.ClearAllPackages);
        Assert.IsTrue(settings.ChangeOptions.ClearGoogleAccounts);

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        JsonElement options = document.RootElement.GetProperty("ChangeOptions");
        Assert.IsTrue(options.GetProperty("UseDefaultMode").GetBoolean());
        Assert.IsTrue(options.GetProperty("ChangeMacAddress").GetBoolean());
        Assert.IsTrue(options.GetProperty("ClearAllPackages").GetBoolean());
        Assert.IsTrue(options.GetProperty("ClearGoogleAccounts").GetBoolean());
    }

    [TestMethod]
    public async Task SaveLoadAsync_PreservesGlobalChangeOptionsAndNormalizesPackages()
    {
        using var fixture = new TestTempDirectory();
        string path = Path.Combine(fixture.Path, "settings.json");
        IThemeService themes = Substitute.For<IThemeService>();
        themes.NormalizeTheme(Arg.Any<string>()).Returns(ThemeConstants.Dark);
        var service = new SettingsService(path, themes, NullLogger<SettingsService>.Instance);
        var settings = new AppSettings
        {
            ChangeOptions = new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ChangeMacAddress = false,
                ClearAllPackages = false,
                ClearSelectedPackages = true,
                ClearGoogleAccounts = false,
                SelectedPackages = [" com.example.two ", "com.example.one", "com.example.one"]
            }
        };

        await service.SaveAsync(settings, CancellationToken.None);
        using (JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
        {
            JsonElement options = document.RootElement.GetProperty("ChangeOptions");
            Assert.IsFalse(options.GetProperty("UseDefaultMode").GetBoolean());
            Assert.IsFalse(options.GetProperty("ChangeMacAddress").GetBoolean());
            Assert.IsFalse(options.GetProperty("ClearAllPackages").GetBoolean());
            Assert.IsTrue(options.GetProperty("ClearSelectedPackages").GetBoolean());
            Assert.IsFalse(options.GetProperty("ClearGoogleAccounts").GetBoolean());
        }

        AppSettings loaded = await service.LoadAsync(CancellationToken.None);

        Assert.IsFalse(loaded.ChangeOptions.UseDefaultMode);
        Assert.IsFalse(loaded.ChangeOptions.ChangeMacAddress);
        Assert.IsFalse(loaded.ChangeOptions.ClearAllPackages);
        Assert.IsTrue(loaded.ChangeOptions.ClearSelectedPackages);
        Assert.IsFalse(loaded.ChangeOptions.ClearGoogleAccounts);
        CollectionAssert.AreEqual(
            new[] { "com.example.one", "com.example.two" },
            loaded.ChangeOptions.SelectedPackages);
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
