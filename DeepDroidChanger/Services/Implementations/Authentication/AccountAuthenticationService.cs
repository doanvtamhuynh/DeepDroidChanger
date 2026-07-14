using DeepDroidChanger.Models;
using Amazon;
using Amazon.CognitoIdentityProvider;
using Amazon.Extensions.CognitoAuthentication;
using Amazon.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeepDroidChanger.Services
{
    public sealed class AccountAuthenticationService : IAccountAuthenticationService
    {
        private readonly DeviceInfoApiOptions _options;
        private readonly ILogger<AccountAuthenticationService> _logger;
        private readonly Func<AccountLoginRequest, CancellationToken, Task<string?>> _authenticate;

        public AccountAuthenticationService(IOptions<DeviceInfoApiOptions> options, ILogger<AccountAuthenticationService> logger)
            : this(options.Value, logger, null)
        {
        }

        internal AccountAuthenticationService(
            DeviceInfoApiOptions options,
            ILogger<AccountAuthenticationService> logger,
            Func<AccountLoginRequest, CancellationToken, Task<string?>>? authenticate)
        {
            _options = options;
            _logger = logger;
            _authenticate = authenticate ?? AuthenticateWithCognitoAsync;
        }

        public async Task<AccountAuthenticationResult> AuthenticateAsync(
            AccountLoginRequest loginRequest,
            CancellationToken cancellationToken)
        {
            AccountAuthenticationStatus? validationFailure = Validate(loginRequest);
            if (validationFailure != null)
                return new AccountAuthenticationResult(validationFailure.Value, null);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? idToken = await _authenticate(loginRequest, cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(idToken))
                {
                    _logger.LogWarning("Authentication succeeded but no ID token was returned.");
                    return new AccountAuthenticationResult(AccountAuthenticationStatus.ServiceUnavailable, null);
                }

                _logger.LogInformation("Device info account authenticated.");
                return new AccountAuthenticationResult(
                    AccountAuthenticationStatus.Success,
                    new AccountSession(_options.Endpoint, _options.AuthenticationHeaderName, idToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AmazonCognitoIdentityProviderException exception)
            {
                _logger.LogWarning(exception, "Identity provider authentication failed.");
                return new AccountAuthenticationResult(AccountAuthenticationStatus.AuthenticationFailed, null);
            }
            catch (AmazonServiceException exception)
            {
                _logger.LogWarning(exception, "Authentication service request failed.");
                return new AccountAuthenticationResult(AccountAuthenticationStatus.ServiceUnavailable, null);
            }
            catch (Exception)
            {
                _logger.LogWarning("Unexpected authentication service failure.");
                return new AccountAuthenticationResult(AccountAuthenticationStatus.ServiceUnavailable, null);
            }
        }

        private async Task<string?> AuthenticateWithCognitoAsync(
            AccountLoginRequest loginRequest,
            CancellationToken cancellationToken)
        {
            var region = RegionEndpoint.GetBySystemName(_options.Region);
            var credentials = new AnonymousAWSCredentials();
            using var provider = new AmazonCognitoIdentityProviderClient(credentials, region);
            var userPool = new CognitoUserPool(_options.UserPoolId, _options.ClientId, provider);
            var user = new CognitoUser(loginRequest.Username, _options.ClientId, userPool, provider);
            var response = await user
                .StartWithSrpAuthAsync(
                    new InitiateSrpAuthRequest { Password = loginRequest.Password },
                    cancellationToken)
                .ConfigureAwait(false);
            return response.AuthenticationResult?.IdToken;
        }

        private AccountAuthenticationStatus? Validate(AccountLoginRequest? loginRequest)
        {
            if (string.IsNullOrWhiteSpace(_options.Endpoint))
                return AccountAuthenticationStatus.ConfigurationError;

            if (!Uri.TryCreate(_options.Endpoint.Trim(), UriKind.Absolute, out _))
                return AccountAuthenticationStatus.ConfigurationError;

            if (string.IsNullOrWhiteSpace(_options.UserPoolId))
                return AccountAuthenticationStatus.ConfigurationError;

            if (string.IsNullOrWhiteSpace(_options.ClientId))
                return AccountAuthenticationStatus.ConfigurationError;

            if (string.IsNullOrWhiteSpace(_options.Region))
                return AccountAuthenticationStatus.ConfigurationError;

            if (string.IsNullOrWhiteSpace(_options.AuthenticationHeaderName))
                return AccountAuthenticationStatus.ConfigurationError;

            if (loginRequest == null
                || string.IsNullOrWhiteSpace(loginRequest.Username)
                || string.IsNullOrWhiteSpace(loginRequest.Password))
            {
                return AccountAuthenticationStatus.InvalidInput;
            }

            return null;
        }
    }
}
