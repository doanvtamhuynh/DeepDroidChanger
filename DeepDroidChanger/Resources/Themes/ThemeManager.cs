using System.Windows;

namespace DeepDroidChanger.Themes;

public enum AppTheme
{
    Light,
    Dark
}

public static class ThemeManager
{
    private const string LightThemePath = "/DeepDroidChanger;component/Resources/Themes/Theme.Light.xaml";
    private const string DarkThemePath = "/DeepDroidChanger;component/Resources/Themes/Theme.Dark.xaml";

    public static void Apply(AppTheme theme)
    {
        ResourceDictionary resources = Application.Current.Resources;

        ResourceDictionary? currentTheme = resources.MergedDictionaries
            .FirstOrDefault(dictionary => IsThemeDictionary(dictionary.Source));

        ResourceDictionary newTheme = new()
        {
            Source = new Uri(
                theme == AppTheme.Dark ? DarkThemePath : LightThemePath,
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

        return path.EndsWith("Theme.Light.xaml", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("Theme.Dark.xaml", StringComparison.OrdinalIgnoreCase);
    }
}
