using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.DialogServices;

[TestClass]
public sealed class DeleteDeviceConfirmationDialogServiceTests
{
    [TestMethod]
    public async Task ShowDeleteDeviceConfirmationAsync_ShowsShortMessageWithIdentityInCaption()
    {
        IConfirmationDialogService confirmationDialog = Substitute.For<IConfirmationDialogService>();
        confirmationDialog.ShowConfirmationAsync(
                Arg.Any<ConfirmationDialogOptions>(), Arg.Any<CancellationToken>())
            .Returns(true);
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(callInfo =>
            callInfo.Arg<string>() == "DeleteDeviceConfirmation_Caption"
                ? "Confirm Delete Device: {0} - {1}"
                : callInfo.Arg<string>());
        var service = new DeleteDeviceConfirmationDialogService(
            confirmationDialog,
            localization,
            NullLogger<DeleteDeviceConfirmationDialogService>.Instance);

        bool confirmed = await service.ShowDeleteDeviceConfirmationAsync(
            "Offline phone",
            "SERIAL",
            CancellationToken.None);

        Assert.IsTrue(confirmed);
        await confirmationDialog.Received(1).ShowConfirmationAsync(
            Arg.Is<ConfirmationDialogOptions>(options =>
                options.Caption == "Confirm Delete Device: Offline phone - SERIAL"
                && options.Message == "DeleteDeviceConfirmation_Message"
                && options.WarningMessage == "DeleteDeviceConfirmation_Warning"
                && options.Icon == ConfirmationDialogIcon.Delete),
            Arg.Any<CancellationToken>());
    }
}
