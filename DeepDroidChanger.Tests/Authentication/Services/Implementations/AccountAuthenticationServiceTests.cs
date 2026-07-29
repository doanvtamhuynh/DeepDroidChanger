using DeepDroidChanger.Authentication;
using DeepDroidChanger.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DeepDroidChanger.Tests.Authentication.Services.Implementations;

[TestClass]
public sealed class AccountAuthenticationServiceTests
{
    [TestMethod]
    public void AddDeepDroidAuthentication_ConfiguresValidProductionDefaults()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDeepDroidAuthentication();
        using ServiceProvider provider = services.BuildServiceProvider();

        AuthenticationOptions authenticationOptions =
            provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        AccountStoreOptions accountStoreOptions =
            provider.GetRequiredService<IOptions<AccountStoreOptions>>().Value;

        Assert.IsFalse(string.IsNullOrWhiteSpace(authenticationOptions.UserPoolId));
        Assert.IsFalse(string.IsNullOrWhiteSpace(authenticationOptions.ClientId));
        Assert.IsFalse(string.IsNullOrWhiteSpace(authenticationOptions.Region));
        Assert.EndsWith(
            Path.Combine("AppSettings", "account.json"),
            accountStoreOptions.AccountFilePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task AuthenticateAsync_ValidToken_ReturnsSessionWithoutLoggingSensitiveValues()
    {
        const string token = "sensitive-id-token";
        const string username = "user@example.test";
        const string password = "sensitive-password";
        IIdentityProviderClient identityProvider = Substitute.For<IIdentityProviderClient>();
        identityProvider
            .AuthenticateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new IdentityProviderAuthenticationResult(
                IdentityProviderAuthenticationStatus.Success,
                token));
        var loggerProvider = new RecordingLoggerProvider();
        await using ServiceProvider provider = CreateProvider(identityProvider, loggerProvider);
        IAccountAuthenticationService service =
            provider.GetRequiredService<IAccountAuthenticationService>();

        AccountAuthenticationResult result = await service.AuthenticateAsync(
            new AccountLoginRequest
            {
                Username = username,
                Password = password
            },
            CancellationToken.None);

        Assert.AreEqual(AccountAuthenticationStatus.Success, result.Status);
        Assert.IsNotNull(result.Session);
        Assert.AreEqual(token, result.Session.IdToken);
        Assert.IsFalse(loggerProvider.Messages.Any(message =>
            message.Contains(token, StringComparison.Ordinal)
            || message.Contains(username, StringComparison.Ordinal)
            || message.Contains(password, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AuthenticateAsync_InvalidConfiguration_DoesNotCallProvider()
    {
        IIdentityProviderClient identityProvider = Substitute.For<IIdentityProviderClient>();
        await using ServiceProvider provider = CreateProvider(
            identityProvider,
            configure: options => options.Region = string.Empty);
        IAccountAuthenticationService service =
            provider.GetRequiredService<IAccountAuthenticationService>();

        AccountAuthenticationResult result = await service.AuthenticateAsync(
            CreateLogin(),
            CancellationToken.None);

        Assert.AreEqual(AccountAuthenticationStatus.ConfigurationError, result.Status);
        Assert.IsNull(result.Session);
        await identityProvider.DidNotReceiveWithAnyArgs()
            .AuthenticateAsync(default!, default!, default);
    }

    [TestMethod]
    public async Task AuthenticateAsync_MissingCredentials_DoesNotCallProvider()
    {
        IIdentityProviderClient identityProvider = Substitute.For<IIdentityProviderClient>();
        await using ServiceProvider provider = CreateProvider(identityProvider);
        IAccountAuthenticationService service =
            provider.GetRequiredService<IAccountAuthenticationService>();

        AccountAuthenticationResult result = await service.AuthenticateAsync(
            new AccountLoginRequest(),
            CancellationToken.None);

        Assert.AreEqual(AccountAuthenticationStatus.InvalidInput, result.Status);
        await identityProvider.DidNotReceiveWithAnyArgs()
            .AuthenticateAsync(default!, default!, default);
    }

    [TestMethod]
    public async Task AuthenticateAsync_MissingToken_ReturnsServiceUnavailable()
    {
        IIdentityProviderClient identityProvider = CreateProviderResult(
            IdentityProviderAuthenticationStatus.Success,
            null);
        await using ServiceProvider provider = CreateProvider(identityProvider);
        IAccountAuthenticationService service =
            provider.GetRequiredService<IAccountAuthenticationService>();

        AccountAuthenticationResult result = await service.AuthenticateAsync(
            CreateLogin(),
            CancellationToken.None);

        Assert.AreEqual(AccountAuthenticationStatus.ServiceUnavailable, result.Status);
        Assert.IsNull(result.Session);
    }

    [TestMethod]
    public async Task AuthenticateAsync_IdentityProviderFailure_ReturnsAuthenticationFailed()
    {
        IIdentityProviderClient identityProvider = CreateProviderResult(
            IdentityProviderAuthenticationStatus.AuthenticationFailed,
            null);
        await using ServiceProvider provider = CreateProvider(identityProvider);
        IAccountAuthenticationService service =
            provider.GetRequiredService<IAccountAuthenticationService>();

        AccountAuthenticationResult result = await service.AuthenticateAsync(
            CreateLogin(),
            CancellationToken.None);

        Assert.AreEqual(AccountAuthenticationStatus.AuthenticationFailed, result.Status);
        Assert.IsNull(result.Session);
    }

    [TestMethod]
    public async Task AuthenticateAsync_ServiceFailure_ReturnsServiceUnavailable()
    {
        IIdentityProviderClient identityProvider = CreateProviderResult(
            IdentityProviderAuthenticationStatus.ServiceUnavailable,
            null);
        await using ServiceProvider provider = CreateProvider(identityProvider);
        IAccountAuthenticationService service =
            provider.GetRequiredService<IAccountAuthenticationService>();

        AccountAuthenticationResult result = await service.AuthenticateAsync(
            CreateLogin(),
            CancellationToken.None);

        Assert.AreEqual(AccountAuthenticationStatus.ServiceUnavailable, result.Status);
        Assert.IsNull(result.Session);
    }

    [TestMethod]
    public async Task AuthenticateAsync_UnexpectedProviderFailure_ReturnsServiceUnavailable()
    {
        IIdentityProviderClient identityProvider = Substitute.For<IIdentityProviderClient>();
        identityProvider
            .AuthenticateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<IdentityProviderAuthenticationResult>>(
                _ => throw new InvalidOperationException("unexpected"));
        await using ServiceProvider provider = CreateProvider(identityProvider);
        IAccountAuthenticationService service =
            provider.GetRequiredService<IAccountAuthenticationService>();

        AccountAuthenticationResult result = await service.AuthenticateAsync(
            CreateLogin(),
            CancellationToken.None);

        Assert.AreEqual(AccountAuthenticationStatus.ServiceUnavailable, result.Status);
        Assert.IsNull(result.Session);
    }

    [TestMethod]
    public async Task AuthenticateAsync_Canceled_PropagatesCancellation()
    {
        IIdentityProviderClient identityProvider = Substitute.For<IIdentityProviderClient>();
        identityProvider
            .AuthenticateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<IdentityProviderAuthenticationResult>>(
                _ => throw new OperationCanceledException());
        await using ServiceProvider provider = CreateProvider(identityProvider);
        IAccountAuthenticationService service =
            provider.GetRequiredService<IAccountAuthenticationService>();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.AuthenticateAsync(CreateLogin(), CancellationToken.None));
    }

    private static IIdentityProviderClient CreateProviderResult(
        IdentityProviderAuthenticationStatus status,
        string? idToken)
    {
        IIdentityProviderClient identityProvider = Substitute.For<IIdentityProviderClient>();
        identityProvider
            .AuthenticateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new IdentityProviderAuthenticationResult(status, idToken));
        return identityProvider;
    }

    private static ServiceProvider CreateProvider(
        IIdentityProviderClient identityProvider,
        RecordingLoggerProvider? loggerProvider = null,
        Action<AuthenticationOptions>? configure = null)
    {
        ServiceCollection services = new();
        services.AddLogging(builder =>
        {
            if (loggerProvider != null)
                builder.AddProvider(loggerProvider);
        });
        services.AddSingleton(identityProvider);
        services.AddDeepDroidAuthentication(configureAuthentication: configure);
        return services.BuildServiceProvider();
    }

    private static AccountLoginRequest CreateLogin()
    {
        return new AccountLoginRequest
        {
            Username = "user@example.test",
            Password = "password"
        };
    }
}
