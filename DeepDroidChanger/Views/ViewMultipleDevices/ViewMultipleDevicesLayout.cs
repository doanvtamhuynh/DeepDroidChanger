using System.Windows;

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

    internal static Rect CalculateVisibleClip(Rect hostBounds, Rect viewportBounds)
    {
        if (!IsUsableRect(hostBounds) || !IsUsableRect(viewportBounds))
            return Rect.Empty;

        Rect intersection = hostBounds;
        intersection.Intersect(viewportBounds);
        if (!IsUsableRect(intersection))
            return Rect.Empty;

        return new Rect(
            intersection.Left - hostBounds.Left,
            intersection.Top - hostBounds.Top,
            intersection.Width,
            intersection.Height);
    }

    internal static Rect ConvertDipRectToDevicePixels(
        Rect dipRect,
        double scaleX,
        double scaleY)
    {
        if (!IsUsableRect(dipRect) ||
            !double.IsFinite(scaleX) ||
            !double.IsFinite(scaleY) ||
            scaleX <= 0 ||
            scaleY <= 0)
        {
            return Rect.Empty;
        }

        double left = Math.Floor(dipRect.Left * scaleX);
        double top = Math.Floor(dipRect.Top * scaleY);
        double right = Math.Ceiling(dipRect.Right * scaleX);
        double bottom = Math.Ceiling(dipRect.Bottom * scaleY);
        if (!double.IsFinite(left) ||
            !double.IsFinite(top) ||
            !double.IsFinite(right) ||
            !double.IsFinite(bottom) ||
            right <= left ||
            bottom <= top)
        {
            return Rect.Empty;
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    private static bool IsUsableRect(Rect rect)
    {
        return !rect.IsEmpty &&
               rect.Width > 0 &&
               rect.Height > 0 &&
               double.IsFinite(rect.Left) &&
               double.IsFinite(rect.Top) &&
               double.IsFinite(rect.Right) &&
               double.IsFinite(rect.Bottom);
    }
}
