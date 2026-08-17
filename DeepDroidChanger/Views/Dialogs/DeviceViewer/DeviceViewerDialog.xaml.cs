using System.ComponentModel;
using System.Windows;
using DeepDroidChanger.ViewModels;

namespace DeepDroidChanger.Views;

public sealed partial class DeviceViewerDialog : Window
{
    private const double DeviceAspectRatio = 9.0 / 20.0;
    private const double CollapsedWindowMinWidth = 350;
    private const double DeviceAreaMinimum = 272;
    private const double ExpandedActionsPanelWidth = 315;
    private const double CollapsedActionsRailWidth = 52;
    private const double ExpandedWindowMinWidth =
        DeviceAreaMinimum + ExpandedActionsPanelWidth;
    private DeviceViewerViewModel? _deviceViewerViewModel;

    public DeviceViewerDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void RefreshViewerLayout()
    {
        ResizeViewerFrame(ViewerContainer.ActualWidth, ViewerContainer.ActualHeight);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshViewerLayout();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_deviceViewerViewModel != null)
            _deviceViewerViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _deviceViewerViewModel = e.NewValue as DeviceViewerViewModel;
        if (_deviceViewerViewModel == null)
            return;

        _deviceViewerViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyActionsPanelLayout(_deviceViewerViewModel.IsActionsPanelExpanded);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceViewerViewModel.IsActionsPanelExpanded))
            ApplyActionsPanelLayout(_deviceViewerViewModel?.IsActionsPanelExpanded == true);
    }

    private void ApplyActionsPanelLayout(bool isExpanded)
    {
        var previousWidth = ActionsColumn.Width.IsAbsolute
            ? ActionsColumn.Width.Value
            : CollapsedActionsRailWidth;
        var nextWidth = isExpanded ? ExpandedActionsPanelWidth : CollapsedActionsRailWidth;
        var currentWindowWidth = !double.IsNaN(Width) && Width > 0
            ? Width
            : ActualWidth;
        if (currentWindowWidth <= 0)
            currentWindowWidth = isExpanded ? ExpandedWindowMinWidth : CollapsedWindowMinWidth;

        var nextMinWidth = CalculateWindowMinWidth(isExpanded);
        var desiredWindowWidth = CalculateDesiredWindowWidth(
            currentWindowWidth,
            previousWidth,
            nextWidth);

        ActionsColumn.Width = new GridLength(nextWidth);
        MinWidth = nextMinWidth;
        Width = Math.Max(nextMinWidth, desiredWindowWidth);

        UpdateLayout();
        RefreshViewerLayout();
    }

    internal static double CalculateWindowMinWidth(bool isExpanded)
    {
        return isExpanded ? ExpandedWindowMinWidth : CollapsedWindowMinWidth;
    }

    internal static double CalculateDesiredWindowWidth(
        double currentWindowWidth,
        double previousActionsWidth,
        double nextActionsWidth)
    {
        return currentWindowWidth + nextActionsWidth - previousActionsWidth;
    }

    private void ViewerContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ResizeViewerFrame(e.NewSize.Width, e.NewSize.Height);
    }

    private void ResizeViewerFrame(double containerWidth, double containerHeight)
    {
        var size = CalculateAspectFitSize(containerWidth, containerHeight, DeviceAspectRatio);
        if (size.Width <= 0 || size.Height <= 0)
            return;

        ViewerFrame.Width = size.Width;
        ViewerFrame.Height = size.Height;
    }

    internal static (double Width, double Height) CalculateAspectFitSize(
        double containerWidth,
        double containerHeight,
        double aspectRatio)
    {
        if (containerWidth <= 0 || containerHeight <= 0 || aspectRatio <= 0)
            return default;

        var containerRatio = containerWidth / containerHeight;
        return containerRatio > aspectRatio
            ? (containerHeight * aspectRatio, containerHeight)
            : (containerWidth, containerWidth / aspectRatio);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        Loaded -= OnLoaded;
        DataContextChanged -= OnDataContextChanged;
        if (_deviceViewerViewModel != null)
            _deviceViewerViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosing(e);
    }
}
