using System.Windows;
using System.Windows.Controls;
using DeepDroidChanger.ViewModels;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Views;

public sealed partial class ViewMultipleDevicesView : UserControl
{
    private readonly ViewMultipleDevicesViewModel _viewModel;
    private readonly ILogger<ViewMultipleDevicesView> _logger;
    private CancellationTokenSource? _viewCancellation;
    private bool _isActive;

    public ViewMultipleDevicesView(
        ViewMultipleDevicesViewModel viewModel,
        ILogger<ViewMultipleDevicesView> logger)
    {
        _viewModel = viewModel;
        _logger = logger;
        try
        {
            InitializeComponent();
        }
        catch (Exception exception)
        {
            _logger.LogCritical(exception, "ViewMultipleDevicesView.InitializeComponent failed.");
            throw;
        }
        DataContext = viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        CancellationTokenSource? viewCancellation = null;
        bool keepCancellationForLoadedLifetime = false;
        try
        {
            if (_isActive)
                return;

            _isActive = true;
            viewCancellation = new CancellationTokenSource();
            _viewCancellation = viewCancellation;

            UpdateViewport();
            await _viewModel.InitializeAsync(viewCancellation!.Token).ConfigureAwait(true);

            if (!_isActive ||
                !ReferenceEquals(_viewCancellation, viewCancellation) ||
                viewCancellation.IsCancellationRequested)
            {
                return;
            }

            UpdateViewport();
            keepCancellationForLoadedLifetime = true;
        }
        catch (OperationCanceledException) when (viewCancellation?.IsCancellationRequested == true)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to initialize the View Multiple Devices view.");
            try
            {
                await _viewModel.DeactivateAsync().ConfigureAwait(true);
            }
            catch (Exception cleanupException)
            {
                _logger.LogError(
                    cleanupException,
                    "Failed to clean up the View Multiple Devices view after initialization failure.");
            }
        }
        finally
        {
            if (viewCancellation is not null &&
                !keepCancellationForLoadedLifetime &&
                ReferenceEquals(_viewCancellation, viewCancellation))
            {
                _viewCancellation = null;
                _isActive = false;
                viewCancellation.Dispose();
            }
        }
    }

    private async void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        CancellationTokenSource? viewCancellation = null;
        try
        {
            if (!_isActive && _viewCancellation is null)
                return;

            _isActive = false;
            viewCancellation = _viewCancellation;
            _viewCancellation = null;

            try
            {
                viewCancellation?.Cancel();
            }
            catch (Exception exception)
            {
                _logger.LogDebug(
                    exception,
                    "Failed to cancel the View Multiple Devices lifetime token during unload.");
            }

            await _viewModel.DeactivateAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to deactivate the View Multiple Devices view.");
        }
        finally
        {
            viewCancellation?.Dispose();
        }
    }

    private void OnDeviceViewportSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        TryUpdateViewport();
    }

    private void OnDeviceScrollChanged(object sender, ScrollChangedEventArgs eventArgs)
    {
        TryUpdateViewport();
    }

    private void UpdateViewport()
    {
        double width = DeviceScrollViewer.ViewportWidth > 0
            ? DeviceScrollViewer.ViewportWidth
            : DeviceScrollViewer.ActualWidth;
        double height = DeviceScrollViewer.ViewportHeight > 0
            ? DeviceScrollViewer.ViewportHeight
            : DeviceScrollViewer.ActualHeight;
        if (width <= 0)
            return;

        const string tileMarginKey = "ViewMultipleDevices.TileMargin";
        object? resource = TryFindResource(tileMarginKey);
        if (resource is not Thickness tileMargin)
        {
            string actualType = resource?.GetType().FullName ?? "null";
            throw new InvalidOperationException(
                $"Resource '{tileMarginKey}' must be a {typeof(Thickness).FullName}; actual type: {actualType}.");
        }

        double horizontalTileFootprint = Math.Max(0, tileMargin.Left) + Math.Max(0, tileMargin.Right);
        double verticalTileFootprint = Math.Max(0, tileMargin.Top) + Math.Max(0, tileMargin.Bottom);
        _viewModel.UpdateViewport(
            width,
            height,
            horizontalTileFootprint,
            verticalTileFootprint);
    }

    private void TryUpdateViewport()
    {
        try
        {
            UpdateViewport();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to update the View Multiple Devices viewport.");
        }
    }
}
