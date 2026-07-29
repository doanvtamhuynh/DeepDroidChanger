using Amazon;
using Amazon.CognitoIdentityProvider;
using Amazon.Extensions.CognitoAuthentication;
using Amazon.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeepDroidChanger.Authentication.Internal;

internal sealed class CognitoIdentityProviderClient : IIdentityProviderClient
{
    private readonly AuthenticationOptions _options;
    private readonly ILogger<CognitoIdentityProviderClient> _logger;

    public CognitoIdentityProviderClient(
        IOptions<AuthenticationOptions> options,
        ILogger<CognitoIdentityProviderClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IdentityProviderAuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        try
        {
            RegionEndpoint region = RegionEndpoint.GetBySystemName(_options.Region);
            var credentials = new AnonymousAWSCredentials();
            using var provider = new AmazonCognitoIdentityProviderClient(credentials, region);
            var userPool = new CognitoUserPool(_options.UserPoolId, _options.ClientId, provider);
            var user = new CognitoUser(username, _options.ClientId, userPool, provider);
            var response = await user
                .StartWithSrpAuthAsync(
                    new InitiateSrpAuthRequest { Password = password },
                    cancellationToken)
                .ConfigureAwait(false);
            string? idToken = response.AuthenticationResult?.IdToken;
            return string.IsNullOrWhiteSpace(idToken)
                ? new IdentityProviderAuthenticationResult(
                    IdentityProviderAuthenticationStatus.ServiceUnavailable,
                    null)
                : new IdentityProviderAuthenticationResult(
                    IdentityProviderAuthenticationStatus.Success,
                    idToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AmazonCognitoIdentityProviderException)
        {
            _logger.LogWarning("Identity provider authentication failed.");
            return new IdentityProviderAuthenticationResult(
                IdentityProviderAuthenticationStatus.AuthenticationFailed,
                null);
        }
        catch (AmazonServiceException)
        {
            _logger.LogWarning("Authentication service request failed.");
            return new IdentityProviderAuthenticationResult(
                IdentityProviderAuthenticationStatus.ServiceUnavailable,
                null);
        }
    }
}
