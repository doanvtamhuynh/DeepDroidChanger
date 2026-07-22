using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;
using MaterialDesignThemes.Wpf;

namespace DeepDroidChanger.Tests.ViewModels.Dialogs;

[TestClass]
public sealed class ConfirmationDialogViewModelTests
{
    [TestMethod]
    public void Initialize_SetsCaptionAndMessageAndWarning()
    {
        var viewModel = new ConfirmationDialogViewModel();

        viewModel.Initialize(
            "Confirm Change Device: Phone - SERIAL",
            "Change device information.",
            "This action will change device information.",
            "Yes",
            "No",
            ConfirmationDialogIcon.ChangeDevice);

        Assert.AreEqual("Confirm Change Device: Phone - SERIAL", viewModel.Caption);
        Assert.AreEqual("Change device information.", viewModel.Message);
        Assert.AreEqual("This action will change device information.", viewModel.WarningMessage);
        Assert.IsTrue(viewModel.HasWarning);
        Assert.AreEqual("Yes", viewModel.ConfirmButtonText);
        Assert.AreEqual("No", viewModel.CancelButtonText);
        Assert.AreEqual(PackIconKind.CellphoneCog, viewModel.IconKind);
    }

    [TestMethod]
    [DataRow(ConfirmationDialogIcon.Question, PackIconKind.HelpCircleOutline)]
    [DataRow(ConfirmationDialogIcon.ChangeDevice, PackIconKind.CellphoneCog)]
    [DataRow(ConfirmationDialogIcon.Wipe, PackIconKind.DeleteSweep)]
    [DataRow(ConfirmationDialogIcon.Sim, PackIconKind.SimCard)]
    [DataRow(ConfirmationDialogIcon.Delete, PackIconKind.Delete)]
    public void Initialize_MapsActionToExpectedIcon(
        ConfirmationDialogIcon icon,
        PackIconKind expectedIconKind)
    {
        var viewModel = new ConfirmationDialogViewModel();

        viewModel.Initialize("Caption", "Message", "Warning", "Yes", "No", icon);

        Assert.AreEqual(expectedIconKind, viewModel.IconKind);
    }

    [TestMethod]
    public void ConfirmAndCancelCommands_ReturnExpectedResults()
    {
        var viewModel = new ConfirmationDialogViewModel();
        var results = new List<bool>();
        viewModel.CloseRequested += (_, confirmed) => results.Add(confirmed);

        viewModel.ConfirmCommand.Execute(null);
        viewModel.CancelCommand.Execute(null);

        CollectionAssert.AreEqual(new[] { true, false }, results);
    }
}
