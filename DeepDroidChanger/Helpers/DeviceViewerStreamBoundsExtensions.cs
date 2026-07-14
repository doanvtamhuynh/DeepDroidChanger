using DeepDroidChanger.Models;

namespace DeepDroidChanger.Helpers;

public static class DeviceViewerStreamBoundsExtensions
{
    public static bool IsValid(this DeviceViewerStreamBounds bounds)
    {
        return bounds.Width > 0 && bounds.Height > 0;
    }
}
