using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.DialogServices;

[TestClass]
public sealed class DeleteDeviceConfirmationDialogServiceTests
{
    [TestMethod]
    public async Task ShowDeleteDeviceConfirmationAsync_ShowsSavedDataWarningAndReturnsSelection()
    {
        IConfirmationDialogService confirmationDialog = Substitute.For<IConfirmationDialogService>();
        confirmationDialog.ShowWarningConfirmationAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());
        var service = new DeleteDeviceConfirmationDialogService(
            confirmationDialog,
            localization,
            NullLogger<DeleteDeviceConfirmationDialogService>.Instance);

        bool confirmed = await service.ShowDeleteDeviceConfirmationAsync(
            "Offline phone",
            "SERIAL",
            CancellationToken.None);

        Assert.IsTrue(confirmed);
        await confirmationDialog.Received(1).ShowWarningConfirmationAsync(
            Arg.Is<string>(message =>
                message.Contains("DeleteDeviceConfirmation_Title", StringComparison.Ordinal)
                && message.Contains("Offline phone", StringComparison.Ordinal)
                && message.Contains("SERIAL", StringComparison.Ordinal)
                && message.Contains("DeleteDeviceConfirmation_Message", StringComparison.Ordinal)),
            "DeleteDeviceConfirmation_WindowTitle",
            Arg.Any<CancellationToken>());
    }
}
