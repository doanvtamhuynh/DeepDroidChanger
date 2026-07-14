namespace DeepDroidChanger.Services
{
    public interface IDeviceViewerDialogService
    {
        Task ShowDeviceViewerAsync(string serial, string name, CancellationToken cancellationToken);
    }
}
