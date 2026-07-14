using DeepDroidChanger.Models;
namespace DeepDroidChanger.Services
{
    public interface IDeviceSessionService
    {
        AccountSession? CurrentSession { get; }
        void SetSession(AccountSession session);
        void ClearSession();
    }
}
