using System;
using System.Windows;
using WpfPoint = System.Windows.Point;

namespace ScrcpyNet.Wpf;

public static class ScrcpyDisplayGeometry
{
    public static Rect CalculateUniformContentRect(
        int sourceWidth,
        int sourceHeight,
        double targetWidth,
        double targetHeight)
    {
        if (sourceWidth <= 0 ||
            sourceHeight <= 0 ||
            !IsPositiveFinite(targetWidth) ||
            !IsPositiveFinite(targetHeight))
        {
            return Rect.Empty;
        }

        double scale = Math.Min(
            targetWidth / sourceWidth,
            targetHeight / sourceHeight);
        double contentWidth = sourceWidth * scale;
        double contentHeight = sourceHeight * scale;
        return new Rect(
            (targetWidth - contentWidth) / 2,
            (targetHeight - contentHeight) / 2,
            contentWidth,
            contentHeight);
    }

    public static bool TryMapPointToDevice(
        WpfPoint targetPoint,
        int sourceWidth,
        int sourceHeight,
        double targetWidth,
        double targetHeight,
        out WpfPoint devicePoint)
    {
        Rect contentRect = CalculateUniformContentRect(
            sourceWidth,
            sourceHeight,
            targetWidth,
            targetHeight);
        if (contentRect.IsEmpty ||
            !IsFinite(targetPoint.X) ||
            !IsFinite(targetPoint.Y) ||
            targetPoint.X < contentRect.Left ||
            targetPoint.X > contentRect.Right ||
            targetPoint.Y < contentRect.Top ||
            targetPoint.Y > contentRect.Bottom)
        {
            devicePoint = default;
            return false;
        }

        double relativeX = (targetPoint.X - contentRect.Left) / contentRect.Width;
        double relativeY = (targetPoint.Y - contentRect.Top) / contentRect.Height;
        int deviceX = (int)Math.Floor(relativeX * sourceWidth);
        int deviceY = (int)Math.Floor(relativeY * sourceHeight);
        devicePoint = new WpfPoint(
            Math.Clamp(deviceX, 0, sourceWidth - 1),
            Math.Clamp(deviceY, 0, sourceHeight - 1));
        return true;
    }

    private static bool IsPositiveFinite(double value)
    {
        return IsFinite(value) && value > 0;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
