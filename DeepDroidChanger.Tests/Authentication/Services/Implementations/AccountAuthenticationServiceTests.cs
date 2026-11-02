using DeepDroidChanger.Authentication;
using DeepDroidChanger.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeepDroidChanger.Tests.Authentication.Services.Implementations;

[TestClass]
public sealed class AccountAuthenticationServiceTests
{
    [TestMethod]
    public void AddDeepDroidAuthentication_RegistersLocalServicesAndAccountStoreDefaults()
    {
        using ServiceProvider provider = CreateProvider();
        AccountStoreOptions accountStoreOptions =
            provider.GetRequiredService<IOptions<AccountStoreOptions>>().Value;

        Assert.IsNotNull(provider.GetRequiredService<IAccountAuthenticationService>());
        Assert.IsNotNull(provider.GetRequiredService<IAuthenticationSessionService>());
        Assert.IsNotNull(provider.GetRequiredService<IAccountStoreService>());
        Assert.EndsWith(
            Path.Combine("AppSettings", "account.json"),
            accountStoreOptions.AccountFilePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task AuthenticateAsync_AdminCredentials_ReturnsSessionWithoutLoggingCredentials()
    {
        var loggerProvider = new RecordingLoggerProvider();
        await using ServiceProvider provider = CreateProvider(loggerProvider);
        IAccountAuthenticationService service =
            provider.GetRequiredService<IAccountAuthenticationService>();

        AccountAuthenticationResult result = await service.AuthenticateAsync(
            CreateLogin(),
            CancellationToken.None);

        Assert.AreEqual(AccountAuthenticationStatus.Success, result.Status);
        Assert.IsNotNull(result.Session);
        Assert.AreEqual("admin", result.Session.Username);
        Assert.IsFalse(loggerProvider.Messages.Any(message =>
            message.Contains("admin", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("admin", "wrong-password")]
    [DataRow("other-user", "admin")]
    [DataRow("Admin", "admin")]
    [DataRow("admin", "Admin")]
    public async Task AuthenticateAsync_InvalidCredentials_ReturnsAuthenticationFailed(
        string username,
        string password)
    {
        await using ServiceProvider provider = CreateProvider();
        IAccountAuthenticationService service =
            provider.GetRequiredService<IAccountAuthenticationService>();

        AccountAuthenticationResult result = await service.AuthenticateAsync(
            new AccountLoginRequest { Username = username, Password = password },
            CancellationToken.None);

        Assert.AreEqual(AccountAuthenticationStatus.AuthenticationFailed, result.Status);
        Assert.IsNull(result.Session);
    }

    [TestMethod]
    [DataRow("", "admin")]
    [DataRow("admin", "")]
    [DataRow(" ", "admin")]
    [DataRow("admin", " ")]
    public async Task AuthenticateAsync_MissingCredentials_ReturnsInvalidInput(
        string username,
        string password)
    {
        await using ServiceProvider provider = CreateProvider();
        IAccountAuthenticationService service =
            provider.GetRequiredService<IAccountAuthenticationService>();

        AccountAuthenticationResult result = await service.AuthenticateAsync(
            new AccountLoginRequest { Username = username, Password = password },
            CancellationToken.None);

        Assert.AreEqual(AccountAuthenticationStatus.InvalidInput, result.Status);
        Assert.IsNull(result.Session);
    }

    [TestMethod]
    public async Task AuthenticateAsync_Canceled_PropagatesCancellation()
    {
        await using ServiceProvider provider = CreateProvider();
        IAccountAuthenticationService service =
            provider.GetRequiredService<IAccountAuthenticationService>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.AuthenticateAsync(CreateLogin(), cancellation.Token));
    }

    private static ServiceProvider CreateProvider(RecordingLoggerProvider? loggerProvider = null)
    {
        ServiceCollection services = new();
        services.AddLogging(builder =>
        {
            if (loggerProvider != null)
                builder.AddProvider(loggerProvider);
        });
        services.AddDeepDroidAuthentication();
        return services.BuildServiceProvider();
    }

    private static AccountLoginRequest CreateLogin() => new()
    {
        Username = "admin",
        Password = "admin"
    };
}
