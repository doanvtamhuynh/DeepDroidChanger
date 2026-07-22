using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IConfirmationDialogService
{
    Task<bool> ShowConfirmationAsync(
        ConfirmationDialogOptions options,
        CancellationToken cancellationToken = default);
}
