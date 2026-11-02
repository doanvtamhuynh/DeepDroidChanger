using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Helpers;

// Shared by persisted preferences, the Settings sample and application resources.
internal static class AppearanceOptionsPolicy
{
    public static AppearanceOptions Defaults { get; } = new();

    public static AppearanceOptions Normalize(AppearanceOptions? options)
    {
        options ??= Defaults;
        return options with
        {
            SelectedButtonColor = NormalizeColorChoice(options.SelectedButtonColor),
            SelectedTextColor = NormalizeColorChoice(options.SelectedTextColor),
            CustomButtonHex = TryParseColor(options.CustomButtonHex, out _) ? options.CustomButtonHex : Defaults.CustomButtonHex,
            CustomTextHex = TryParseColor(options.CustomTextHex, out _) ? options.CustomTextHex : Defaults.CustomTextHex,
            FontFamily = Choice(options.FontFamily, Defaults.FontFamily, Defaults.FontFamily, "Segoe UI", "Arial", "Tahoma", "Consolas"),
            TypeStyle = Choice(options.TypeStyle, Defaults.TypeStyle, "Regular", "Semibold", "Bold", "Italic", "BoldItalic"),
            FontSize = double.IsFinite(options.FontSize) ? Math.Clamp(options.FontSize, 12, 22) : Defaults.FontSize,
            ButtonVariant = Choice(options.ButtonVariant, Defaults.ButtonVariant, "Filled", "Outlined", "Soft"),
            ButtonSize = Choice(options.ButtonSize, Defaults.ButtonSize, "Compact", "Comfortable", "Large")
        };
    }

    public static bool IsValid(AppearanceOptions options) =>
        (options.SelectedButtonColor != "Custom" || TryParseColor(options.CustomButtonHex, out _))
        && (options.SelectedTextColor != "Custom" || TryParseColor(options.CustomTextHex, out _));

    public static Color? ResolveColor(string selection, string customHex)
    {
        string? hex = selection switch
        {
            "Blue" => "#3B82F6",
            "Purple" => "#8B5CF6",
            "Teal" => "#14B8A6",
            "Orange" => "#F97316",
            "Rose" => "#EC4899",
            "Custom" => customHex,
            _ => null
        };
        return TryParseColor(hex, out Color color) ? color : null;
    }

    public static bool TryParseColor(string? value, out Color color)
    {
        color = default;
        if (value is not { Length: 7 } || value[0] != '#'
            || !uint.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb))
            return false;

        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    public static Brush ContrastingForeground(Color color) =>
        Luminance(color) > .179 ? Brushes.Black : Brushes.White;

    public static Color ReadableText(Color color, params Color[] backgrounds)
    {
        if (backgrounds.Length == 0)
            return color;

        double[] luminances = backgrounds.Select(Luminance).ToArray();
        double MinimumContrast(Color candidate)
        {
            double luminance = Luminance(candidate);
            return luminances.Min(background =>
                (Math.Max(luminance, background) + .05) / (Math.Min(luminance, background) + .05));
        }

        if (MinimumContrast(color) >= 4.5)
            return color;

        Color target = MinimumContrast(Colors.Black) > MinimumContrast(Colors.White) ? Colors.Black : Colors.White;
        for (int step = 1; step <= 32; step++)
        {
            Color candidate = Blend(color, target, step / 32d);
            if (MinimumContrast(candidate) >= 4.5)
                return candidate;
        }
        return target;
    }

    public static Color Blend(Color color, Color target, double amount) => Color.FromRgb(
        (byte)(color.R + (target.R - color.R) * amount),
        (byte)(color.G + (target.G - color.G) * amount),
        (byte)(color.B + (target.B - color.B) * amount));

    public static FontWeight GetFontWeight(string style) => style switch
    {
        "Semibold" => FontWeights.SemiBold,
        "Bold" or "BoldItalic" => FontWeights.Bold,
        _ => FontWeights.Normal
    };

    public static FontStyle GetFontStyle(string style) =>
        style is "Italic" or "BoldItalic" ? FontStyles.Italic : FontStyles.Normal;

    public static double GetButtonHeight(string size) => size switch
    {
        "Compact" => 32,
        "Large" => 48,
        _ => 40
    };

    private static string NormalizeColorChoice(string? value) =>
        Choice(value, "Theme", "Theme", "Blue", "Purple", "Teal", "Orange", "Rose", "Custom");

    private static string Choice(string? value, string fallback, params string[] choices) =>
        choices.Contains(value, StringComparer.Ordinal) ? value! : fallback;

    private static double Luminance(Color color) =>
        .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);

    private static double Linear(byte channel)
    {
        double value = channel / 255d;
        return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
    }
}
