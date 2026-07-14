using DeepDroidChanger.Models;
namespace DeepDroidChanger.Services
{
    public interface IAccountStoreService
    {
        Task<AccountLoginRequest?> LoadSavedLoginAsync(CancellationToken cancellationToken);
        Task SaveAsync(AccountLoginRequest loginRequest, CancellationToken cancellationToken);
        Task ClearAsync(CancellationToken cancellationToken);
    }
}
