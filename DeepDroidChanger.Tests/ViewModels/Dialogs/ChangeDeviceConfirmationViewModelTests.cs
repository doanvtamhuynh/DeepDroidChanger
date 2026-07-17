using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using NSubstitute;

namespace DeepDroidChanger.Tests.ViewModels.Dialogs;

[TestClass]
public sealed class ChangeDeviceConfirmationViewModelTests
{
    [TestMethod]
    public void Initialize_DefaultWipe_WarnsAboutFullIdentityPackageAndAccountCleanup()
    {
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var viewModel = new ChangeDeviceConfirmationViewModel(localization);

        viewModel.Initialize(
            "Phone",
            "SERIAL",
            new DeviceChangeOptions { UseDefaultMode = true });

        Assert.AreEqual(
            "ChangeDeviceConfirmation_DefaultProfileNotice",
            viewModel.ProfileNotice);
        Assert.Contains(
            "ChangeDeviceConfirmation_ClearAllPackages",
            viewModel.CleanNotice,
            StringComparison.Ordinal);
        Assert.Contains(
            "ChangeDeviceConfirmation_ClearGoogleAccounts",
            viewModel.CleanNotice,
            StringComparison.Ordinal);
        Assert.AreEqual(
            "ChangeDeviceConfirmation_GoogleDataMayBeCleared",
            viewModel.GoogleNotice);
    }

    [TestMethod]
    public void Initialize_RmRfPackageCleanup_DescribesActiveFilesystemCleanup()
    {
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var viewModel = new ChangeDeviceConfirmationViewModel(localization);

        viewModel.Initialize(
            "Phone",
            "SERIAL",
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                UseRmRfForPackageCleanup = true,
                ClearAllPackages = false,
                ClearSelectedPackages = true,
                SelectedPackages = ["com.example.app"],
                ClearGoogleAccounts = false
            });

        Assert.Contains(
            "ChangeDeviceConfirmation_RmRfPackageCleanup",
            viewModel.CleanNotice,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ChangeDeviceConfirmation_RmRfPackageCleanup",
            viewModel.ProfileNotice,
            StringComparison.Ordinal);
    }

    [TestMethod]
    public void Initialize_RmRfOptionWithoutPackageCleanup_DoesNotClaimItWillRun()
    {
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var viewModel = new ChangeDeviceConfirmationViewModel(localization);

        viewModel.Initialize(
            "Phone",
            "SERIAL",
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                UseRmRfForPackageCleanup = true,
                ClearAllPackages = false,
                ClearGoogleAccounts = false
            });

        Assert.AreEqual("ChangeDeviceConfirmation_NoPackageCleanup", viewModel.CleanNotice);
        localization.DidNotReceive().GetString("ChangeDeviceConfirmation_RmRfPackageCleanup");
    }

    [TestMethod]
    public void Initialize_AdvancedAndroidIdDisabled_WarnsThatStoredAndroidIdWillBeDeleted()
    {
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var viewModel = new ChangeDeviceConfirmationViewModel(localization);

        viewModel.Initialize(
            "Phone",
            "SERIAL",
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ChangeAndroidId = false,
                ChangeMacAddress = true,
                ClearAllPackages = false,
                ClearGoogleAccounts = false
            });

        localization.Received(1).GetString("ChangeDeviceConfirmation_DeleteAndroidId");
        localization.Received(1).GetString("ChangeDeviceConfirmation_ChangeMac");
        localization.DidNotReceive().GetString("ChangeDeviceConfirmation_ChangeAndroidId");
    }
}
