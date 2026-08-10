using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class DeviceLocationServiceTests
{
    [TestMethod]
    public async Task ApplyLocationAsync_ValidCoordinates_WritesNormalizedProperties()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        var service = new DeviceLocationService(
            adb,
            Substitute.For<IIpGeolocationService>(),
            Substitute.For<IRandomService>(),
            NullLogger<DeviceLocationService>.Instance);

        await service.ApplyLocationAsync("SERIAL", "10.5", "-20.25", CancellationToken.None);

        await adb.Received(1).SetPropertyAsync(
            "SERIAL", PropertyConstants.Latitude, "10.5000", Arg.Any<CancellationToken>());
        await adb.Received(1).SetPropertyAsync(
            "SERIAL", PropertyConstants.Longitude, "-20.2500", Arg.Any<CancellationToken>());
    }

    [DataRow("91", "0")]
    [DataRow("0", "181")]
    [DataRow("not-a-number", "106")]
    [TestMethod]
    public async Task ApplyLocationAsync_InvalidCoordinates_DoesNotCallAdb(string latitude, string longitude)
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        var service = new DeviceLocationService(
            adb,
            Substitute.For<IIpGeolocationService>(),
            Substitute.For<IRandomService>(),
            NullLogger<DeviceLocationService>.Instance);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            service.ApplyLocationAsync("SERIAL", latitude, longitude, CancellationToken.None));

        await adb.DidNotReceiveWithAnyArgs().SetPropertyAsync(default!, default!, default!, default);
    }

    [TestMethod]
    public async Task ResolveLocationByDeviceIpAsync_InvalidLookup_DoesNotReturnCoordinates()
    {
        IIpGeolocationService geolocation = Substitute.For<IIpGeolocationService>();
        geolocation.GetDeviceIpGeolocationAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(new IpGeolocationInfo());
        var service = new DeviceLocationService(
            Substitute.For<IAdbCommandService>(),
            geolocation,
            Substitute.For<IRandomService>(),
            NullLogger<DeviceLocationService>.Instance);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.ResolveLocationByDeviceIpAsync("SERIAL", CancellationToken.None));
    }

    [DataRow(90d, 180d, "90.0000", "180.0000")]
    [DataRow(-90d, -180d, "-90.0000", "-180.0000")]
    [TestMethod]
    public async Task ResolveLocationByDeviceIpAsync_BoundaryCoordinates_RemainInValidRange(
        double latitude,
        double longitude,
        string expectedLatitude,
        string expectedLongitude)
    {
        IIpGeolocationService geolocation = Substitute.For<IIpGeolocationService>();
        geolocation.GetDeviceIpGeolocationAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(new IpGeolocationInfo
            {
                Success = true,
                CountryCode = "XX",
                Latitude = latitude,
                Longitude = longitude,
                Timezone = "Etc/UTC"
            });
        IRandomService random = Substitute.For<IRandomService>();
        random.RandomInRange(0, 1000).Returns(999);
        var service = new DeviceLocationService(
            Substitute.For<IAdbCommandService>(),
            geolocation,
            random,
            NullLogger<DeviceLocationService>.Instance);

        (string resolvedLatitude, string resolvedLongitude) =
            await service.ResolveLocationByDeviceIpAsync("SERIAL", CancellationToken.None);

        Assert.AreEqual(expectedLatitude, resolvedLatitude);
        Assert.AreEqual(expectedLongitude, resolvedLongitude);
    }

    [TestMethod]
    public async Task ApplyAsync_Config_ReturnsNormalizedCoordinatesThatWereApplied()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        var service = new DeviceLocationService(
            adb,
            Substitute.For<IIpGeolocationService>(),
            Substitute.For<IRandomService>(),
            NullLogger<DeviceLocationService>.Instance);

        (string latitude, string longitude) = await service.ApplyAsync(
            "SERIAL",
            new ChangeLocationDialogResult(ChangeLocationMode.Config, "10.5", "106.25"),
            CancellationToken.None);

        Assert.AreEqual("10.5000", latitude);
        Assert.AreEqual("106.2500", longitude);
    }

    [TestMethod]
    public async Task ResolveLocationByDeviceIpAsync_ResolvesCountryAndCityFromDataService()
    {
        IIpGeolocationService geolocation = Substitute.For<IIpGeolocationService>();
        geolocation.GetDeviceIpGeolocationAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(new IpGeolocationInfo
            {
                Success = true,
                CountryCode = "VN",
                Latitude = 10.75,
                Longitude = 106.666,
                Timezone = "Asia/Ho_Chi_Minh"
            });

        ILocationDataService locationDataService = Substitute.For<ILocationDataService>();
        var locations = new List<LocationOption>
        {
            new LocationOption("VN", "Vietnam", "Ho Chi Minh City", "Asia/Ho_Chi_Minh", "UTC +07:00", 10.75, 106.666)
        };
        locationDataService.GetLocationsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LocationOption>>(locations));

        IRandomService random = Substitute.For<IRandomService>();
        random.RandomInRange(0, 1000).Returns(0);

        var service = new DeviceLocationService(
            Substitute.For<IAdbCommandService>(),
            geolocation,
            locationDataService,
            random,
            NullLogger<DeviceLocationService>.Instance);

        DeviceLocationResult result = await service.ResolveLocationByDeviceIpAsync("SERIAL", CancellationToken.None);

        Assert.AreEqual("VN", result.CountryCode);
        Assert.AreEqual("Ho Chi Minh City", result.CityName);
    }

    [TestMethod]
    public async Task ResolveLocationByDeviceIpAsync_RandomizesTheTrailingCoordinateDigits()
    {
        IIpGeolocationService geolocation = Substitute.For<IIpGeolocationService>();
        geolocation.GetDeviceIpGeolocationAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(new IpGeolocationInfo
            {
                Success = true,
                CountryCode = "XX",
                Latitude = 21.0285,
                Longitude = -74.0060
            });

        IRandomService random = Substitute.For<IRandomService>();
        random.RandomInRange(0, 1000).Returns(123, 456);
        var service = new DeviceLocationService(
            Substitute.For<IAdbCommandService>(),
            geolocation,
            random,
            NullLogger<DeviceLocationService>.Instance);

        DeviceLocationResult result = await service.ResolveLocationByDeviceIpAsync("SERIAL", CancellationToken.None);

        Assert.AreEqual("21.0123", result.Latitude);
        Assert.AreEqual("-74.0456", result.Longitude);
    }
}
