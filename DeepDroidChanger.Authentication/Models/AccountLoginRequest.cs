namespace DeepDroidChanger.Authentication;

public sealed class AccountLoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberAccount { get; set; }
}
