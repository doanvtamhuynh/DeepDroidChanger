namespace DeepDroidChanger.Authentication;

public interface IIdentityProviderClient
{
    Task<IdentityProviderAuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken);
}
