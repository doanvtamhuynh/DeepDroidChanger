using DeepDroidChanger.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace DeepDroidChanger.Views
{
    public sealed partial class ChangeMultipleDevicesView : UserControl
    {
        private readonly ChangeMultipleDevicesViewModel _viewModel;
        private CancellationTokenSource? _viewCancellation;
        private bool _isActive;

        public ChangeMultipleDevicesView(ChangeMultipleDevicesViewModel viewModel)
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
    }
}
