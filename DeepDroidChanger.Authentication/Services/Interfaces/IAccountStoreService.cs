namespace DeepDroidChanger.Authentication;

public interface IAccountStoreService
{
    Task<AccountLoginRequest?> LoadSavedLoginAsync(CancellationToken cancellationToken);
    Task SaveAsync(AccountLoginRequest loginRequest, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
