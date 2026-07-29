using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeepDroidChanger.Authentication.Internal;

internal sealed class AccountAuthenticationService : IAccountAuthenticationService
{
    private readonly AuthenticationOptions _options;
    private readonly IIdentityProviderClient _identityProviderClient;
    private readonly ILogger<AccountAuthenticationService> _logger;

    public AccountAuthenticationService(
        IOptions<AuthenticationOptions> options,
        IIdentityProviderClient identityProviderClient,
        ILogger<AccountAuthenticationService> logger)
    {
        _options = options.Value;
        _identityProviderClient = identityProviderClient;
        _logger = logger;
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
            IdentityProviderAuthenticationResult providerResult = await _identityProviderClient
                .AuthenticateAsync(
                    loginRequest.Username,
                    loginRequest.Password,
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (providerResult.Status == IdentityProviderAuthenticationStatus.AuthenticationFailed)
                return new AccountAuthenticationResult(AccountAuthenticationStatus.AuthenticationFailed, null);

            if (providerResult.Status != IdentityProviderAuthenticationStatus.Success
                || string.IsNullOrWhiteSpace(providerResult.IdToken))
            {
                _logger.LogWarning("Authentication completed without a usable ID token.");
                return new AccountAuthenticationResult(AccountAuthenticationStatus.ServiceUnavailable, null);
            }

            _logger.LogInformation("Device info account authenticated.");
            return new AccountAuthenticationResult(
                AccountAuthenticationStatus.Success,
                new AccountSession(providerResult.IdToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            _logger.LogWarning("Unexpected authentication service failure.");
            return new AccountAuthenticationResult(AccountAuthenticationStatus.ServiceUnavailable, null);
        }
    }

    private AccountAuthenticationStatus? Validate(AccountLoginRequest? loginRequest)
    {
        if (string.IsNullOrWhiteSpace(_options.UserPoolId)
            || string.IsNullOrWhiteSpace(_options.ClientId)
            || string.IsNullOrWhiteSpace(_options.Region))
        {
            return AccountAuthenticationStatus.ConfigurationError;
        }

        if (loginRequest == null
            || string.IsNullOrWhiteSpace(loginRequest.Username)
            || string.IsNullOrWhiteSpace(loginRequest.Password))
        {
            return AccountAuthenticationStatus.InvalidInput;
        }

        return null;
    }
}
