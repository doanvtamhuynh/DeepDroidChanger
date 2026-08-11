using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using NSubstitute;

namespace DeepDroidChanger.Tests.ViewModels.Dialogs;

[TestClass]
public sealed class InstallPackageBatchViewModelTests
{
    [TestMethod]
    public void Initialize_SetsBatchTargetHeader()
    {
        using var viewModel = CreateViewModel(Substitute.For<IFilePickerDialogService>());

        viewModel.InitializeBatch(3);

        Assert.AreEqual(3, viewModel.BatchTargetCount);
        Assert.AreEqual("Install packages on 3 devices", viewModel.DeviceInfoText);
    }

    [TestMethod]
    public void StartInstallCommand_NoPackages_CannotExecute()
    {
        using var viewModel = CreateViewModel(Substitute.For<IFilePickerDialogService>());
        viewModel.InitializeBatch(2);

        Assert.IsFalse(viewModel.StartInstallCommand.CanExecute(null));
    }

    [TestMethod]
    public void AddFilesCommand_AddsFilesAndIgnoresDuplicatePaths()
    {
        IFilePickerDialogService filePicker = Substitute.For<IFilePickerDialogService>();
        filePicker.ShowOpenFileDialogMulti(Arg.Any<string>(), Arg.Any<string>())
            .Returns(["one.apk", "two.xapk", "ONE.APK"]);
        using var viewModel = CreateViewModel(filePicker);

        viewModel.AddFilesCommand.Execute(null);

        CollectionAssert.AreEqual(
            new[] { "one.apk", "two.xapk" },
            viewModel.Packages.Select(package => package.FilePath).ToArray());
    }

    [TestMethod]
    public void RemoveSelectedPackageCommand_RemovesSelectedFile()
    {
        IFilePickerDialogService filePicker = Substitute.For<IFilePickerDialogService>();
        filePicker.ShowOpenFileDialogMulti(Arg.Any<string>(), Arg.Any<string>())
            .Returns(["one.apk", "two.xapk"]);
        using var viewModel = CreateViewModel(filePicker);
        viewModel.AddFilesCommand.Execute(null);
        viewModel.SelectedPackage = viewModel.Packages[0];

        viewModel.RemoveSelectedPackageCommand.Execute(null);

        CollectionAssert.AreEqual(
            new[] { "two.xapk" },
            viewModel.Packages.Select(package => package.FilePath).ToArray());
        Assert.IsNull(viewModel.SelectedPackage);
    }

    [TestMethod]
    public async Task StartInstallCommand_CreatesStableBatchRequestWithQueueOrderAndOptions()
    {
        IFilePickerDialogService filePicker = Substitute.For<IFilePickerDialogService>();
        filePicker.ShowOpenFileDialogMulti(Arg.Any<string>(), Arg.Any<string>())
            .Returns(["one.apk", "two.xapk"]);
        using var viewModel = CreateViewModel(filePicker);
        viewModel.InitializeBatch(2);
        viewModel.GrantPermissions = false;
        viewModel.AllowDowngrade = true;
        viewModel.AddFilesCommand.Execute(null);
        bool? closeResult = null;
        viewModel.CloseRequested += (_, result) => closeResult = result;

        await viewModel.StartInstallCommand.ExecuteAsync(null);
        InstallPackageBatchRequest request = viewModel.BuildRequest()!;
        viewModel.Packages.Clear();

        CollectionAssert.AreEqual(
            new[] { "one.apk", "two.xapk" },
            request.FilePaths.ToArray());
        Assert.IsFalse(request.Options.GrantPermissions);
        Assert.IsTrue(request.Options.AllowDowngrade);
        Assert.AreEqual(true, closeResult);
    }

    private static InstallPackageBatchViewModel CreateViewModel(
        IFilePickerDialogService filePicker)
    {
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(callInfo =>
            callInfo.Arg<string>() == "InstallPackage_BatchDeviceInfo"
                ? "Install packages on {0} devices"
                : callInfo.Arg<string>());
        return new InstallPackageBatchViewModel(filePicker, localization);
    }
}
