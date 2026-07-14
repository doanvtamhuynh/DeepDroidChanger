using DeepDroidChanger.Models;
namespace DeepDroidChanger.Services
{
    public interface IAccountAuthenticationService
    {
        Task<AccountAuthenticationResult> AuthenticateAsync(
            AccountLoginRequest loginRequest,
            CancellationToken cancellationToken);
    }
}
