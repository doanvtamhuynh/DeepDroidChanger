using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
public sealed class ProxyWorkflowServiceTests
{
    [TestMethod]
    public async Task ApplyAsync_OptionalLocationFails_ContinuesTimezoneAndReturnsTypedPartialResult()
    {
        IProxyService proxy = Substitute.For<IProxyService>();
        ConfigurePostConnectGate(proxy);
        IDeviceLocationService location = Substitute.For<IDeviceLocationService>();
        location.ResolveLocationByDeviceIpAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns<Task<DeviceLocationResult>>(_ => throw new InvalidOperationException("location failed"));
        IDeviceTimezoneService timezone = Substitute.For<IDeviceTimezoneService>();
        timezone.ResolveTimezoneByDeviceIpAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns("Asia/Ho_Chi_Minh");
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        var service = new ProxyWorkflowService(
            proxy,
            location,
            timezone,
            deviceConfig,
            NullLogger<ProxyWorkflowService>.Instance);
        var configuration = CreateConfiguration(changeLocation: true, changeTimezone: true);

        ProxyWorkflowResult result = await service.ApplyAsync(
            "SERIAL",
            configuration,
            CancellationToken.None);

        Assert.IsTrue(result.LocationUpdateFailed);
        Assert.IsFalse(result.TimezoneUpdateFailed);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("Asia/Ho_Chi_Minh", result.AppliedTimezone);
        await proxy.Received(1).StartProxyAsync(
            "SERIAL", "proxy.example", 1080, "user", "password", "Socks 5", Arg.Any<CancellationToken>());
        await deviceConfig.Received(1).SaveProxyConfigAsync(
            "SERIAL", configuration, CancellationToken.None);
        await deviceConfig.DidNotReceive().SaveLocationConfigAsync(
            Arg.Any<string>(),
            Arg.Any<ChangeLocationMode>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await deviceConfig.Received(1).SaveTimezoneConfigAsync(
            "SERIAL",
            ChangeTimezoneMode.DeviceIp,
            "Asia/Ho_Chi_Minh",
            CancellationToken.None);
        await timezone.Received(1).ApplyTimezoneAsync(
            "SERIAL", "Asia/Ho_Chi_Minh", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ApplyAsync_Success_PersistsActualProxyLocationAndTimezone()
    {
        IProxyService proxy = Substitute.For<IProxyService>();
        ConfigurePostConnectGate(proxy);
        IDeviceLocationService location = Substitute.For<IDeviceLocationService>();
        location.ResolveLocationByDeviceIpAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(new DeviceLocationResult("10.7626", "106.6602"));
        IDeviceTimezoneService timezone = Substitute.For<IDeviceTimezoneService>();
        timezone.ResolveTimezoneByDeviceIpAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns("Asia/Ho_Chi_Minh");
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        var configuration = CreateConfiguration(changeLocation: true, changeTimezone: true);
        deviceConfig.SaveProxyConfigAsync("SERIAL", configuration, CancellationToken.None)
            .Returns(Task.FromResult(true));
        deviceConfig.SaveLocationConfigAsync(
                "SERIAL",
                ChangeLocationMode.DeviceIp,
                "10.7626",
                "106.6602",
                CancellationToken.None)
            .Returns(Task.FromResult(true));
        deviceConfig.SaveTimezoneConfigAsync(
                "SERIAL",
                ChangeTimezoneMode.DeviceIp,
                "Asia/Ho_Chi_Minh",
                CancellationToken.None)
            .Returns(Task.FromResult(true));
        var service = new ProxyWorkflowService(
            proxy,
            location,
            timezone,
            deviceConfig,
            NullLogger<ProxyWorkflowService>.Instance);

        ProxyWorkflowResult result = await service.ApplyAsync(
            "SERIAL",
            configuration,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        await deviceConfig.Received(1).SaveProxyConfigAsync(
            "SERIAL", configuration, CancellationToken.None);
        await deviceConfig.Received(1).SaveLocationConfigAsync(
            "SERIAL",
            ChangeLocationMode.DeviceIp,
            "10.7626",
            "106.6602",
            CancellationToken.None);
        await deviceConfig.Received(1).SaveTimezoneConfigAsync(
            "SERIAL",
            ChangeTimezoneMode.DeviceIp,
            "Asia/Ho_Chi_Minh",
            CancellationToken.None);
    }

    [TestMethod]
    public async Task ApplyAsync_PersistenceFalse_DoesNotChangeWorkflowSuccess()
    {
        IProxyService proxy = Substitute.For<IProxyService>();
        ConfigurePostConnectGate(proxy);
        IDeviceLocationService location = Substitute.For<IDeviceLocationService>();
        location.ResolveLocationByDeviceIpAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(new DeviceLocationResult("10.7626", "106.6602"));
        IDeviceTimezoneService timezone = Substitute.For<IDeviceTimezoneService>();
        timezone.ResolveTimezoneByDeviceIpAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns("Asia/Ho_Chi_Minh");
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        var configuration = CreateConfiguration(changeLocation: true, changeTimezone: true);
        deviceConfig.SaveProxyConfigAsync("SERIAL", configuration, CancellationToken.None)
            .Returns(Task.FromResult(false));
        deviceConfig.SaveLocationConfigAsync(
                "SERIAL",
                ChangeLocationMode.DeviceIp,
                "10.7626",
                "106.6602",
                CancellationToken.None)
            .Returns(Task.FromResult(false));
        deviceConfig.SaveTimezoneConfigAsync(
                "SERIAL",
                ChangeTimezoneMode.DeviceIp,
                "Asia/Ho_Chi_Minh",
                CancellationToken.None)
            .Returns(Task.FromResult(false));
        var service = new ProxyWorkflowService(
            proxy,
            location,
            timezone,
            deviceConfig,
            NullLogger<ProxyWorkflowService>.Instance);

        ProxyWorkflowResult result = await service.ApplyAsync(
            "SERIAL",
            configuration,
            CancellationToken.None);

        Assert.IsFalse(result.LocationUpdateFailed);
        Assert.IsFalse(result.TimezoneUpdateFailed);
        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task ApplyAsync_PersistenceException_DoesNotChangeWorkflowSuccess()
    {
        IProxyService proxy = Substitute.For<IProxyService>();
        ConfigurePostConnectGate(proxy);
        IDeviceLocationService location = Substitute.For<IDeviceLocationService>();
        location.ResolveLocationByDeviceIpAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(new DeviceLocationResult("10.7626", "106.6602"));
        IDeviceTimezoneService timezone = Substitute.For<IDeviceTimezoneService>();
        timezone.ResolveTimezoneByDeviceIpAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns("Asia/Ho_Chi_Minh");
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        var configuration = CreateConfiguration(changeLocation: true, changeTimezone: true);
        deviceConfig.SaveProxyConfigAsync("SERIAL", configuration, CancellationToken.None)
            .Returns<Task<bool>>(_ => throw new IOException("proxy config write failed"));
        deviceConfig.SaveLocationConfigAsync(
                "SERIAL",
                ChangeLocationMode.DeviceIp,
                "10.7626",
                "106.6602",
                CancellationToken.None)
            .Returns(Task.FromResult(true));
        deviceConfig.SaveTimezoneConfigAsync(
                "SERIAL",
                ChangeTimezoneMode.DeviceIp,
                "Asia/Ho_Chi_Minh",
                CancellationToken.None)
            .Returns(Task.FromResult(true));
        var service = new ProxyWorkflowService(
            proxy,
            location,
            timezone,
            deviceConfig,
            NullLogger<ProxyWorkflowService>.Instance);

        ProxyWorkflowResult result = await service.ApplyAsync(
            "SERIAL",
            configuration,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        await deviceConfig.Received(1).SaveTimezoneConfigAsync(
            "SERIAL",
            ChangeTimezoneMode.DeviceIp,
            "Asia/Ho_Chi_Minh",
            CancellationToken.None);
    }

    [TestMethod]
    public async Task ApplyAsync_WaitsForInternetBeforeLocationAndTimezone()
    {
        IProxyService proxy = Substitute.For<IProxyService>();
        proxy.StartProxyAsync(
                "SERIAL", "proxy.example", 1080, "user", "password", "Socks 5", Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        proxy.WaitForInternetAndOpenBrowserLeaksAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        IDeviceLocationService location = Substitute.For<IDeviceLocationService>();
        location.ResolveLocationByDeviceIpAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(new DeviceLocationResult("10.7626", "106.6602"));
        IDeviceTimezoneService timezone = Substitute.For<IDeviceTimezoneService>();
        timezone.ResolveTimezoneByDeviceIpAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns("Asia/Ho_Chi_Minh");
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        var configuration = CreateConfiguration(changeLocation: true, changeTimezone: true);
        deviceConfig.SaveProxyConfigAsync("SERIAL", configuration, CancellationToken.None)
            .Returns(Task.FromResult(true));
        deviceConfig.SaveLocationConfigAsync(
                "SERIAL",
                ChangeLocationMode.DeviceIp,
                "10.7626",
                "106.6602",
                CancellationToken.None)
            .Returns(Task.FromResult(true));
        deviceConfig.SaveTimezoneConfigAsync(
                "SERIAL",
                ChangeTimezoneMode.DeviceIp,
                "Asia/Ho_Chi_Minh",
                CancellationToken.None)
            .Returns(Task.FromResult(true));
        var service = new ProxyWorkflowService(
            proxy,
            location,
            timezone,
            deviceConfig,
            NullLogger<ProxyWorkflowService>.Instance);

        await service.ApplyAsync("SERIAL", configuration, CancellationToken.None);

        Received.InOrder(() =>
        {
            proxy.StartProxyAsync(
                "SERIAL", "proxy.example", 1080, "user", "password", "Socks 5", Arg.Any<CancellationToken>());
            deviceConfig.SaveProxyConfigAsync("SERIAL", configuration, CancellationToken.None);
            proxy.WaitForInternetAndOpenBrowserLeaksAsync("SERIAL", Arg.Any<CancellationToken>());
            location.ResolveLocationByDeviceIpAsync("SERIAL", Arg.Any<CancellationToken>());
            location.ApplyLocationAsync("SERIAL", "10.7626", "106.6602", Arg.Any<CancellationToken>());
            deviceConfig.SaveLocationConfigAsync(
                "SERIAL",
                ChangeLocationMode.DeviceIp,
                "10.7626",
                "106.6602",
                CancellationToken.None);
            timezone.ResolveTimezoneByDeviceIpAsync("SERIAL", Arg.Any<CancellationToken>());
            timezone.ApplyTimezoneAsync("SERIAL", "Asia/Ho_Chi_Minh", Arg.Any<CancellationToken>());
            deviceConfig.SaveTimezoneConfigAsync(
                "SERIAL",
                ChangeTimezoneMode.DeviceIp,
                "Asia/Ho_Chi_Minh",
                CancellationToken.None);
        });
    }

    [TestMethod]
    public async Task ApplyAsync_InternetWaitFails_DoesNotRunLocationOrTimezone()
    {
        IProxyService proxy = Substitute.For<IProxyService>();
        proxy.StartProxyAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        proxy.WaitForInternetAndOpenBrowserLeaksAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new TimeoutException("internet unavailable")));
        IDeviceLocationService location = Substitute.For<IDeviceLocationService>();
        IDeviceTimezoneService timezone = Substitute.For<IDeviceTimezoneService>();
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        deviceConfig.SaveProxyConfigAsync("SERIAL", Arg.Any<FakeProxyDialogResult>(), CancellationToken.None)
            .Returns(Task.FromResult(true));
        var service = new ProxyWorkflowService(
            proxy,
            location,
            timezone,
            deviceConfig,
            NullLogger<ProxyWorkflowService>.Instance);

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => service.ApplyAsync(
            "SERIAL",
            CreateConfiguration(changeLocation: true, changeTimezone: true),
            CancellationToken.None));

        await location.DidNotReceiveWithAnyArgs().ResolveLocationByDeviceIpAsync(default!, default);
        await timezone.DidNotReceiveWithAnyArgs().ResolveTimezoneByDeviceIpAsync(default!, default);
    }

    [TestMethod]
    public async Task ApplyAsync_InternetWaitCanceled_DoesNotRunLocationOrStopProxy()
    {
        IProxyService proxy = Substitute.For<IProxyService>();
        proxy.StartProxyAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        proxy.WaitForInternetAndOpenBrowserLeaksAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new OperationCanceledException()));
        IDeviceLocationService location = Substitute.For<IDeviceLocationService>();
        IDeviceTimezoneService timezone = Substitute.For<IDeviceTimezoneService>();
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        deviceConfig.SaveProxyConfigAsync("SERIAL", Arg.Any<FakeProxyDialogResult>(), CancellationToken.None)
            .Returns(Task.FromResult(true));
        var service = new ProxyWorkflowService(
            proxy,
            location,
            timezone,
            deviceConfig,
            NullLogger<ProxyWorkflowService>.Instance);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.ApplyAsync(
            "SERIAL",
            CreateConfiguration(changeLocation: true, changeTimezone: true),
            CancellationToken.None));

        await location.DidNotReceiveWithAnyArgs().ResolveLocationByDeviceIpAsync(default!, default);
        await timezone.DidNotReceiveWithAnyArgs().ResolveTimezoneByDeviceIpAsync(default!, default);
        await proxy.DidNotReceiveWithAnyArgs().StopProxyAsync(default!, default);
    }

    [TestMethod]
    public async Task ApplyAsync_ProxyStartFails_DoesNotRunOptionalUpdates()
    {
        IProxyService proxy = Substitute.For<IProxyService>();
        proxy.StartProxyAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("proxy failed"));
        IDeviceLocationService location = Substitute.For<IDeviceLocationService>();
        IDeviceTimezoneService timezone = Substitute.For<IDeviceTimezoneService>();
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        var service = new ProxyWorkflowService(
            proxy,
            location,
            timezone,
            deviceConfig,
            NullLogger<ProxyWorkflowService>.Instance);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ApplyAsync(
            "SERIAL",
            CreateConfiguration(changeLocation: true, changeTimezone: true),
            CancellationToken.None));

        await location.DidNotReceiveWithAnyArgs().ResolveLocationByDeviceIpAsync(default!, default);
        await timezone.DidNotReceiveWithAnyArgs().ResolveTimezoneByDeviceIpAsync(default!, default);
    }

    private static FakeProxyDialogResult CreateConfiguration(bool changeLocation, bool changeTimezone)
    {
        return new FakeProxyDialogResult(
            "proxy.example",
            1080,
            "user",
            "password",
            "Socks 5",
            changeLocation,
            changeTimezone);
    }

    private static void ConfigurePostConnectGate(IProxyService proxy)
    {
        proxy.WaitForInternetAndOpenBrowserLeaksAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }
}
