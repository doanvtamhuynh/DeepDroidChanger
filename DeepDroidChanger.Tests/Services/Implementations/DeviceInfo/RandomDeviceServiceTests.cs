using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.DeviceInfo;

[TestClass]
public sealed class RandomDeviceServiceTests
{
    [TestMethod]
    public async Task CreateRandomProfileAsync_NoSession_ReturnsLoginRequired()
    {
        IDeviceSessionService session = Substitute.For<IDeviceSessionService>();
        var service = new RandomDeviceService(session, Substitute.For<IDeviceRandomProfileService>());

        RandomDeviceResult result = await service.CreateRandomProfileAsync(
            new RandomDeviceRequest(),
            CancellationToken.None);

        Assert.AreEqual(RandomDeviceStatus.LoginRequired, result.Status);
        Assert.IsNull(result.Profile);
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_ApiFailure_ReturnsTypedFailure()
    {
        IDeviceSessionService session = Substitute.For<IDeviceSessionService>();
        session.CurrentSession.Returns(new AccountSession("https://example.test", "authorization", "token"));
        IDeviceRandomProfileService profiles = Substitute.For<IDeviceRandomProfileService>();
        profiles.CreateRandomProfileAsync(
                Arg.Any<AccountSession>(),
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<DeviceInfoApiDevice>>(_ => throw new DeviceRandomApiException("api failure"));
        var service = new RandomDeviceService(session, profiles);

        RandomDeviceResult result = await service.CreateRandomProfileAsync(
            new RandomDeviceRequest(),
            CancellationToken.None);

        Assert.AreEqual(RandomDeviceStatus.Failed, result.Status);
        Assert.IsNull(result.Profile);
    }

    [TestMethod]
    public async Task CreateRandomProfileAsync_Success_ReturnsProfile()
    {
        IDeviceSessionService session = Substitute.For<IDeviceSessionService>();
        session.CurrentSession.Returns(new AccountSession("https://example.test", "authorization", "token"));
        var expected = new DeviceInfoApiDevice { Model = "Pixel" };
        IDeviceRandomProfileService profiles = Substitute.For<IDeviceRandomProfileService>();
        profiles.CreateRandomProfileAsync(
                Arg.Any<AccountSession>(),
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var service = new RandomDeviceService(session, profiles);

        RandomDeviceResult result = await service.CreateRandomProfileAsync(
            new RandomDeviceRequest(),
            CancellationToken.None);

        Assert.AreEqual(RandomDeviceStatus.Created, result.Status);
        Assert.AreSame(expected, result.Profile);
    }
}
