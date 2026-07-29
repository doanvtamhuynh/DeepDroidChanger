namespace DeepDroidChanger.Authentication;

public interface IAccountAuthenticationService
{
    Task<AccountAuthenticationResult> AuthenticateAsync(
        AccountLoginRequest loginRequest,
        CancellationToken cancellationToken);
}
