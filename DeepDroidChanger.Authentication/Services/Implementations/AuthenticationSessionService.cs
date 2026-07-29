namespace DeepDroidChanger.Authentication.Internal;

internal sealed class AuthenticationSessionService : IAuthenticationSessionService
{
    private AccountSession? _currentSession;

    public AccountSession? CurrentSession => _currentSession;

    public void SetSession(AccountSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _currentSession = session;
    }

    public void ClearSession()
    {
        _currentSession = null;
    }
}
