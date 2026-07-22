using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.DialogServices;

[TestClass]
public sealed class ChangeDeviceConfirmationDialogServiceTests
{
    [TestMethod]
    public async Task ShowChangeDeviceConfirmationAsync_DefaultMode_ShowsShortMessageWithIdentityInCaption()
    {
        IConfirmationDialogService confirmationDialog = CreateConfirmationDialog(true);
        var service = new ChangeDeviceConfirmationDialogService(
            confirmationDialog,
            CreateLocalizationService(),
            NullLogger<ChangeDeviceConfirmationDialogService>.Instance);

        bool confirmed = await service.ShowChangeDeviceConfirmationAsync(
            "Phone",
            "SERIAL",
            new DeviceChangeOptions { UseDefaultMode = true },
            CancellationToken.None);

        Assert.IsTrue(confirmed);
        await confirmationDialog.Received(1).ShowConfirmationAsync(
            Arg.Is<ConfirmationDialogOptions>(options =>
                options.Caption == "Confirm Change Device: Phone - SERIAL"
                && options.Message == "ChangeDeviceConfirmation_Message"
                && options.WarningMessage == "ChangeDeviceConfirmation_Warning"
                && options.Icon == ConfirmationDialogIcon.ChangeDevice),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ShowChangeDeviceConfirmationAsync_AdvancedMode_DoesNotExposeCleanupDetails()
    {
        IConfirmationDialogService confirmationDialog = CreateConfirmationDialog(false);
        var service = new ChangeDeviceConfirmationDialogService(
            confirmationDialog,
            CreateLocalizationService(),
            NullLogger<ChangeDeviceConfirmationDialogService>.Instance);

        bool confirmed = await service.ShowChangeDeviceConfirmationAsync(
            "Phone",
            "SERIAL",
            new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ChangeAndroidId = true,
                ChangeMacAddress = true,
                ClearSelectedPackages = true,
                SelectedPackages = ["com.example.app"],
                UseRmRfForPackageCleanup = true
            },
            CancellationToken.None);

        Assert.IsFalse(confirmed);
        await confirmationDialog.Received(1).ShowConfirmationAsync(
            Arg.Is<ConfirmationDialogOptions>(options =>
                options.Caption == "Confirm Change Device: Phone - SERIAL"
                && options.Message == "ChangeDeviceConfirmation_Message"
                && options.WarningMessage == "ChangeDeviceConfirmation_Warning"
                && options.Icon == ConfirmationDialogIcon.ChangeDevice),
            Arg.Any<CancellationToken>());
    }

    private static IConfirmationDialogService CreateConfirmationDialog(bool result)
    {
        IConfirmationDialogService confirmationDialog = Substitute.For<IConfirmationDialogService>();
        confirmationDialog.ShowConfirmationAsync(
                Arg.Any<ConfirmationDialogOptions>(), Arg.Any<CancellationToken>())
            .Returns(result);
        return confirmationDialog;
    }

    private static ILocalizationService CreateLocalizationService()
    {
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(callInfo =>
            callInfo.Arg<string>() == "ChangeDeviceConfirmation_Caption"
                ? "Confirm Change Device: {0} - {1}"
                : callInfo.Arg<string>());
        return localization;
    }
}
