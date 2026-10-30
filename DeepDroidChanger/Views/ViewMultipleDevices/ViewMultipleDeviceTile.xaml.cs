using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DeepDroidChanger.ViewModels;

namespace DeepDroidChanger.Views;

public sealed partial class ViewMultipleDeviceTile : UserControl
{
    private ViewMultipleDeviceItemViewModel? _viewModel;
    private bool _isLoaded;
    private readonly ViewDeviceShortcutResolver _shortcutResolver = new();

    public ViewMultipleDeviceTile()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ScrcpyDisplay.GotKeyboardFocus += (_, _) => _viewModel?.Controls.Activate();
        ScrcpyDisplay.PreviewMouseDown += (_, _) => _viewModel?.Controls.Activate();
        ScrcpyDisplay.LostKeyboardFocus += (_, _) => _shortcutResolver.Reset();
        PreviewKeyUp += (_, args) =>
        {
            if (args.Key is Key.LeftAlt or Key.RightAlt || args.SystemKey is Key.LeftAlt or Key.RightAlt) _shortcutResolver.Reset();
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = eventArgs.NewValue as ViewMultipleDeviceItemViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        if (_isLoaded)
            UpdateStreamViewportLayout();
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        _isLoaded = true;
        UpdateStreamViewportLayout();
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        _isLoaded = false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is ViewMultipleDeviceItemViewModel item &&
            ReferenceEquals(item, _viewModel) &&
            eventArgs.PropertyName is nameof(ViewMultipleDeviceItemViewModel.DeviceAspectRatio) or
                nameof(ViewMultipleDeviceItemViewModel.ViewAspectRatio))
        {
            UpdateStreamViewportLayout();
        }
    }

    private void OnStreamViewportSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        UpdateStreamViewportLayout();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (!ScrcpyDisplay.IsKeyboardFocusWithin || _viewModel is null) return;
        if (Keyboard.IsKeyDown(Key.RightAlt) && !Keyboard.IsKeyDown(Key.LeftAlt)) return;
        Key physicalKey = eventArgs.Key == Key.System
            ? eventArgs.SystemKey
            : eventArgs.Key;
        ViewDeviceShortcutResolution resolution = _shortcutResolver.Resolve(physicalKey, eventArgs.KeyboardDevice.Modifiers, eventArgs.IsRepeat);
        if (!resolution.Consume) return;
        eventArgs.Handled = true;
        if (!ViewDeviceShortcutPolicy.ShouldExecute(resolution, eventArgs.IsRepeat)) return;
        ViewDeviceControlsViewModel controls = _viewModel.Controls;
        switch (resolution.Action)
        {
            case ViewDeviceShortcutAction.Paste: controls.PasteCommand.Execute(null); break;
            case ViewDeviceShortcutAction.Back: controls.BackCommand.Execute(null); break;
            case ViewDeviceShortcutAction.ScreenOff: controls.ScreenOffCommand.Execute(null); break;
            case ViewDeviceShortcutAction.ScreenOn: controls.ScreenOnCommand.Execute(null); break;
            case ViewDeviceShortcutAction.ExpandNotifications: controls.NotificationsCommand.Execute(null); break;
            case ViewDeviceShortcutAction.ExpandSettings: controls.SettingsPanelCommand.Execute(null); break;
            case ViewDeviceShortcutAction.CollapsePanels: controls.CollapsePanelsCommand.Execute(null); break;
            case ViewDeviceShortcutAction.Home: controls.SendKeyCommand.Execute(3); break;
            case ViewDeviceShortcutAction.Recent: controls.SendKeyCommand.Execute(187); break;
            case ViewDeviceShortcutAction.Power: controls.SendKeyCommand.Execute(26); break;
            case ViewDeviceShortcutAction.VolumeUp: controls.SendKeyCommand.Execute(24); break;
            case ViewDeviceShortcutAction.VolumeDown: controls.SendKeyCommand.Execute(25); break;
            case ViewDeviceShortcutAction.Menu: controls.SendKeyCommand.Execute(82); break;
        }
    }


    private void OnDragOver(object sender, DragEventArgs args)
    {
        string[]? files = ViewDeviceWindow.GetDroppedFiles(args.Data);
        args.Effects = files is not null && _viewModel?.Controls.DropFilesCommand.CanExecute(files) == true ? DragDropEffects.Copy : DragDropEffects.None;
        args.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs args)
    {
        string[]? files = ViewDeviceWindow.GetDroppedFiles(args.Data);
        if (files is not null && _viewModel?.Controls.DropFilesCommand.CanExecute(files) == true) _viewModel.Controls.DropFilesCommand.Execute(files);
        args.Handled = true;
    }

    private void UpdateStreamViewportLayout()
    {
        if (!_isLoaded)
            return;

        double width = StreamViewport.ActualWidth;
        if (!double.IsFinite(width) || width <= 0)
            return;

        double height = ViewMultipleDevicesLayout.CalculateStreamHeight(
            width,
            _viewModel?.ViewAspectRatio ?? ViewMultipleDevicesLayout.FallbackDeviceAspectRatio);
        if (height <= 0)
            return;

        if (double.IsNaN(StreamViewport.Height) ||
            Math.Abs(StreamViewport.Height - height) > 0.5)
        {
            StreamViewport.Height = height;
        }
        bool rotated = _viewModel?.ViewRotationAngle is 90 or 270;
        RotatedDisplayHost.Width = rotated ? height : width;
        RotatedDisplayHost.Height = rotated ? width : height;
    }
}
