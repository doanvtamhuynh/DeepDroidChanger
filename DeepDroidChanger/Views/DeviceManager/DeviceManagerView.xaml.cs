using DeepDroidChanger.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DeepDroidChanger.Views
{
    public sealed partial class DeviceManagerView : UserControl
    {
        private readonly DeviceManagerViewModel _viewModel;
        private CancellationTokenSource? _viewCancellation;
        private bool _isActive;

        public DeviceManagerView(DeviceManagerViewModel viewModel)
        {
            _viewModel = viewModel;
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
            _viewCancellation = new CancellationTokenSource();
            try
            {
                await _viewModel.InitializeAsync(_viewCancellation.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                _isActive = false;
                _viewCancellation.Dispose();
                _viewCancellation = null;
                throw;
            }
        }

        private async void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (!_isActive)
                return;

            _isActive = false;
            try
            {
                _viewCancellation?.Cancel();
                await _viewModel.DeactivateAsync().ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _viewCancellation?.Dispose();
                _viewCancellation = null;
            }
        }

        private void OnDeviceGridPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row == null)
                return;

            row.IsSelected = true;
            row.Focus();
        }

        private static T? FindVisualParent<T>(DependencyObject? dependencyObject)
            where T : DependencyObject
        {
            while (dependencyObject != null)
            {
                if (dependencyObject is T parent)
                    return parent;

                dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
            }

            return null;
        }
    }
}
