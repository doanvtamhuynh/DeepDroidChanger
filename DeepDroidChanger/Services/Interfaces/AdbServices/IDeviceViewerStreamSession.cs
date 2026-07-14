using DeepDroidChanger.Models;
namespace DeepDroidChanger.Services
{
    public interface IDeviceViewerStreamSession : IDisposable
    {
        event EventHandler? Exited;

        bool HasExited { get; }

        void UpdateBounds(DeviceViewerStreamBounds bounds);

        void SetVisible(bool isVisible);

        void Activate();

        Task StopAsync(CancellationToken cancellationToken = default);
    }
}
