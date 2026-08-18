using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Fakes;
using DeepDroidChanger.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.ViewModels.Dialogs;

[TestClass]
public sealed class AdvancedChangeConfigViewModelTests
{
    [TestMethod]
    public async Task Initialize_DoesNotLoadPackagesUntilUserRequestsIt()
    {
        IDevicePackageService packageService = Substitute.For<IDevicePackageService>();
        AdvancedChangeConfigViewModel viewModel = CreateViewModel(packageService);

        viewModel.Initialize("SERIAL", new DeviceChangeOptions());

        Assert.IsFalse(viewModel.IsPackageSelectionActive);
        Assert.IsFalse(viewModel.ChangeAndroidId);
        Assert.IsTrue(viewModel.ChangeMacAddress);
        Assert.IsFalse(viewModel.UseRmRfForPackageCleanup);
        Assert.IsTrue(viewModel.ClearAllPackages);
        Assert.IsTrue(viewModel.ClearGoogleAccounts);
        Assert.IsFalse(viewModel.ClearGooglePackages);
        Assert.IsFalse(viewModel.IsSelectiveWipeEnabled);
        Assert.HasCount(0, viewModel.AvailablePackages);
        await packageService.DidNotReceiveWithAnyArgs().GetInstalledPackagesAsync(default!, default);
        await packageService.DidNotReceiveWithAnyArgs().GetUserInstalledPackagesAsync(default!, default);
    }

    [TestMethod]
    public async Task LoadPackages_AllScope_LoadsOnlyAfterSelectionModeIsEnabled()
    {
        IDevicePackageService packageService = Substitute.For<IDevicePackageService>();
        packageService.GetInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(new[] { "com.example.system" });
        packageService.GetUserInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(new[] { "com.example.two", "com.example.one" });
        AdvancedChangeConfigViewModel viewModel = CreateViewModel(packageService);
        viewModel.Initialize(
            "SERIAL",
            new DeviceChangeOptions { ClearAllPackages = false });
        viewModel.ClearSelectedPackages = true;

        await viewModel.LoadPackagesCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(
            new[] { "com.example.one", "com.example.system", "com.example.two" },
            viewModel.AvailablePackages.ToArray());
        await packageService.Received(1).GetInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>());
        await packageService.Received(1).GetUserInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task LoadPackages_UserScope_UsesThirdPartyPackageCommand()
    {
        IDevicePackageService packageService = Substitute.For<IDevicePackageService>();
        packageService.GetUserInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(new[] { "com.example.user" });
        AdvancedChangeConfigViewModel viewModel = CreateViewModel(packageService);
        viewModel.Initialize(
            "SERIAL",
            new DeviceChangeOptions { ClearAllPackages = false });
        viewModel.ClearSelectedPackages = true;
        viewModel.SelectedPackageScope = viewModel.PackageScopes.Single(option => option.Scope == PackageListScope.User);

        await viewModel.LoadPackagesCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "com.example.user" }, viewModel.AvailablePackages.ToArray());
        await packageService.Received(1).GetUserInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task LoadPackages_AllScope_UsesInstalledPackagesFromFirstDeviceAndUsersFromEveryDevice()
    {
        IDevicePackageService packageService = Substitute.For<IDevicePackageService>();
        packageService.GetInstalledPackagesAsync("FIRST", Arg.Any<CancellationToken>())
            .Returns(new[] { "com.example.system", "com.example.first", "com.example.duplicate" });
        packageService.GetUserInstalledPackagesAsync("FIRST", Arg.Any<CancellationToken>())
            .Returns(new[] { "com.example.first", "com.example.duplicate" });
        packageService.GetUserInstalledPackagesAsync("SECOND", Arg.Any<CancellationToken>())
            .Returns(new[] { "com.example.second", "com.example.duplicate" });
        AdvancedChangeConfigViewModel viewModel = CreateViewModel(packageService);
        viewModel.Initialize(
            new[] { "FIRST", "SECOND" },
            new DeviceChangeOptions { ClearAllPackages = false });
        viewModel.ClearSelectedPackages = true;

        await viewModel.LoadPackagesCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(
            new[] { "com.example.duplicate", "com.example.first", "com.example.second", "com.example.system" },
            viewModel.AvailablePackages.ToArray());
        await packageService.Received(1).GetInstalledPackagesAsync("FIRST", Arg.Any<CancellationToken>());
        await packageService.DidNotReceive().GetInstalledPackagesAsync("SECOND", Arg.Any<CancellationToken>());
        await packageService.Received(1).GetUserInstalledPackagesAsync("FIRST", Arg.Any<CancellationToken>());
        await packageService.Received(1).GetUserInstalledPackagesAsync("SECOND", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task LoadPackages_AllScope_FallsBackWhenFirstDeviceCannotListInstalledPackages()
    {
        IDevicePackageService packageService = Substitute.For<IDevicePackageService>();
        packageService.GetInstalledPackagesAsync("FIRST", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<string>>(
                new InvalidOperationException("first device disconnected")));
        packageService.GetInstalledPackagesAsync("SECOND", Arg.Any<CancellationToken>())
            .Returns(new[] { "com.example.system" });
        packageService.GetUserInstalledPackagesAsync("FIRST", Arg.Any<CancellationToken>())
            .Returns(new[] { "com.example.first" });
        packageService.GetUserInstalledPackagesAsync("SECOND", Arg.Any<CancellationToken>())
            .Returns(new[] { "com.example.second" });
        AdvancedChangeConfigViewModel viewModel = CreateViewModel(packageService);
        viewModel.Initialize(
            new[] { "FIRST", "SECOND" },
            new DeviceChangeOptions { ClearAllPackages = false });
        viewModel.ClearSelectedPackages = true;

        await viewModel.LoadPackagesCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(
            new[] { "com.example.first", "com.example.second", "com.example.system" },
            viewModel.AvailablePackages.ToArray());
        await packageService.Received(1).GetInstalledPackagesAsync("FIRST", Arg.Any<CancellationToken>());
        await packageService.Received(1).GetInstalledPackagesAsync("SECOND", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task LoadPackages_UserScope_UsesThirdPartyPackagesFromEverySelectedDevice()
    {
        IDevicePackageService packageService = Substitute.For<IDevicePackageService>();
        packageService.GetUserInstalledPackagesAsync("FIRST", Arg.Any<CancellationToken>())
            .Returns(new[] { "com.example.first" });
        packageService.GetUserInstalledPackagesAsync("SECOND", Arg.Any<CancellationToken>())
            .Returns(new[] { "com.example.second" });
        AdvancedChangeConfigViewModel viewModel = CreateViewModel(packageService);
        viewModel.Initialize(
            new[] { "FIRST", "SECOND" },
            new DeviceChangeOptions { ClearAllPackages = false });
        viewModel.ClearSelectedPackages = true;
        viewModel.SelectedPackageScope = viewModel.PackageScopes.Single(option => option.Scope == PackageListScope.User);

        await viewModel.LoadPackagesCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(
            new[] { "com.example.first", "com.example.second" },
            viewModel.AvailablePackages.ToArray());
        await packageService.DidNotReceiveWithAnyArgs().GetInstalledPackagesAsync(default!, default);
        await packageService.Received(1).GetUserInstalledPackagesAsync("FIRST", Arg.Any<CancellationToken>());
        await packageService.Received(1).GetUserInstalledPackagesAsync("SECOND", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task LoadPackages_Failure_ClearsPackagesShowsFailureAndWritesDiagnosticLog()
    {
        IDevicePackageService packageService = Substitute.For<IDevicePackageService>();
        packageService.GetInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("adb failed")));
        packageService.GetUserInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("adb failed")));
        var logger = new TestLogger<AdvancedChangeConfigViewModel>();
        AdvancedChangeConfigViewModel viewModel = CreateViewModel(packageService, logger);
        viewModel.Initialize(
            "SERIAL",
            new DeviceChangeOptions
            {
                ClearAllPackages = false,
                ClearSelectedPackages = true
            });

        await viewModel.LoadPackagesCommand.ExecuteAsync(null);

        Assert.HasCount(0, viewModel.AvailablePackages);
        Assert.AreEqual("AdvancedChangeConfig_LoadPackagesFailed", viewModel.PackageLoadStatus);
        Assert.IsTrue(logger.Messages.Any(message => message.Contains("SERIAL", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PackagePicker_MovesPackagesBetweenAvailableAndWipeLists()
    {
        IDevicePackageService packageService = Substitute.For<IDevicePackageService>();
        packageService.GetInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(new[] { "com.example.one" });
        packageService.GetUserInstalledPackagesAsync("SERIAL", Arg.Any<CancellationToken>())
            .Returns(new[] { "com.example.two" });
        AdvancedChangeConfigViewModel viewModel = CreateViewModel(packageService);
        viewModel.Initialize(
            "SERIAL",
            new DeviceChangeOptions
            {
                ClearAllPackages = false,
                ClearSelectedPackages = true
            });
        await viewModel.LoadPackagesCommand.ExecuteAsync(null);

        viewModel.SelectedAvailablePackage = "com.example.one";
        viewModel.AddSelectedPackageCommand.Execute(null);

        CollectionAssert.AreEqual(new[] { "com.example.one" }, viewModel.SelectedPackages.ToArray());
        CollectionAssert.AreEqual(new[] { "com.example.two" }, viewModel.AvailablePackages.ToArray());
        Assert.IsTrue(viewModel.ConfirmCommand.CanExecute(null));

        viewModel.SelectedWipePackage = "com.example.one";
        viewModel.RemoveSelectedPackageCommand.Execute(null);

        Assert.HasCount(0, viewModel.SelectedPackages);
        Assert.IsFalse(viewModel.ConfirmCommand.CanExecute(null));
    }

    [TestMethod]
    public void ClearAllPackages_DisablesSelectiveWipeButKeepsPackagePanelState()
    {
        AdvancedChangeConfigViewModel viewModel = CreateViewModel(Substitute.For<IDevicePackageService>());
        viewModel.Initialize(
            "SERIAL",
            new DeviceChangeOptions { ClearSelectedPackages = true });

        viewModel.ClearAllPackages = true;

        Assert.IsFalse(viewModel.IsSelectiveWipeEnabled);
        Assert.IsFalse(viewModel.IsPackageSelectionActive);
        Assert.IsFalse(viewModel.LoadPackagesCommand.CanExecute(null));
        Assert.IsTrue(viewModel.ConfirmCommand.CanExecute(null));
    }

    [TestMethod]
    public void PackageSelectionActive_RequiresSelectiveModeAndSelectedPackageWipe()
    {
        AdvancedChangeConfigViewModel viewModel = CreateViewModel(Substitute.For<IDevicePackageService>());
        viewModel.Initialize("SERIAL", new DeviceChangeOptions());

        Assert.IsFalse(viewModel.IsPackageSelectionActive);

        viewModel.ClearAllPackages = false;
        Assert.IsFalse(viewModel.IsPackageSelectionActive);

        viewModel.ClearSelectedPackages = true;
        Assert.IsTrue(viewModel.IsPackageSelectionActive);

        viewModel.ClearAllPackages = true;
        Assert.IsFalse(viewModel.IsPackageSelectionActive);
    }

    [DataRow(false)]
    [DataRow(true)]
    [TestMethod]
    public void Confirm_ReturnsSeparatedChangeWipeAndIntegrityOptions(bool useIntegritySecurityPatch)
    {
        AdvancedChangeConfigViewModel viewModel = CreateViewModel(Substitute.For<IDevicePackageService>());
        viewModel.Initialize(
            "SERIAL",
            new DeviceChangeOptions
            {
                ChangeAndroidId = true,
                ChangeMacAddress = false,
                UseRmRfForPackageCleanup = true,
                ClearAllPackages = false,
                ClearSelectedPackages = true,
                ClearGooglePackages = true,
                ClearGoogleAccounts = true,
                SelectedPackages = ["com.example.app"]
            },
            useIntegritySecurityPatch);
        AdvancedChangeConfigDialogResult? result = null;
        viewModel.CloseRequested += (_, dialogResult) => result = dialogResult;

        viewModel.ConfirmCommand.Execute(null);

        Assert.IsNotNull(result);
        Assert.AreEqual(useIntegritySecurityPatch, result.UseIntegritySecurityPatch);
        DeviceChangeOptions options = result.Options;
        Assert.IsFalse(options.UseDefaultMode);
        Assert.IsTrue(options.ChangeAndroidId);
        Assert.IsFalse(options.ChangeMacAddress);
        Assert.IsTrue(options.UseRmRfForPackageCleanup);
        Assert.IsTrue(options.ClearGooglePackages);
        Assert.IsTrue(options.ClearGoogleAccounts);
        CollectionAssert.AreEqual(new[] { "com.example.app" }, options.SelectedPackages);
    }

    private static AdvancedChangeConfigViewModel CreateViewModel(
        IDevicePackageService packageService,
        ILogger<AdvancedChangeConfigViewModel>? logger = null)
    {
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>())
            .Returns(callInfo => callInfo.Arg<string>());
        return new AdvancedChangeConfigViewModel(
            packageService,
            localization,
            logger ?? NullLogger<AdvancedChangeConfigViewModel>.Instance);
    }
}
