using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepDroidChanger.Tests.Services.Implementations.Authentication
{
    [TestClass]
    [DoNotParallelize]
    public sealed class AccountStoreServiceTests
    {
        [TestMethod]
        public async Task SaveAsync_RememberAccount_ProtectsPasswordInJson()
        {
            var service = CreateService();
            var cancellationToken = CancellationToken.None;
            var loginRequest = new AccountLoginRequest
            {
                Username = "user@example.com",
                Password = "secret-password",
                RememberAccount = true
            };

            await service.SaveAsync(loginRequest, cancellationToken);
            var rawJson = await File.ReadAllTextAsync(GetAccountFilePath(), cancellationToken);
            var loadedLogin = await service.LoadSavedLoginAsync(cancellationToken);
            await service.ClearAsync(cancellationToken);

            Assert.DoesNotContain(loginRequest.Password, rawJson, StringComparison.Ordinal);
            Assert.DoesNotContain("endpoint", rawJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("userPoolId", rawJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("clientId", rawJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("authenticationHeaderName", rawJson, StringComparison.OrdinalIgnoreCase);
            Assert.AreEqual(loginRequest.Password, loadedLogin?.Password);
            Assert.AreEqual(loginRequest.Username, loadedLogin?.Username);
        }

        [TestMethod]
        public async Task SaveAsync_RememberDisabled_ClearsAccountFile()
        {
            var service = CreateService();
            var cancellationToken = CancellationToken.None;
            await service.SaveAsync(new AccountLoginRequest
            {
                Username = "user@example.com",
                Password = "secret-password",
                RememberAccount = true
            }, cancellationToken);

            await service.SaveAsync(new AccountLoginRequest { RememberAccount = false }, cancellationToken);

            Assert.IsFalse(File.Exists(GetAccountFilePath()));
        }

        [TestMethod]
        public async Task LoadSavedLoginAsync_CorruptJson_QuarantinesInvalidAccount()
        {
            var service = CreateService();
            string accountPath = GetAccountFilePath();
            string settingsDirectory = Path.GetDirectoryName(accountPath)!;
            Directory.CreateDirectory(settingsDirectory);
            foreach (string oldBackup in Directory.GetFiles(settingsDirectory, "account.json.corrupt-*"))
                File.Delete(oldBackup);
            await File.WriteAllTextAsync(accountPath, "{not-json");

            AccountLoginRequest? result = await service.LoadSavedLoginAsync(CancellationToken.None);

            Assert.IsNull(result);
            Assert.IsFalse(File.Exists(accountPath));
            string[] backups = Directory.GetFiles(settingsDirectory, "account.json.corrupt-*");
            Assert.HasCount(1, backups);
            File.Delete(backups[0]);
        }

        [TestMethod]
        public async Task LoadSavedLoginAsync_InvalidBase64_QuarantinesInvalidAccount()
        {
            var service = CreateService();
            string accountPath = GetAccountFilePath();
            string settingsDirectory = Path.GetDirectoryName(accountPath)!;
            Directory.CreateDirectory(settingsDirectory);
            DeleteCorruptBackups(settingsDirectory);
            await File.WriteAllTextAsync(
                accountPath,
                "{\"rememberAccount\":true,\"username\":\"user@example.com\",\"protectedPassword\":\"not-base64\",\"entropy\":\"also-not-base64\"}");

            AccountLoginRequest? result = await service.LoadSavedLoginAsync(CancellationToken.None);

            Assert.IsNull(result);
            Assert.IsFalse(File.Exists(accountPath));
            string[] backups = Directory.GetFiles(settingsDirectory, "account.json.corrupt-*");
            Assert.HasCount(1, backups);
            File.Delete(backups[0]);
        }

        private static AccountStoreService CreateService()
        {
            return new AccountStoreService(NullLogger<AccountStoreService>.Instance);
        }

        private static string GetAccountFilePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "Settings", "account.json");
        }

        private static void DeleteCorruptBackups(string settingsDirectory)
        {
            foreach (string backup in Directory.GetFiles(settingsDirectory, "account.json.corrupt-*"))
                File.Delete(backup);
        }
    }
}
