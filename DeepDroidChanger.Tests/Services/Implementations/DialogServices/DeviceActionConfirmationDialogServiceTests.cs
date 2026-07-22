using DeepDroidChanger.Services;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.DialogServices;

[TestClass]
public sealed class DeviceActionConfirmationDialogServiceTests
{
    [TestMethod]
    public async Task ConfirmActions_ShowLocalizedWarningsWithDeviceIdentity()
    {
        IConfirmationDialogService confirmationDialog = Substitute.For<IConfirmationDialogService>();
        confirmationDialog.ShowWarningConfirmationAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(callInfo =>
        {
            string key = callInfo.Arg<string>();
            return key.EndsWith("Message", StringComparison.Ordinal)
                ? $"{key}: {{0}}/{{1}}"
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
        await confirmationDialog.Received(1).ShowWarningConfirmationAsync(
            "DeviceManager_ConfirmChangeWithoutWipeMessage: Phone/SERIAL",
            "DeviceManager_ConfirmChangeWithoutWipeTitle",
            Arg.Any<CancellationToken>());
        await confirmationDialog.Received(1).ShowWarningConfirmationAsync(
            "DeviceManager_ConfirmWipeWithoutChangeMessage: Phone/SERIAL",
            "DeviceManager_ConfirmWipeWithoutChangeTitle",
            Arg.Any<CancellationToken>());
        await confirmationDialog.Received(1).ShowWarningConfirmationAsync(
            "DeviceManager_ConfirmChangeSimMessage: Phone/SERIAL",
            "DeviceManager_ConfirmChangeSimTitle",
            Arg.Any<CancellationToken>());
    }
}
