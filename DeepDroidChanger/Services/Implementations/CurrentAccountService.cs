using System.Security.Cryptography;
using System.Text;
using DeepDroidChanger.Authentication;

namespace DeepDroidChanger.Services;

// Credentials stay protected in memory and in the existing DPAPI account store.
// They never enter application settings or logs.
public sealed class CurrentAccountService(IAccountStoreService store) : ICurrentAccountService, IDisposable
{
    private byte[]? _protectedPassword;
    private string _username = string.Empty;
    public event Func<Task<bool>>? SignOutRequested;

    public void Capture(AccountLoginRequest request)
    {
        ClearCredentials();
        _username = request.Username;
        byte[] bytes = Encoding.UTF8.GetBytes(request.Password);
        try
        {
            _protectedPassword = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public async Task SetRememberAsync(bool remember, CancellationToken cancellationToken)
    {
        if (!remember)
        {
            await store.ClearAsync(cancellationToken).ConfigureAwait(false);
            return;
        }
        if (_protectedPassword is null)
            throw new InvalidOperationException("A signed-in account is required.");
        byte[] bytes = ProtectedData.Unprotect(_protectedPassword, null, DataProtectionScope.CurrentUser);
        try
        {
            await store.SaveAsync(new AccountLoginRequest
            {
                Username = _username,
                Password = Encoding.UTF8.GetString(bytes),
                RememberAccount = true
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public Task<bool> SignOutAsync() => SignOutRequested?.Invoke() ?? Task.FromResult(false);

    private void ClearCredentials()
    {
        if (_protectedPassword is not null) CryptographicOperations.ZeroMemory(_protectedPassword);
        _protectedPassword = null;
        _username = string.Empty;
    }

    public void Dispose() => ClearCredentials();
}
