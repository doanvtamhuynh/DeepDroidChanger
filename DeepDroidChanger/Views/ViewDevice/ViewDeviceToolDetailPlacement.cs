using System.Windows;

namespace DeepDroidChanger.Views;

internal static class ViewDeviceToolDetailPlacement
{
    internal static Point Calculate(
        Rect ownerBounds,
        Rect toolsBounds,
        Size detailSize,
        Rect workArea,
        double visibleGap,
        double shadowMargin = 0)
    {
        double width = Math.Max(0, detailSize.Width);
        double height = Math.Max(0, detailSize.Height);
        double gap = Math.Max(0, visibleGap - shadowMargin);
        double maximumLeft = Math.Max(workArea.Left, workArea.Right - width);
        double maximumTop = Math.Max(workArea.Top, workArea.Bottom - height);
        bool toolsAreRightOfOwner = toolsBounds.Left >= ownerBounds.Right - 1;

        double outward = toolsAreRightOfOwner
            ? toolsBounds.Right + gap
            : toolsBounds.Left - width - gap;
        double opposite = toolsAreRightOfOwner
            ? toolsBounds.Left - width - gap
            : toolsBounds.Right + gap;

        double left = FitsHorizontally(outward, width, workArea)
            ? outward
            : FitsHorizontally(opposite, width, workArea)
                ? opposite
                : Math.Clamp(outward, workArea.Left, maximumLeft);
        double top = Math.Clamp(toolsBounds.Top, workArea.Top, maximumTop);
        return new Point(left, top);
    }

    private static bool FitsHorizontally(double left, double width, Rect workArea) =>
        left >= workArea.Left && left + width <= workArea.Right;
}
