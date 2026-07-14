using DeepDroidChanger.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepDroidChanger.Tests.Services.Implementations.DeviceInfo;

[TestClass]
public sealed class CarrierDataServiceTests
{
    [TestMethod]
    public async Task GetCarrierProfilesAsync_SameNameVariants_PreservesMccMncIdentity()
    {
        CarrierDataService service = new(NullLogger<CarrierDataService>.Instance);

        IReadOnlyList<Models.CarrierProfile> profiles =
            await service.GetCarrierProfilesAsync(CancellationToken.None);
        List<Models.CarrierProfile> bellVariants = profiles
            .Where(profile => profile.CarrierName == "Bell Mobility")
            .ToList();

        Assert.HasCount(2, bellVariants);
        Assert.HasCount(
            2,
            bellVariants.Select(profile => $"{profile.Mcc}-{profile.Mnc}").Distinct().ToList());
    }
}
