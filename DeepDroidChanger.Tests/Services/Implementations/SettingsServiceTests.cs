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
