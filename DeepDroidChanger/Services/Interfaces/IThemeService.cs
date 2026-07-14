namespace DeepDroidChanger.Services
{
    public interface IThemeService
    {
        string NormalizeTheme(string theme);
        string ToggleTheme(string theme);
        bool IsDarkTheme(string theme);
        void ApplyTheme(string theme);
    }
}
