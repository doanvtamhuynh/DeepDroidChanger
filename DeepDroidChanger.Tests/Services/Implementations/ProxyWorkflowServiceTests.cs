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
        IDeviceLocationService location = Substitute.For<IDeviceLocationService>();
        location.ResolveLocationByDeviceIpAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns<Task<DeviceLocationResult>>(_ => throw new InvalidOperationException("location failed"));
        IDeviceTimezoneService timezone = Substitute.For<IDeviceTimezoneService>();
        timezone.ResolveTimezoneByDeviceIpAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns("Asia/Ho_Chi_Minh");
        var service = new ProxyWorkflowService(
            proxy,
            location,
            timezone,
            NullLogger<ProxyWorkflowService>.Instance);
        var configuration = CreateConfiguration(changeLocation: true, changeTimezone: true);

        ProxyWorkflowResult result = await service.ApplyAsync(
            "SERIAL",
            configuration,
            CancellationToken.None);

        Assert.IsTrue(result.LocationUpdateFailed);
        Assert.IsFalse(result.TimezoneUpdateFailed);
        Assert.AreEqual("Asia/Ho_Chi_Minh", result.AppliedTimezone);
        await proxy.Received(1).StartProxyAsync(
            "SERIAL", "proxy.example", 1080, "user", "password", "Socks 5", Arg.Any<CancellationToken>());
        await timezone.Received(1).ApplyTimezoneAsync(
            "SERIAL", "Asia/Ho_Chi_Minh", Arg.Any<CancellationToken>());
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
        var service = new ProxyWorkflowService(
            proxy,
            location,
            timezone,
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
}
