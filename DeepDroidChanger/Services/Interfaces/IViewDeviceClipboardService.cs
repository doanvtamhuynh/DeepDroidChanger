namespace DeepDroidChanger.Services;

public interface IViewDeviceClipboardService
{
    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
    Task<bool> TrySetTextAsync(string text, Func<Action, bool> tryCommit, CancellationToken cancellationToken = default) =>
        Task.FromException<bool>(new NotSupportedException("Atomic device clipboard writes are not supported by this provider."));
}
