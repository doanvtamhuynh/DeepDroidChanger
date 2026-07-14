using Amazon.CognitoIdentityProvider;
using Amazon.Runtime;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepDroidChanger.Tests.Services.Implementations.Authentication;

[TestClass]
public sealed class AccountAuthenticationServiceTests
{
    [TestMethod]
    public async Task AuthenticateAsync_ValidToken_ReturnsSessionWithoutLoggingToken()
    {
        const string token = "sensitive-id-token";
        var logger = new TestLogger<AccountAuthenticationService>();
        var service = new AccountAuthenticationService(
            CreateValidOptions(),
            logger,
            (_, _) => Task.FromResult<string?>(token));

        AccountAuthenticationResult result = await service.AuthenticateAsync(
            CreateLogin(),
            CancellationToken.None);

        Assert.AreEqual(AccountAuthenticationStatus.Success, result.Status);
        Assert.IsNotNull(result.Session);
        Assert.AreEqual(token, result.Session.IdToken);
        Assert.IsFalse(logger.Messages.Any(message => message.Contains(token, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AuthenticateAsync_InvalidConfiguration_DoesNotCallProvider()
    {
        var providerCalled = false;
        DeviceInfoApiOptions options = CreateValidOptions();
        options.Endpoint = "not-a-uri";
        var service = new AccountAuthenticationService(
            options,
            NullLogger<AccountAuthenticationService>.Instance,
            (_, _) =>
            {
                providerCalled = true;
                return Task.FromResult<string?>("token");
            });

        AccountAuthenticationResult result = await service.AuthenticateAsync(
            CreateLogin(),
            CancellationToken.None);

        Assert.AreEqual(AccountAuthenticationStatus.ConfigurationError, result.Status);
        Assert.IsNull(result.Session);
        Assert.IsFalse(providerCalled);
    }

    [TestMethod]
    public async Task AuthenticateAsync_MissingCredentials_DoesNotCallProvider()
    {
        var providerCalled = false;
        var service = new AccountAuthenticationService(
            CreateValidOptions(),
            NullLogger<AccountAuthenticationService>.Instance,
            (_, _) =>
            {
                providerCalled = true;
                return Task.FromResult<string?>("token");
            });

        AccountAuthenticationResult result = await service.AuthenticateAsync(
            new AccountLoginRequest(),
            CancellationToken.None);

        Assert.AreEqual(AccountAuthenticationStatus.InvalidInput, result.Status);
        Assert.IsFalse(providerCalled);
    }

    [TestMethod]
    public async Task AuthenticateAsync_MissingToken_ReturnsServiceUnavailable()
    {
        var service = new AccountAuthenticationService(
            CreateValidOptions(),
            NullLogger<AccountAuthenticationService>.Instance,
            (_, _) => Task.FromResult<string?>(null));

        AccountAuthenticationResult result = await service.AuthenticateAsync(
            CreateLogin(),
            CancellationToken.None);

        Assert.AreEqual(AccountAuthenticationStatus.ServiceUnavailable, result.Status);
        Assert.IsNull(result.Session);
    }

    [TestMethod]
    public async Task AuthenticateAsync_IdentityProviderFailure_ReturnsAuthenticationFailed()
    {
        var service = new AccountAuthenticationService(
            CreateValidOptions(),
            NullLogger<AccountAuthenticationService>.Instance,
            (_, _) => throw new AmazonCognitoIdentityProviderException("invalid credentials"));

        AccountAuthenticationResult result = await service.AuthenticateAsync(
            CreateLogin(),
            CancellationToken.None);

        Assert.AreEqual(AccountAuthenticationStatus.AuthenticationFailed, result.Status);
    }

    [TestMethod]
    public async Task AuthenticateAsync_ServiceFailure_ReturnsServiceUnavailable()
    {
        var service = new AccountAuthenticationService(
            CreateValidOptions(),
            NullLogger<AccountAuthenticationService>.Instance,
            (_, _) => throw new AmazonServiceException("service unavailable"));

        AccountAuthenticationResult result = await service.AuthenticateAsync(
            CreateLogin(),
            CancellationToken.None);

        Assert.AreEqual(AccountAuthenticationStatus.ServiceUnavailable, result.Status);
    }

    [TestMethod]
    public async Task AuthenticateAsync_UnexpectedProviderFailure_ReturnsServiceUnavailable()
    {
        var service = new AccountAuthenticationService(
            CreateValidOptions(),
            NullLogger<AccountAuthenticationService>.Instance,
            (_, _) => throw new InvalidOperationException("unexpected"));

        AccountAuthenticationResult result = await service.AuthenticateAsync(
            CreateLogin(),
            CancellationToken.None);

        Assert.AreEqual(AccountAuthenticationStatus.ServiceUnavailable, result.Status);
        Assert.IsNull(result.Session);
    }

    [TestMethod]
    public async Task AuthenticateAsync_Canceled_PropagatesCancellation()
    {
        var service = new AccountAuthenticationService(
            CreateValidOptions(),
            NullLogger<AccountAuthenticationService>.Instance,
            (_, _) => throw new OperationCanceledException());

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.AuthenticateAsync(CreateLogin(), CancellationToken.None));
    }

    private static DeviceInfoApiOptions CreateValidOptions()
    {
        return new DeviceInfoApiOptions
        {
            Endpoint = "https://example.test/graphql",
            UserPoolId = "pool",
            ClientId = "client",
            Region = "ap-southeast-1",
            AuthenticationHeaderName = "authorization"
        };
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
