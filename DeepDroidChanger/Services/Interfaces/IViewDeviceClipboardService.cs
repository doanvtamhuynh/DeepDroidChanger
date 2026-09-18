namespace DeepDroidChanger.Services;

public interface IViewDeviceClipboardService
{
    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}
