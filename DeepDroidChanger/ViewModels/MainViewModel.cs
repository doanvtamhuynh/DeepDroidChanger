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

        private readonly AppSettings _settings;
        private readonly ILocalizationService _localizationService;
        private readonly IThemeService _themeService;
        private readonly ISettingsService _settingsService;
        private readonly IDeviceActionCoordinatorService? _deviceActionCoordinatorService;
        private readonly IConfirmationDialogService? _confirmationDialogService;
        private bool _isSidebarCollapsed;
        private bool _isDevicesManagerSubmenuOpen;
        private bool _isDevicesManagerFlyoutOpen;
        private AppView _activeView;

        [ObservableProperty]
        private bool _isShutdownInProgress;

        public MainViewModel(
            AppSettings settings,
            ILocalizationService localizationService,
            IThemeService themeService,
            ISettingsService settingsService,
            IDeviceActionCoordinatorService? deviceActionCoordinatorService = null,
            IConfirmationDialogService? confirmationDialogService = null)
        {
            _settings = settings;
            _localizationService = localizationService;
            _themeService = themeService;
            _settingsService = settingsService;
            _deviceActionCoordinatorService = deviceActionCoordinatorService;
            _confirmationDialogService = confirmationDialogService;

            Language = _localizationService.NormalizeLanguage(_settings.Language ?? "en");
            Theme = _themeService.NormalizeTheme(_settings.Theme ?? "Dark");
            _isSidebarCollapsed = _settings.SidebarCollapsed;
            _activeView = AppView.ChangeSingleDevice;

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

                SynchronizeDevicesManagerMenuState();
                _settings.SidebarCollapsed = value;
                OnSidebarLayoutChanged();
            }
        }

        public bool IsDevicesManagerSubmenuOpen
        {
            get => _isDevicesManagerSubmenuOpen;
            private set
            {
                if (!SetProperty(ref _isDevicesManagerSubmenuOpen, value))
                    return;

                OnPropertyChanged(nameof(DevicesManagerChevronIconKind));
            }
        }

        public bool IsDevicesManagerFlyoutOpen
        {
            get => _isDevicesManagerFlyoutOpen;
            set => SetProperty(ref _isDevicesManagerFlyoutOpen, value);
        }

        public AppView ActiveView
        {
            get => _activeView;
            private set
            {
                if (!SetProperty(ref _activeView, value))
                    return;

                OnPropertyChanged(nameof(IsDevicesManagerActive));
                OnPropertyChanged(nameof(IsChangeSingleDeviceActive));
                OnPropertyChanged(nameof(IsChangeMultipleDevicesActive));
                OnPropertyChanged(nameof(IsSettingsActive));
                OnPropertyChanged(nameof(DevicesManagerIconKind));
                SynchronizeDevicesManagerMenuState();
                NavigationRequested?.Invoke(value);
            }
        }

        public bool IsDevicesManagerActive =>
            ActiveView is AppView.ChangeSingleDevice or AppView.ChangeMultipleDevices;
        public bool IsChangeSingleDeviceActive => ActiveView == AppView.ChangeSingleDevice;
        public bool IsChangeMultipleDevicesActive => ActiveView == AppView.ChangeMultipleDevices;
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
        public PackIconKind DevicesManagerIconKind => IsSidebarCollapsed
            ? ActiveView switch
            {
                AppView.ChangeSingleDevice => PackIconKind.Cellphone,
                AppView.ChangeMultipleDevices => PackIconKind.ViewGrid,
                _ => PackIconKind.CellphoneLink
            }
            : PackIconKind.CellphoneLink;
        public PackIconKind DevicesManagerChevronIconKind =>
            IsDevicesManagerSubmenuOpen ? PackIconKind.ChevronUp : PackIconKind.ChevronDown;
        public PackIconKind ThemeIconKind => _themeService.IsDarkTheme(Theme) ? PackIconKind.WeatherNight : PackIconKind.WhiteBalanceSunny;
        public string LanguageFlagSource => Language == "vi"
            ? AssetConstants.Icons.VietnameseFlag
            : AssetConstants.Icons.EnglishFlag;

        public void NavigateInitialView()
        {
            SynchronizeDevicesManagerMenuState();
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
            Language = Language == "vi" ? "en" : "vi";
            _localizationService.ApplyLanguage(Language);
        }

        [RelayCommand]
        private void ToggleTheme()
        {
            Theme = _themeService.ToggleTheme(Theme);
            _themeService.ApplyTheme(Theme);
        }

        [RelayCommand]
        private void ToggleDevicesManagerMenu()
        {
            if (IsSidebarCollapsed)
            {
                IsDevicesManagerSubmenuOpen = false;
                IsDevicesManagerFlyoutOpen = !IsDevicesManagerFlyoutOpen;
                return;
            }

            IsDevicesManagerFlyoutOpen = false;
            IsDevicesManagerSubmenuOpen = !IsDevicesManagerSubmenuOpen;
        }

        [RelayCommand]
        private void NavigateChangeSingleDevice()
        {
            ActiveView = AppView.ChangeSingleDevice;
            CloseFlyoutAfterNavigation();
        }

        [RelayCommand]
        private void NavigateChangeMultipleDevices()
        {
            ActiveView = AppView.ChangeMultipleDevices;
            CloseFlyoutAfterNavigation();
        }

        [RelayCommand]
        private void NavigateSettings()
        {
            CloseDevicesManagerMenus();
            ActiveView = AppView.Settings;
        }

        private void CloseFlyoutAfterNavigation()
        {
            if (IsSidebarCollapsed)
                IsDevicesManagerFlyoutOpen = false;
        }

        public async Task<ApplicationShutdownDecision> PrepareShutdownAsync(
            CancellationToken cancellationToken)
        {
            IDeviceActionCoordinatorService? coordinator = _deviceActionCoordinatorService;
            if (coordinator == null)
                return ApplicationShutdownDecision.ReadyToClose;

            IReadOnlyList<DeviceActionSessionSnapshot> activeSessions = coordinator.GetActiveSessions();
            if (activeSessions.Count == 0)
            {
                return ApplicationShutdownDecision.ReadyToClose;
            }

            if (_confirmationDialogService == null)
                return ApplicationShutdownDecision.Canceled;

            bool confirmed = await _confirmationDialogService.ShowConfirmationAsync(
                    new ConfirmationDialogOptions
                    {
                        Caption = _localizationService.GetString("MainWindow_ShutdownActiveActionsCaption"),
                        Message = string.Format(
                            _localizationService.GetString("MainWindow_ShutdownActiveActionsMessage"),
                            activeSessions.Count),
                        WarningMessage = _localizationService.GetString("MainWindow_ShutdownActiveActionsWarning"),
                        ConfirmButtonText = _localizationService.GetString("MainWindow_ShutdownStopAndExit"),
                        CancelButtonText = _localizationService.GetString("MainWindow_ShutdownKeepWorking"),
                        Icon = ConfirmationDialogIcon.Warning
                    },
                    cancellationToken)
                .ConfigureAwait(true);
            if (!confirmed)
                return ApplicationShutdownDecision.Canceled;

            IsShutdownInProgress = true;
            coordinator.RequestShutdownCancellation();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                await coordinator.WaitForIdleAsync(timeout.Token)
                    .ConfigureAwait(true);
                return ApplicationShutdownDecision.ReadyToClose;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested
                                                   && !cancellationToken.IsCancellationRequested)
            {
                return ApplicationShutdownDecision.ForceExit;
            }
            catch
            {
                IsShutdownInProgress = false;
                throw;
            }
        }

        private void SynchronizeDevicesManagerMenuState()
        {
            IsDevicesManagerFlyoutOpen = false;
            IsDevicesManagerSubmenuOpen = !IsSidebarCollapsed && IsDevicesManagerActive;
        }

        private void CloseDevicesManagerMenus()
        {
            IsDevicesManagerSubmenuOpen = false;
            IsDevicesManagerFlyoutOpen = false;
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
            OnPropertyChanged(nameof(DevicesManagerIconKind));
        }
    }
}
