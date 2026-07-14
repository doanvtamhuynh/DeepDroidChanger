using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using DeepDroidChanger.Models;
using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger
{
    public sealed partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly DeviceManagerViewModel _deviceManagerViewModel;
        private readonly DeviceManagerView _deviceManagerView;
        private readonly SettingsView _settingsView;
        private readonly ILogger<MainWindow> _logger;
        private bool _isClosingAfterSave;
        private bool _isCloseCleanupInProgress;

        public MainWindow(
            MainViewModel viewModel,
            DeviceManagerViewModel deviceManagerViewModel,
            DeviceManagerView deviceManagerView,
            SettingsView settingsView,
            ILogger<MainWindow> logger)
        {
            InitializeComponent();

            _viewModel = viewModel;
            _deviceManagerViewModel = deviceManagerViewModel;
            _deviceManagerView = deviceManagerView;
            _settingsView = settingsView;
            _logger = logger;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_Loaded;

            if (_isClosingAfterSave)
                return;

            _viewModel.NavigationRequested += NavigateTo;
            DataContext = _viewModel;

            _viewModel.NavigateInitialView();
        }

        private void NavigateTo(AppView view)
        {
            MainContent.Content = view switch
            {
                AppView.DeviceManager => _deviceManagerView,
                AppView.Settings => _settingsView,
                _ => throw new ArgumentOutOfRangeException(nameof(view), view, null)
            };
        }

        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_isClosingAfterSave)
                return;

            e.Cancel = true;
            if (_isCloseCleanupInProgress)
                return;

            _isCloseCleanupInProgress = true;

            try
            {
                await _deviceManagerViewModel.DeactivateAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to stop device polling while closing the application.");
            }

            try
            {
                await _viewModel.SaveSettingsAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to save settings while closing the application.");
            }
            finally
            {
                _viewModel.NavigationRequested -= NavigateTo;
                _isClosingAfterSave = true;
                _isCloseCleanupInProgress = false;
                Close();
            }
        }
    }
}
