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

    [TestMethod]
    public async Task BatchMode_StartInstall_DoesNotCallPackageInstallService()
    {
        IFilePickerDialogService filePicker = Substitute.For<IFilePickerDialogService>();
        filePicker.ShowOpenFileDialogMulti(Arg.Any<string>(), Arg.Any<string>())
            .Returns(["batch.apk"]);
        IPackageInstallService installer = Substitute.For<IPackageInstallService>();
        using var viewModel = CreateViewModel(filePicker, installer);
        viewModel.InitializeBatch(2);
        viewModel.AddFilesCommand.Execute(null);

        await viewModel.StartInstallCommand.ExecuteAsync(null);

        await installer.DidNotReceiveWithAnyArgs()
            .InstallAsync(default!, default!, default!, default);
        Assert.IsFalse(viewModel.IsInstalling);
        Assert.IsFalse(viewModel.HasCompleted);
    }

    [TestMethod]
    public async Task BatchMode_StartInstall_RequestsSuccessfulDialogClose()
    {
        IFilePickerDialogService filePicker = Substitute.For<IFilePickerDialogService>();
        filePicker.ShowOpenFileDialogMulti(Arg.Any<string>(), Arg.Any<string>())
            .Returns(["batch.apk"]);
        using var viewModel = CreateViewModel(filePicker, Substitute.For<IPackageInstallService>());
        viewModel.InitializeBatch(2);
        viewModel.AddFilesCommand.Execute(null);
        bool? closeResult = null;
        viewModel.CloseRequested += (_, result) => closeResult = result;

        await viewModel.StartInstallCommand.ExecuteAsync(null);

        Assert.AreEqual(true, closeResult);
    }

    [TestMethod]
    public async Task BatchMode_BuildRequest_CapturesQueueOrderAndOptionsAsStableSnapshot()
    {
        IFilePickerDialogService filePicker = Substitute.For<IFilePickerDialogService>();
        filePicker.ShowOpenFileDialogMulti(Arg.Any<string>(), Arg.Any<string>())
            .Returns(["one.apk", "two.xapk"]);
        using var viewModel = CreateViewModel(filePicker, Substitute.For<IPackageInstallService>());
        viewModel.InitializeBatch(2);
        viewModel.GrantPermissions = false;
        viewModel.AllowDowngrade = true;
        viewModel.AddFilesCommand.Execute(null);

        await viewModel.StartInstallCommand.ExecuteAsync(null);
        InstallPackageBatchRequest request = viewModel.BuildBatchRequest()!;
        viewModel.Packages.Clear();

        CollectionAssert.AreEqual(new[] { "one.apk", "two.xapk" }, request.FilePaths.ToArray());
        Assert.IsFalse(request.Options.GrantPermissions);
        Assert.IsTrue(request.Options.AllowDowngrade);
        Assert.AreEqual(2, request.FilePaths.Count);
    }

    [TestMethod]
    public async Task BatchMode_StartInstallRequiresAtLeastOneFile()
    {
        using var viewModel = CreateViewModel(
            Substitute.For<IFilePickerDialogService>(),
            Substitute.For<IPackageInstallService>());
        viewModel.InitializeBatch(2);
        bool closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        await viewModel.StartInstallCommand.ExecuteAsync(null);

        Assert.AreEqual("Log_InstallPackageNoFiles", viewModel.SummaryText);
        Assert.IsFalse(closeRequested);
    }

    [TestMethod]
    public void Initialize_RestoresSingleModeAfterBatchInitialization()
    {
        using var viewModel = CreateViewModel(
            Substitute.For<IFilePickerDialogService>(),
            Substitute.For<IPackageInstallService>());

        viewModel.InitializeBatch(3);
        viewModel.Initialize("SERIAL", "Pixel");

        Assert.IsFalse(viewModel.IsBatchMode);
        Assert.AreEqual(0, viewModel.BatchTargetCount);
        Assert.AreEqual("SERIAL", viewModel.DeviceSerial);
        Assert.AreEqual("Pixel", viewModel.DeviceName);
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
