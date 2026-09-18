namespace DeepDroidChanger.Views;

internal static class ViewMultipleDevicesLayout
{
    internal const double FallbackDeviceAspectRatio = 9d / 16d;

    internal static double CalculateStreamHeight(double width, double deviceAspectRatio)
    {
        if (!double.IsFinite(width) || width <= 0)
            return 0;

        double aspectRatio = double.IsFinite(deviceAspectRatio) && deviceAspectRatio > 0
            ? deviceAspectRatio
            : FallbackDeviceAspectRatio;
        double height = width / aspectRatio;
        return double.IsFinite(height) && height > 0 ? height : 0;
    }
}
