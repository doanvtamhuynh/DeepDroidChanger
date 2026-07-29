namespace DeepDroidChanger.Authentication;

public interface IAuthenticationSessionService
{
    AccountSession? CurrentSession { get; }
    void SetSession(AccountSession session);
    void ClearSession();
}
