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

        Assert.IsTrue(changeWithoutWipe);
        Assert.IsTrue(wipeWithoutChange);
        Assert.IsTrue(changeSim);
        await confirmationDialog.Received(1).ShowConfirmationAsync(
            Arg.Is<ConfirmationDialogOptions>(options =>
                options.Caption == "DeviceManager_ConfirmChangeWithoutWipeCaption: Phone - SERIAL"
                && options.Message == "DeviceManager_ConfirmChangeWithoutWipeMessage"
                && options.WarningMessage == "DeviceManager_ConfirmChangeWithoutWipeWarning"
                && options.Icon == ConfirmationDialogIcon.ChangeDevice),
            Arg.Any<CancellationToken>());
        await confirmationDialog.Received(1).ShowConfirmationAsync(
            Arg.Is<ConfirmationDialogOptions>(options =>
                options.Caption == "DeviceManager_ConfirmWipeWithoutChangeCaption: Phone - SERIAL"
                && options.Message == "DeviceManager_ConfirmWipeWithoutChangeMessage"
                && options.WarningMessage == "DeviceManager_ConfirmWipeWithoutChangeWarning"
                && options.Icon == ConfirmationDialogIcon.Wipe),
            Arg.Any<CancellationToken>());
        await confirmationDialog.Received(1).ShowConfirmationAsync(
            Arg.Is<ConfirmationDialogOptions>(options =>
                options.Caption == "DeviceManager_ConfirmChangeSimCaption: Phone - SERIAL"
                && options.Message == "DeviceManager_ConfirmChangeSimMessage"
                && options.WarningMessage == "DeviceManager_ConfirmChangeSimWarning"
                && options.Icon == ConfirmationDialogIcon.Sim),
            Arg.Any<CancellationToken>());
    }
}
