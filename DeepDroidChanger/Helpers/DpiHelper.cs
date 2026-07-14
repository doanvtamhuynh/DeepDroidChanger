using System.Windows;
using System.Windows.Media;

namespace DeepDroidChanger.Helpers
{
    internal static class DpiHelper
    {
        /// <summary>
        /// Gets the DPI scale of the visual. Returns 1.0 if unavailable (safe fallback).
        /// </summary>
        public static (double ScaleX, double ScaleY) GetDpiScale(Visual visual)
        {
            var source = PresentationSource.FromVisual(visual);
            if (source?.CompositionTarget == null)
                return (1.0, 1.0);

            var m = source.CompositionTarget.TransformToDevice;
            return (m.M11, m.M22);
        }

        /// <summary>
        /// Converts WPF DIP value to physical pixels.
        /// </summary>
        public static int ToPhysicalPixels(double dipValue, double dpiScale)
            => Math.Max(0, (int)Math.Round(dipValue * dpiScale));
    }
}
