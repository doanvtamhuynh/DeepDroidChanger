namespace DeepDroidChanger.Authentication;

public sealed class AuthenticationOptions
{
    public string UserPoolId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
}
