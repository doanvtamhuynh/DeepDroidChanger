namespace DeepDroidChanger.Authentication;

public sealed class AccountSession
{
    public AccountSession(string username)
    {
        Username = username;
    }

    public string Username { get; }
}
