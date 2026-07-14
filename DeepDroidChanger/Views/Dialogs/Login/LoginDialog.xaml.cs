using DeepDroidChanger.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;

namespace DeepDroidChanger.Views
{
    public sealed partial class LoginDialog : Window
    {
        private bool _isPasswordRevealed;
        private bool _isCancelingForClose;
        private bool _allowClose;

        public LoginDialog()
        {
            InitializeComponent();
        }

        public void SetPassword(string password)
        {
            LoginPasswordBox.Password = password;
            LoginPasswordTextBox.Text = password;
            UpdatePasswordPlaceholder();
        }

        public void CompleteDialog(bool result)
        {
            _allowClose = true;
            DialogResult = result;
        }

        protected override async void OnClosing(CancelEventArgs e)
        {
            if (_allowClose || DataContext is not LoginViewModel { IsLoggingIn: true } viewModel)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            if (_isCancelingForClose)
                return;

            _isCancelingForClose = true;
            viewModel.SignInCommand.Cancel();
            try
            {
                if (viewModel.SignInCommand.ExecutionTask is { } signInTask)
                    await signInTask.ConfigureAwait(true);
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

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is LoginViewModel viewModel && !_isPasswordRevealed)
                viewModel.Password = LoginPasswordBox.Password;

            UpdatePasswordPlaceholder();
        }

        private void OnPasswordTextBoxChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is LoginViewModel viewModel && _isPasswordRevealed)
                viewModel.Password = LoginPasswordTextBox.Text;
        }

        private void OnRevealPasswordClick(object sender, RoutedEventArgs e)
        {
            _isPasswordRevealed = !_isPasswordRevealed;
            if (_isPasswordRevealed)
            {
                LoginPasswordTextBox.Text = LoginPasswordBox.Password;
                LoginPasswordBox.Visibility = Visibility.Collapsed;
                LoginPasswordTextBox.Visibility = Visibility.Visible;
                LoginPasswordTextBox.Focus();
                LoginPasswordTextBox.SelectionStart = LoginPasswordTextBox.Text.Length;

                RevealIcon.Kind = PackIconKind.EyeOffOutline;
            }
            else
            {
                LoginPasswordBox.Password = LoginPasswordTextBox.Text;
                LoginPasswordTextBox.Visibility = Visibility.Collapsed;
                LoginPasswordBox.Visibility = Visibility.Visible;
                LoginPasswordBox.Focus();

                RevealIcon.Kind = PackIconKind.EyeOutline;
            }
        }

        private void UpdatePasswordPlaceholder()
        {
            if (string.IsNullOrEmpty(LoginPasswordBox.Password))
            {
                LoginPasswordBox.Tag = TryFindResource("Login_PasswordHint") as string ?? string.Empty;
            }
            else
            {
                LoginPasswordBox.Tag = string.Empty;
            }
        }
    }
}
