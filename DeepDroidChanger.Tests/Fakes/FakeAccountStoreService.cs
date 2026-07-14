using DeepDroidChanger.Models;
using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Fakes
{
    public sealed class FakeAccountStoreService : IAccountStoreService
    {
        public AccountLoginRequest? SavedLogin { get; set; }
        public AccountLoginRequest? SavedRequest { get; private set; }
        public bool WasCleared { get; private set; }

        public Task<AccountLoginRequest?> LoadSavedLoginAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(SavedLogin);
        }

        public Task SaveAsync(AccountLoginRequest loginRequest, CancellationToken cancellationToken)
        {
            SavedRequest = loginRequest;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            WasCleared = true;
            return Task.CompletedTask;
        }
    }
}