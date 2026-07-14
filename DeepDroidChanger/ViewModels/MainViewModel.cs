using DeepDroidChanger.Services;
using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;

namespace DeepDroidChanger.ViewModels
{
    public sealed partial class MainViewModel : ObservableObject
    {
        private const double ExpandedWidth = 248;
        private const double CollapsedWidth = 56;

        private const string EnglishFlag = "pack://application:,,,/Assets/Icons/flag_en.ico";
        private const string VietnameseFlag = "pack://application:,,,/Assets/Icons/flag_vn.ico";

        private readonly AppSettings _settings;
        private readonly ILocalizationService _localizationService;
        private readonly IThemeService _themeService;
        private readonly ISettingsService _settingsService;
        private bool _isSidebarCollapsed;
        private AppView _activeView;

        public MainViewModel(
            AppSettings settings,
            ILocalizationService localizationService,
            IThemeService themeService,
            ISettingsService settingsService)
        {
            _settings = settings;
            _localizationService = localizationService;
            _themeService = themeService;
            _settingsService = settingsService;

            Language = _localizationService.NormalizeLanguage(_settings.Language ?? LanguageConstants.English);
            Theme = _themeService.NormalizeTheme(_settings.Theme ?? ThemeConstants.Dark);
            _isSidebarCollapsed = _settings.SidebarCollapsed;
            _activeView = AppView.DeviceManager;

            _localizationService.ApplyLanguage(Language);
            _themeService.ApplyTheme(Theme);
        }

        public event Action<AppView>? NavigationRequested;

        public string Language
        {
            get => _settings.Language;
            private set
            {
                if (_settings.Language == value)
                    return;

                _settings.Language = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LanguageFlagSource));
            }
        }

        public string Theme
        {
            get => _settings.Theme;
            private set
            {
                if (_settings.Theme == value)
                    return;

                _settings.Theme = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ThemeIconKind));
            }
        }

        public bool IsSidebarCollapsed
        {
            get => _isSidebarCollapsed;
            set
            {
                if (!SetProperty(ref _isSidebarCollapsed, value))
                    return;

                _settings.SidebarCollapsed = value;
                OnSidebarLayoutChanged();
            }
        }

        public AppView ActiveView
        {
            get => _activeView;
            private set
            {
                if (!SetProperty(ref _activeView, value))
                    return;

                OnPropertyChanged(nameof(IsDeviceManagerActive));
                OnPropertyChanged(nameof(IsSettingsActive));
                NavigationRequested?.Invoke(value);
            }
        }

        public bool IsDeviceManagerActive => ActiveView == AppView.DeviceManager;
        public bool IsSettingsActive => ActiveView == AppView.Settings;
        public GridLength SidebarWidth => new(IsSidebarCollapsed ? CollapsedWidth : ExpandedWidth);
        public Thickness HeaderMargin => IsSidebarCollapsed ? new Thickness(0, 16, 0, 8) : new Thickness(14, 16, 10, 8);
        public int HeaderToggleColumn => IsSidebarCollapsed ? 0 : 1;
        public int HeaderToggleColumnSpan => IsSidebarCollapsed ? 2 : 1;
        public HorizontalAlignment ToggleHorizontalAlignment => IsSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Right;
        public HorizontalAlignment LogoHorizontalAlignment => IsSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        public Thickness LogoMargin => IsSidebarCollapsed ? new Thickness(0, 8, 0, 0) : new Thickness(0);
        public int LogoRow => IsSidebarCollapsed ? 1 : 0;
        public int LogoColumnSpan => IsSidebarCollapsed ? 2 : 1;
        public Visibility LogoTextVisibility => IsSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        public Visibility NavLabelVisibility => IsSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        public Thickness BottomActionsPadding => IsSidebarCollapsed ? new Thickness(8, 8, 8, 8) : new Thickness(12, 8, 12, 8);
        public HorizontalAlignment VersionHorizontalAlignment => IsSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        public Visibility VersionTextVisibility => IsSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        public Thickness VersionValueMargin => IsSidebarCollapsed ? new Thickness(0) : new Thickness(3, 0, 0, 0);
        public Orientation BottomActionsOrientation => IsSidebarCollapsed ? Orientation.Vertical : Orientation.Horizontal;
        public HorizontalAlignment BottomActionsHorizontalAlignment => IsSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        public Thickness ThemeButtonMargin => IsSidebarCollapsed ? new Thickness(0, 6, 0, 0) : new Thickness(12, 0, 0, 0);
        public PackIconKind ToggleIconKind => IsSidebarCollapsed ? PackIconKind.ChevronRight : PackIconKind.ChevronLeft;
        public PackIconKind ThemeIconKind => _themeService.IsDarkTheme(Theme) ? PackIconKind.WeatherNight : PackIconKind.WhiteBalanceSunny;
        public string LanguageFlagSource => Language == LanguageConstants.Vietnamese ? VietnameseFlag : EnglishFlag;

        public void NavigateInitialView()
        {
            NavigationRequested?.Invoke(ActiveView);
        }

        public async Task SaveSettingsAsync(CancellationToken cancellationToken)
        {
            await _settingsService.SaveAsync(_settings, cancellationToken).ConfigureAwait(false);
        }

        [RelayCommand]
        private void ToggleSidebar()
        {
            IsSidebarCollapsed = !IsSidebarCollapsed;
        }

        [RelayCommand]
        private void ToggleLanguage()
        {
            Language = Language == LanguageConstants.Vietnamese ? LanguageConstants.English : LanguageConstants.Vietnamese;
            _localizationService.ApplyLanguage(Language);
        }

        [RelayCommand]
        private void ToggleTheme()
        {
            Theme = _themeService.ToggleTheme(Theme);
            _themeService.ApplyTheme(Theme);
        }

        [RelayCommand]
        private void NavigateDeviceManager()
        {
            ActiveView = AppView.DeviceManager;
        }

        [RelayCommand]
        private void NavigateSettings()
        {
            ActiveView = AppView.Settings;
        }

        private void OnSidebarLayoutChanged()
        {
            OnPropertyChanged(nameof(SidebarWidth));
            OnPropertyChanged(nameof(HeaderMargin));
            OnPropertyChanged(nameof(HeaderToggleColumn));
            OnPropertyChanged(nameof(HeaderToggleColumnSpan));
            OnPropertyChanged(nameof(ToggleHorizontalAlignment));
            OnPropertyChanged(nameof(LogoHorizontalAlignment));
            OnPropertyChanged(nameof(LogoMargin));
            OnPropertyChanged(nameof(LogoRow));
            OnPropertyChanged(nameof(LogoColumnSpan));
            OnPropertyChanged(nameof(LogoTextVisibility));
            OnPropertyChanged(nameof(NavLabelVisibility));
            OnPropertyChanged(nameof(BottomActionsPadding));
            OnPropertyChanged(nameof(VersionHorizontalAlignment));
            OnPropertyChanged(nameof(VersionTextVisibility));
            OnPropertyChanged(nameof(VersionValueMargin));
            OnPropertyChanged(nameof(BottomActionsOrientation));
            OnPropertyChanged(nameof(BottomActionsHorizontalAlignment));
            OnPropertyChanged(nameof(ThemeButtonMargin));
            OnPropertyChanged(nameof(ToggleIconKind));
        }
    }
}
