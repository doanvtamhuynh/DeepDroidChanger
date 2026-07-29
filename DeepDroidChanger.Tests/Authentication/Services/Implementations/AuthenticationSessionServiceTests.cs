using DeepDroidChanger.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace DeepDroidChanger.Tests.Authentication.Services.Implementations;

[TestClass]
public sealed class AuthenticationSessionServiceTests
{
    [TestMethod]
    public void SetAndClearSession_UpdatesMemoryOnlyState()
    {
        using ServiceProvider provider = CreateProvider();
        IAuthenticationSessionService service =
            provider.GetRequiredService<IAuthenticationSessionService>();
        var session = new AccountSession("token");

        service.SetSession(session);
        Assert.AreSame(session, service.CurrentSession);

        service.ClearSession();
        Assert.IsNull(service.CurrentSession);
    }

    [TestMethod]
    public void SetSession_Null_Throws()
    {
        using ServiceProvider provider = CreateProvider();
        IAuthenticationSessionService service =
            provider.GetRequiredService<IAuthenticationSessionService>();

        Assert.ThrowsExactly<ArgumentNullException>(() => service.SetSession(null!));
        Assert.IsNull(service.CurrentSession);
    }

    private static ServiceProvider CreateProvider()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDeepDroidAuthentication();
        return services.BuildServiceProvider();
    }
}
