using DeepDroidChanger.Models;
namespace DeepDroidChanger.Services
{
    public interface ISettingsService
    {
        Task<AppSettings> LoadAsync(CancellationToken cancellationToken);
        Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
        Task SavePreferencesAsync(
            AppSettings settings,
            ApplicationPreferences preferences,
            CancellationToken cancellationToken);
    }
}
