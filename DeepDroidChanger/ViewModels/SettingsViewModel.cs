using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDroidChanger.Models;
using DeepDroidChanger.Authentication;
using DeepDroidChanger.Helpers;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IApplicationPreferencesService _preferences;
    private readonly ILocalizationService _localization;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IAuthenticationSessionService? _authenticationSession;
    private readonly IFilePickerDialogService? _picker;
    private readonly ICurrentAccountService? _account;
    private readonly IConfirmationDialogService? _confirmation;
    private bool _accountBusy;
    private ApplicationPreferences _applied;
    private bool _synchronizing;
    private string? _statusResourceKey;

    [ObservableProperty]
    private string _selectedTheme;

    [ObservableProperty]
    private string _selectedLanguage;

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    [ObservableProperty]
    private string _selectedSection = "Appearance";

    public SettingsDraftViewModel Draft { get; } = new();
    public string AccountUsername => _authenticationSession?.CurrentSession?.Username
        ?? _localization.GetString("Settings_NotSignedIn");
    public string AccountName => string.IsNullOrWhiteSpace(Draft.AccountDisplayName) ? AccountUsername : Draft.AccountDisplayName;
    public bool HasValidationError => !SettingsOptionsPolicy.IsValid(Draft.Capture());

    public SettingsViewModel(
        IApplicationPreferencesService preferences,
        ILocalizationService localization,
        ILogger<SettingsViewModel> logger,
        IAuthenticationSessionService? authenticationSession = null,
        IFilePickerDialogService? picker = null,
        ICurrentAccountService? account = null,
        IConfirmationDialogService? confirmation = null)
    {
        _preferences = preferences;
        _localization = localization;
        _logger = logger;
        _authenticationSession = authenticationSession;
        _picker = picker;
        _account = account;
        _confirmation = confirmation;
        _applied = preferences.Current;
        _selectedTheme = _applied.Theme;
        _selectedLanguage = _applied.Language;
        _isSidebarCollapsed = _applied.SidebarCollapsed;
        Draft.Load(_applied.Options);
        Draft.PropertyChanged += OnOptionsChanged;
        Draft.Appearance.PropertyChanged += OnOptionsChanged;
        _preferences.Changed += OnPreferencesChanged;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public bool HasChanges => SelectedTheme != _applied.Theme
        || SelectedLanguage != _applied.Language
        || IsSidebarCollapsed != _applied.SidebarCollapsed
        || Draft.Capture() != _applied.Options;
    public bool IsSaving => _preferences.IsSaving;
    public bool CanEdit => !IsSaving && !_accountBusy;
    public bool IsDarkThemeSelected
    {
        get => SelectedTheme == "Dark";
        set { if (value) SelectedTheme = "Dark"; }
    }
    public bool IsLightThemeSelected
    {
        get => SelectedTheme == "Light";
        set { if (value) SelectedTheme = "Light"; }
    }
    public bool HasError => HasValidationError || _statusResourceKey is "Settings_SaveFailed" or "Settings_SignOutBlocked";
    public string StatusText
    {
        get
        {
            string resourceKey;
            if (IsSaving)
                resourceKey = "Settings_Saving";
            else if (HasValidationError)
                resourceKey = "Settings_InvalidOptions";
            else
                resourceKey = _statusResourceKey ?? (HasChanges ? "Settings_Unsaved" : "Settings_UpToDate");

            return _localization.GetString(resourceKey);
        }
    }

    partial void OnSelectedThemeChanged(string value)
    {
        OnPropertyChanged(nameof(IsDarkThemeSelected));
        OnPropertyChanged(nameof(IsLightThemeSelected));
        OnDraftChanged();
    }
    partial void OnSelectedLanguageChanged(string value) => OnDraftChanged();
    partial void OnIsSidebarCollapsedChanged(bool value) => OnDraftChanged();

    partial void OnSelectedSectionChanged(string value)
    {
        OnPropertyChanged(nameof(AccountUsername));
        RefreshState();
    }

    private bool CanSave() => HasChanges && CanEdit && !HasValidationError;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        // Merge only edited fields with the current applied preferences.
        ApplicationPreferences current = _preferences.Current;
        var requested = new ApplicationPreferences(
            SelectedTheme != _applied.Theme ? SelectedTheme : current.Theme,
            SelectedLanguage != _applied.Language ? SelectedLanguage : current.Language,
            IsSidebarCollapsed != _applied.SidebarCollapsed ? IsSidebarCollapsed : current.SidebarCollapsed)
        { Options = Draft.Capture() };
        _statusResourceKey = null;
        try
        {
            await _preferences.SaveAsync(requested, cancellationToken).ConfigureAwait(true);
            ResetDraft();
            _statusResourceKey = "Settings_Saved";
        }
        catch (OperationCanceledException)
        {
            _statusResourceKey = "Settings_SaveCanceled";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to save application preferences.");
            _statusResourceKey = "Settings_SaveFailed";
        }
        RefreshState();
    }

    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private void DiscardChanges() => ResetDraft();

    private bool CanDiscard() => HasChanges && CanEdit;

    private void ResetDraft() => SynchronizeDraft(discardChanges: true);

    private void OnPreferencesChanged(object? sender, EventArgs e) => SynchronizeDraft();

    private void SynchronizeDraft(bool discardChanges = false)
    {
        ApplicationPreferences current = _preferences.Current;
        _synchronizing = true;
        try
        {
            Draft.Rebase(_applied.Options, current.Options, discardChanges);
            if (discardChanges || SelectedTheme == _applied.Theme) SelectedTheme = current.Theme;
            if (discardChanges || SelectedLanguage == _applied.Language) SelectedLanguage = current.Language;
            if (discardChanges || IsSidebarCollapsed == _applied.SidebarCollapsed) IsSidebarCollapsed = current.SidebarCollapsed;
            _applied = current;
            _statusResourceKey = null;
        }
        finally
        {
            _synchronizing = false;
        }
        RefreshState();
    }

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e) => OnDraftChanged();

    private void OnDraftChanged()
    {
        if (_synchronizing)
            return;
        _statusResourceKey = null;
        RefreshState();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(AccountUsername));
        OnPropertyChanged(nameof(AccountName));
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(HasValidationError));
        OnPropertyChanged(nameof(AccountName));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(IsSaving));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(StatusText));
        SaveCommand.NotifyCanExecuteChanged();
        DiscardChangesCommand.NotifyCanExecuteChanged();
        BrowseFolderCommand.NotifyCanExecuteChanged();
        ForgetAccountCommand.NotifyCanExecuteChanged();
        SignOutCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void BrowseFolder(string target)
    {
        string? path = _picker?.ShowOpenFolderDialog(_localization.GetString("Settings_SelectFolder"));
        if (string.IsNullOrWhiteSpace(path)) return;
        if (target == "Screenshot")
        {
            Draft.ScreenshotFolder = path;
            Draft.ScreenshotDestination = "Custom";
        }
        else if (target == "Export")
        {
            Draft.ExportFolder = path;
            Draft.ExportDestination = "Custom";
        }
        else
        {
            Draft.BackupFolder = path;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ForgetAccountAsync(CancellationToken token)
    {
        SetAccountBusy(true);
        try
        {
            // Save only the applied account option; pending edits remain drafts.
            await _preferences.SaveAsync(_preferences.Current with
            { Options = _preferences.Current.Options with { RememberAccount = false } }, token);
            // Clear even when the applied flag was already false.
            if (_account is not null) await _account.SetRememberAsync(false, token);
            Draft.RememberAccount = false;
            _statusResourceKey = "Settings_AccountForgotten";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clear saved account.");
            _statusResourceKey = "Settings_SaveFailed";
        }
        finally
        {
            SetAccountBusy(false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SignOutAsync()
    {
        if (HasChanges && _confirmation is not null && !await _confirmation.ShowConfirmationAsync(new ConfirmationDialogOptions
        {
            Caption = _localization.GetString("Settings_SignOut"),
            Message = _localization.GetString("Settings_SignOutConfirm"),
            ConfirmButtonText = _localization.GetString("Settings_SignOut"),
            IsDestructive = true
        })) return;
        SetAccountBusy(true);
        try
        {
            if (_account is null || !await _account.SignOutAsync()) _statusResourceKey = "Settings_SignOutBlocked";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not sign out.");
            _statusResourceKey = "Settings_SaveFailed";
        }
        finally
        {
            SetAccountBusy(false);
        }
    }

    private void SetAccountBusy(bool value)
    {
        _accountBusy = value;
        RefreshState();
    }

    public void Dispose()
    {
        Draft.PropertyChanged -= OnOptionsChanged;
        Draft.Appearance.PropertyChanged -= OnOptionsChanged;
        _preferences.Changed -= OnPreferencesChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
    }
}
