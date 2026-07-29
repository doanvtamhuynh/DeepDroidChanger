using DeepDroidChanger.Authentication;

namespace DeepDroidChanger.Tests.Fakes
{
    public sealed class FakeAuthenticationSessionService : IAuthenticationSessionService
    {
        public AccountSession? CurrentSession { get; private set; }
        public bool WasCleared { get; private set; }

        public void SetSession(AccountSession session)
        {
            CurrentSession = session;
        }

        public void ClearSession()
        {
            WasCleared = true;
            CurrentSession = null;
        }
    }
}
