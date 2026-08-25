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
            "Socks 5",
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
        const string readinessCommand = "dumpsys activity services hev.sockstun/.TProxyService";
        adb.GetPropertyAsync("SERIAL", PropertyConstants.DeepDroidDevice, Arg.Any<CancellationToken>())
            .Returns("1");
        adb.RunAdbShellAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));
        adb.RunAdbShellAsync("SERIAL", readinessCommand, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(
                0,
                """
                ServiceRecord{abc hev.sockstun/.TProxyService}
                  app=ProcessRecord{xyz}
                  isForeground=true
                  startRequested=true
                """,
                string.Empty));
        adb.IsWifiEnabledAsync("SERIAL", Arg.Any<CancellationToken>()).Returns(true);
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
            "Socks 5",
            CancellationToken.None);

        await adb.Received(1).SetWifiAsync("SERIAL", false, Arg.Any<CancellationToken>());
        await adb.Received(1).SetWifiAsync("SERIAL", true, Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL", PropertyConstants.Proxy.Ip, "proxy.example", Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL", PropertyConstants.Proxy.InterfaceIpv4, "10.20.30.40", Arg.Any<CancellationToken>());
        await adb.DidNotReceiveWithAnyArgs().OpenLinkAsync(default!, default!, default);
        await adb.DidNotReceiveWithAnyArgs().CurlAsync(default!, default!, default);
    }

    [TestMethod]
    public async Task WaitForInternetAndOpenBrowserLeaksAsync_Ready_OpensBrowserLeaksWithoutRecovery()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.CurlAsync("SERIAL", UrlConstants.PublicIp, Arg.Any<CancellationToken>())
            .Returns("203.0.113.10");
        var service = new ProxyService(
            adb,
            Substitute.For<IRandomService>(),
            NullLogger<ProxyService>.Instance);

        await service.WaitForInternetAndOpenBrowserLeaksAsync("SERIAL", CancellationToken.None);

        await adb.Received(1).CurlAsync(
            "SERIAL", UrlConstants.PublicIp, Arg.Any<CancellationToken>());
        await adb.Received(1).OpenLinkAsync(
            "SERIAL", UrlConstants.BrowserLeaks, Arg.Any<CancellationToken>());
        await adb.DidNotReceiveWithAnyArgs().SetWifiAsync(default!, default, default);
        await adb.DidNotReceiveWithAnyArgs().OpenWifiSettingsAsync(default!, default);
    }

    [TestMethod]
    public async Task WaitForInternetAndOpenBrowserLeaksAsync_InitiallyUnavailable_RetriesAfterWifiRecovery()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.CurlAsync("SERIAL", UrlConstants.PublicIp, Arg.Any<CancellationToken>())
            .Returns(string.Empty, "203.0.113.10");
        var service = new ProxyService(
            adb,
            Substitute.For<IRandomService>(),
            NullLogger<ProxyService>.Instance,
            (_, _, _, _, _) => Task.FromResult<SocksProxyCheckResult?>(null),
            (_, _, _) => Task.FromResult("10.20.30.40/24"),
            (_, _) => Task.CompletedTask);

        await service.WaitForInternetAndOpenBrowserLeaksAsync("SERIAL", CancellationToken.None);

        Received.InOrder(() =>
        {
            adb.CurlAsync("SERIAL", UrlConstants.PublicIp, Arg.Any<CancellationToken>());
            adb.SetWifiAsync("SERIAL", enabled: true, Arg.Any<CancellationToken>());
            adb.OpenWifiSettingsAsync("SERIAL", Arg.Any<CancellationToken>());
            adb.CurlAsync("SERIAL", UrlConstants.PublicIp, Arg.Any<CancellationToken>());
            adb.OpenLinkAsync("SERIAL", UrlConstants.BrowserLeaks, Arg.Any<CancellationToken>());
        });
        await adb.Received(2).CurlAsync(
            "SERIAL", UrlConstants.PublicIp, Arg.Any<CancellationToken>());
        await adb.Received(1).OpenLinkAsync(
            "SERIAL", UrlConstants.BrowserLeaks, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task WaitForInternetAndOpenBrowserLeaksAsync_CanceledDuringRetry_PropagatesWithoutOpeningBrowserLeaks()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.CurlAsync("SERIAL", UrlConstants.PublicIp, Arg.Any<CancellationToken>())
            .Returns(string.Empty);
        using var cancellation = new CancellationTokenSource();
        var service = new ProxyService(
            adb,
            Substitute.For<IRandomService>(),
            NullLogger<ProxyService>.Instance,
            (_, _, _, _, _) => Task.FromResult<SocksProxyCheckResult?>(null),
            (_, _, _) => Task.FromResult("10.20.30.40/24"),
            (_, _) =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            });

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.WaitForInternetAndOpenBrowserLeaksAsync("SERIAL", cancellation.Token));

        await adb.Received(1).SetWifiAsync("SERIAL", enabled: true, Arg.Any<CancellationToken>());
        await adb.Received(1).OpenWifiSettingsAsync("SERIAL", Arg.Any<CancellationToken>());
        await adb.DidNotReceiveWithAnyArgs().OpenLinkAsync(default!, default!, default);
    }

    [TestMethod]
    public async Task StartProxyAsync_SetupFails_RestoresWifiAndCleansPartialProxyState()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbShellAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));
        adb.GetPropertyAsync("SERIAL", PropertyConstants.DeepDroidDevice, Arg.Any<CancellationToken>())
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
            "Socks 5", CancellationToken.None));

        await adb.Received(1).SetWifiAsync("SERIAL", false, Arg.Any<CancellationToken>());
        await adb.Received(1).SetWifiAsync("SERIAL", true, CancellationToken.None);
        await adb.Received(2).SetPropertyAsync(
            "SERIAL", PropertyConstants.Proxy.Ip, string.Empty, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task StartProxyAsync_CanceledStart_RestoresWifiWithoutRollingBackProxy()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.GetPropertyAsync("SERIAL", PropertyConstants.DeepDroidDevice, Arg.Any<CancellationToken>())
            .Returns("1");
        adb.RunAdbShellAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, string.Empty, string.Empty));
        using var cancellation = new CancellationTokenSource();
        int delayCalls = 0;
        var service = new ProxyService(
            adb,
            Substitute.For<IRandomService>(),
            NullLogger<ProxyService>.Instance,
            (_, _, _, _, _) => Task.FromResult<SocksProxyCheckResult?>(null),
            (_, _, _) => Task.FromResult("10.20.30.40/24"),
            (_, _) =>
            {
                if (Interlocked.Increment(ref delayCalls) == 2)
                    cancellation.Cancel();

                return Task.CompletedTask;
            });

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.StartProxyAsync(
            "SERIAL", "proxy.example", 1080, string.Empty, string.Empty,
            "Socks 5", cancellation.Token));

        await adb.Received(1).SetWifiAsync("SERIAL", false, Arg.Any<CancellationToken>());
        await adb.Received(1).SetWifiAsync("SERIAL", true, CancellationToken.None);
        await adb.Received(1).ClearGlobalHttpProxyAsync("SERIAL", Arg.Any<CancellationToken>());
        await adb.Received(1).ForceStopPackageAsync("SERIAL", "hev.sockstun", Arg.Any<CancellationToken>());
        await adb.Received(1).ClearPackageAsync("SERIAL", "hev.sockstun", Arg.Any<CancellationToken>());
        await adb.Received(1).RunAdbShellAsync(
            "SERIAL",
            Arg.Is<string>(command => command.Contains("hev.sockstun.DISCONNECT", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL", PropertyConstants.Proxy.Ip, string.Empty, Arg.Any<CancellationToken>());
    }
}
