namespace DeepDroidChanger.Authentication;

public sealed class AccountSession
{
    public AccountSession(string idToken)
    {
        IdToken = idToken;
    }

    public string IdToken { get; }
}
