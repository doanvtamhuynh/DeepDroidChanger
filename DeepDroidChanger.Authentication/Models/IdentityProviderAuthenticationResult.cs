namespace DeepDroidChanger.Authentication;

public sealed record IdentityProviderAuthenticationResult(
    IdentityProviderAuthenticationStatus Status,
    string? IdToken);

public enum IdentityProviderAuthenticationStatus
{
    Success,
    AuthenticationFailed,
    ServiceUnavailable
}
