using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DeepDroidChanger.ViewModels;

namespace DeepDroidChanger.Views;

public sealed partial class ViewMultipleDeviceTile : UserControl
{
    private ViewMultipleDeviceItemViewModel? _viewModel;
    private ScrollViewer? _deviceScrollViewer;
    private bool _isLoaded;
    private bool _nativeClipUpdateQueued;

    public ViewMultipleDeviceTile()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"ViewMultipleDeviceTile.InitializeComponent failed: {exception}");
            Trace.WriteLine($"ViewMultipleDeviceTile.InitializeComponent failed: {exception}");
            throw;
        }
        NativeHost.AttachSucceeded += OnNativeHostAttachSucceeded;
        NativeHost.AttachFailed += OnNativeHostAttachFailed;
        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnTileSizeChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs eventArgs)
    {
        DetachNativeHostSafely();

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = eventArgs.NewValue as ViewMultipleDeviceItemViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        if (_isLoaded)
        {
            AttachToDeviceScrollViewer();
            SafeRefreshNativeHost(_viewModel);
            QueueNativeClipUpdate();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        _isLoaded = true;
        AttachToDeviceScrollViewer();
        SafeRefreshNativeHost(_viewModel);
        QueueNativeClipUpdate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        _isLoaded = false;
        _nativeClipUpdateQueued = false;
        DetachFromDeviceScrollViewer();
        DetachNativeHostSafely();
    }

    private void OnNativeHostAttachFailed(Exception exception, IntPtr windowHandle)
    {
        if (!_isLoaded ||
            _viewModel is null ||
            !_viewModel.IsRunning ||
            windowHandle == IntPtr.Zero)
            return;

        if (!_viewModel.IsCurrentNativeWindowHandle(windowHandle))
            return;

        ReportNativeHostFailure(_viewModel, exception, windowHandle);
    }

    private void OnNativeHostAttachSucceeded(IntPtr windowHandle)
    {
        if (!_isLoaded ||
            _viewModel is null ||
            !ReferenceEquals(DataContext, _viewModel) ||
            windowHandle == IntPtr.Zero ||
            !_viewModel.IsCurrentNativeWindowHandle(windowHandle))
        {
            return;
        }

        _viewModel.NotifyNativeHostAttached(windowHandle);
        UpdateNativeClipSafely();
        QueueNativeClipUpdate();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is not ViewMultipleDeviceItemViewModel item ||
            !ReferenceEquals(item, _viewModel))
        {
            return;
        }

        if (eventArgs.PropertyName is nameof(ViewMultipleDeviceItemViewModel.NativeWindowHandle)
            or nameof(ViewMultipleDeviceItemViewModel.IsRunning))
        {
            bool nativeWindowCleared =
                !item.IsRunning || item.NativeWindowHandle == IntPtr.Zero;
            if (nativeWindowCleared)
            {
                DetachNativeHostSafely();
                return;
            }
        }

        if (eventArgs.PropertyName is nameof(ViewMultipleDeviceItemViewModel.NativeWindowHandle)
            or nameof(ViewMultipleDeviceItemViewModel.IsRunning)
            or nameof(ViewMultipleDeviceItemViewModel.DeviceAspectRatio))
        {
            ViewMultipleDeviceItemViewModel expectedViewModel = item;
            IntPtr expectedWindowHandle = expectedViewModel.NativeWindowHandle;
            try
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                    return;

                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(() => SafeRefreshNativeHost(expectedViewModel)));
            }
            catch (Exception exception)
            {
                LogUiFailure("ViewMultipleDeviceTile.Dispatcher.BeginInvoke failed", exception);
                ReportNativeHostFailure(expectedViewModel, exception, expectedWindowHandle);
            }
        }
    }

    private void OnStreamViewportSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        try
        {
            UpdateStreamViewportLayout();
            UpdateNativeViewport();
            UpdateNativeClipSafely();
            QueueNativeClipUpdate();
        }
        catch (Exception exception)
        {
            LogUiFailure("ViewMultipleDeviceTile viewport update failed", exception);
        }
    }

    private void OnTileSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        UpdateNativeClipSafely();
        QueueNativeClipUpdate();
    }

    private void OnDeviceScrollViewerScrollChanged(object sender, ScrollChangedEventArgs eventArgs)
    {
        UpdateNativeClipSafely();
        QueueNativeClipUpdate();
    }

    private void OnDeviceScrollViewerSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        UpdateNativeClipSafely();
        QueueNativeClipUpdate();
    }

    private void AttachToDeviceScrollViewer()
    {
        ScrollViewer? scrollViewer = FindVisualAncestor<ScrollViewer>();
        if (ReferenceEquals(_deviceScrollViewer, scrollViewer))
            return;

        DetachFromDeviceScrollViewer();
        _deviceScrollViewer = scrollViewer;
        if (_deviceScrollViewer is null)
            return;

        _deviceScrollViewer.ScrollChanged += OnDeviceScrollViewerScrollChanged;
        _deviceScrollViewer.SizeChanged += OnDeviceScrollViewerSizeChanged;
    }

    private void DetachFromDeviceScrollViewer()
    {
        if (_deviceScrollViewer is null)
            return;

        _deviceScrollViewer.ScrollChanged -= OnDeviceScrollViewerScrollChanged;
        _deviceScrollViewer.SizeChanged -= OnDeviceScrollViewerSizeChanged;
        _deviceScrollViewer = null;
    }

    private T? FindVisualAncestor<T>() where T : DependencyObject
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void SafeRefreshNativeHost(ViewMultipleDeviceItemViewModel? expectedViewModel)
    {
        if (!_isLoaded || !IsLoaded ||
            expectedViewModel is null ||
            !ReferenceEquals(_viewModel, expectedViewModel) ||
            !ReferenceEquals(DataContext, expectedViewModel))
        {
            return;
        }

        IntPtr expectedWindowHandle = IntPtr.Zero;
        try
        {
            UpdateStreamViewportLayout();
            UpdateNativeViewport();
            UpdateNativeClipSafely();

            expectedWindowHandle = expectedViewModel.NativeWindowHandle;
            if (!expectedViewModel.IsRunning || expectedWindowHandle == IntPtr.Zero)
            {
                DetachNativeHostSafely();
                return;
            }

            if (!expectedViewModel.IsCurrentNativeWindowHandle(expectedWindowHandle))
            {
                DetachNativeHostSafely();
                return;
            }

            NativeHost.AttachWindow(expectedWindowHandle);
            UpdateNativeClipSafely();
            QueueNativeClipUpdate();
        }
        catch (Exception exception)
        {
            LogUiFailure("ViewMultipleDeviceTile native host refresh failed", exception);
            DetachNativeHostSafely();
            ReportNativeHostFailure(expectedViewModel, exception, expectedWindowHandle);
        }
    }

    private void DetachNativeHostSafely()
    {
        try
        {
            NativeHost.DetachWindow();
        }
        catch (Exception exception)
        {
            LogUiFailure("ViewMultipleDeviceTile.DetachWindow failed", exception);
        }
    }

    private void ReportNativeHostFailure(
        ViewMultipleDeviceItemViewModel item,
        Exception exception,
        IntPtr expectedWindowHandle)
    {
        _ = ReportNativeHostFailureAsync(item, exception, expectedWindowHandle);
    }

    private async Task ReportNativeHostFailureAsync(
        ViewMultipleDeviceItemViewModel item,
        Exception exception,
        IntPtr expectedWindowHandle)
    {
        try
        {
            await item.HandleNativeHostFailureAsync(
                    exception,
                    expectedWindowHandle)
                .ConfigureAwait(false);
        }
        catch (Exception reportException)
        {
            LogUiFailure("ViewMultipleDeviceTile failed to report native host failure", reportException);
        }
    }

    private static void LogUiFailure(string message, Exception exception)
    {
        string details = $"{message}: {exception}";
        Debug.WriteLine(details);
        Trace.WriteLine(details);
    }

    private void UpdateNativeViewport()
    {
        if (_viewModel is null)
            return;

        double viewportWidth = StreamViewport.ActualWidth;
        double viewportHeight = StreamViewport.ActualHeight;
        if (!double.IsFinite(viewportWidth) ||
            !double.IsFinite(viewportHeight) ||
            viewportWidth <= 0 ||
            viewportHeight <= 0)
        {
            return;
        }

        NativeHost.Width = Math.Max(1, viewportWidth);
        NativeHost.Height = Math.Max(1, viewportHeight);
    }

    private void UpdateNativeClipSafely()
    {
        try
        {
            UpdateNativeClip();
        }
        catch (Exception exception)
        {
            LogUiFailure("ViewMultipleDeviceTile native clip update failed", exception);
        }
    }

    private void UpdateNativeClip()
    {
        if (!_isLoaded || !IsLoaded)
            return;

        AttachToDeviceScrollViewer();
        ScrollViewer? scrollViewer = _deviceScrollViewer;
        if (scrollViewer is null)
        {
            NativeHost.SetVisibleClip(null);
            return;
        }

        double viewportWidth = scrollViewer.ViewportWidth;
        double viewportHeight = scrollViewer.ViewportHeight;
        if (!double.IsFinite(viewportWidth) ||
            !double.IsFinite(viewportHeight) ||
            viewportWidth <= 0 ||
            viewportHeight <= 0)
        {
            NativeHost.SetVisibleClip(Rect.Empty);
            return;
        }

        double hostWidth = NativeHost.ActualWidth;
        double hostHeight = NativeHost.ActualHeight;
        if (!double.IsFinite(hostWidth) || hostWidth <= 0)
            hostWidth = StreamViewport.ActualWidth;
        if (!double.IsFinite(hostHeight) || hostHeight <= 0)
            hostHeight = StreamViewport.ActualHeight;
        if (!double.IsFinite(hostWidth) ||
            !double.IsFinite(hostHeight) ||
            hostWidth <= 0 ||
            hostHeight <= 0)
        {
            return;
        }

        Rect hostLocalBounds = new(0, 0, hostWidth, hostHeight);
        Rect hostBounds;
        try
        {
            hostBounds = NativeHost
                .TransformToAncestor(scrollViewer)
                .TransformBounds(hostLocalBounds);
        }
        catch (InvalidOperationException)
        {
            NativeHost.SetVisibleClip(Rect.Empty);
            return;
        }

        Rect viewportBounds = new(0, 0, viewportWidth, viewportHeight);
        Rect visibleClip = ViewMultipleDevicesLayout.CalculateVisibleClip(
            hostBounds,
            viewportBounds);

        if (visibleClip.IsEmpty)
        {
            NativeHost.SetVisibleClip(visibleClip);
        }
        else if (CoversEntireHost(visibleClip, hostWidth, hostHeight))
        {
            NativeHost.SetVisibleClip(null);
        }
        else
        {
            NativeHost.SetVisibleClip(visibleClip);
        }
    }

    private static bool CoversEntireHost(Rect visibleClip, double hostWidth, double hostHeight)
    {
        return visibleClip.Left <= 0 &&
               visibleClip.Top <= 0 &&
               visibleClip.Right >= hostWidth &&
               visibleClip.Bottom >= hostHeight;
    }

    private void QueueNativeClipUpdate()
    {
        if (!_isLoaded ||
            !IsLoaded ||
            _nativeClipUpdateQueued ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return;
        }

        _nativeClipUpdateQueued = true;
        try
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    _nativeClipUpdateQueued = false;
                    if (_isLoaded && IsLoaded)
                        UpdateNativeClipSafely();
                }));
        }
        catch (Exception exception)
        {
            _nativeClipUpdateQueued = false;
            LogUiFailure("ViewMultipleDeviceTile clip update scheduling failed", exception);
        }
    }

    private void UpdateStreamViewportLayout()
    {
        double width = StreamViewport.ActualWidth;
        if (!double.IsFinite(width) || width <= 0)
            return;

        double height = ViewMultipleDevicesLayout.CalculateStreamHeight(
            width,
            _viewModel?.DeviceAspectRatio ?? ViewMultipleDevicesLayout.FallbackDeviceAspectRatio);
        if (height <= 0)
            return;

        if (double.IsNaN(StreamViewport.Height) ||
            Math.Abs(StreamViewport.Height - height) > 0.5)
        {
            StreamViewport.Height = height;
        }
    }
}
