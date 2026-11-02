using DeepDroidChanger.Authentication;

namespace DeepDroidChanger.Services;

public interface ICurrentAccountService
{
    void Capture(AccountLoginRequest request);
    Task SetRememberAsync(bool remember, CancellationToken cancellationToken);
    event Func<Task<bool>>? SignOutRequested;
    Task<bool> SignOutAsync();
}
