using System.Windows;
using System.Windows.Media;
using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed class ApplicationAppearanceService : IApplicationAppearanceService, IDisposable
{
    private static readonly (string Name, double Size)[] FontSizes =
    [
        ("Micro", 11), ("Small", 12), ("Body", 14), ("Subtitle", 16),
        ("SectionTitle", 18), ("Title", 20), ("Display", 24)
    ];

    private readonly ResourceDictionary _overrides = new();

    public void Apply(AppearanceOptions options)
    {
        if (Application.Current is not { } app)
            return;

        // Read base theme resources without the previous custom overrides.
        ClearOverrides(app);
        ApplyColors(app, options);
        ApplyTypography(options);
        app.Resources.MergedDictionaries.Add(_overrides);
    }

    private void ApplyColors(Application app, AppearanceOptions options)
    {
        Color[] surfaces = [ReadColor(app, "Brush.Surface"), ReadColor(app, "Brush.SurfaceAlt")];
        Color? customAccent = AppearanceOptionsPolicy.ResolveColor(options.SelectedButtonColor, options.CustomButtonHex);
        Color accent = customAccent ?? ReadColor(app, "Brush.Accent");
        Color readableAccent = AppearanceOptionsPolicy.ReadableText(accent, surfaces);
        Color hover = customAccent is null ? ReadColor(app, "Brush.AccentHover")
            : AppearanceOptionsPolicy.Blend(accent, Colors.White, .15);
        Color pressed = customAccent is null ? ReadColor(app, "Brush.AccentPressed")
            : AppearanceOptionsPolicy.Blend(accent, Colors.Black, .16);

        // Primary buttons keep the chosen fill; accent text/icons need contrast on surfaces.
        SetBrush("Brush.Accent", readableAccent);
        SetBrush("Brush.AccentHover", hover);
        SetBrush("Brush.AccentPressed", pressed);
        _overrides["Brush.AccentForeground"] = AppearanceOptionsPolicy.ContrastingForeground(readableAccent);
        _overrides["Brush.AccentHoverForeground"] = AppearanceOptionsPolicy.ContrastingForeground(hover);
        _overrides["Brush.AccentPressedForeground"] = AppearanceOptionsPolicy.ContrastingForeground(pressed);
        _overrides["Brush.PrimaryButtonBackground"] = options.ButtonVariant switch
        {
            "Outlined" => Brushes.Transparent,
            "Soft" => new SolidColorBrush(Color.FromArgb(35, accent.R, accent.G, accent.B)),
            _ => new SolidColorBrush(accent)
        };
        _overrides["Brush.PrimaryButtonForeground"] = options.ButtonVariant == "Filled"
            ? AppearanceOptionsPolicy.ContrastingForeground(accent)
            : new SolidColorBrush(readableAccent);

        if (AppearanceOptionsPolicy.ResolveColor(options.SelectedTextColor, options.CustomTextHex) is { } text)
            SetBrush("Brush.TextPrimary", AppearanceOptionsPolicy.ReadableText(text,
                ReadColor(app, "Brush.WindowBackground"), surfaces[0], surfaces[1]));
    }

    private void ApplyTypography(AppearanceOptions options)
    {
        _overrides["Metric.FontFamily"] = new FontFamily(options.FontFamily);
        foreach ((string name, double size) in FontSizes)
            _overrides[$"Metric.FontSize.{name}"] = size * options.FontSize / AppearanceOptionsPolicy.Defaults.FontSize;

        _overrides["Metric.FontWeight"] = AppearanceOptionsPolicy.GetFontWeight(options.TypeStyle);
        _overrides["Metric.FontStyle"] = AppearanceOptionsPolicy.GetFontStyle(options.TypeStyle);
        _overrides["Metric.ButtonHeight"] = Math.Max(AppearanceOptionsPolicy.GetButtonHeight(options.ButtonSize), options.FontSize + 18);
        _overrides["Metric.ControlHeight"] = Math.Max(38, options.FontSize + 22);
        _overrides["Metric.ControlHeight.Comfortable"] = Math.Max(40, options.FontSize + 22);
    }

    private static Color ReadColor(Application app, string key) => ((SolidColorBrush)app.FindResource(key)).Color;

    private void SetBrush(string key, Color color) => _overrides[key] = new SolidColorBrush(color);

    private void ClearOverrides(Application? app)
    {
        app?.Resources.MergedDictionaries.Remove(_overrides);
        _overrides.Clear();
    }

    public void Dispose() => ClearOverrides(Application.Current);
}
