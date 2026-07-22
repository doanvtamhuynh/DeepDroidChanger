using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.DialogServices;

[TestClass]
public sealed class ChangeDeviceConfirmationDialogServiceTests
{
    [TestMethod]
    public async Task ShowChangeDeviceConfirmationAsync_DefaultMode_ShowsWarningMessageAndReturnsSelection()
    {
        IConfirmationDialogService confirmationDialog = Substitute.For<IConfirmationDialogService>();
        confirmationDialog.ShowWarningConfirmationAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        ILocalizationService localization = CreateLocalizationService();
        var service = new ChangeDeviceConfirmationDialogService(
            confirmationDialog,
            localization,
            NullLogger<ChangeDeviceConfirmationDialogService>.Instance);

        bool confirmed = await service.ShowChangeDeviceConfirmationAsync(
            "Phone",
            "SERIAL",
            new DeviceChangeOptions
            {
                UseDefaultMode = true,
                ChangeAndroidId = true
            },
            CancellationToken.None);

        Assert.IsTrue(confirmed);
        await confirmationDialog.Received(1).ShowWarningConfirmationAsync(
            Arg.Is<string>(message =>
                message.Contains("ChangeDeviceConfirmation_Title", StringComparison.Ordinal)
                && message.Contains("Phone", StringComparison.Ordinal)
                && message.Contains("SERIAL", StringComparison.Ordinal)
                && message.Contains("ChangeDeviceConfirmation_DefaultProfileNotice", StringComparison.Ordinal)
                && !message.Contains("ChangeDeviceConfirmation_RegenerateAndroidId", StringComparison.Ordinal)
                && message.Contains("ChangeDeviceConfirmation_ClearAllPackages", StringComparison.Ordinal)
                && message.Contains("ChangeDeviceConfirmation_ClearGoogleAccounts", StringComparison.Ordinal)
                && !message.Contains("ChangeDeviceConfirmation_DeepPackageWipe", StringComparison.Ordinal)
                && message.Contains("ChangeDeviceConfirmation_GoogleDataMayBeCleared", StringComparison.Ordinal)),
            "ChangeDeviceConfirmation_WindowTitle",
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ShowChangeDeviceConfirmationAsync_AdvancedMode_ShowsAutomaticCleanupDetails()
    {
        IConfirmationDialogService confirmationDialog = Substitute.For<IConfirmationDialogService>();
        confirmationDialog.ShowWarningConfirmationAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        ILocalizationService localization = CreateLocalizationService();
        var service = new ChangeDeviceConfirmationDialogService(
            confirmationDialog,
            localization,
            NullLogger<ChangeDeviceConfirmationDialogService>.Instance);

        bool confirmed = await service.ShowChangeDeviceConfirmationAsync(
            "Phone",
            "SERIAL",
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ChangeAndroidId = true,
                ChangeMacAddress = true,
                ClearAllPackages = false,
                ClearSelectedPackages = true,
                SelectedPackages = ["com.example.app"],
                ClearGoogleAccounts = false,
                UseRmRfForPackageCleanup = true
            },
            CancellationToken.None);

        Assert.IsFalse(confirmed);
        await confirmationDialog.Received(1).ShowWarningConfirmationAsync(
            Arg.Is<string>(message =>
                message.Contains("ChangeDeviceConfirmation_RegenerateAndroidId", StringComparison.Ordinal)
                && message.Contains("ChangeDeviceConfirmation_ChangeMac", StringComparison.Ordinal)
                && message.Contains("selected 1", StringComparison.Ordinal)
                && message.Contains("ChangeDeviceConfirmation_DeepPackageWipe", StringComparison.Ordinal)
                && message.Contains("ChangeDeviceConfirmation_GoogleDataPreserved", StringComparison.Ordinal)),
            "ChangeDeviceConfirmation_WindowTitle",
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ShowChangeDeviceConfirmationAsync_AdvancedAndroidIdDisabled_ShowsPreservedState()
    {
        IConfirmationDialogService confirmationDialog = Substitute.For<IConfirmationDialogService>();
        confirmationDialog.ShowWarningConfirmationAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var service = new ChangeDeviceConfirmationDialogService(
            confirmationDialog,
            CreateLocalizationService(),
            NullLogger<ChangeDeviceConfirmationDialogService>.Instance);

        await service.ShowChangeDeviceConfirmationAsync(
            "Phone",
            "SERIAL",
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ChangeAndroidId = false,
                ChangeMacAddress = false,
                ClearAllPackages = false,
                ClearGoogleAccounts = false
            },
            CancellationToken.None);

        await confirmationDialog.Received(1).ShowWarningConfirmationAsync(
            Arg.Is<string>(message =>
                message.Contains("ChangeDeviceConfirmation_PreserveAndroidId", StringComparison.Ordinal)
                && !message.Contains("ChangeDeviceConfirmation_RegenerateAndroidId", StringComparison.Ordinal)),
            "ChangeDeviceConfirmation_WindowTitle",
            Arg.Any<CancellationToken>());
    }

    private static ILocalizationService CreateLocalizationService()
    {
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(callInfo =>
        {
            string key = callInfo.Arg<string>();
            return key switch
            {
                "ChangeDeviceConfirmation_AdvancedProfileNotice" => "advanced {0}",
                "ChangeDeviceConfirmation_ClearSelectedPackages" => "selected {0}",
                _ => key
            };
        });
        return localization;
    }
}
