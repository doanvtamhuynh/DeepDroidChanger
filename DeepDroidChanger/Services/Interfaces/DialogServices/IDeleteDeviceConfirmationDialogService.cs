namespace DeepDroidChanger.Services
{
    public interface IDeleteDeviceConfirmationDialogService
    {
        Task<bool> ShowDeleteDeviceConfirmationAsync(string deviceName, string deviceSerial, CancellationToken cancellationToken);
    }
}
