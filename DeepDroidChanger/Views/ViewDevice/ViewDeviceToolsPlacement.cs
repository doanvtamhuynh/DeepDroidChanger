using System.Windows;

namespace DeepDroidChanger.Views;

internal static class ViewDeviceToolsPlacement
{
    internal static Point Calculate(
        Rect ownerBounds,
        Size toolsSize,
        Rect workArea,
        double visibleGap,
        double shadowMargin = 0)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
            return new Point(ownerBounds.Right + Math.Max(0, visibleGap - shadowMargin), ownerBounds.Top);

        double width = Math.Max(0, toolsSize.Width);
        double height = Math.Max(0, toolsSize.Height);
        double gap = Math.Max(0, visibleGap - shadowMargin);
        double rightCandidate = ownerBounds.Right + gap;
        double leftCandidate = ownerBounds.Left - width - gap;
        double maximumLeft = Math.Max(workArea.Left, workArea.Right - width);
        double maximumTop = Math.Max(workArea.Top, workArea.Bottom - height);

        double left = rightCandidate + width <= workArea.Right
            ? rightCandidate
            : leftCandidate >= workArea.Left
                ? leftCandidate
                : Math.Clamp(rightCandidate, workArea.Left, maximumLeft);
        double top = Math.Clamp(ownerBounds.Top, workArea.Top, maximumTop);

        return new Point(left, top);
    }
}
