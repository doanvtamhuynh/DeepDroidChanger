using DeepDroidChanger.Themes;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed class ThemeService : IThemeService
{
    private const string MaterialPaletteHelperTypeName =
        "MaterialDesignThemes.Wpf.PaletteHelper, MaterialDesignThemes.Wpf";
    private const string GetThemeMethodName = "GetTheme";
    private const string SetThemeMethodName = "SetTheme";
    private const string SetBaseThemeMethodName = "SetBaseTheme";
    private const string RecreateThemeDictionariesMethodName = "RecreateThemeDictionaries";

    private readonly ILogger<ThemeService> _logger;

    public ThemeService(ILogger<ThemeService> logger)
    {
        _logger = logger;
    }

    public string NormalizeTheme(string theme)
    {
        return string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : "Dark";
    }

    public string ToggleTheme(string theme)
    {
        return IsDarkTheme(theme) ? "Light" : "Dark";
    }

    public bool IsDarkTheme(string theme)
    {
        return NormalizeTheme(theme) == "Dark";
    }

    public void ApplyTheme(string theme)
    {
        bool isDark = IsDarkTheme(theme);
        _logger.LogDebug("Applying theme: {Theme} (isDark={IsDark})", theme, isDark);
        ThemeManager.Apply(isDark ? AppTheme.Dark : AppTheme.Light);
        ApplyMaterialDesignTheme(isDark);
    }

    private static void ApplyMaterialDesignTheme(bool isDark)
    {
        Type? paletteHelperType = Type.GetType(MaterialPaletteHelperTypeName);
        if (paletteHelperType == null)
            return;

        object? paletteHelper = Activator.CreateInstance(paletteHelperType);
        if (paletteHelper == null)
            return;

        var getTheme = paletteHelperType.GetMethod(GetThemeMethodName, Type.EmptyTypes);
        object? theme = getTheme?.Invoke(paletteHelper, null);
        if (theme == null)
            return;

        var setTheme = paletteHelperType.GetMethods()
            .FirstOrDefault(method =>
                method.Name == SetThemeMethodName
                && method.GetParameters().Length == 1
                && method.GetParameters()[0].ParameterType.IsInstanceOfType(theme));
        if (setTheme == null)
            return;

        var setBaseTheme = theme.GetType().GetMethod(SetBaseThemeMethodName);
        var baseThemeParameter = setBaseTheme?.GetParameters().FirstOrDefault();
        if (setBaseTheme == null || baseThemeParameter == null)
            return;

        object baseTheme = Enum.Parse(
            baseThemeParameter.ParameterType,
            isDark ? "Dark" : "Light");
        setBaseTheme.Invoke(theme, [baseTheme]);
        setTheme.Invoke(paletteHelper, [theme]);

        var recreate = paletteHelperType.GetMethod(RecreateThemeDictionariesMethodName, Type.EmptyTypes);
        recreate?.Invoke(recreate.IsStatic ? null : paletteHelper, null);
    }
}
