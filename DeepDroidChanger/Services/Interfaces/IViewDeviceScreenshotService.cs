namespace DeepDroidChanger.Services;

public interface IViewDeviceScreenshotService
{
    Task CapturePngAsync(
        string serial,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
