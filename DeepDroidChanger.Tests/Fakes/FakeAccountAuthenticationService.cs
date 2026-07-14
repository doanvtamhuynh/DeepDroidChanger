using DeepDroidChanger.Models;
using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Fakes
{
    public sealed class FakeAccountAuthenticationService : IAccountAuthenticationService
    {
        public AccountSession Session { get; set; } = new("https://example.com/graphql", "authorization", "id-token");
        public AccountAuthenticationStatus Status { get; set; } = AccountAuthenticationStatus.Success;
        public Exception? ExceptionToThrow { get; set; }
        public AccountLoginRequest? LastRequest { get; private set; }

        public Task<AccountAuthenticationResult> AuthenticateAsync(
            AccountLoginRequest loginRequest,
            CancellationToken cancellationToken)
        {
            LastRequest = loginRequest;

            if (ExceptionToThrow != null)
                throw ExceptionToThrow;

            return Task.FromResult(new AccountAuthenticationResult(
                Status,
                Status == AccountAuthenticationStatus.Success ? Session : null));
        }
    }
}
