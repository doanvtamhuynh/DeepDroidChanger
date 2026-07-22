using DeepDroidChanger.ViewModels;

namespace DeepDroidChanger.Tests.ViewModels.Dialogs;

[TestClass]
public sealed class ConfirmationDialogViewModelTests
{
    [TestMethod]
    public void Initialize_SetsCaptionAndMessage()
    {
        var viewModel = new ConfirmationDialogViewModel();

        viewModel.Initialize("Confirm action", "Continue with this action?");

        Assert.AreEqual("Confirm action", viewModel.Caption);
        Assert.AreEqual("Continue with this action?", viewModel.Message);
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
