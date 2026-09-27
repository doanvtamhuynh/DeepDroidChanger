using DeepDroidChanger.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace DeepDroidChanger.Views;

public sealed partial class BackupDeviceDialog : Window
{
    public BackupDeviceDialog()
    {
        InitializeComponent();
    }

    private void OnBackupPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is BackupDeviceViewModel viewModel
            && sender is PasswordBox passwordBox)
        {
            viewModel.BackupPassword = passwordBox.Password;
        }
    }

    private void OnBackupPasswordConfirmationChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is BackupDeviceViewModel viewModel
            && sender is PasswordBox passwordBox)
        {
            viewModel.BackupPasswordConfirmation = passwordBox.Password;
        }
    }

    public void ClearSensitiveInputs()
    {
        BackupPasswordBox.Clear();
        BackupPasswordConfirmationBox.Clear();
    }
}
