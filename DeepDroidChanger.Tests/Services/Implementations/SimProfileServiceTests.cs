using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
public sealed class SimProfileServiceTests
{
    [TestMethod]
    public void CreateRandomProfile_ExplicitSelection_GeneratesConsistentCarrierIdentity()
    {
        IRandomService randomService = Substitute.For<IRandomService>();
        randomService.GenerateImsi("452", "04").Returns("452041234567890");
        randomService.GenerateIccid("84", "04").Returns("8984041234567890123");
        randomService.GeneratePhoneNumber().Returns("901234567");
        var service = new SimProfileService(randomService);

        SimProfile result = service.CreateRandomProfile(
            new CarrierCountryOption("VN", "84", "Vietnam"),
            new CarrierOption("Viettel - Mobile", "452", "04"));

        Assert.AreEqual("452041234567890", result.Imsi);
        Assert.AreEqual("8984041234567890123", result.Iccid);
        Assert.AreEqual("+84901234567", result.PhoneNumber);
        Assert.AreEqual("45204", result.OperatorNumeric);
        Assert.AreEqual("vn", result.OperatorCountry);
        Assert.AreEqual("Viettel", result.OperatorName);
    }

    [TestMethod]
    public void CreateRandomProfile_MissingSelection_UsesUnitedStatesDefaults()
    {
        IRandomService randomService = Substitute.For<IRandomService>();
        randomService.GenerateImsi("310", "260").Returns("310260123456789");
        randomService.GenerateIccid("1", "260").Returns("8912601234567890123");
        randomService.GeneratePhoneNumber().Returns("2125550100");
        var service = new SimProfileService(randomService);

        SimProfile result = service.CreateRandomProfile(null, null);

        Assert.AreEqual("310260123456789", result.Imsi);
        Assert.AreEqual("8912601234567890123", result.Iccid);
        Assert.AreEqual("+12125550100", result.PhoneNumber);
        Assert.AreEqual("310260", result.OperatorNumeric);
        Assert.AreEqual("us", result.OperatorCountry);
        Assert.AreEqual("T-Mobile", result.OperatorName);
    }
}
