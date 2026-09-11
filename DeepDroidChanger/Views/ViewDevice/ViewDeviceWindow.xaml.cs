using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DeepDroidChanger.ViewModels;

namespace DeepDroidChanger.Views;

public sealed partial class ViewDeviceWindow : Window
{
    private const double ToolbarWidth = 54;
    private static readonly Thickness NormalStreamMargin = new(8);
    private readonly ViewDeviceViewModel _viewModel;
    private WindowStyle _savedWindowStyle;
    private ResizeMode _savedResizeMode;
    private WindowState _savedWindowState;
    private double _fullscreenRestoreStreamArea;
    private bool _isLoaded;
    private bool _isClosing;
    private bool _hasFullscreenSnapshot;

    public ViewDeviceWindow(ViewDeviceViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        _isLoaded = true;
        if (_viewModel.IsFullscreen)
            ApplyFullscreen(true);
        else
        {
            ToolbarColumn.Width = new GridLength(ToolbarWidth);
            StreamContainer.Margin = NormalStreamMargin;
            FitWindowToDeviceAspect();
        }

        FocusDisplay();
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        _isClosing = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
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
            case nameof(ViewDeviceViewModel.DeviceAspectRatio):
                FitWindowToDeviceAspect();
                break;
            case nameof(ViewDeviceViewModel.IsRunning):
                if (_viewModel.IsRunning)
                    FocusDisplay();
                break;
        }
    }

    private void ApplyFullscreen(bool fullscreen)
    {
        if (!_isLoaded)
            return;

        if (fullscreen)
        {
            if (!_hasFullscreenSnapshot)
            {
                _fullscreenRestoreStreamArea = GetCurrentStreamArea();
                _savedWindowStyle = WindowStyle;
                _savedResizeMode = ResizeMode;
                _savedWindowState = WindowState;
                _hasFullscreenSnapshot = true;
            }

            HeaderPanel.Visibility = Visibility.Collapsed;
            ToolbarPanel.Visibility = Visibility.Collapsed;
            ToolbarColumn.Width = new GridLength(0);
            StreamContainer.Margin = new Thickness(0);
            RootBorder.BorderThickness = new Thickness(0);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            FocusDisplay();
            return;
        }

        if (_hasFullscreenSnapshot)
        {
            WindowState = _savedWindowState == WindowState.Minimized
                ? WindowState.Normal
                : _savedWindowState;
            ResizeMode = _savedResizeMode;
            WindowStyle = _savedWindowStyle;
        }

        HeaderPanel.Visibility = Visibility.Visible;
        ToolbarPanel.Visibility = Visibility.Visible;
        ToolbarColumn.Width = new GridLength(ToolbarWidth);
        StreamContainer.Margin = NormalStreamMargin;
        RootBorder.BorderThickness = new Thickness(1);

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (_isClosing)
                    return;

                FitWindowToDeviceAspect(_fullscreenRestoreStreamArea);
                _fullscreenRestoreStreamArea = 0;
                _hasFullscreenSnapshot = false;
                ClampWindowToCurrentWorkArea();
                FocusDisplay();
            }));
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
    }

    private double GetCurrentStreamArea()
    {
        double width = StreamContainer.ActualWidth > 0 ? StreamContainer.ActualWidth : 1;
        double height = StreamContainer.ActualHeight > 0 ? StreamContainer.ActualHeight : 1;
        return Math.Max(1, width * height);
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

    private void FocusDisplay()
    {
        if (!_isLoaded ||
            _isClosing ||
            !_viewModel.IsRunning ||
            !IsActive)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (!_isClosing && _isLoaded && _viewModel.IsRunning && IsActive)
                    ScrcpyDisplay.Focus();
            }));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape && _viewModel.IsFullscreen)
        {
            _viewModel.IsFullscreen = false;
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key == Key.F11 ||
            (eventArgs.Key == Key.F && eventArgs.KeyboardDevice.Modifiers == ModifierKeys.Control))
        {
            _viewModel.IsFullscreen = !_viewModel.IsFullscreen;
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.KeyboardDevice.Modifiers != ModifierKeys.Control)
            return;

        switch (eventArgs.Key)
        {
            case Key.H:
                _viewModel.HomeCommand.Execute(null);
                eventArgs.Handled = true;
                break;
            case Key.B:
                _viewModel.BackCommand.Execute(null);
                eventArgs.Handled = true;
                break;
            case Key.S:
                _viewModel.RecentCommand.Execute(null);
                eventArgs.Handled = true;
                break;
            case Key.Up:
                _viewModel.VolumeUpCommand.Execute(null);
                eventArgs.Handled = true;
                break;
            case Key.Down:
                _viewModel.VolumeDownCommand.Execute(null);
                eventArgs.Handled = true;
                break;
            case Key.P:
                _viewModel.PowerCommand.Execute(null);
                eventArgs.Handled = true;
                break;
            case Key.R:
                _viewModel.RotateCommand.Execute(null);
                eventArgs.Handled = true;
                break;
        }
    }
}
