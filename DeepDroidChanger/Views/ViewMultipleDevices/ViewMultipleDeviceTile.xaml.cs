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

    public ViewMultipleDeviceTile()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
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
            eventArgs.PropertyName == nameof(ViewMultipleDeviceItemViewModel.DeviceAspectRatio))
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
        Key physicalKey = eventArgs.Key == Key.System
            ? eventArgs.SystemKey
            : eventArgs.Key;
        if (!ViewMultipleDeviceShortcutPolicy.IsExactHostPaste(
                physicalKey,
                eventArgs.KeyboardDevice.Modifiers))
        {
            return;
        }

        eventArgs.Handled = true;
        if (ViewMultipleDeviceShortcutPolicy.ShouldExecuteHostPaste(
                physicalKey,
                eventArgs.KeyboardDevice.Modifiers,
                eventArgs.IsRepeat))
            _viewModel?.PasteHostClipboardCommand.Execute(null);
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
