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
    public async Task GetDisabledPackagesAsync_UsesDisabledPackageManagerFilter()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbShellAsync("SERIAL", "pm list packages -d", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(
                0,
                "package:com.google.android.gms\npackage:com.android.vending\n",
                string.Empty));
        var service = new DevicePackageService(adb);

        IReadOnlyList<string> packages =
            await service.GetDisabledPackagesAsync("SERIAL", CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "com.android.vending", "com.google.android.gms" },
            packages.ToArray());
        await adb.Received(1).RunAdbShellAsync(
            "SERIAL",
            "pm list packages -d",
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(true, "pm enable com.google.android.gms")]
    [DataRow(false, "pm disable com.google.android.gms")]
    public async Task SetPackageEnabledAsync_UsesRequestedPackageManagerAction(
        bool enabled,
        string expectedCommand)
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbShellAsync("SERIAL", expectedCommand, Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "Package state changed", string.Empty));
        var service = new DevicePackageService(adb);

        await service.SetPackageEnabledAsync(
            "SERIAL",
            "com.google.android.gms",
            enabled,
            CancellationToken.None);

        await adb.Received(1).RunAdbShellAsync(
            "SERIAL",
            expectedCommand,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task SetPackageEnabledAsync_AdbFailure_Throws()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbShellAsync(
                "SERIAL",
                "pm disable com.google.android.gms",
                Arg.Any<CancellationToken>())
            .Returns(new CommandResult(1, string.Empty, "device offline"));
        var service = new DevicePackageService(adb);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.SetPackageEnabledAsync(
                "SERIAL",
                "com.google.android.gms",
                enabled: false,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task GetInstalledPackagesAsync_FiltersPackageNamesEndingWithUnderscore()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbShellAsync("SERIAL", "pm list packages", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(
                0,
                """
                package:com.android.bips.auto_generated_rro_product__
                package:com.example.generated_
                package:com.example.valid_name
                """,
                string.Empty));
        var service = new DevicePackageService(adb);

        IReadOnlyList<string> packages =
            await service.GetInstalledPackagesAsync("SERIAL", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "com.example.valid_name" }, packages.ToArray());
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
