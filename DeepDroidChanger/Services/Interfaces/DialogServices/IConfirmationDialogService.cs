namespace DeepDroidChanger.Services;

public interface IConfirmationDialogService
{
    Task<bool> ShowWarningConfirmationAsync(
        string message,
        string caption,
        CancellationToken cancellationToken);
}
