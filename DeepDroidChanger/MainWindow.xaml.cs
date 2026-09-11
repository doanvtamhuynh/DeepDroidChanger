using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger
{
    public sealed partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly ChangeSingleDeviceViewModel _changeSingleDeviceViewModel;
        private readonly ChangeMultipleDevicesViewModel _changeMultipleDevicesViewModel;
        private readonly Func<ViewMultipleDevicesViewModel> _viewMultipleDevicesViewModelFactory;
        private readonly ChangeSingleDeviceView _changeSingleDeviceView;
        private readonly ChangeMultipleDevicesView _changeMultipleDevicesView;
        private readonly Func<ViewMultipleDevicesView> _viewMultipleDevicesViewFactory;
        private ViewMultipleDevicesViewModel? _viewMultipleDevicesViewModel;
        private ViewMultipleDevicesView? _viewMultipleDevicesView;
        private readonly SettingsView _settingsView;
        private readonly IViewDeviceWindowService _viewDeviceWindowService;
        private readonly ILogger<MainWindow> _logger;
        private bool _isClosingAfterSave;
        private bool _isCloseCleanupInProgress;
        private AppView _lastSuccessfullyDisplayedView = AppView.ChangeSingleDevice;

        public MainWindow(
            MainViewModel viewModel,
            ChangeSingleDeviceViewModel changeSingleDeviceViewModel,
            ChangeMultipleDevicesViewModel changeMultipleDevicesViewModel,
            Func<ViewMultipleDevicesViewModel> viewMultipleDevicesViewModelFactory,
            ChangeSingleDeviceView changeSingleDeviceView,
            ChangeMultipleDevicesView changeMultipleDevicesView,
            Func<ViewMultipleDevicesView> viewMultipleDevicesViewFactory,
            SettingsView settingsView,
            IViewDeviceWindowService viewDeviceWindowService,
            ILogger<MainWindow> logger)
        {
            _viewModel = viewModel;
            _changeSingleDeviceViewModel = changeSingleDeviceViewModel;
            _changeMultipleDevicesViewModel = changeMultipleDevicesViewModel;
            _viewMultipleDevicesViewModelFactory = viewMultipleDevicesViewModelFactory;
            _changeSingleDeviceView = changeSingleDeviceView;
            _changeMultipleDevicesView = changeMultipleDevicesView;
            _viewMultipleDevicesViewFactory = viewMultipleDevicesViewFactory;
            _settingsView = settingsView;
            _viewDeviceWindowService = viewDeviceWindowService;
            _logger = logger;

            try
            {
                InitializeComponent();
            }
            catch (Exception exception)
            {
                _logger.LogCritical(exception, "MainWindow.InitializeComponent failed during startup.");
                throw;
            }

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_Loaded;

            if (_isClosingAfterSave)
                return;

            try
            {
                _viewModel.NavigationRequested += NavigateTo;
                DataContext = _viewModel;

                _viewModel.NavigateInitialView();
            }
            catch (Exception exception)
            {
                _logger.LogCritical(
                    exception,
                    "Initial ChangeSingleDevice navigation failed after MainWindow.Show.");
                throw;
            }
        }

        private void NavigateTo(AppView view)
        {
            try
            {
                MainContent.Content = view switch
                {
                    AppView.ChangeSingleDevice => _changeSingleDeviceView,
                    AppView.ChangeMultipleDevices => _changeMultipleDevicesView,
                    AppView.ViewMultipleDevices => GetViewMultipleDevicesView(),
                    AppView.Settings => _settingsView,
                    _ => throw new ArgumentOutOfRangeException(nameof(view), view, null)
                };
                _lastSuccessfullyDisplayedView = view;
            }
            catch (Exception exception)
            {
                _logger.LogCritical(exception, "Navigation to {AppView} failed.", view);
                if (view != AppView.ViewMultipleDevices)
                    throw;

                // Keep the already displayed page alive when the optional
                // multi-view factory or its XAML fails. Restore the navigation
                // state without raising another navigation event.
                _viewModel.RestoreActiveView(_lastSuccessfullyDisplayedView);
            }
        }

        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_isClosingAfterSave)
                return;

            e.Cancel = true;
            if (_isCloseCleanupInProgress)
                return;

            _isCloseCleanupInProgress = true;

            ApplicationShutdownDecision decision;
            try
            {
                decision = await _viewModel.PrepareShutdownAsync(CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to prepare active device actions for shutdown.");
                _isCloseCleanupInProgress = false;
                return;
            }

            if (decision == ApplicationShutdownDecision.Canceled)
            {
                _isCloseCleanupInProgress = false;
                return;
            }

            if (decision == ApplicationShutdownDecision.ForceExit)
            {
                await CloseViewDevicesAsync().ConfigureAwait(true);
                await DeactivateViewMultipleDevicesViewModelAsync().ConfigureAwait(true);
                try
                {
                    await _viewModel.SaveSettingsAsync(CancellationToken.None).ConfigureAwait(true);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to save settings before forced application exit.");
                }

                _logger.LogCritical("Device actions did not stop within the shutdown timeout; forcing application exit.");
                Environment.Exit(0);
                return;
            }

            await CloseViewDevicesAsync().ConfigureAwait(true);

            await DeactivateViewModelAsync(
                    _changeSingleDeviceViewModel.DeactivateAsync,
                    "Single Device")
                .ConfigureAwait(true);
            await DeactivateViewModelAsync(
                    _changeMultipleDevicesViewModel.DeactivateAsync,
                    "Multiple Devices")
                .ConfigureAwait(true);
            await DeactivateViewMultipleDevicesViewModelAsync().ConfigureAwait(true);

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

        private ViewMultipleDevicesView GetViewMultipleDevicesView()
        {
            if (_viewMultipleDevicesViewModel is null)
            {
                try
                {
                    _viewMultipleDevicesViewModel = _viewMultipleDevicesViewModelFactory();
                }
                catch (Exception exception)
                {
                    _logger.LogCritical(
                        exception,
                        "Resolving ViewMultipleDevicesViewModel dependencies failed during navigation.");
                    throw;
                }
            }

            if (_viewMultipleDevicesView is null)
            {
                try
                {
                    _viewMultipleDevicesView = _viewMultipleDevicesViewFactory();
                }
                catch (Exception exception)
                {
                    _logger.LogCritical(
                        exception,
                        "Constructing ViewMultipleDevicesView failed during navigation.");
                    throw;
                }
            }

            return _viewMultipleDevicesView;
        }

        private async Task DeactivateViewMultipleDevicesViewModelAsync()
        {
            if (_viewMultipleDevicesViewModel is null)
                return;

            await DeactivateViewModelAsync(
                    _viewMultipleDevicesViewModel.DeactivateAsync,
                    "View Multiple Devices")
                .ConfigureAwait(true);
        }

        private async Task CloseViewDevicesAsync()
        {
            using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));
            try
            {
                await _viewDeviceWindowService.CloseAllAsync(cancellation.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                _logger.LogWarning("Timed out while closing View Device windows during application shutdown.");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to close View Device windows while closing the application.");
            }
        }

        private async Task DeactivateViewModelAsync(Func<Task> deactivateAsync, string viewName)
        {
            try
            {
                await deactivateAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to deactivate {ViewName} while closing the application.", viewName);
            }
        }
    }
}
