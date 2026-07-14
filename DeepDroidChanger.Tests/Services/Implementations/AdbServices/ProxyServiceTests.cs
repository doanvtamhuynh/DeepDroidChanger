using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Net;
using System.Net.Http.Headers;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class ProxyServiceTests
{
    [TestMethod]
    public void IsRateLimited_TooManyRequestsWithRetryAfter_StopsWithoutRetrySignal()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));

        bool rateLimited = ProxyService.IsRateLimited(response, out bool retryAfterProvided);

        Assert.IsTrue(rateLimited);
        Assert.IsTrue(retryAfterProvided);
    }

    [TestMethod]
    public void IsRateLimited_Success_IsNotRateLimited()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        bool rateLimited = ProxyService.IsRateLimited(response, out bool retryAfterProvided);

        Assert.IsFalse(rateLimited);
        Assert.IsFalse(retryAfterProvided);
    }

    [DataRow("", 1080, "", "")]
    [DataRow("proxy.example", 0, "", "")]
    [DataRow("proxy.example", 65536, "", "")]
    [DataRow("proxy.example", 1080, "user", "")]
    [TestMethod]
    public async Task StartProxyAsync_InvalidEndpoint_DoesNotCallAdb(
        string host,
        int port,
        string username,
        string password)
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        var service = new ProxyService(
            adb,
            Substitute.For<IRandomService>(),
            NullLogger<ProxyService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => service.StartProxyAsync(
            "SERIAL",
            host,
            port,
            username,
            password,
            DeepProxyConstants.SocksProxyType,
            CancellationToken.None));

        await adb.DidNotReceiveWithAnyArgs().SetPropertyAsync(default!, default!, default!, default);
    }

    [TestMethod]
    public async Task StartProxyAsync_UnsupportedType_DoesNotCallAdb()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        var service = new ProxyService(
            adb,
            Substitute.For<IRandomService>(),
            NullLogger<ProxyService>.Instance);

        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.StartProxyAsync(
            "SERIAL", "proxy.example", 1080, "", "", "HTTP", CancellationToken.None));

        await adb.DidNotReceiveWithAnyArgs().SetPropertyAsync(default!, default!, default!, default);
    }

    [TestMethod]
    public async Task StartProxyAsync_ValidEndpoint_OrchestratesAdbWithoutLiveNetworkOrDelay()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.GetPropertyAsync("SERIAL", PropertyConstants.Prop_DeepDroidDevice, Arg.Any<CancellationToken>())
            .Returns("1");
        adb.RunAdbShellAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));
        adb.CurlAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("203.0.113.10");
        var service = new ProxyService(
            adb,
            Substitute.For<IRandomService>(),
            NullLogger<ProxyService>.Instance,
            (_, _, _, _, _) => Task.FromResult<SocksProxyCheckResult?>(
                new SocksProxyCheckResult("203.0.113.10", "US")),
            (_, _, _) => Task.FromResult("10.20.30.40/24"),
            (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });

        await service.StartProxyAsync(
            "SERIAL",
            "proxy.example",
            1080,
            "user",
            "password",
            DeepProxyConstants.SocksProxyType,
            CancellationToken.None);

        await adb.Received(1).SetWifiAsync("SERIAL", false, Arg.Any<CancellationToken>());
        await adb.Received(1).SetWifiAsync("SERIAL", true, Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL", DeepProxyConstants.ProxyIpProperty, "proxy.example", Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL", DeepProxyConstants.InterfaceIpv4Property, "10.20.30.40", Arg.Any<CancellationToken>());
        await adb.Received(1).OpenLinkAsync(
            "SERIAL", DeepProxyConstants.BrowserLeaksUrl, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task StartProxyAsync_SetupFails_RestoresWifiAndCleansPartialProxyState()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbShellAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));
        adb.GetPropertyAsync("SERIAL", PropertyConstants.Prop_DeepDroidDevice, Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new InvalidOperationException("device validation failed"));
        var service = new ProxyService(
            adb,
            Substitute.For<IRandomService>(),
            NullLogger<ProxyService>.Instance,
            (_, _, _, _, _) => Task.FromResult<SocksProxyCheckResult?>(null),
            (_, _, _) => Task.FromResult("10.20.30.40/24"),
            (_, _) => Task.CompletedTask);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.StartProxyAsync(
            "SERIAL", "proxy.example", 1080, string.Empty, string.Empty,
            DeepProxyConstants.SocksProxyType, CancellationToken.None));

        await adb.Received(1).SetWifiAsync("SERIAL", false, Arg.Any<CancellationToken>());
        await adb.Received(1).SetWifiAsync("SERIAL", true, CancellationToken.None);
        await adb.Received(2).SetPropertyAsync(
            "SERIAL", DeepProxyConstants.ProxyIpProperty, string.Empty, Arg.Any<CancellationToken>());
    }
}
