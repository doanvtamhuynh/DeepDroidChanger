namespace DeepDroidChanger.Services
{
    public interface ILocalizationService
    {
        string NormalizeLanguage(string languageCode);
        string GetString(string resourceKey);
        void ApplyLanguage(string languageCode);
    }
}
