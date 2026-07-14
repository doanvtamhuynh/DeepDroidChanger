namespace DeepDroidChanger.Models;

public sealed record AccountAuthenticationResult(
    AccountAuthenticationStatus Status,
    AccountSession? Session);

public enum AccountAuthenticationStatus
{
    Success,
    AuthenticationFailed,
    ConfigurationError,
    ServiceUnavailable,
    InvalidInput
}
