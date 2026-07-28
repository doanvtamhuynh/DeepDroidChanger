using DeepDroidChanger.Constants;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class LocalizationService : ILocalizationService
    {
        private readonly ILogger<LocalizationService> _logger;

        public LocalizationService(ILogger<LocalizationService> logger)
        {
            _logger = logger;
        }

        public string NormalizeLanguage(string language)
        {
            return string.Equals(language, "vi", StringComparison.OrdinalIgnoreCase)
                ? "vi"
                : "en";
        }

        public string GetString(string resourceKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
            return Application.Current?.TryFindResource(resourceKey) as string ?? resourceKey;
        }

        public void ApplyLanguage(string language)
        {
            var normalizedLanguage = NormalizeLanguage(language);
            _logger.LogDebug("Applying language: {Language}", normalizedLanguage);

            EnsureBaseDictionary();
            RemoveVietnameseDictionary();

            if (normalizedLanguage == "vi")
            {
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(AssetConstants.Localization.VietnameseStrings, UriKind.Relative)
                });
            }
        }

        private static void EnsureBaseDictionary()
        {
            if (Application.Current.Resources.MergedDictionaries.Any(IsBaseDictionary))
                return;

            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(AssetConstants.Localization.BaseStrings, UriKind.Relative)
            });
        }

        private static void RemoveVietnameseDictionary()
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries
                .Where(IsVietnameseDictionary)
                .ToList();

            foreach (var dictionary in dictionaries)
                Application.Current.Resources.MergedDictionaries.Remove(dictionary);
        }

        private static bool IsBaseDictionary(ResourceDictionary dictionary)
        {
            return IsDictionary(dictionary, AssetConstants.Localization.BaseStrings);
        }

        private static bool IsVietnameseDictionary(ResourceDictionary dictionary)
        {
            return IsDictionary(dictionary, AssetConstants.Localization.VietnameseStrings);
        }

        private static bool IsDictionary(ResourceDictionary dictionary, string source)
        {
            return dictionary.Source?.OriginalString.Replace('\\', '/') == source;
        }
    }
}
