using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
public sealed class IpGeolocationServiceTests
{
    [TestMethod]
    public async Task GetDeviceIpGeolocationAsync_ValidHttpsResponse_MapsProviderNeutralModel()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.CurlAsync("SERIAL", "https://ipwho.is/", Arg.Any<CancellationToken>())
            .Returns("""
                {
                  "ip": "203.0.113.25",
                  "success": true,
                  "country_code": "VN",
                  "latitude": 10.8231,
                  "longitude": 106.6297,
                  "timezone": { "id": "Asia/Ho_Chi_Minh" }
                }
                """);
        var service = new IpGeolocationService(adb, NullLogger<IpGeolocationService>.Instance);

        IpGeolocationInfo result = await service.GetDeviceIpGeolocationAsync("SERIAL", CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("203.0.113.25", result.PublicIp);
        Assert.AreEqual("VN", result.CountryCode);
        Assert.AreEqual(10.8231, result.Latitude);
        Assert.AreEqual(106.6297, result.Longitude);
        Assert.AreEqual("Asia/Ho_Chi_Minh", result.Timezone);
    }

    [DataRow("{\"success\":false,\"message\":\"rate limited\"}")]
    [DataRow("{not-json")]
    [DataRow("")]
    [TestMethod]
    public async Task GetDeviceIpGeolocationAsync_InvalidResponse_FailsWithoutReturningPartialData(string response)
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.CurlAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(response);
        var service = new IpGeolocationService(adb, NullLogger<IpGeolocationService>.Instance);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.GetDeviceIpGeolocationAsync("SERIAL", CancellationToken.None));
    }

    [TestMethod]
    public async Task GetDeviceIpGeolocationAsync_Canceled_PropagatesCancellation()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.CurlAsync("SERIAL", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new OperationCanceledException());
        var service = new IpGeolocationService(adb, NullLogger<IpGeolocationService>.Instance);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            service.GetDeviceIpGeolocationAsync("SERIAL", CancellationToken.None));
    }
}
