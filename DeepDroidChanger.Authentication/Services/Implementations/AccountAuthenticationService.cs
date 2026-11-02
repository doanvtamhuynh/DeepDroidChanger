namespace DeepDroidChanger.Authentication.Internal;

internal sealed class AccountAuthenticationService : IAccountAuthenticationService
{
    public Task<AccountAuthenticationResult> AuthenticateAsync(
        AccountLoginRequest loginRequest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (loginRequest == null
            || string.IsNullOrWhiteSpace(loginRequest.Username)
            || string.IsNullOrWhiteSpace(loginRequest.Password))
        {
            return Task.FromResult(new AccountAuthenticationResult(
                AccountAuthenticationStatus.InvalidInput,
                null));
        }

        // Temporary local credentials for testing without a server.
        bool authenticated = string.Equals(loginRequest.Username, "admin", StringComparison.Ordinal)
            && string.Equals(loginRequest.Password, "admin", StringComparison.Ordinal);

        return Task.FromResult(authenticated
            ? new AccountAuthenticationResult(
                AccountAuthenticationStatus.Success,
                new AccountSession(loginRequest.Username))
            : new AccountAuthenticationResult(AccountAuthenticationStatus.AuthenticationFailed, null));
    }
}
