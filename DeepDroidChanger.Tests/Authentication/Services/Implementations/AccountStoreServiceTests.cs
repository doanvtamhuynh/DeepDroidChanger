using DeepDroidChanger.Authentication;
using DeepDroidChanger.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace DeepDroidChanger.Tests.Authentication.Services.Implementations;

[TestClass]
[DoNotParallelize]
public sealed class AccountStoreServiceTests
{
    [TestMethod]
    public async Task SaveAsync_RememberAccount_ProtectsPasswordInJson()
    {
        using var fixture = new TestTempDirectory();
        string accountPath = GetAccountFilePath(fixture.Path);
        await using ServiceProvider provider = CreateProvider(accountPath);
        IAccountStoreService service = provider.GetRequiredService<IAccountStoreService>();
        var cancellationToken = CancellationToken.None;
        var loginRequest = new AccountLoginRequest
        {
            Username = "user@example.com",
            Password = "secret-password",
            RememberAccount = true
        };

        await service.SaveAsync(loginRequest, cancellationToken);
        string rawJson = await File.ReadAllTextAsync(accountPath, cancellationToken);
        AccountLoginRequest? loadedLogin =
            await service.LoadSavedLoginAsync(cancellationToken);

        Assert.DoesNotContain(loginRequest.Password, rawJson, StringComparison.Ordinal);
        Assert.DoesNotContain("endpoint", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userPoolId", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clientId", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.AreEqual(loginRequest.Password, loadedLogin?.Password);
        Assert.AreEqual(loginRequest.Username, loadedLogin?.Username);
    }

    [TestMethod]
    public async Task SaveAsync_RememberDisabled_ClearsAccountFile()
    {
        using var fixture = new TestTempDirectory();
        string accountPath = GetAccountFilePath(fixture.Path);
        await using ServiceProvider provider = CreateProvider(accountPath);
        IAccountStoreService service = provider.GetRequiredService<IAccountStoreService>();
        var cancellationToken = CancellationToken.None;
        await service.SaveAsync(new AccountLoginRequest
        {
            Username = "user@example.com",
            Password = "secret-password",
            RememberAccount = true
        }, cancellationToken);

        await service.SaveAsync(
            new AccountLoginRequest { RememberAccount = false },
            cancellationToken);

        Assert.IsFalse(File.Exists(accountPath));
    }

    [TestMethod]
    public async Task LoadSavedLoginAsync_CorruptJson_QuarantinesInvalidAccount()
    {
        using var fixture = new TestTempDirectory();
        string accountPath = GetAccountFilePath(fixture.Path);
        await using ServiceProvider provider = CreateProvider(accountPath);
        IAccountStoreService service = provider.GetRequiredService<IAccountStoreService>();
        string settingsDirectory = Path.GetDirectoryName(accountPath)!;
        Directory.CreateDirectory(settingsDirectory);
        await File.WriteAllTextAsync(accountPath, "{not-json");

        AccountLoginRequest? result =
            await service.LoadSavedLoginAsync(CancellationToken.None);

        Assert.IsNull(result);
        Assert.IsFalse(File.Exists(accountPath));
        string[] backups = Directory.GetFiles(settingsDirectory, "account.json.corrupt-*");
        Assert.HasCount(1, backups);
    }

    [TestMethod]
    public async Task LoadSavedLoginAsync_InvalidBase64_QuarantinesInvalidAccount()
    {
        using var fixture = new TestTempDirectory();
        string accountPath = GetAccountFilePath(fixture.Path);
        await using ServiceProvider provider = CreateProvider(accountPath);
        IAccountStoreService service = provider.GetRequiredService<IAccountStoreService>();
        string settingsDirectory = Path.GetDirectoryName(accountPath)!;
        Directory.CreateDirectory(settingsDirectory);
        await File.WriteAllTextAsync(
            accountPath,
            "{\"rememberAccount\":true,\"username\":\"user@example.com\",\"protectedPassword\":\"not-base64\",\"entropy\":\"also-not-base64\"}");

        AccountLoginRequest? result =
            await service.LoadSavedLoginAsync(CancellationToken.None);

        Assert.IsNull(result);
        Assert.IsFalse(File.Exists(accountPath));
        string[] backups = Directory.GetFiles(settingsDirectory, "account.json.corrupt-*");
        Assert.HasCount(1, backups);
    }

    private static ServiceProvider CreateProvider(string accountPath)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDeepDroidAuthentication(
            configureAccountStore: options => options.AccountFilePath = accountPath);
        return services.BuildServiceProvider();
    }

    private static string GetAccountFilePath(string rootPath)
    {
        return Path.Combine(rootPath, "AppSettings", "account.json");
    }
}
