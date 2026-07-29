using DeepDroidChanger.Authentication.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DeepDroidChanger.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddDeepDroidAuthentication(
        this IServiceCollection services,
        Action<AuthenticationOptions>? configureAuthentication = null,
        Action<AccountStoreOptions>? configureAccountStore = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<AuthenticationOptions>(options =>
        {
            options.UserPoolId = AuthenticationConstants.UserPoolId;
            options.ClientId = AuthenticationConstants.ClientId;
            options.Region = AuthenticationConstants.Region;
        });
        services.Configure<AccountStoreOptions>(options =>
        {
            options.AccountFilePath = Path.Combine(
                AppContext.BaseDirectory,
                AccountStoreConstants.AppSettingsDirectoryName,
                AccountStoreConstants.AccountFileName);
        });

        if (configureAuthentication != null)
            services.Configure(configureAuthentication);

        if (configureAccountStore != null)
            services.Configure(configureAccountStore);

        services.TryAddSingleton<IIdentityProviderClient, CognitoIdentityProviderClient>();
        services.TryAddSingleton<IAccountAuthenticationService, AccountAuthenticationService>();
        services.TryAddSingleton<IAccountStoreService, AccountStoreService>();
        services.TryAddSingleton<IAuthenticationSessionService, AuthenticationSessionService>();
        return services;
    }
}
