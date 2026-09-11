using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using NSubstitute;

namespace DeepDroidChanger.Tests.ViewModels;

[TestClass]
public sealed class MainViewModelNavigationTests
{
    [TestMethod]
    public void RestoreActiveView_RestoresStateWithoutRaisingNavigationRequest()
    {
        AppSettings settings = new();
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.NormalizeLanguage(Arg.Any<string>()).Returns("en");
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        IThemeService theme = Substitute.For<IThemeService>();
        theme.NormalizeTheme(Arg.Any<string>()).Returns("Dark");
        theme.IsDarkTheme(Arg.Any<string>()).Returns(true);
        ISettingsService settingsService = Substitute.For<ISettingsService>();
        MainViewModel viewModel = new(
            settings,
            localization,
            theme,
            settingsService);
        List<AppView> navigationRequests = [];
        viewModel.NavigationRequested += navigationRequests.Add;

        viewModel.RestoreActiveView(AppView.ViewMultipleDevices);
        viewModel.NavigateInitialView();
        viewModel.RestoreActiveView(AppView.ChangeSingleDevice);

        Assert.AreEqual(AppView.ChangeSingleDevice, viewModel.ActiveView);
        CollectionAssert.AreEqual(
            new[] { AppView.ViewMultipleDevices },
            navigationRequests);
    }
}
