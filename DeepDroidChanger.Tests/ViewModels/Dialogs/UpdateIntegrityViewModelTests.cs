using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using NSubstitute;

namespace DeepDroidChanger.Tests.ViewModels.Dialogs;

[TestClass]
public sealed class UpdateIntegrityViewModelTests
{
    [TestMethod]
    public void InitializeFromConfig_MissingSavedFiles_SanitizesAndRequestsPersistence()
    {
        IFileSystemService fileSystem = Substitute.For<IFileSystemService>();
        var viewModel = new UpdateIntegrityViewModel(
            Substitute.For<IFilePickerDialogService>(),
            Substitute.For<ILocalizationService>(),
            fileSystem);
        UpdateIntegrityDialogResult? changedResult = null;
        viewModel.SettingsChanged += (_, result) => changedResult = result;

        viewModel.InitializeFromConfig(new StoredDeviceConfig
        {
            UpdateIntegrityFromServer = false,
            UpdateIntegrityEnabled = true,
            UpdateKeyboxEnabled = true,
            UpdateIntegrityFile = "missing-pif.json",
            UpdateKeyboxFile = "missing-keybox.xml"
        });

        Assert.IsNotNull(changedResult);
        Assert.AreEqual(string.Empty, changedResult.UpdateIntegrityFile);
        Assert.AreEqual(string.Empty, changedResult.UpdateKeyboxFile);
        Assert.IsTrue(changedResult.UpdateIntegrityFromServer);
        Assert.IsTrue(viewModel.UpdateCommand.CanExecute(null));
    }

    [TestMethod]
    public void InitializeFromConfig_OneMissingLocalTarget_DisablesOnlyMissingTarget()
    {
        const string pifPath = "available-pif.json";
        IFileSystemService fileSystem = Substitute.For<IFileSystemService>();
        fileSystem.FileExists(pifPath).Returns(true);
        var viewModel = new UpdateIntegrityViewModel(
            Substitute.For<IFilePickerDialogService>(),
            Substitute.For<ILocalizationService>(),
            fileSystem);
        UpdateIntegrityDialogResult? changedResult = null;
        viewModel.SettingsChanged += (_, result) => changedResult = result;

        viewModel.InitializeFromConfig(new StoredDeviceConfig
        {
            UpdateIntegrityFromServer = false,
            UpdateIntegrityEnabled = true,
            UpdateKeyboxEnabled = true,
            UpdateIntegrityFile = pifPath,
            UpdateKeyboxFile = "missing-keybox.xml"
        });

        Assert.IsNotNull(changedResult);
        Assert.IsFalse(changedResult.UpdateIntegrityFromServer);
        Assert.IsTrue(changedResult.UpdateIntegrityEnabled);
        Assert.IsFalse(changedResult.UpdateKeyboxEnabled);
        Assert.AreEqual(pifPath, changedResult.UpdateIntegrityFile);
        Assert.AreEqual(string.Empty, changedResult.UpdateKeyboxFile);
    }

    [TestMethod]
    public void EditingDialog_RaisesCompletePerDeviceConfigForPersistence()
    {
        const string pifPath = "available-pif.json";
        const string keyboxPath = "available-keybox.xml";
        IFileSystemService fileSystem = Substitute.For<IFileSystemService>();
        fileSystem.FileExists(pifPath).Returns(true);
        fileSystem.FileExists(keyboxPath).Returns(true);
        var viewModel = new UpdateIntegrityViewModel(
            Substitute.For<IFilePickerDialogService>(),
            Substitute.For<ILocalizationService>(),
            fileSystem);
        UpdateIntegrityDialogResult? changedResult = null;
        viewModel.SettingsChanged += (_, result) => changedResult = result;
        viewModel.InitializeFromConfig(new StoredDeviceConfig());

        viewModel.UpdateIntegrityFromServer = false;
        viewModel.UpdateIntegrityFile = pifPath;
        viewModel.UpdateKeyboxFile = keyboxPath;
        viewModel.UpdateIntegrityEnabled = true;
        viewModel.UpdateKeyboxEnabled = false;

        Assert.IsNotNull(changedResult);
        Assert.IsFalse(changedResult.UpdateIntegrityFromServer);
        Assert.IsTrue(changedResult.UpdateIntegrityEnabled);
        Assert.IsFalse(changedResult.UpdateKeyboxEnabled);
        Assert.AreEqual(pifPath, changedResult.UpdateIntegrityFile);
        Assert.AreEqual(keyboxPath, changedResult.UpdateKeyboxFile);
    }
}
