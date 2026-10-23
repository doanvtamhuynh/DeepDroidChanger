namespace DeepDroidChanger.Services;

public interface ITwrpWorkspaceDialogService
{
    Task<DeepDroidChanger.Models.TwrpDialogResult?> ShowBackupAsync(string serial, CancellationToken cancellationToken);
    Task<DeepDroidChanger.Models.TwrpDialogResult?> ShowRestoreAsync(string serial, CancellationToken cancellationToken);
    Task<DeepDroidChanger.Models.TwrpDialogResult?> ShowBackupAsync(string serial, string deviceName, CancellationToken cancellationToken);
    Task<DeepDroidChanger.Models.TwrpDialogResult?> ShowRestoreAsync(string serial, string deviceName, CancellationToken cancellationToken);
}
