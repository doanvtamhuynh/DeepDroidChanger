using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DeepDroidChanger.ViewModels;
using MaterialDesignThemes.Wpf;

namespace DeepDroidChanger.Views;

public partial class ViewMultipleDevicesControlPanel : UserControl
{
    private const string DeviceDragFormat = "DeepDroidChanger.MultipleDevices.DeviceSerial";
    private Canvas? _overlay;
    private bool _isCollapsed;
    private bool _positioned;
    private double _expandedWidth;
    private double _expandedHeightLimit;
    private Point? _dragStart;
    private Border? _dropTarget;

    public ViewMultipleDevicesControlPanel()
    {
        InitializeComponent();
        Width = _expandedWidth = Metric("PanelWidth");
        _expandedHeightLimit = MaxHeight = Metric("PanelHeight");
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnPanelSizeChanged;
    }

    private double Metric(string key) => (double)FindResource($"ViewMultipleDevices.{key}");

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        DisplaySection.IsExpanded = false;
        LayoutSection.IsExpanded = false;
        SelectionSection.IsExpanded = false;
        ToolsSection.IsExpanded = false;
        if (DataContext is ViewMultipleDevicesViewModel viewModel)
            viewModel.IsOrderEditorOpen = false;

        if (_overlay is not null)
            _overlay.SizeChanged -= OnOverlaySizeChanged;
        _overlay = Parent as Canvas;
        if (_overlay is null)
            return;

        _overlay.SizeChanged += OnOverlaySizeChanged;
        FitInsideOverlay();
        if (!_positioned)
        {
            Canvas.SetLeft(this, Math.Max(0, _overlay.ActualWidth - Width));
            Canvas.SetTop(this, 0);
            _positioned = true;
        }
        ClampPosition();
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_overlay is not null)
            _overlay.SizeChanged -= OnOverlaySizeChanged;
        _overlay = null;
        ClearDropTarget();
        _dragStart = null;
    }

    private void OnOverlaySizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        FitInsideOverlay();
        ClampPosition();
    }

    private void OnPanelSizeChanged(object sender, SizeChangedEventArgs eventArgs) => ClampPosition();

    private void OnOpenToolEditorClick(object sender, RoutedEventArgs eventArgs)
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() => ToolEditorPanel.BringIntoView()));
    }

    private void OnPanelPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        // ToggleButton handles Space itself; Enter should also expand the focused section.
        if (eventArgs.Key == Key.Enter &&
            eventArgs.OriginalSource is ToggleButton { Name: "SectionHeader", TemplatedParent: Expander section })
        {
            section.IsExpanded = !section.IsExpanded;
            eventArgs.Handled = true;
        }
    }

    private void FitInsideOverlay()
    {
        if (_overlay is null || _overlay.ActualWidth <= 0 || _overlay.ActualHeight <= 0)
            return;

        Width = Math.Min(_isCollapsed ? Metric("PanelCollapsedWidth") : _expandedWidth, _overlay.ActualWidth);
        MaxHeight = Math.Min(_isCollapsed ? Metric("PanelHeaderHeight") : _expandedHeightLimit, _overlay.ActualHeight);
        Height = _isCollapsed ? MaxHeight : double.NaN;
    }

    private void ClampPosition()
    {
        if (_overlay is null)
            return;

        double left = Canvas.GetLeft(this);
        double top = Canvas.GetTop(this);
        Canvas.SetLeft(this, Math.Clamp(double.IsNaN(left) ? 0 : left, 0, Math.Max(0, _overlay.ActualWidth - ActualWidth)));
        Canvas.SetTop(this, Math.Clamp(double.IsNaN(top) ? 0 : top, 0, Math.Max(0, _overlay.ActualHeight - ActualHeight)));
    }

    private void MovePanel(double horizontal, double vertical)
    {
        Canvas.SetLeft(this, Canvas.GetLeft(this) + horizontal);
        Canvas.SetTop(this, Canvas.GetTop(this) + vertical);
        ClampPosition();
    }

    private void OnMoveDelta(object sender, DragDeltaEventArgs eventArgs)
    {
        MovePanel(eventArgs.HorizontalChange, eventArgs.VerticalChange);
        eventArgs.Handled = true;
    }

    private void ResizePanel(double horizontal, double vertical)
    {
        if (_overlay is null || _isCollapsed)
            return;

        double availableWidth = Math.Max(1, _overlay.ActualWidth - Canvas.GetLeft(this));
        double availableHeight = Math.Max(1, _overlay.ActualHeight);
        _expandedWidth = Math.Clamp(Width + horizontal, Math.Min(Metric("PanelMinWidth"), availableWidth), availableWidth);
        _expandedHeightLimit = Math.Clamp(_expandedHeightLimit + vertical, Math.Min(Metric("PanelMinHeight"), availableHeight), availableHeight);
        FitInsideOverlay();
        ClampPosition();
    }

    private void OnResizeDelta(object sender, DragDeltaEventArgs eventArgs)
    {
        ResizePanel(eventArgs.HorizontalChange, eventArgs.VerticalChange);
        eventArgs.Handled = true;
    }

    private void OnResizeStarted(object sender, DragStartedEventArgs eventArgs)
    {
        if (!_isCollapsed)
            _expandedHeightLimit = Math.Min(_expandedHeightLimit, ActualHeight);
    }

    private void OnToggleCollapsed(object sender, RoutedEventArgs eventArgs)
    {
        _isCollapsed = !_isCollapsed;
        PanelBody.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ResizeHandle.Visibility = PanelBody.Visibility;
        OnlineSummary.Visibility = PanelBody.Visibility;
        CollapseIcon.Kind = _isCollapsed ? PackIconKind.ChevronDoubleLeft : PackIconKind.ChevronDoubleRight;
        string key = _isCollapsed ? "ViewMultipleDevices_ExpandPanel" : "ViewMultipleDevices_CollapsePanel";
        CollapseButton.SetResourceReference(ToolTipProperty, key);
        CollapseButton.SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty, key);
        FitInsideOverlay();
        ClampPosition();
    }

    private void OnMoveKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (TryGetArrowDelta(eventArgs.Key, out Vector delta))
        {
            MovePanel(delta.X, delta.Y);
            eventArgs.Handled = true;
        }
    }

    private void OnResizeKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (TryGetArrowDelta(eventArgs.Key, out Vector delta))
        {
            ResizePanel(delta.X, delta.Y);
            eventArgs.Handled = true;
        }
    }

    private static bool TryGetArrowDelta(Key key, out Vector delta)
    {
        delta = key switch
        {
            Key.Left => new Vector(-12, 0), Key.Right => new Vector(12, 0),
            Key.Up => new Vector(0, -12), Key.Down => new Vector(0, 12),
            _ => default
        };
        return delta != default;
    }

    private void OnOrderDragStart(object sender, MouseButtonEventArgs eventArgs)
    {
        _dragStart = eventArgs.GetPosition(this);
    }

    private void OnOrderDragMove(object sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.LeftButton != MouseButtonState.Pressed)
        {
            _dragStart = null;
            return;
        }
        if (_dragStart is not Point start || sender is not FrameworkElement { DataContext: ViewMultipleDeviceItemViewModel item } ||
            DataContext is not ViewMultipleDevicesViewModel { SortIndex: 0, CanChangeLayout: true })
            return;

        Point current = eventArgs.GetPosition(this);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragStart = null;
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DeviceDragFormat, item.Serial), DragDropEffects.Move);
        }
        finally
        {
            ClearDropTarget();
        }
        eventArgs.Handled = true;
    }

    private bool CanDrop(DragEventArgs eventArgs) =>
        DataContext is ViewMultipleDevicesViewModel { SortIndex: 0, CanChangeLayout: true } viewModel &&
        eventArgs.Data.GetData(DeviceDragFormat) is string serial &&
        viewModel.OrderedDevices.Any(item => string.Equals(item.Serial, serial, StringComparison.OrdinalIgnoreCase));

    private void OnOrderDragOver(object sender, DragEventArgs eventArgs)
    {
        bool canDrop = CanDrop(eventArgs);
        eventArgs.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
        if (canDrop && sender is Border border)
        {
            ClearDropTarget();
            _dropTarget = border;
            border.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
            border.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceHover");
            Point point = eventArgs.GetPosition(PanelBody);
            if (point.Y < 36)
                PanelBody.ScrollToVerticalOffset(PanelBody.VerticalOffset - 12);
            else if (point.Y > PanelBody.ActualHeight - 36)
                PanelBody.ScrollToVerticalOffset(PanelBody.VerticalOffset + 12);
        }
        eventArgs.Handled = true;
    }

    private void OnOrderDragLeave(object sender, DragEventArgs eventArgs) => ClearDropTarget();

    private void OnOrderDrop(object sender, DragEventArgs eventArgs)
    {
        if (CanDrop(eventArgs) && sender is Border { DataContext: ViewMultipleDeviceItemViewModel target } border &&
            DataContext is ViewMultipleDevicesViewModel viewModel &&
            eventArgs.Data.GetData(DeviceDragFormat) is string serial)
        {
            viewModel.MoveOrderedDevice(serial, target.Serial, eventArgs.GetPosition(border).Y >= border.ActualHeight / 2);
            eventArgs.Effects = DragDropEffects.Move;
        }
        ClearDropTarget();
        eventArgs.Handled = true;
    }

    private void ClearDropTarget()
    {
        _dropTarget?.ClearValue(Border.BorderBrushProperty);
        _dropTarget?.ClearValue(Border.BackgroundProperty);
        _dropTarget = null;
    }

    private void OnOrderMoveKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (Keyboard.Modifiers == ModifierKeys.Alt && eventArgs.Key is Key.Up or Key.Down &&
            sender is FrameworkElement { DataContext: ViewMultipleDeviceItemViewModel item } &&
            DataContext is ViewMultipleDevicesViewModel viewModel)
        {
            viewModel.MoveOrderedDeviceBy(item.Serial, eventArgs.Key == Key.Up ? -1 : 1);
            eventArgs.Handled = true;
            ((FrameworkElement)sender).BringIntoView();
        }
    }
}
