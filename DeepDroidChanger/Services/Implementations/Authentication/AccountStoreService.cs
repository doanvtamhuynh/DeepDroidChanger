using DeepDroidChanger.Models;
using DeepDroidChanger.Helpers;
using DeepDroidChanger.Constants;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services
{
    public sealed class AccountStoreService : IAccountStoreService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        private static readonly SemaphoreSlim FileLock = new(1, 1);

        private readonly ILogger<AccountStoreService> _logger;
        private readonly string _accountDirectory;
        private readonly string _accountPath;

        public AccountStoreService(ILogger<AccountStoreService> logger)
            : this(
                Path.Combine(
                    AppContext.BaseDirectory,
                    AssetConstants.RuntimeData.AppSettingsDirectoryName,
                    AssetConstants.RuntimeData.AccountFileName),
                logger)
        {
        }

        internal AccountStoreService(
            string accountPath,
            ILogger<AccountStoreService> logger)
        {
            _accountPath = Path.GetFullPath(accountPath);
            _accountDirectory = Path.GetDirectoryName(_accountPath)
                ?? throw new ArgumentException("Account path must include a directory.", nameof(accountPath));
            _logger = logger;
        }

        public async Task<AccountLoginRequest?> LoadSavedLoginAsync(CancellationToken cancellationToken)
        {
            await FileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            var path = _accountPath;
            try
            {
                if (!File.Exists(path))
                    return null;

                await using var stream = File.OpenRead(path);
                var account = await JsonSerializer.DeserializeAsync<AccountSettings>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                if (account is not { RememberAccount: true })
                    return null;

                var password = Unprotect(account.ProtectedPassword, account.Entropy);
                return new AccountLoginRequest
                {
                    Username = account.Username,
                    Password = password,
                    RememberAccount = true
                };
            }
            catch (CryptographicException exception)
            {
                _logger.LogWarning(exception, "Saved account could not be decrypted.");
                QuarantineInvalidAccount(path);
                return null;
            }
            catch (IOException exception)
            {
                _logger.LogWarning(exception, "Saved account could not be read.");
                return null;
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Saved account file is invalid.");
                QuarantineInvalidAccount(path);
                return null;
            }
            catch (FormatException exception)
            {
                _logger.LogWarning(exception, "Saved account encryption data is invalid.");
                QuarantineInvalidAccount(path);
                return null;
            }
            finally
            {
                FileLock.Release();
            }
        }

        public async Task SaveAsync(AccountLoginRequest loginRequest, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(loginRequest);
            await FileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!loginRequest.RememberAccount)
                {
                    DeleteAccountFiles();

                    return;
                }

                Directory.CreateDirectory(_accountDirectory);
                byte[] entropy = RandomNumberGenerator.GetBytes(32);
                AccountSettings account = new()
                {
                    RememberAccount = true,
                    Username = loginRequest.Username,
                    ProtectedPassword = Protect(loginRequest.Password, entropy),
                    Entropy = Convert.ToBase64String(entropy),
                    LastLoginUtc = DateTimeOffset.UtcNow
                };

                string json = JsonSerializer.Serialize(account, JsonOptions);
                await AtomicFileWriter.WriteAllTextAsync(_accountPath, json, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                FileLock.Release();
            }
        }

        public async Task ClearAsync(CancellationToken cancellationToken)
        {
            await FileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DeleteAccountFiles();
            }
            finally
            {
                FileLock.Release();
            }

        }

        private void DeleteAccountFiles()
        {
            if (File.Exists(_accountPath))
                File.Delete(_accountPath);
        }

        private static string Protect(string value, byte[] entropy)
        {
            var data = Encoding.UTF8.GetBytes(value);
            var protectedData = ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedData);
        }

        private static string Unprotect(string protectedValue, string entropyValue)
        {
            if (string.IsNullOrWhiteSpace(protectedValue) || string.IsNullOrWhiteSpace(entropyValue))
                return string.Empty;

            var data = Convert.FromBase64String(protectedValue);
            var entropy = Convert.FromBase64String(entropyValue);
            var unprotectedData = ProtectedData.Unprotect(data, entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(unprotectedData);
        }

        private void QuarantineInvalidAccount(string accountPath)
        {
            if (!File.Exists(accountPath))
                return;

            string quarantinePath = string.Concat(
                accountPath,
                ".corrupt-",
                DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture),
                "-",
                Guid.NewGuid().ToString("N"));
            try
            {
                File.Move(accountPath, quarantinePath);
                _logger.LogWarning("Moved invalid saved account to quarantine.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "Failed to quarantine invalid saved account file.");
            }
        }
    }
}
