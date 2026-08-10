using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.DialogServices;

[TestClass]
public sealed class DeviceActionConfirmationDialogServiceTests
{
    [TestMethod]
    public async Task ConfirmActions_ShowShortLocalizedMessagesWithDeviceIdentityInCaption()
    {
        IConfirmationDialogService confirmationDialog = Substitute.For<IConfirmationDialogService>();
        confirmationDialog.ShowConfirmationAsync(
                Arg.Any<ConfirmationDialogOptions>(), Arg.Any<CancellationToken>())
            .Returns(true);
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(callInfo =>
        {
            string key = callInfo.Arg<string>();
            if (key.StartsWith("ChangeMultipleDevices_", StringComparison.Ordinal)
                && (key.EndsWith("Caption", StringComparison.Ordinal)
                    || key.EndsWith("Message", StringComparison.Ordinal)))
            {
                return $"{key}: {{0}}";
            }

            return key.EndsWith("Caption", StringComparison.Ordinal)
                ? $"{key}: {{0}} - {{1}}"
                : key;
        });
        var service = new DeviceActionConfirmationDialogService(confirmationDialog, localization);

        bool changeWithoutWipe = await service.ConfirmChangeWithoutWipeAsync(
            "Phone", "SERIAL", CancellationToken.None);
        bool wipeWithoutChange = await service.ConfirmWipeWithoutChangeAsync(
            "Phone", "SERIAL", CancellationToken.None);
        bool changeSim = await service.ConfirmChangeSimAsync(
            "Phone", "SERIAL", CancellationToken.None);
        bool multipleChange = await service.ConfirmMultipleAsync(
            MultipleDeviceBatchAction.ChangeAndWipe, 3, CancellationToken.None);

        Assert.IsTrue(changeWithoutWipe);
        Assert.IsTrue(wipeWithoutChange);
        Assert.IsTrue(changeSim);
        Assert.IsTrue(multipleChange);
        await confirmationDialog.Received(1).ShowConfirmationAsync(
            Arg.Is<ConfirmationDialogOptions>(options =>
                options.Caption == "ChangeSingleDevice_ConfirmChangeWithoutWipeCaption: Phone - SERIAL"
                && options.Message == "ChangeSingleDevice_ConfirmChangeWithoutWipeMessage"
                && options.WarningMessage == "ChangeSingleDevice_ConfirmChangeWithoutWipeWarning"
                && options.Icon == ConfirmationDialogIcon.ChangeDevice),
            Arg.Any<CancellationToken>());
        await confirmationDialog.Received(1).ShowConfirmationAsync(
            Arg.Is<ConfirmationDialogOptions>(options =>
                options.Caption == "ChangeMultipleDevices_ConfirmChangeAndWipeCaption: 3"
                && options.Message == "ChangeMultipleDevices_ConfirmChangeAndWipeMessage: 3"
                && options.WarningMessage == "ChangeMultipleDevices_ConfirmChangeAndWipeWarning"
                && options.Icon == ConfirmationDialogIcon.ChangeDevice),
            Arg.Any<CancellationToken>());
        await confirmationDialog.Received(1).ShowConfirmationAsync(
            Arg.Is<ConfirmationDialogOptions>(options =>
                options.Caption == "ChangeSingleDevice_ConfirmWipeWithoutChangeCaption: Phone - SERIAL"
                && options.Message == "ChangeSingleDevice_ConfirmWipeWithoutChangeMessage"
                && options.WarningMessage == "ChangeSingleDevice_ConfirmWipeWithoutChangeWarning"
                && options.Icon == ConfirmationDialogIcon.Wipe),
            Arg.Any<CancellationToken>());
        await confirmationDialog.Received(1).ShowConfirmationAsync(
            Arg.Is<ConfirmationDialogOptions>(options =>
                options.Caption == "ChangeSingleDevice_ConfirmChangeSimCaption: Phone - SERIAL"
                && options.Message == "ChangeSingleDevice_ConfirmChangeSimMessage"
                && options.WarningMessage == "ChangeSingleDevice_ConfirmChangeSimWarning"
                && options.Icon == ConfirmationDialogIcon.Sim),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ConfirmDeleteAndChangeAndWipe_UseExistingLocalizedOptions()
    {
        IConfirmationDialogService confirmationDialog = Substitute.For<IConfirmationDialogService>();
        confirmationDialog.ShowConfirmationAsync(
                Arg.Any<ConfirmationDialogOptions>(), Arg.Any<CancellationToken>())
            .Returns(true);
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(callInfo =>
        {
            string key = callInfo.Arg<string>();
            return key is "DeleteDeviceConfirmation_Caption" or "ChangeDeviceConfirmation_Caption"
                ? $"{key}: {{0}} - {{1}}"
                : key;
        });
        var service = new DeviceActionConfirmationDialogService(confirmationDialog, localization);

        bool deleteConfirmed = await service.ConfirmDeleteDeviceAsync(
            "Offline phone", "SERIAL", CancellationToken.None);
        bool changeConfirmed = await service.ConfirmChangeAndWipeAsync(
            "Phone", "SERIAL", new DeviceChangeOptions { UseDefaultMode = true }, CancellationToken.None);

        Assert.IsTrue(deleteConfirmed);
        Assert.IsTrue(changeConfirmed);
        await confirmationDialog.Received(1).ShowConfirmationAsync(
            Arg.Is<ConfirmationDialogOptions>(options =>
                options.Caption == "DeleteDeviceConfirmation_Caption: Offline phone - SERIAL"
                && options.Message == "DeleteDeviceConfirmation_Message"
                && options.WarningMessage == "DeleteDeviceConfirmation_Warning"
                && options.Icon == ConfirmationDialogIcon.Delete),
            Arg.Any<CancellationToken>());
        await confirmationDialog.Received(1).ShowConfirmationAsync(
            Arg.Is<ConfirmationDialogOptions>(options =>
                options.Caption == "ChangeDeviceConfirmation_Caption: Phone - SERIAL"
                && options.Message == "ChangeDeviceConfirmation_Message"
                && options.WarningMessage == "ChangeDeviceConfirmation_Warning"
                && options.Icon == ConfirmationDialogIcon.ChangeDevice),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ConfirmChangeAndWipe_AdvancedOptions_DoNotExposeCleanupDetails()
    {
        IConfirmationDialogService confirmationDialog = Substitute.For<IConfirmationDialogService>();
        confirmationDialog.ShowConfirmationAsync(
                Arg.Any<ConfirmationDialogOptions>(), Arg.Any<CancellationToken>())
            .Returns(false);
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(callInfo =>
            callInfo.Arg<string>() == "ChangeDeviceConfirmation_Caption"
                ? "Confirm Change Device: {0} - {1}"
                : callInfo.Arg<string>());
        var service = new DeviceActionConfirmationDialogService(confirmationDialog, localization);

        bool confirmed = await service.ConfirmChangeAndWipeAsync(
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
}
