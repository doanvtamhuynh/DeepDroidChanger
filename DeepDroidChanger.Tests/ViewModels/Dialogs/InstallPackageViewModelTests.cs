using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.ViewModels.Dialogs;

[TestClass]
public sealed class InstallPackageViewModelTests
{
    [TestMethod]
    public async Task StartInstallCommand_AggregatesSuccessAndFailureResults()
    {
        IFilePickerDialogService filePicker = Substitute.For<IFilePickerDialogService>();
        filePicker.ShowOpenFileDialogMulti(Arg.Any<string>(), Arg.Any<string>())
            .Returns(["first.apk", "second.apk"]);
        IPackageInstallService installer = Substitute.For<IPackageInstallService>();
        installer.InstallAsync("SERIAL", "first.apk", Arg.Any<InstallPackageOptions>(), Arg.Any<CancellationToken>())
            .Returns(new InstallPackageResult("first.apk", true, "Log_InstallPackageSuccess"));
        installer.InstallAsync("SERIAL", "second.apk", Arg.Any<InstallPackageOptions>(), Arg.Any<CancellationToken>())
            .Returns(new InstallPackageResult("second.apk", false, "Log_InstallPackageAdbFailure"));
        using var viewModel = CreateViewModel(filePicker, installer);
        viewModel.Initialize("SERIAL", "Pixel");
        viewModel.AddFilesCommand.Execute(null);

        await viewModel.StartInstallCommand.ExecuteAsync(null);
        InstallPackageDialogResult result = viewModel.BuildResult();

        Assert.AreEqual(2, result.TotalCount);
        Assert.AreEqual(1, result.SuccessCount);
        Assert.AreEqual(1, result.FailedCount);
        Assert.IsFalse(result.Canceled);
        Assert.AreEqual(100, viewModel.OverallProgress);
    }

    [TestMethod]
    public async Task CancelCommand_CancelsCurrentInstallAndReturnsCanceledResult()
    {
        IFilePickerDialogService filePicker = Substitute.For<IFilePickerDialogService>();
        filePicker.ShowOpenFileDialogMulti(Arg.Any<string>(), Arg.Any<string>())
            .Returns(["slow.apk"]);
        IPackageInstallService installer = Substitute.For<IPackageInstallService>();
        installer.InstallAsync("SERIAL", "slow.apk", Arg.Any<InstallPackageOptions>(), Arg.Any<CancellationToken>())
            .Returns<Task<InstallPackageResult>>(async callInfo =>
            {
                CancellationToken token = callInfo.ArgAt<CancellationToken>(3);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new InstallPackageResult("slow.apk", true, "Log_InstallPackageSuccess");
            });
        using var viewModel = CreateViewModel(filePicker, installer);
        viewModel.Initialize("SERIAL", "Pixel");
        viewModel.AddFilesCommand.Execute(null);

        Task installTask = viewModel.StartInstallCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => viewModel.IsInstalling);
        viewModel.CancelCommand.Execute(null);
        await installTask;
        InstallPackageDialogResult result = viewModel.BuildResult();

        Assert.IsTrue(result.Canceled);
        Assert.IsTrue(viewModel.HasCompleted);
        Assert.IsFalse(viewModel.IsInstalling);
    }

    private static InstallPackageViewModel CreateViewModel(
        IFilePickerDialogService filePicker,
        IPackageInstallService installer)
    {
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(callInfo =>
            callInfo.Arg<string>() is "Log_InstallPackageCompleteFormat"
                or "Log_InstallPackagePartialFormat"
                or "Log_InstallPackageFailedFormat"
                ? "{0}/{1}"
                : callInfo.Arg<string>());
        return new InstallPackageViewModel(
            filePicker,
            installer,
            localization,
            NullLogger<InstallPackageViewModel>.Instance);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (predicate())
                return;
            await Task.Delay(10);
        }

        Assert.Fail("Condition was not reached before timeout.");
    }
}
