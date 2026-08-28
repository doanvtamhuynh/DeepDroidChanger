namespace DeepDroidChanger.Services;

public interface IViewDeviceWindowService
{
    Task OpenAsync(string serial, string? displayName, CancellationToken cancellationToken = default);
    Task CloseAllAsync(CancellationToken cancellationToken = default);
}
