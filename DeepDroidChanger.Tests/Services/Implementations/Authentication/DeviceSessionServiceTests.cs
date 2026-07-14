using DeepDroidChanger.Models;
using DeepDroidChanger.Services;

namespace DeepDroidChanger.Tests.Services.Implementations.Authentication;

[TestClass]
public sealed class DeviceSessionServiceTests
{
    [TestMethod]
    public void SetAndClearSession_UpdatesMemoryOnlyState()
    {
        var service = new DeviceSessionService();
        var session = new AccountSession("https://example.test/graphql", "authorization", "token");

        service.SetSession(session);
        Assert.AreSame(session, service.CurrentSession);

        service.ClearSession();
        Assert.IsNull(service.CurrentSession);
    }

    [TestMethod]
    public void SetSession_Null_Throws()
    {
        var service = new DeviceSessionService();

        Assert.ThrowsExactly<ArgumentNullException>(() => service.SetSession(null!));
        Assert.IsNull(service.CurrentSession);
    }
}
