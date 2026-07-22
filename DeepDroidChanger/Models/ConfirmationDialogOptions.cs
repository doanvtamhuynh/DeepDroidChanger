namespace DeepDroidChanger.Models;

public sealed class ConfirmationDialogOptions
{
    public string? Caption { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? WarningMessage { get; set; }
    public string? ConfirmButtonText { get; set; }
    public string? CancelButtonText { get; set; }
    public ConfirmationDialogIcon Icon { get; set; } = ConfirmationDialogIcon.Question;
}
