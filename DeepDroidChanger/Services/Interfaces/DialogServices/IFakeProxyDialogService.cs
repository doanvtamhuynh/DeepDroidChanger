using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public interface IFakeProxyDialogService
    {
        Task<FakeProxyDialogResult?> ShowFakeProxyDialogAsync(
            string deviceSerial,
            string deviceName,
            CancellationToken cancellationToken);
    }
}
