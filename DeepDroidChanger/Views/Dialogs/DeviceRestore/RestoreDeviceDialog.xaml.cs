using DeepDroidChanger.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace DeepDroidChanger.Views;

public sealed partial class RestoreDeviceDialog : Window
{
    public RestoreDeviceDialog()
    {
        InitializeComponent();
    }

    private void OnRestorePasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is RestoreDeviceViewModel viewModel
            && sender is PasswordBox passwordBox)
        {
            viewModel.RestorePassword = passwordBox.Password;
        }
    }

    public void ClearSensitiveInputs()
    {
        RestorePasswordBox.Clear();
    }
}
