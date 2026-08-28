using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DeepDroidChanger.ViewModels;

namespace DeepDroidChanger.Views;

public sealed partial class ViewDeviceWindow : Window
{
    private const double CollapsedActionsWidth = 52;
    private const double ExpandedActionsWidth = 315;
    private const double FullscreenActionsWidth = 44;
    private readonly ViewDeviceViewModel _viewModel;
    private readonly double _collapsedMinimumWidth;
    private WindowStyle _savedWindowStyle;
    private ResizeMode _savedResizeMode;
    private WindowState _savedWindowState;
    private double _fullscreenRestoreStreamArea;
    private bool _isLoaded;
    private bool _isClosing;

    public ViewDeviceWindow(ViewDeviceViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        _collapsedMinimumWidth = MinWidth;
        DataContext = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.NativeWindowHandleChanged += OnNativeWindowHandleChanged;
        _viewModel.NativeFocusRequested += OnNativeFocusRequested;
        Loaded += OnLoaded;
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        _isLoaded = true;
        ApplyActionsPanelLayout(adjustWindowWidth: false);
        FitWindowToDeviceAspect();
        UpdateStreamViewport();
        AttachNativeWindow();
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        _isClosing = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.NativeWindowHandleChanged -= OnNativeWindowHandleChanged;
        _viewModel.NativeFocusRequested -= OnNativeFocusRequested;
        Loaded -= OnLoaded;
        Closed -= OnClosed;
        PreviewKeyDown -= OnPreviewKeyDown;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (_isClosing)
            return;

        switch (eventArgs.PropertyName)
        {
            case nameof(ViewDeviceViewModel.IsFullscreen):
                ApplyFullscreen(_viewModel.IsFullscreen);
                break;
            case nameof(ViewDeviceViewModel.IsActionsPanelExpanded):
                ApplyActionsPanelLayout(adjustWindowWidth: !_viewModel.IsFullscreen);
                RestoreNativeFocus();
                break;
            case nameof(ViewDeviceViewModel.DeviceAspectRatio):
                FitWindowToDeviceAspect();
                UpdateStreamViewport();
                break;
            case nameof(ViewDeviceViewModel.IsRunning):
                UpdateStreamViewport();
                if (_viewModel.IsRunning)
                    Dispatcher.BeginInvoke(new Action(AttachNativeWindow));
                break;
        }
    }

    private void OnNativeWindowHandleChanged(object? sender, EventArgs eventArgs)
    {
        if (!_isClosing)
            Dispatcher.BeginInvoke(new Action(AttachNativeWindow));
    }

    private void OnNativeFocusRequested(object? sender, EventArgs eventArgs)
    {
        if (!_isClosing && IsActive)
            Dispatcher.BeginInvoke(new Action(NativeHost.FocusNativeWindow));
    }

    private void AttachNativeWindow()
    {
        if (!_isClosing && _isLoaded)
        {
            try
            {
                NativeHost.AttachWindow(_viewModel.NativeWindowHandle);
                RestoreNativeFocus();
            }
            catch (Win32Exception exception)
            {
                NativeHost.DetachWindow();
                _ = _viewModel.HandleNativeHostFailureAsync(exception);
            }
        }
    }

    private void OnStreamContainerSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        UpdateStreamViewport();
    }

    private void FitWindowToDeviceAspect(double? preferredAreaOverride = null)
    {
        double aspectRatio = _viewModel.DeviceAspectRatio;
        if (!_isLoaded ||
            _viewModel.IsFullscreen ||
            WindowState != WindowState.Normal ||
            !double.IsFinite(aspectRatio) ||
            aspectRatio <= 0 ||
            StreamContainer.ActualHeight <= 0)
        {
            return;
        }

        Rect workArea = ViewDeviceMonitorWorkArea.GetFor(this);
        double horizontalChrome = Math.Max(0, ActualWidth - StreamContainer.ActualWidth);
        double verticalChrome = Math.Max(0, ActualHeight - StreamContainer.ActualHeight);
        double preferredArea = preferredAreaOverride is > 0
            ? preferredAreaOverride.Value
            : GetCurrentStreamArea();
        double desiredStreamWidth = Math.Sqrt(preferredArea * aspectRatio);
        double desiredStreamHeight = desiredStreamWidth / aspectRatio;

        double availableStreamWidth = Math.Max(1, workArea.Width - horizontalChrome);
        double availableStreamHeight = Math.Max(1, workArea.Height - verticalChrome);
        double scale = Math.Min(
            1,
            Math.Min(
                availableStreamWidth / desiredStreamWidth,
                availableStreamHeight / desiredStreamHeight));
        desiredStreamWidth *= scale;
        desiredStreamHeight *= scale;

        ApplyWindowBounds(
            Math.Max(MinWidth, desiredStreamWidth + horizontalChrome),
            Math.Max(MinHeight, desiredStreamHeight + verticalChrome),
            workArea);
        UpdateStreamViewport();
    }

    private double GetCurrentStreamArea()
    {
        double preferredStreamWidth = StreamViewport.ActualWidth > 0
            ? StreamViewport.ActualWidth
            : StreamContainer.ActualWidth;
        double preferredStreamHeight = StreamViewport.ActualHeight > 0
            ? StreamViewport.ActualHeight
            : StreamContainer.ActualHeight;
        return Math.Max(1, preferredStreamWidth * preferredStreamHeight);
    }

    private void UpdateStreamViewport()
    {
        if (!_isLoaded || StreamContainer.ActualWidth <= 0 || StreamContainer.ActualHeight <= 0)
            return;

        double availableWidth = StreamContainer.ActualWidth;
        double availableHeight = StreamContainer.ActualHeight;
        double aspectRatio = _viewModel.DeviceAspectRatio;
        if (!double.IsFinite(aspectRatio) || aspectRatio <= 0)
        {
            StreamViewport.Width = availableWidth;
            StreamViewport.Height = availableHeight;
            return;
        }

        double width = availableWidth;
        double height = width / aspectRatio;
        if (height > availableHeight)
        {
            height = availableHeight;
            width = height * aspectRatio;
        }

        StreamViewport.Width = Math.Max(1, width);
        StreamViewport.Height = Math.Max(1, height);
    }

    private void ApplyActionsPanelLayout(bool adjustWindowWidth)
    {
        double oldWidth = ActionsColumn.Width.IsAbsolute
            ? ActionsColumn.Width.Value
            : CollapsedActionsWidth;
        double newWidth = _viewModel.IsFullscreen
            ? FullscreenActionsWidth
            : _viewModel.IsActionsPanelExpanded
                ? ExpandedActionsWidth
                : CollapsedActionsWidth;
        double windowWidthBeforeLayout = ActualWidth > 0 ? ActualWidth : Width;
        double windowHeightBeforeLayout = ActualHeight > 0 ? ActualHeight : Height;

        ActionsColumn.Width = new GridLength(newWidth);
        MinWidth = !_viewModel.IsFullscreen && _viewModel.IsActionsPanelExpanded
            ? _collapsedMinimumWidth + ExpandedActionsWidth - CollapsedActionsWidth
            : _collapsedMinimumWidth;
        if (adjustWindowWidth &&
            WindowState == WindowState.Normal &&
            double.IsFinite(windowWidthBeforeLayout) &&
            double.IsFinite(windowHeightBeforeLayout))
        {
            // MinWidth can resize the WPF window immediately. Base the panel delta on
            // the dimensions captured before that coercion so the stream stays fixed.
            ApplyWindowBounds(
                windowWidthBeforeLayout + newWidth - oldWidth,
                windowHeightBeforeLayout,
                ViewDeviceMonitorWorkArea.GetFor(this));
        }
        else if (WindowState == WindowState.Normal)
        {
            ClampWindowToCurrentWorkArea();
        }

        UpdateStreamViewport();
    }

    private void ApplyFullscreen(bool fullscreen)
    {
        if (fullscreen)
        {
            _fullscreenRestoreStreamArea = GetCurrentStreamArea();
            _savedWindowStyle = WindowStyle;
            _savedResizeMode = ResizeMode;
            _savedWindowState = WindowState;
            HeaderPanel.Visibility = Visibility.Collapsed;
            NavigationPanel.Visibility = Visibility.Collapsed;
            ActionsColumn.Width = new GridLength(FullscreenActionsWidth);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = _savedWindowState == WindowState.Minimized
                ? WindowState.Normal
                : _savedWindowState;
            ResizeMode = _savedResizeMode;
            WindowStyle = _savedWindowStyle;
            HeaderPanel.Visibility = Visibility.Visible;
            NavigationPanel.Visibility = Visibility.Visible;
            ApplyActionsPanelLayout(adjustWindowWidth: false);
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!fullscreen)
            {
                FitWindowToDeviceAspect(_fullscreenRestoreStreamArea);
                _fullscreenRestoreStreamArea = 0;
            }
            ClampWindowToCurrentWorkArea();
            UpdateStreamViewport();
            RestoreNativeFocus();
        }));
    }

    private void RestoreNativeFocus()
    {
        if (_isClosing || !_isLoaded || !_viewModel.IsRunning || !IsActive)
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_isClosing && _isLoaded && _viewModel.IsRunning && IsActive)
                NativeHost.FocusNativeWindow();
        }));
    }

    private void ClampWindowToCurrentWorkArea()
    {
        if (!_isLoaded || WindowState != WindowState.Normal)
            return;

        ApplyWindowBounds(Width, Height, ViewDeviceMonitorWorkArea.GetFor(this));
    }

    private void ApplyWindowBounds(double desiredWidth, double desiredHeight, Rect workArea)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
            return;

        double maximumWidth = Math.Max(MinWidth, workArea.Width);
        double maximumHeight = Math.Max(MinHeight, workArea.Height);
        double width = Math.Clamp(desiredWidth, MinWidth, maximumWidth);
        double height = Math.Clamp(desiredHeight, MinHeight, maximumHeight);

        Width = width;
        Height = height;

        double maximumLeft = Math.Max(workArea.Left, workArea.Right - width);
        double maximumTop = Math.Max(workArea.Top, workArea.Bottom - height);
        double currentLeft = double.IsFinite(Left) ? Left : workArea.Left;
        double currentTop = double.IsFinite(Top) ? Top : workArea.Top;
        Left = Math.Clamp(currentLeft, workArea.Left, maximumLeft);
        Top = Math.Clamp(currentTop, workArea.Top, maximumTop);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.F11)
            return;

        _viewModel.IsFullscreen = !_viewModel.IsFullscreen;
        eventArgs.Handled = true;
    }
}
