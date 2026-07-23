using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
public sealed class LocationDataServiceTests
{
    [TestMethod]
    public async Task GetLocationsAsync_ReturnsParsedLocationsFromEmbeddedAsset()
    {
        var service = new LocationDataService(NullLogger<LocationDataService>.Instance);

        IReadOnlyList<LocationOption> locations = await service.GetLocationsAsync(CancellationToken.None);

        Assert.IsNotNull(locations);
        Assert.IsTrue(locations.Count > 0, "Location dataset should contain location records.");

        var first = locations[0];
        Assert.IsFalse(string.IsNullOrWhiteSpace(first.CountryCode));
        Assert.IsFalse(string.IsNullOrWhiteSpace(first.CountryName));
        Assert.IsNotNull(first.LocationDisplayText);
    }
}
