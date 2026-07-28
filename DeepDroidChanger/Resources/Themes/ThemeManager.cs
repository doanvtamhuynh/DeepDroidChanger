using System.Windows;
using DeepDroidChanger.Constants;

namespace DeepDroidChanger.Themes;

public enum AppTheme
{
    Light,
    Dark
}

public static class ThemeManager
{
    public static void Apply(AppTheme theme)
    {
        ResourceDictionary resources = Application.Current.Resources;

        ResourceDictionary? currentTheme = resources.MergedDictionaries
            .FirstOrDefault(dictionary => IsThemeDictionary(dictionary.Source));

        ResourceDictionary newTheme = new()
        {
            Source = new Uri(
                theme == AppTheme.Dark
                    ? AssetConstants.Themes.DarkDictionary
                    : AssetConstants.Themes.LightDictionary,
                UriKind.Relative)
        };

        if (currentTheme is null)
        {
            resources.MergedDictionaries.Insert(0, newTheme);
            return;
        }

        int currentIndex = resources.MergedDictionaries.IndexOf(currentTheme);
        resources.MergedDictionaries[currentIndex] = newTheme;
    }

    private static bool IsThemeDictionary(Uri? source)
    {
        if (source is null)
        {
            return false;
        }

        string path = source.OriginalString;

        return path.EndsWith(AssetConstants.Themes.LightFileName, StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(AssetConstants.Themes.DarkFileName, StringComparison.OrdinalIgnoreCase);
    }
}
