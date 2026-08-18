using DeepDroidChanger.ViewModels;
using Microsoft.Extensions.Logging;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeepDroidChanger.Views
{
    public sealed partial class ChangeMultipleDevicesView : UserControl
    {
        private readonly ChangeMultipleDevicesViewModel _viewModel;
        private readonly ILogger<ChangeMultipleDevicesView> _logger;
        private CancellationTokenSource? _viewCancellation;
        private bool _isActive;

        public ChangeMultipleDevicesView(
            ChangeMultipleDevicesViewModel viewModel,
            ILogger<ChangeMultipleDevicesView> logger)
        {
            _viewModel = viewModel;
            _logger = logger;
            InitializeComponent();
            DataContext = viewModel;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_isActive)
                return;

            _isActive = true;
            _viewModel.RunningActions.CollectionChanged += OnRunningActionsCollectionChanged;
            var viewCancellation = new CancellationTokenSource();
            _viewCancellation = viewCancellation;
            try
            {
                await _viewModel.InitializeAsync(viewCancellation.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (viewCancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to initialize the Multiple Devices view.");
                if (ReferenceEquals(_viewCancellation, viewCancellation))
                {
                    _viewCancellation = null;
                    _isActive = false;
                    _viewModel.RunningActions.CollectionChanged -= OnRunningActionsCollectionChanged;
                    viewCancellation.Dispose();
                }
            }
        }

        private async void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (!_isActive)
                return;

            _isActive = false;
            _viewModel.RunningActions.CollectionChanged -= OnRunningActionsCollectionChanged;
            CancellationTokenSource? viewCancellation = _viewCancellation;
            _viewCancellation = null;
            try
            {
                viewCancellation?.Cancel();
                await _viewModel.SuspendAsync().ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to suspend the Multiple Devices view.");
            }
            finally
            {
                viewCancellation?.Dispose();
            }
        }

        private void OnRunningActionsCollectionChanged(
            object? sender,
            NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Remove &&
                e.Action != NotifyCollectionChangedAction.Replace &&
                e.Action != NotifyCollectionChangedAction.Reset)
            {
                return;
            }

            double verticalOffset =
                MultipleDeviceProfilePanelScrollViewer.VerticalOffset;

            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    if (!_isActive)
                        return;

                    double targetOffset = Math.Min(
                        verticalOffset,
                        MultipleDeviceProfilePanelScrollViewer.ScrollableHeight);

                    MultipleDeviceProfilePanelScrollViewer
                        .ScrollToVerticalOffset(targetOffset);
                }));
        }

        private void OnDeviceGridPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row == null)
                return;

            row.IsSelected = true;
            row.Focus();
        }

        private async void OnDeviceRowContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not DataGridRow { DataContext: DeviceRowViewModel device })
                return;

            await _viewModel.RefreshContextMenuStateCommand
                .ExecuteAsync(device)
                .ConfigureAwait(true);
        }

        private static T? FindVisualParent<T>(DependencyObject? dependencyObject)
            where T : DependencyObject
        {
            while (dependencyObject != null)
            {
                if (dependencyObject is T target)
                    return target;

                dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
            }

            return null;
        }
    }
}
