using DeepDroidChanger.Services;
using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
using System.IO;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.ViewModels
{
    public sealed partial class LoginViewModel : ObservableObject
    {
        private readonly IAccountStoreService _accountStoreService;
        private readonly IAccountAuthenticationService _accountAuthenticationService;
        private readonly IDeviceSessionService _deviceSessionService;
        private readonly ILocalizationService _localizationService;
        private readonly ILogger<LoginViewModel> _logger;

        [ObservableProperty]
        private string _username = string.Empty;

        [ObservableProperty]
        private string _password = string.Empty;

        [ObservableProperty]
        private bool _rememberAccount = true;

        [ObservableProperty]
        private bool _isLoggingIn;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        public LoginViewModel(
            IAccountStoreService accountStoreService,
            IAccountAuthenticationService accountAuthenticationService,
            IDeviceSessionService deviceSessionService,
            ILocalizationService localizationService,
            ILogger<LoginViewModel> logger)
        {
            _accountStoreService = accountStoreService;
            _accountAuthenticationService = accountAuthenticationService;
            _deviceSessionService = deviceSessionService;
            _localizationService = localizationService;
            _logger = logger;
        }

        public event EventHandler<bool>? CloseRequested;

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            var savedLogin = await _accountStoreService.LoadSavedLoginAsync(cancellationToken).ConfigureAwait(true);
            if (savedLogin == null)
                return;

            Username = savedLogin.Username;
            Password = savedLogin.Password;
            RememberAccount = savedLogin.RememberAccount;
        }

        [RelayCommand]
        private async Task SignInAsync(CancellationToken cancellationToken)
        {
            if (IsLoggingIn || !Validate())
                return;

            IsLoggingIn = true;
            try
            {
                var loginRequest = new AccountLoginRequest
                {
                    Username = Username.Trim(),
                    Password = Password,
                    RememberAccount = RememberAccount
                };

                AccountAuthenticationResult authentication = await _accountAuthenticationService
                    .AuthenticateAsync(loginRequest, cancellationToken)
                    .ConfigureAwait(true);

                if (authentication.Status != AccountAuthenticationStatus.Success
                    || authentication.Session == null)
                {
                    _deviceSessionService.ClearSession();
                    ErrorMessage = authentication.Status is
                        AccountAuthenticationStatus.ConfigurationError or
                        AccountAuthenticationStatus.ServiceUnavailable
                            ? GetText(LoginResourceKeys.ServiceUnavailable)
                            : GetText(LoginResourceKeys.AuthenticationFailed);
                    return;
                }

                _deviceSessionService.SetSession(authentication.Session);

                if (loginRequest.RememberAccount)
                    await _accountStoreService.SaveAsync(loginRequest, cancellationToken).ConfigureAwait(true);
                else
                    await _accountStoreService.ClearAsync(cancellationToken).ConfigureAwait(true);

                ErrorMessage = string.Empty;
                CloseRequested?.Invoke(this, true);
            }
            catch (OperationCanceledException)
            {
                _deviceSessionService.ClearSession();
                ErrorMessage = GetText(LoginResourceKeys.Canceled);
            }
            catch (IOException exception)
            {
                _deviceSessionService.ClearSession();
                _logger.LogWarning(exception, "Saved account could not be updated.");
                ErrorMessage = GetText(LoginResourceKeys.AccountSaveFailed);
            }
            catch (UnauthorizedAccessException exception)
            {
                _deviceSessionService.ClearSession();
                _logger.LogWarning(exception, "Saved account could not be updated.");
                ErrorMessage = GetText(LoginResourceKeys.AccountSaveFailed);
            }
            catch (CryptographicException exception)
            {
                _deviceSessionService.ClearSession();
                _logger.LogWarning(exception, "Saved account could not be protected.");
                ErrorMessage = GetText(LoginResourceKeys.AccountSaveFailed);
            }
            finally
            {
                IsLoggingIn = false;
            }
        }

        private bool Validate()
        {
            ErrorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(Username)
                || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = GetText(LoginResourceKeys.MissingFields);
                return false;
            }

            return true;
        }

        private string GetText(string resourceKey)
        {
            return _localizationService.GetString(resourceKey);
        }
    }
}
