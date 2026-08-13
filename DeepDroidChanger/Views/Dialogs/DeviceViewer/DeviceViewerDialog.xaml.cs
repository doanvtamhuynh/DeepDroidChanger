using DeepDroidChanger.Models;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Helpers;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DeepDroidChanger.Views
{
    public sealed partial class DeviceViewerDialog : Window
    {
        private const double FallbackDeviceAspectRatio = 9.0 / 20.0;
        private const double CollapsedWindowMinWidth = 350;
        private const double DeviceAreaMinimum = 272;
        private const double ExpandedActionsPanelWidth = 315;
        private const double CollapsedActionsRailWidth = 52;
        private const double ExpandedWindowMinWidth =
            DeviceAreaMinimum + ExpandedActionsPanelWidth;
        private bool _boundsChangedPending;
        private bool _visibilityChangedPending;
        private bool _readyPending;
        private DeviceViewerViewModel? _deviceViewerViewModel;

        public DeviceViewerDialog()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            LocationChanged += OnWindowLocationChanged;
            SizeChanged += OnWindowSizeChanged;
            StateChanged += OnWindowStateChanged;
            IsVisibleChanged += OnWindowIsVisibleChanged;
            DeviceViewerBounds.SizeChanged += OnDeviceViewerBoundsSizeChanged;
            DataContextChanged += OnDataContextChanged;
        }

        public event EventHandler? ViewerBoundsReady;
        public event EventHandler? ViewerBoundsChanged;
        public event EventHandler? ViewerVisibilityChanged;

        internal System.Threading.Tasks.Task LastStartTask { get; set; } = System.Threading.Tasks.Task.CompletedTask;

        public IntPtr NativeOwnerHandle => new WindowInteropHelper(this).Handle;

        public bool IsViewerVisibleForNativeWindow =>
            IsLoaded &&
            IsVisible &&
            WindowState != WindowState.Minimized &&
            DeviceViewerBounds.IsVisible &&
            DeviceViewerBounds.ActualWidth > 0 &&
            DeviceViewerBounds.ActualHeight > 0;

        public void RefreshStreamLayout()
        {
            ResizeStreamFrame(StreamContainer.ActualWidth, StreamContainer.ActualHeight);
            QueueViewerStateChanged();
        }

        public bool TryGetViewerBounds(out DeviceViewerStreamBounds bounds)
        {
            bounds = default;

            if (!IsViewerVisibleForNativeWindow)
                return false;

            var source = PresentationSource.FromVisual(DeviceViewerBounds);
            if (source?.CompositionTarget == null)
                return false;

            var (scaleX, scaleY) = DpiHelper.GetDpiScale(DeviceViewerBounds);
            var width = DpiHelper.ToPhysicalPixels(DeviceViewerBounds.ActualWidth, scaleX);
            var height = DpiHelper.ToPhysicalPixels(DeviceViewerBounds.ActualHeight, scaleY);
            if (width <= 0 || height <= 0)
                return false;

            try
            {
                var screenPoint = DeviceViewerBounds.PointToScreen(new Point(0, 0));
                bounds = new DeviceViewerStreamBounds(
                    (int)Math.Round(screenPoint.X),
                    (int)Math.Round(screenPoint.Y),
                    width,
                    height);
                return bounds.IsValid();
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshStreamLayout();
            QueueViewerBoundsReady();
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
            RefreshStreamLayout();
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

        private void OnWindowLocationChanged(object? sender, EventArgs e)
        {
            QueueViewerBoundsChanged();
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            QueueViewerStateChanged();
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            QueueViewerStateChanged();
        }

        private void OnWindowIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            QueueViewerStateChanged();
        }

        private void OnDeviceViewerBoundsSizeChanged(object sender, SizeChangedEventArgs e)
        {
            QueueViewerStateChanged();
        }

        private void StreamContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ResizeStreamFrame(e.NewSize.Width, e.NewSize.Height);
            QueueViewerStateChanged();
        }

        private void QueueViewerBoundsReady()
        {
            if (_readyPending)
                return;

            _readyPending = true;
            Dispatcher.InvokeAsync(() =>
            {
                _readyPending = false;
                ViewerBoundsReady?.Invoke(this, EventArgs.Empty);
            }, DispatcherPriority.Render);
        }

        private void QueueViewerBoundsChanged()
        {
            if (_boundsChangedPending)
                return;

            _boundsChangedPending = true;
            Dispatcher.InvokeAsync(() =>
            {
                _boundsChangedPending = false;
                ViewerBoundsChanged?.Invoke(this, EventArgs.Empty);
            }, DispatcherPriority.Render);
        }

        private void QueueViewerVisibilityChanged()
        {
            if (_visibilityChangedPending)
                return;

            _visibilityChangedPending = true;
            Dispatcher.InvokeAsync(() =>
            {
                _visibilityChangedPending = false;
                ViewerVisibilityChanged?.Invoke(this, EventArgs.Empty);
            }, DispatcherPriority.Render);
        }

        private void QueueViewerStateChanged()
        {
            QueueViewerBoundsChanged();
            QueueViewerVisibilityChanged();
        }

        private void ResizeStreamFrame(double containerWidth, double containerHeight)
        {
            if (containerWidth <= 0 || containerHeight <= 0)
                return;

            var targetRatio = (DataContext as DeviceViewerViewModel)?.DeviceAspectRatio
                              ?? FallbackDeviceAspectRatio;
            if (targetRatio <= 0)
                targetRatio = FallbackDeviceAspectRatio;

            var containerRatio = containerWidth / containerHeight;
            double targetWidth;
            double targetHeight;

            if (containerRatio > targetRatio)
            {
                targetWidth = containerHeight * targetRatio;
                targetHeight = containerHeight;
            }
            else
            {
                targetWidth = containerWidth;
                targetHeight = containerWidth / targetRatio;
            }

            StreamFrame.Width = targetWidth;
            StreamFrame.Height = targetHeight;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            Loaded -= OnLoaded;
            LocationChanged -= OnWindowLocationChanged;
            SizeChanged -= OnWindowSizeChanged;
            StateChanged -= OnWindowStateChanged;
            IsVisibleChanged -= OnWindowIsVisibleChanged;
            DeviceViewerBounds.SizeChanged -= OnDeviceViewerBoundsSizeChanged;
            DataContextChanged -= OnDataContextChanged;
            if (_deviceViewerViewModel != null)
                _deviceViewerViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            base.OnClosing(e);
        }
    }
}
