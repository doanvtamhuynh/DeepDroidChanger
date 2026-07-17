using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations.AdbServices;

[TestClass]
public sealed class DevicePackageServiceTests
{
    [TestMethod]
    public async Task GetInstalledPackagesAsync_ParsesSortsAndDeduplicatesPackageManagerOutput()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbShellAsync("SERIAL", "pm list packages", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(
                0,
                "package:com.example.two\r\npackage:com.example.one\npackage:com.example.two\n",
                string.Empty));
        var service = new DevicePackageService(adb);

        IReadOnlyList<string> packages =
            await service.GetInstalledPackagesAsync("SERIAL", CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "com.example.one", "com.example.two" },
            packages.ToArray());
    }

    [TestMethod]
    public async Task GetUserInstalledPackagesAsync_UsesThirdPartyPackageManagerFilter()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbShellAsync("SERIAL", "pm list packages -3", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(
                0,
                "package:com.example.user\n",
                string.Empty));
        var service = new DevicePackageService(adb);

        IReadOnlyList<string> packages =
            await service.GetUserInstalledPackagesAsync("SERIAL", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "com.example.user" }, packages.ToArray());
        await adb.Received(1).RunAdbShellAsync(
            "SERIAL",
            "pm list packages -3",
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetInstalledPackagesAsync_AdbFailure_Throws()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbShellAsync("SERIAL", "pm list packages", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "device offline"));
        var service = new DevicePackageService(adb);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.GetInstalledPackagesAsync("SERIAL", CancellationToken.None));
    }
}
