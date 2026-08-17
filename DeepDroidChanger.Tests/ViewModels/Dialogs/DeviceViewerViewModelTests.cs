using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using NSubstitute;

namespace DeepDroidChanger.Tests.ViewModels.Dialogs;

[TestClass]
public sealed class DeviceViewerViewModelTests
{
    [TestMethod]
    public void Initialize_BuildsLocalizedWindowTitle()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("DeviceViewer_WindowTitleFormat")
            .Returns("View Device - {0} ({1})");
        var viewModel = new DeviceViewerViewModel(localization);

        viewModel.Initialize("SERIAL", "Device");

        Assert.AreEqual("View Device - Device (SERIAL)", viewModel.WindowTitle);
        Assert.IsFalse(viewModel.IsActionsPanelExpanded);
    }

    [TestMethod]
    public void ToggleActionsPanel_ChangesOnlyPresentationState()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns("{0} ({1})");
        var viewModel = new DeviceViewerViewModel(localization);
        viewModel.Initialize("SERIAL", "Device");

        viewModel.ToggleActionsPanelCommand.Execute(null);
        Assert.IsTrue(viewModel.IsActionsPanelExpanded);

        viewModel.ToggleActionsPanelCommand.Execute(null);
        Assert.IsFalse(viewModel.IsActionsPanelExpanded);
    }
}
