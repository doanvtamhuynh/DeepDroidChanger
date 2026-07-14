using DeepDroidChanger.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace DeepDroidChanger.Views
{
    public sealed partial class InstallPackageDialog : Window
    {
        private bool _isCancelingForClose;
        private bool _allowClose;

        public InstallPackageDialog()
        {
            InitializeComponent();
        }

        protected override async void OnClosing(CancelEventArgs e)
        {
            if (_allowClose || DataContext is not InstallPackageViewModel { IsInstalling: true } viewModel)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            if (_isCancelingForClose)
                return;

            _isCancelingForClose = true;
            viewModel.CancelCommand.Execute(null);
            try
            {
                if (viewModel.StartInstallCommand.ExecutionTask is { } installTask)
                    await installTask.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _allowClose = true;
                Close();
            }
        }
    }
}
