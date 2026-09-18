using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;

namespace DeepDroidChanger.Views;

public sealed partial class ViewDeviceWindow : Window
{
    private const double ToolsWindowVisibleGap = 12;
    private const double ToolsWindowShadowMargin = 8;
    private static readonly Thickness NormalStreamMargin = new(8);
    private readonly ViewDeviceViewModel _viewModel;
    private readonly ViewDeviceShortcutResolver _shortcutResolver = new();
    private ViewDeviceToolsWindow? _toolsWindow;
    private bool _toolsEnabled;
    private bool _isLoaded;
    private bool _isClosing;

    public ViewDeviceWindow(ViewDeviceViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        SettingsButton.Tag = _toolsEnabled;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
        Deactivated += OnDeactivated;
        LocationChanged += OnOwnerLocationChanged;
        SizeChanged += OnOwnerSizeChanged;
        StateChanged += OnOwnerStateChanged;
        PreviewMouseDown += OnPreviewMouseDown;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        _isLoaded = true;
        StreamContainer.Margin = NormalStreamMargin;
        RootBorder.BorderThickness = new Thickness(1);
        FitWindowToDeviceAspect();
        FocusDisplay();

        if (_toolsEnabled)
            ShowToolsWindow();
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        _isClosing = true;
        _shortcutResolver.Reset();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Loaded -= OnLoaded;
        Closed -= OnClosed;
        Deactivated -= OnDeactivated;
        LocationChanged -= OnOwnerLocationChanged;
        SizeChanged -= OnOwnerSizeChanged;
        StateChanged -= OnOwnerStateChanged;
        PreviewMouseDown -= OnPreviewMouseDown;
        PreviewKeyDown -= OnPreviewKeyDown;
        PreviewKeyUp -= OnPreviewKeyUp;
        CloseToolsWindow();
    }

    private void OnDeactivated(object? sender, EventArgs eventArgs)
    {
        _shortcutResolver.Reset();
        ScrcpyDisplay.ReleaseActiveInput();
    }

    private void OnOwnerLocationChanged(object? sender, EventArgs eventArgs)
    {
        UpdateToolsWindowPosition();
    }

    private void OnOwnerSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        UpdateToolsWindowPosition();
    }

    private void OnOwnerStateChanged(object? sender, EventArgs eventArgs)
    {
        if (_isClosing || _toolsWindow is null)
            return;

        if (WindowState == WindowState.Minimized)
        {
            HideToolsWindow();
            return;
        }

        if (_toolsEnabled && _isLoaded && IsVisible)
            ShowToolsWindow();
        else
            UpdateToolsWindowPosition();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_isClosing)
            return;

        _toolsEnabled = !_toolsEnabled;
        SettingsButton.Tag = _toolsEnabled;

        if (_toolsEnabled)
        {
            if (_isLoaded && IsVisible && WindowState != WindowState.Minimized)
                ShowToolsWindow();
        }
        else
        {
            HideToolsWindow();
        }
    }

    private ViewDeviceToolsWindow EnsureToolsWindow()
    {
        if (_toolsWindow is not null)
            return _toolsWindow;

        ViewDeviceToolsWindow toolsWindow = new(_viewModel)
        {
            Owner = this,
            DataContext = _viewModel,
            ShowInTaskbar = false
        };
        toolsWindow.Closing += OnToolsWindowClosing;
        _toolsWindow = toolsWindow;
        return toolsWindow;
    }

    private void ShowToolsWindow()
    {
        if (_isClosing || !_isLoaded || !IsVisible || WindowState == WindowState.Minimized)
            return;

        _viewModel.SetToolsVisibility(true);
        ViewDeviceToolsWindow toolsWindow = EnsureToolsWindow();
        UpdateToolsWindowPosition();
        if (!toolsWindow.IsVisible)
            toolsWindow.Show();
        UpdateToolsWindowPosition();
    }

    private void HideToolsWindow()
    {
        _toolsWindow?.DismissDetailWindow();
        _viewModel.CloseToolEditor();
        _viewModel.SetToolsVisibility(false);
        if (_toolsWindow?.IsVisible == true)
            _toolsWindow.Hide();
    }

    private void OnToolsWindowClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (!_isClosing)
            eventArgs.Cancel = true;
    }

    private void CloseToolsWindow()
    {
        _toolsWindow?.DismissDetailWindow();
        _viewModel.SetToolsVisibility(false);
        ViewDeviceToolsWindow? toolsWindow = _toolsWindow;
        _toolsWindow = null;
        _toolsEnabled = false;
        SettingsButton.Tag = false;

        if (toolsWindow is null)
            return;

        toolsWindow.CloseDetailWindow();
        toolsWindow.Closing -= OnToolsWindowClosing;
        toolsWindow.DataContext = null;
        if (toolsWindow.IsLoaded)
            toolsWindow.Close();
    }

    private void UpdateToolsWindowPosition()
    {
        if (_toolsWindow is null ||
            !_toolsEnabled ||
            !_isLoaded ||
            _isClosing ||
            !IsVisible ||
            WindowState == WindowState.Minimized)
        {
            return;
        }

        Rect workArea = ViewDeviceMonitorWorkArea.GetFor(this);
        double ownerWidth = ActualWidth > 0 ? ActualWidth : Width;
        double ownerHeight = ActualHeight > 0 ? ActualHeight : Height;
        if (!double.IsFinite(ownerWidth) ||
            !double.IsFinite(ownerHeight) ||
            ownerWidth <= 0 ||
            ownerHeight <= 0)
        {
            return;
        }

        double toolsHeight = Math.Max(_toolsWindow.MinHeight, ownerHeight);
        if (workArea.Height > 0)
            toolsHeight = Math.Min(toolsHeight, Math.Max(_toolsWindow.MinHeight, workArea.Height));
        _toolsWindow.Height = toolsHeight;

        double toolsWidth = _toolsWindow.Width;
        if (!double.IsFinite(toolsWidth) || toolsWidth <= 0)
            toolsWidth = 310;

        double ownerLeft = double.IsFinite(Left) ? Left : workArea.Left;
        double ownerTop = double.IsFinite(Top) ? Top : workArea.Top;
        Point position = ViewDeviceToolsPlacement.Calculate(
            new Rect(ownerLeft, ownerTop, ownerWidth, ownerHeight),
            new Size(toolsWidth, toolsHeight),
            workArea,
            ToolsWindowVisibleGap,
            ToolsWindowShadowMargin);

        _toolsWindow.Left = position.X;
        _toolsWindow.Top = position.Y;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (_isClosing)
            return;

        switch (eventArgs.PropertyName)
        {
            case nameof(ViewDeviceViewModel.DeviceName):
                Title = ViewDeviceWindowService.FormatWindowTitle(
                    _viewModel.Serial,
                    _viewModel.DeviceName);
                break;
            case nameof(ViewDeviceViewModel.DeviceAspectRatio):
                FitWindowToDeviceAspect();
                break;
            case nameof(ViewDeviceViewModel.ViewRotationQuarterTurns):
                ApplyLocalViewRotation();
                break;
            case nameof(ViewDeviceViewModel.IsRunning):
                if (_viewModel.IsRunning)
                    FocusDisplay();
                break;
        }
    }

    private void FitWindowToDeviceAspect()
    {
        double aspectRatio = _viewModel.ViewAspectRatio;
        if (!_isLoaded ||
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
        double availableStreamWidth = Math.Max(1, workArea.Width - horizontalChrome);
        double availableStreamHeight = Math.Max(1, workArea.Height - verticalChrome);
        double currentStreamHeight = StreamContainer.ActualHeight > 0
            ? StreamContainer.ActualHeight
            : Math.Max(1, ActualHeight - verticalChrome);
        double desiredStreamHeight = Math.Min(currentStreamHeight, availableStreamHeight);
        double desiredStreamWidth = desiredStreamHeight * aspectRatio;
        if (desiredStreamWidth > availableStreamWidth)
        {
            double scale = availableStreamWidth / desiredStreamWidth;
            desiredStreamWidth *= scale;
            desiredStreamHeight *= scale;
        }

        ApplyWindowBounds(
            Math.Max(MinWidth, desiredStreamWidth + horizontalChrome),
            Math.Max(MinHeight, desiredStreamHeight + verticalChrome),
            workArea);
    }

    private void ApplyLocalViewRotation()
    {
        ViewRotationTransform.Angle = _viewModel.ViewRotationAngle;
        if (!_isLoaded ||
            _isClosing ||
            WindowState != WindowState.Normal ||
            StreamContainer.ActualWidth <= 0 ||
            StreamContainer.ActualHeight <= 0)
        {
            return;
        }

        double streamWidth = StreamContainer.ActualWidth;
        double streamHeight = StreamContainer.ActualHeight;
        double horizontalChrome = Math.Max(0, ActualWidth - streamWidth);
        double verticalChrome = Math.Max(0, ActualHeight - streamHeight);
        double desiredStreamWidth = streamHeight;
        double desiredStreamHeight = streamWidth;
        Rect workArea = ViewDeviceMonitorWorkArea.GetFor(this);
        double availableStreamWidth = Math.Max(1, workArea.Width - horizontalChrome);
        double availableStreamHeight = Math.Max(1, workArea.Height - verticalChrome);
        double scale = Math.Min(
            1,
            Math.Min(
                availableStreamWidth / desiredStreamWidth,
                availableStreamHeight / desiredStreamHeight));
        if (double.IsFinite(scale) && scale > 0 && scale < 1)
        {
            desiredStreamWidth *= scale;
            desiredStreamHeight *= scale;
        }

        ApplyWindowBounds(
            Math.Max(MinWidth, desiredStreamWidth + horizontalChrome),
            Math.Max(MinHeight, desiredStreamHeight + verticalChrome),
            workArea);
        UpdateToolsWindowPosition();
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
        Key physicalKey = NormalizePhysicalKey(eventArgs.Key, eventArgs.SystemKey);
        ModifierKeys modifiers = eventArgs.KeyboardDevice.Modifiers;
        ViewDeviceShortcutResolution resolution = _shortcutResolver.Resolve(
            physicalKey,
            modifiers,
            eventArgs.IsRepeat);
        if (!resolution.Consume)
            return;

        ConsumeShortcut(eventArgs);
        if (ViewDeviceShortcutPolicy.ShouldExecute(resolution, eventArgs.IsRepeat))
        {
            ExecuteShortcut(resolution.Action);
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs eventArgs)
    {
        Key physicalKey = NormalizePhysicalKey(eventArgs.Key, eventArgs.SystemKey);
        if (physicalKey is Key.LeftAlt or Key.RightAlt)
            _shortcutResolver.Reset();
    }

    private void ExecuteShortcut(ViewDeviceShortcutAction action)
    {
        switch (action)
        {
            case ViewDeviceShortcutAction.Home:
                _viewModel.HomeCommand.Execute(null);
                break;
            case ViewDeviceShortcutAction.Back:
                _viewModel.BackCommand.Execute(null);
                break;
            case ViewDeviceShortcutAction.Recent:
                _viewModel.RecentCommand.Execute(null);
                break;
            case ViewDeviceShortcutAction.VolumeUp:
                _viewModel.VolumeUpCommand.Execute(null);
                break;
            case ViewDeviceShortcutAction.VolumeDown:
                _viewModel.VolumeDownCommand.Execute(null);
                break;
            case ViewDeviceShortcutAction.Power:
                _viewModel.PowerCommand.Execute(null);
                break;
            case ViewDeviceShortcutAction.ScreenOff:
                _viewModel.ScreenOffCommand.Execute(null);
                break;
            case ViewDeviceShortcutAction.ScreenOn:
                _viewModel.ScreenOnCommand.Execute(null);
                break;
            case ViewDeviceShortcutAction.Copy:
                _viewModel.CopyClipboardCommand.Execute(null);
                break;
            case ViewDeviceShortcutAction.Cut:
                _viewModel.CutClipboardCommand.Execute(null);
                break;
            case ViewDeviceShortcutAction.Paste:
                _viewModel.PasteHostClipboardCommand.Execute(null);
                break;
            case ViewDeviceShortcutAction.PasteWithPasteKey:
                _viewModel.PasteHostClipboardWithPasteKeyCommand.Execute(null);
                break;
            case ViewDeviceShortcutAction.InjectClipboardText:
                _viewModel.InjectHostClipboardCommand.Execute(null);
                break;
            case ViewDeviceShortcutAction.ExpandNotifications:
                _viewModel.ExpandNotificationPanelCommand.Execute(null);
                break;
            case ViewDeviceShortcutAction.ExpandSettings:
                _viewModel.ExpandSettingsPanelCommand.Execute(null);
                break;
            case ViewDeviceShortcutAction.CollapsePanels:
                _viewModel.CollapsePanelsCommand.Execute(null);
                break;
            case ViewDeviceShortcutAction.Menu:
                _viewModel.MenuCommand.Execute(null);
                break;
        }
    }

    private void OnPreviewDragEnter(object sender, DragEventArgs eventArgs)
    {
        UpdatePackageDropState(eventArgs);
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        _toolsWindow?.DismissDetailWindow();
    }

    private void OnPreviewDragLeave(object sender, DragEventArgs eventArgs)
    {
        PackageDropOverlay.Visibility = Visibility.Collapsed;
        eventArgs.Effects = DragDropEffects.None;
        eventArgs.Handled = true;
    }

    private void OnPreviewDragOver(object sender, DragEventArgs eventArgs)
    {
        UpdatePackageDropState(eventArgs);
    }

    private void UpdatePackageDropState(DragEventArgs eventArgs)
    {
        bool canInstall = CanAcceptPackageDrop(_viewModel, eventArgs.Data);
        eventArgs.Effects = canInstall
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        PackageDropOverlay.Visibility = canInstall
            ? Visibility.Visible
            : Visibility.Collapsed;
        eventArgs.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs eventArgs)
    {
        PackageDropOverlay.Visibility = Visibility.Collapsed;
        if (CanAcceptPackageDrop(_viewModel, eventArgs.Data) &&
            TryGetSupportedPackagePath(eventArgs.Data, out string path))
            _viewModel.InstallPackageCommand.Execute(path);

        eventArgs.Effects = DragDropEffects.None;
        eventArgs.Handled = true;
    }

    internal static bool CanAcceptPackageDrop(
        ViewDeviceViewModel viewModel,
        IDataObject data)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        return TryGetSupportedPackagePath(data, out string path) &&
               viewModel.InstallPackageCommand.CanExecute(path);
    }

    internal static bool TryGetSupportedPackagePath(
        IDataObject data,
        out string path)
    {
        path = string.Empty;
        if (!data.GetDataPresent(DataFormats.FileDrop))
            return false;

        if (data.GetData(DataFormats.FileDrop) is not string[] files ||
            files.Length != 1 ||
            string.IsNullOrWhiteSpace(files[0]) ||
            !ViewDeviceViewModel.IsSupportedPackagePath(files[0]))
        {
            return false;
        }

        path = files[0];
        return true;
    }

    private static void ConsumeShortcut(KeyEventArgs eventArgs)
    {
        eventArgs.Handled = true;
    }

    internal static Key NormalizePhysicalKey(Key key, Key systemKey)
    {
        return key == Key.System ? systemKey : key;
    }
}
