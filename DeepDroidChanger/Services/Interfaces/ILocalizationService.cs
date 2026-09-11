namespace DeepDroidChanger.Services
{
    public interface ILocalizationService
    {
        event EventHandler? LanguageChanged;

        string NormalizeLanguage(string languageCode);
        string GetString(string resourceKey);
        void ApplyLanguage(string languageCode);
    }
}
