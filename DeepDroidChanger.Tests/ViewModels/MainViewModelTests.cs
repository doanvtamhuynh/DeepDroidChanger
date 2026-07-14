using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using NSubstitute;

namespace DeepDroidChanger.Tests.ViewModels;

[TestClass]
public sealed class MainViewModelTests
{
    [TestMethod]
    public void Commands_ToggleShellPreferencesAndNavigation()
    {
        var settings = new AppSettings
        {
            Language = LanguageConstants.English,
            Theme = ThemeConstants.Dark,
        };
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.NormalizeLanguage(Arg.Any<string>()).Returns(LanguageConstants.English);
        IThemeService themes = Substitute.For<IThemeService>();
        themes.NormalizeTheme(Arg.Any<string>()).Returns(ThemeConstants.Dark);
        themes.IsDarkTheme(ThemeConstants.Dark).Returns(true);
        themes.IsDarkTheme(ThemeConstants.Light).Returns(false);
        themes.ToggleTheme(ThemeConstants.Dark).Returns(ThemeConstants.Light);
        var viewModel = new MainViewModel(settings, localization, themes, Substitute.For<ISettingsService>());
        var navigation = new List<AppView>();
        viewModel.NavigationRequested += navigation.Add;

        viewModel.NavigateDeviceManagerCommand.Execute(null);
        Assert.IsEmpty(navigation);

        viewModel.ToggleSidebarCommand.Execute(null);
        Assert.IsTrue(viewModel.IsSidebarCollapsed);
        Assert.AreEqual(56d, viewModel.SidebarWidth.Value);
        Assert.AreEqual(System.Windows.Visibility.Collapsed, viewModel.NavLabelVisibility);
        viewModel.ToggleSidebarCommand.Execute(null);
        Assert.IsFalse(viewModel.IsSidebarCollapsed);
        Assert.AreEqual(248d, viewModel.SidebarWidth.Value);

        viewModel.ToggleLanguageCommand.Execute(null);
        Assert.AreEqual(LanguageConstants.Vietnamese, viewModel.Language);
        Assert.Contains("flag_vn.ico", viewModel.LanguageFlagSource);
        localization.Received(1).ApplyLanguage(LanguageConstants.Vietnamese);

        viewModel.ToggleThemeCommand.Execute(null);
        Assert.AreEqual(ThemeConstants.Light, viewModel.Theme);
        themes.Received(1).ApplyTheme(ThemeConstants.Light);

        viewModel.NavigateSettingsCommand.Execute(null);
        Assert.IsTrue(viewModel.IsSettingsActive);
        viewModel.NavigateDeviceManagerCommand.Execute(null);
        Assert.IsTrue(viewModel.IsDeviceManagerActive);
        CollectionAssert.AreEqual(new[] { AppView.Settings, AppView.DeviceManager }, navigation);
    }

    [TestMethod]
    public void NavigateInitialView_RaisesCurrentNavigation()
    {
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.NormalizeLanguage(Arg.Any<string>()).Returns(LanguageConstants.English);
        IThemeService themes = Substitute.For<IThemeService>();
        themes.NormalizeTheme(Arg.Any<string>()).Returns(ThemeConstants.Dark);
        var viewModel = new MainViewModel(
            new AppSettings { Language = LanguageConstants.English, Theme = ThemeConstants.Dark },
            localization,
            themes,
            Substitute.For<ISettingsService>());
        AppView? requested = null;
        viewModel.NavigationRequested += view => requested = view;

        viewModel.NavigateInitialView();

        Assert.AreEqual(AppView.DeviceManager, requested);
    }

    [TestMethod]
    public async Task SaveSettingsAsync_SavesSharedSettingsWithoutReloadingStaleState()
    {
        var settings = new AppSettings
        {
            Language = LanguageConstants.English,
            Theme = ThemeConstants.Dark,
            SelectedDeviceSerial = "SERIAL",
            DeviceTableColumnRatios = new Dictionary<string, double>
            {
                [DeviceTableColumnSettings.Name] = 0.75,
                [DeviceTableColumnSettings.Status] = 0.25
            }
        };
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.NormalizeLanguage(Arg.Any<string>()).Returns(LanguageConstants.English);
        IThemeService themes = Substitute.For<IThemeService>();
        themes.NormalizeTheme(Arg.Any<string>()).Returns(ThemeConstants.Dark);
        themes.IsDarkTheme(Arg.Any<string>()).Returns(true);
        ISettingsService settingsService = Substitute.For<ISettingsService>();
        var viewModel = new MainViewModel(settings, localization, themes, settingsService);

        await viewModel.SaveSettingsAsync(CancellationToken.None);

        await settingsService.Received(1).SaveAsync(settings, CancellationToken.None);
        await settingsService.DidNotReceiveWithAnyArgs().LoadAsync(default);
        Assert.AreEqual("SERIAL", settings.SelectedDeviceSerial);
        Assert.AreEqual(0.75, settings.DeviceTableColumnRatios[DeviceTableColumnSettings.Name]);
    }
}
