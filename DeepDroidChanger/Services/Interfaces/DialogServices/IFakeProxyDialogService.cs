using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface IFakeProxyDialogService
    {
        Task<FakeProxyDialogResult?> ShowFakeProxyDialogAsync(
            string deviceSerial,
            string deviceName,
            CancellationToken cancellationToken);

        Task<FakeProxyDialogResult?> ShowFakeProxyDialogAsync(
            string deviceSerial,
            string deviceName,
            StoredDeviceConfig? configurationSnapshot,
            CancellationToken cancellationToken)
            => ShowFakeProxyDialogAsync(deviceSerial, deviceName, cancellationToken);
    }
}
