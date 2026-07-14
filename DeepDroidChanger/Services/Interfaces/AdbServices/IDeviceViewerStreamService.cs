using DeepDroidChanger.Models;
namespace DeepDroidChanger.Services
{
    public interface IDeviceViewerStreamService
    {
        Task<IDeviceViewerStreamSession> StartAsync(string serial, IntPtr ownerHwnd, DeviceViewerStreamBounds bounds, CancellationToken cancellationToken);
    }
}
