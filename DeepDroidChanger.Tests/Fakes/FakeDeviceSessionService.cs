using DeepDroidChanger.Models;
using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Fakes
{
    public sealed class FakeDeviceSessionService : IDeviceSessionService
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
