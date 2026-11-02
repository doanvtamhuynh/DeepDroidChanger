using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed class ApplicationPreferencesService : IApplicationPreferencesService
{
    private readonly AppSettings _settings;
    private readonly IApplicationAppearanceService? _appearance;
    private readonly ICurrentAccountService? _account;
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themes;
    private readonly ILocalizationService _localization;
    private TaskCompletionSource? _saveCompletion;

    public ApplicationPreferencesService(
        AppSettings settings,
        ISettingsService settingsService,
        IThemeService themes,
        ILocalizationService localization,
        IApplicationAppearanceService? appearance = null,
        ICurrentAccountService? account = null)
    {
        _settings = settings;
        _appearance = appearance;
        _account = account;
        _settingsService = settingsService;
        _themes = themes;
        _localization = localization;
        Current = Normalize(ApplicationPreferences.FromSettings(settings));
        Current.ApplyTo(settings);
    }

    public ApplicationPreferences Current { get; private set; }
    public bool IsSaving { get; private set; }
    public event EventHandler? Changed;
    public Task WaitForSaveAsync() => _saveCompletion?.Task ?? Task.CompletedTask;

    public void Apply(ApplicationPreferences preferences)
    {
        if (IsSaving)
            return;

        ApplyCore(Normalize(preferences));
    }

    public async Task SaveAsync(ApplicationPreferences preferences, CancellationToken cancellationToken)
    {
        if (IsSaving)
            throw new InvalidOperationException("Application preferences are already being saved.");

        preferences = Normalize(preferences);
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _saveCompletion = completion;
        IsSaving = true;
        bool accountChanged = preferences.Options.RememberAccount != Current.Options.RememberAccount;
        bool oldRemember = Current.Options.RememberAccount;
        bool accountUpdated = false;
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
            if (accountChanged && _account is not null)
            {
                await _account.SetRememberAsync(preferences.Options.RememberAccount, cancellationToken).ConfigureAwait(true);
                accountUpdated = true;
            }
            await _settingsService.SavePreferencesAsync(_settings, preferences, cancellationToken)
                .ConfigureAwait(true);
            ApplyCore(preferences);
        }
        catch
        {
            if (accountUpdated && _account is not null)
                await _account.SetRememberAsync(oldRemember, CancellationToken.None).ConfigureAwait(true);
            throw;
        }
        finally
        {
            IsSaving = false;
            try
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                completion.TrySetResult();
            }
        }
    }

    private void ApplyCore(ApplicationPreferences preferences)
    {
        if (preferences == Current)
            return;

        if (preferences.Theme != Current.Theme)
            _themes.ApplyTheme(preferences.Theme);
        if (preferences.Language != Current.Language)
            _localization.ApplyLanguage(preferences.Language);

        _appearance?.Apply(preferences.Options.Appearance);
        Current = preferences;
        preferences.ApplyTo(_settings);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private ApplicationPreferences Normalize(ApplicationPreferences preferences) => new(
        _themes.NormalizeTheme(preferences.Theme),
        _localization.NormalizeLanguage(preferences.Language),
        preferences.SidebarCollapsed) { Options = SettingsOptionsPolicy.Normalize(preferences.Options) };

}
