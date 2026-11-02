using DeepDroidChanger.Models;
using DeepDroidChanger.Helpers;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DeepDroidChanger.ViewModels;

public sealed partial class SettingsAppearanceViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomButtonSelection), nameof(ButtonBrush), nameof(HasCustomButtonColor), nameof(ButtonForeground), nameof(ButtonTextBrush), nameof(IsButtonColorInvalid))]
    private string _selectedButtonColor = "Theme";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonBrush), nameof(HasCustomButtonColor), nameof(ButtonForeground), nameof(ButtonTextBrush), nameof(IsButtonColorInvalid))]
    private string _customButtonHex = "#3B82F6";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomTextSelection), nameof(TextBrush), nameof(HasCustomTextColor), nameof(IsTextColorInvalid))]
    private string _selectedTextColor = "Theme";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextBrush), nameof(HasCustomTextColor), nameof(IsTextColorInvalid))]
    private string _customTextHex = "#F8FAFC";

    [ObservableProperty] private string _fontFamily = "Segoe UI Variable, Segoe UI";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SampleFontWeight), nameof(SampleFontStyle))]
    private string _typeStyle = "Regular";
    [ObservableProperty] private double _fontSize = 14;
    [ObservableProperty] private string _buttonVariant = "Filled";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonHeight))]
    private string _buttonSize = "Comfortable";

    public bool IsCustomButtonSelection => SelectedButtonColor == "Custom";
    public bool IsCustomTextSelection => SelectedTextColor == "Custom";
    public bool IsButtonColorInvalid => IsCustomButtonSelection && !AppearanceOptionsPolicy.TryParseColor(CustomButtonHex, out _);
    public bool IsTextColorInvalid => IsCustomTextSelection && !AppearanceOptionsPolicy.TryParseColor(CustomTextHex, out _);
    public SolidColorBrush? ButtonBrush => GetBrush(SelectedButtonColor, CustomButtonHex);
    public SolidColorBrush? TextBrush => GetReadableBrush(SelectedTextColor, CustomTextHex);
    public Brush? ButtonTextBrush => GetReadableBrush(SelectedButtonColor, CustomButtonHex);
    public bool HasCustomButtonColor => ButtonBrush is not null;
    public bool HasCustomTextColor => TextBrush is not null;
    public Brush ButtonForeground => ButtonBrush is { } brush
        ? AppearanceOptionsPolicy.ContrastingForeground(brush.Color) : Brushes.White;
    public FontWeight SampleFontWeight => AppearanceOptionsPolicy.GetFontWeight(TypeStyle);
    public FontStyle SampleFontStyle => AppearanceOptionsPolicy.GetFontStyle(TypeStyle);
    public double ButtonHeight => AppearanceOptionsPolicy.GetButtonHeight(ButtonSize);

    [RelayCommand]
    private void Reset() => Load(AppearanceOptionsPolicy.Defaults);

    public void Load(AppearanceOptions options) => Rebase(options, options, force: true);

    private static SolidColorBrush? GetBrush(string selection, string customHex)
    {
        if (AppearanceOptionsPolicy.ResolveColor(selection, customHex) is not { } color)
            return null;

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush? GetReadableBrush(string selection, string customHex)
    {
        SolidColorBrush? brush = GetBrush(selection, customHex);
        if (brush is null || Application.Current?.TryFindResource("Brush.SurfaceAlt") is not SolidColorBrush background)
            return brush;

        return new SolidColorBrush(AppearanceOptionsPolicy.ReadableText(brush.Color, background.Color));
    }

    public AppearanceOptions Capture() => new()
    {
        SelectedButtonColor = SelectedButtonColor,
        CustomButtonHex = CustomButtonHex,
        SelectedTextColor = SelectedTextColor,
        CustomTextHex = CustomTextHex,
        FontFamily = FontFamily,
        TypeStyle = TypeStyle,
        FontSize = FontSize,
        ButtonVariant = ButtonVariant,
        ButtonSize = ButtonSize
    };

    public void Rebase(AppearanceOptions previous, AppearanceOptions current, bool force = false)
    {
        if (force || SelectedButtonColor == previous.SelectedButtonColor) SelectedButtonColor = current.SelectedButtonColor;
        if (force || CustomButtonHex == previous.CustomButtonHex) CustomButtonHex = current.CustomButtonHex;
        if (force || SelectedTextColor == previous.SelectedTextColor) SelectedTextColor = current.SelectedTextColor;
        if (force || CustomTextHex == previous.CustomTextHex) CustomTextHex = current.CustomTextHex;
        if (force || FontFamily == previous.FontFamily) FontFamily = current.FontFamily;
        if (force || TypeStyle == previous.TypeStyle) TypeStyle = current.TypeStyle;
        if (force || FontSize == previous.FontSize) FontSize = current.FontSize;
        if (force || ButtonVariant == previous.ButtonVariant) ButtonVariant = current.ButtonVariant;
        if (force || ButtonSize == previous.ButtonSize) ButtonSize = current.ButtonSize;
        OnPropertyChanged(nameof(TextBrush));
        OnPropertyChanged(nameof(ButtonTextBrush));
    }
}
