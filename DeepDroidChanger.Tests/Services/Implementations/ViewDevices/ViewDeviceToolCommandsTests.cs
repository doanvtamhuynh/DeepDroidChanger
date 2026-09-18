using System.Runtime.CompilerServices;
using System.Windows;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Fakes;
using DeepDroidChanger.Tests.ViewModels;
using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Models;
using DeepDroidChanger.ViewModels;
using DeepDroidChanger.Views;
using NSubstitute;
using ScrcpyNet;
using SharpAdbClient;

namespace DeepDroidChanger.Tests.Services.Implementations.ViewDevices;

[TestClass]
public sealed class ViewDeviceToolCommandsTests
{
    private const string Serial = "SERIAL-123";

    [TestMethod]
    public async Task RenameCommand_IsAvailableOffline_UpdatesStoredName()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Offline)]);
        var config = Substitute.For<IDeviceConfigService>();
        config.RenameDeviceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker, config: config);

        await viewModel.InitializeAsync(Serial, "Original");

        Assert.IsTrue(viewModel.RenameConfigCommand.CanExecute(null));
        viewModel.RenameConfigCommand.Execute(null);
        viewModel.RenameName = "  Renamed device  ";
        await viewModel.SaveRenameConfigCommand.ExecuteAsync(null);

        Assert.AreEqual("Renamed device", viewModel.DeviceName);
        Assert.AreEqual(ViewDeviceToolEditor.None, viewModel.ActiveToolEditor);
        await config.Received(1).RenameDeviceAsync(
            Serial,
            "Renamed device",
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task InputCommand_UsesCurrentInteractiveSession()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        var session = new TrackingSession(Serial);
        var factory = new TrackingSessionFactory(session);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory: factory,
            actions: Substitute.For<IDeviceActionService>());

        await viewModel.InitializeAsync(Serial, "Device");

        viewModel.InputText = "hello from tools";
        await viewModel.InputDeviceTextCommand.ExecuteAsync(null);

        CollectionAssert.Contains(session.Actions, "inject:hello from tools");
    }

    [TestMethod]
    public async Task RotateView_DefaultsToZeroAndCyclesWithoutDeviceServices()
    {
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            new FakeTracker([]),
            adb: adb,
            actions: actions);

        Assert.AreEqual(0, viewModel.ViewRotationQuarterTurns);
        Assert.AreEqual(0d, viewModel.ViewRotationAngle);
        Assert.IsFalse(viewModel.RotateViewCommand.CanExecute(null));

        viewModel.SetContentSizeForTesting(1080, 2220);
        Assert.IsTrue(viewModel.RotateViewCommand.CanExecute(null));

        for (int expected = 1; expected <= 4; expected++)
        {
            viewModel.RotateViewCommand.Execute(null);
            int expectedQuarterTurns = expected % 4;
            Assert.AreEqual(expectedQuarterTurns, viewModel.ViewRotationQuarterTurns);
            Assert.AreEqual(expectedQuarterTurns * 90d, viewModel.ViewRotationAngle);
        }

        Assert.AreEqual(0, actions.ReceivedCalls().Count());
        await adb.DidNotReceive().RunAdbShellAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ViewAspectRatio_UsesRawAspectForEvenAndInverseForOddTurns()
    {
        await using ViewDeviceViewModel viewModel = CreateViewModel(new FakeTracker([]));
        viewModel.SetContentSizeForTesting(1080, 2220);

        Assert.AreEqual(1080d / 2220d, viewModel.DeviceAspectRatio, 0.000001d);
        Assert.AreEqual(1080d / 2220d, viewModel.ViewAspectRatio, 0.000001d);

        viewModel.RotateViewCommand.Execute(null);
        Assert.AreEqual(2220d / 1080d, viewModel.ViewAspectRatio, 0.000001d);

        viewModel.RotateViewCommand.Execute(null);
        Assert.AreEqual(1080d / 2220d, viewModel.ViewAspectRatio, 0.000001d);
    }

    [TestMethod]
    public async Task ShellCommand_UsesStatusWithoutExposingOutput_AndIgnoresStaleCompletion()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        var first = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        adb.RunAdbAsync(Serial, "get-state", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "device", string.Empty));
        adb.RunAdbShellAsync(Serial, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.ArgAt<string>(1) == "first" ? first.Task : second.Task);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            adb: adb,
            factory: new TrackingSessionFactory(new TrackingSession(Serial)));

        await viewModel.InitializeAsync(Serial, "Device");
        Task firstRun = viewModel.RunAdbShellForTestingAsync("first");
        Task secondRun = viewModel.RunAdbShellForTestingAsync("second");

        second.SetResult(new CommandResult(0, "new output", "new warning"));
        await secondRun;
        first.SetResult(new CommandResult(1, "old output", "old warning"));
        await firstRun;

        Assert.AreEqual("ViewDevice_AdbShellCompleted", viewModel.ToolStatusText);
    }

    [TestMethod]
    public async Task InstallPackage_UsesConservativeInstallOptions()
    {
        string apkPath = Path.Combine(Path.GetTempPath(), $"deep-droid-{Guid.NewGuid():N}.apk");
        try
        {
            File.WriteAllText(apkPath, "apk");
            var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
            IPackageInstallService packages = Substitute.For<IPackageInstallService>();
            packages.InstallAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<InstallPackageOptions>(),
                    Arg.Any<CancellationToken>())
                .Returns(new InstallPackageResult(apkPath, true, "success"));
            await using ViewDeviceViewModel viewModel = CreateViewModel(
                tracker,
                packages: packages,
                factory: new TrackingSessionFactory(new TrackingSession(Serial)));

            await viewModel.InitializeAsync(Serial, "Device");
            await viewModel.InstallPackageCommand.ExecuteAsync(apkPath);

            await packages.Received(1).InstallAsync(
                Serial,
                apkPath,
                Arg.Is<InstallPackageOptions>(options =>
                    !options.GrantPermissions && !options.AllowDowngrade),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            if (File.Exists(apkPath))
                File.Delete(apkPath);
        }
    }

    [TestMethod]
    public async Task InitializeAsync_RefreshesRuntimeStateImmediately_AndUsesThreeSecondPolling()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        var actions = Substitute.For<IDeviceActionService>();
        actions.GetWifiEnabledAsync(Serial, Arg.Any<CancellationToken>()).Returns(true);
        actions.GetScreenOnAsync(Serial, Arg.Any<CancellationToken>()).Returns(false);
        actions.GetGooglePackageStateAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(new GooglePackageState(IsGmsDisabled: true, IsPlayStoreDisabled: false));
        var polling = new CapturingPollingService();
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            polling: polling,
            factory: new TrackingSessionFactory(new TrackingSession(Serial)));

        await viewModel.InitializeAsync(Serial, "Device");

        Assert.IsNull(viewModel.IsWifiEnabled);
        Assert.IsNull(viewModel.IsScreenOn);
        Assert.IsNull(viewModel.IsGmsEnabled);
        Assert.IsNull(viewModel.IsPlayStoreEnabled);
        Assert.IsNull(polling.Operation);

        await viewModel.SetToolsVisibilityForTestingAsync(true);

        Assert.IsTrue(viewModel.IsWifiEnabled);
        Assert.IsFalse(viewModel.IsScreenOn);
        Assert.IsFalse(viewModel.IsGmsEnabled);
        Assert.IsTrue(viewModel.IsPlayStoreEnabled);
        Assert.AreEqual(TimeSpan.FromSeconds(3), polling.Interval);
        Assert.IsNotNull(polling.Operation);
    }

    [TestMethod]
    public async Task WifiUnknown_ToggleDisabled()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        actions.GetWifiEnabledAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("unknown")));
        ConfigureKnownScreenAndPackages(actions);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            runtimeVerificationDelay: NoVerificationDelay);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);

        Assert.IsNull(viewModel.IsWifiEnabled);
        Assert.IsFalse(viewModel.IsWifiActionAvailable);
        Assert.IsFalse(viewModel.ToggleWifiCommand.CanExecute(null));
        await viewModel.ToggleWifiCommand.ExecuteAsync(null);
        await actions.DidNotReceive().SetWifiEnabledAsync(
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GmsUnknown_ToggleDisabled()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        ConfigureKnownWifiAndScreen(actions);
        actions.GetGooglePackageStateAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(new GooglePackageState(false, false, IsGmsInstalled: false));
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            runtimeVerificationDelay: NoVerificationDelay);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);

        Assert.IsNull(viewModel.IsGmsEnabled);
        Assert.IsFalse(viewModel.IsGmsActionAvailable);
        Assert.IsFalse(viewModel.ToggleGmsCommand.CanExecute(null));
        await viewModel.ToggleGmsCommand.ExecuteAsync(null);
        await actions.DidNotReceive().SetGmsEnabledAsync(
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task PlayStoreUnknown_ToggleDisabled()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        ConfigureKnownWifiAndScreen(actions);
        actions.GetGooglePackageStateAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(new GooglePackageState(false, false, IsPlayStoreInstalled: false));
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            runtimeVerificationDelay: NoVerificationDelay);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);

        Assert.IsNull(viewModel.IsPlayStoreEnabled);
        Assert.IsFalse(viewModel.IsPlayStoreActionAvailable);
        Assert.IsFalse(viewModel.TogglePlayStoreCommand.CanExecute(null));
        await viewModel.TogglePlayStoreCommand.ExecuteAsync(null);
        await actions.DidNotReceive().SetPlayStoreEnabledAsync(
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ScreenUnknown_ToggleDisabled()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        var session = new TrackingSession(Serial);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        ConfigureKnownWifiAndPackages(actions);
        actions.GetScreenOnAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(null));
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            runtimeVerificationDelay: NoVerificationDelay,
            factory: new TrackingSessionFactory(session));

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);

        Assert.IsNull(viewModel.IsScreenOn);
        Assert.IsFalse(viewModel.IsScreenActionAvailable);
        Assert.IsFalse(viewModel.ToggleScreenCommand.CanExecute(null));
        await viewModel.ToggleScreenCommand.ExecuteAsync(null);
        CollectionAssert.DoesNotContain(session.Actions, "screen:POWER_MODE_OFF");
    }

    [TestMethod]
    public async Task ScreenToggle_DisabledWhenScrcpySessionIsNotRunning()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        var session = new TrackingSession(Serial);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        ConfigureKnownWifiAndPackages(actions);
        actions.GetScreenOnAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            runtimeVerificationDelay: NoVerificationDelay,
            factory: new TrackingSessionFactory(session));

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);
        session.SetStateForTesting(SingleViewDeviceSessionState.Starting);

        Assert.IsFalse(viewModel.IsScreenActionAvailable);
        Assert.IsFalse(viewModel.ToggleScreenCommand.CanExecute(null));
        await viewModel.ToggleScreenCommand.ExecuteAsync(null);
        CollectionAssert.DoesNotContain(session.Actions, "screen:POWER_MODE_OFF");
    }

    [TestMethod]
    public async Task WifiToggle_SucceedsOnlyWhenVerificationMatchesDesiredState()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        int queryCount = 0;
        actions.GetWifiEnabledAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Interlocked.Increment(ref queryCount) <= 2));
        ConfigureKnownScreenAndPackages(actions);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            runtimeVerificationDelay: NoVerificationDelay);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);
        await viewModel.ToggleWifiCommand.ExecuteAsync(null);

        await actions.Received(1).SetWifiEnabledAsync(
            Serial,
            false,
            Arg.Any<CancellationToken>());
        Assert.AreEqual("ViewDevice_RuntimeToggleSucceeded", viewModel.ToolStatusText);
        Assert.IsFalse(viewModel.IsWifiEnabled);
    }

    [TestMethod]
    public async Task WifiToggle_ReportsVerificationFailureWhenDeviceKeepsOppositeState()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        actions.GetWifiEnabledAsync(Serial, Arg.Any<CancellationToken>()).Returns(true);
        ConfigureKnownScreenAndPackages(actions);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            runtimeVerificationDelay: NoVerificationDelay);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);
        await viewModel.ToggleWifiCommand.ExecuteAsync(null);

        Assert.AreEqual(
            "ViewDevice_RuntimeToggleVerificationFailed",
            viewModel.ToolStatusText);
        Assert.IsTrue(viewModel.IsWifiEnabled);
    }

    [TestMethod]
    public async Task WifiToggle_ReportsUnableToVerifyWhenQueryFails()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        int queryCount = 0;
        actions.GetWifiEnabledAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                return Interlocked.Increment(ref queryCount) == 1
                    ? Task.FromResult(true)
                    : Task.FromException<bool>(new InvalidOperationException("unknown"));
            });
        ConfigureKnownScreenAndPackages(actions);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            runtimeVerificationDelay: NoVerificationDelay);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);
        await viewModel.ToggleWifiCommand.ExecuteAsync(null);

        Assert.AreEqual("ViewDevice_RuntimeToggleUnableToVerify", viewModel.ToolStatusText);
    }

    [TestMethod]
    public async Task GmsToggle_VerifiesGmsStateIndependently()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        ConfigureKnownWifiAndScreen(actions);
        int packageQueryCount = 0;
        actions.GetGooglePackageStateAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref packageQueryCount) <= 2
                ? new GooglePackageState(false, false)
                : new GooglePackageState(true, false));
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            runtimeVerificationDelay: NoVerificationDelay);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);
        await viewModel.ToggleGmsCommand.ExecuteAsync(null);

        await actions.Received(1).SetGmsEnabledAsync(
            Serial,
            false,
            Arg.Any<CancellationToken>());
        Assert.IsFalse(viewModel.IsGmsEnabled);
        Assert.AreEqual("ViewDevice_RuntimeToggleSucceeded", viewModel.ToolStatusText);
    }

    [TestMethod]
    public async Task PlayStoreToggle_VerifiesPlayStoreStateIndependently()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        ConfigureKnownWifiAndScreen(actions);
        int packageQueryCount = 0;
        actions.GetGooglePackageStateAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref packageQueryCount) <= 2
                ? new GooglePackageState(false, true)
                : new GooglePackageState(false, false));
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            runtimeVerificationDelay: NoVerificationDelay);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);
        await viewModel.TogglePlayStoreCommand.ExecuteAsync(null);

        await actions.Received(1).SetPlayStoreEnabledAsync(
            Serial,
            true,
            Arg.Any<CancellationToken>());
        Assert.IsTrue(viewModel.IsPlayStoreEnabled);
        Assert.AreEqual("ViewDevice_RuntimeToggleSucceeded", viewModel.ToolStatusText);
    }

    [TestMethod]
    public async Task ScreenToggle_VerifiesAdbStateAfterScrcpyAction()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        var session = new TrackingSession(Serial);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        ConfigureKnownWifiAndPackages(actions);
        int screenQueryCount = 0;
        actions.GetScreenOnAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<bool?>(
                Interlocked.Increment(ref screenQueryCount) > 2));
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory: new TrackingSessionFactory(session),
            actions: actions,
            runtimeVerificationDelay: NoVerificationDelay);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);
        await viewModel.ToggleScreenCommand.ExecuteAsync(null);

        CollectionAssert.Contains(session.Actions, "screen:POWER_MODE_NORMAL");
        Assert.IsTrue(viewModel.IsScreenOn);
        Assert.AreEqual("ViewDevice_RuntimeToggleSucceeded", viewModel.ToolStatusText);
    }

    [TestMethod]
    public async Task InstallPackage_PreservesSpecificFailureMessage()
    {
        string apkPath = Path.Combine(Path.GetTempPath(), $"deep-droid-{Guid.NewGuid():N}.apk");
        try
        {
            File.WriteAllText(apkPath, "apk");
            var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
            IPackageInstallService packages = Substitute.For<IPackageInstallService>();
            packages.InstallAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<InstallPackageOptions>(),
                    Arg.Any<CancellationToken>())
                .Returns(new InstallPackageResult(
                    apkPath,
                    false,
                    "ViewDevice_TestSpecificFailure",
                    "INSTALL_FAILED_VERSION_DOWNGRADE",
                    "detail"));
            var localization = Substitute.For<ILocalizationService>();
            localization.GetString(Arg.Any<string>()).Returns(callInfo =>
                callInfo.Arg<string>() == "ViewDevice_TestSpecificFailure"
                    ? "Specific failure: {0}"
                    : callInfo.Arg<string>());
            await using ViewDeviceViewModel viewModel = CreateViewModel(
                tracker,
                packages: packages,
                localization: localization);

            await viewModel.InitializeAsync(Serial, "Device");
            await viewModel.InstallPackageCommand.ExecuteAsync(apkPath);

            Assert.AreEqual("Specific failure: detail", viewModel.ToolStatusText);
        }
        finally
        {
            if (File.Exists(apkPath))
                File.Delete(apkPath);
        }
    }

    [TestMethod]
    public async Task AdbShellFailure_UsesStatusWithoutExposingCommandOutput()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        IAdbCommandService adb = Substitute.For<IAdbCommandService>();
        CommandResult rawResult = new(1, "secret stdout", "secret stderr");
        adb.RunAdbAsync(Serial, "get-state", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "device", string.Empty));
        adb.RunAdbShellAsync(Serial, "dump", Arg.Any<CancellationToken>())
            .Returns(rawResult);
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            adb: adb);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.RunAdbShellForTestingAsync("dump");

        Assert.AreEqual("ViewDevice_AdbShellFailedFormat", viewModel.ToolStatusText);
    }

    [TestMethod]
    public async Task ToolStatus_ReResolvesWhenLanguageChanges()
    {
        string apkPath = Path.Combine(Path.GetTempPath(), $"deep-droid-{Guid.NewGuid():N}.apk");
        try
        {
            File.WriteAllText(apkPath, "apk");
            var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
            var localization = new FakeLocalizationService();
            IPackageInstallService packages = Substitute.For<IPackageInstallService>();
            packages.InstallAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<InstallPackageOptions>(),
                    Arg.Any<CancellationToken>())
                .Returns(new InstallPackageResult(
                    apkPath,
                    true,
                    "ViewDevice_TestStatus",
                    messageArguments: ["42"]));
            await using ViewDeviceViewModel viewModel = CreateViewModel(
                tracker,
                packages: packages,
                localization: localization);

            await viewModel.InitializeAsync(Serial, "Device");
            await viewModel.InstallPackageCommand.ExecuteAsync(apkPath);
            Assert.AreEqual("Status 42", viewModel.ToolStatusText);

            localization.ApplyLanguage("vi");
            Assert.AreEqual("Trạng thái 42", viewModel.ToolStatusText);
        }
        finally
        {
            if (File.Exists(apkPath))
                File.Delete(apkPath);
        }
    }

    [TestMethod]
    public async Task ToolsPolling_HiddenDoesNotQueryAdb()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        var polling = new LifecyclePollingService();
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            polling: polling);

        await viewModel.InitializeAsync(Serial, "Device");

        await actions.DidNotReceive().GetWifiEnabledAsync(
            Serial,
            Arg.Any<CancellationToken>());
        await actions.DidNotReceive().GetGooglePackageStateAsync(
            Serial,
            Arg.Any<CancellationToken>());
        Assert.AreEqual(0, polling.RunCount);
    }

    [TestMethod]
    public async Task ToolsPolling_ShowPerformsImmediateRefreshAndStartsOneLoop()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        ConfigureKnownRuntimeState(actions, wifiEnabled: true, screenOn: false);
        var polling = new LifecyclePollingService();
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            polling: polling,
            runtimeVerificationDelay: NoVerificationDelay);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);

        await actions.Received(1).GetWifiEnabledAsync(
            Serial,
            Arg.Any<CancellationToken>());
        await actions.Received(1).GetGooglePackageStateAsync(
            Serial,
            Arg.Any<CancellationToken>());
        Assert.AreEqual(1, polling.RunCount);
        Assert.AreEqual(TimeSpan.FromSeconds(3), polling.Interval);
        Assert.AreEqual(1, polling.ActiveLoopCount);
    }

    [TestMethod]
    public async Task ToolsPolling_AdbOnlineContinuesWhileScrcpyIsNotRunning()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        var session = new TrackingSession(Serial);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        int wifiQueryCount = 0;
        actions.GetWifiEnabledAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref wifiQueryCount);
                return Task.FromResult(true);
            });
        actions.GetScreenOnAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));
        actions.GetGooglePackageStateAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(new GooglePackageState(false, false));
        var polling = new LifecyclePollingService();
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            factory: new TrackingSessionFactory(session),
            actions: actions,
            polling: polling,
            runtimeVerificationDelay: NoVerificationDelay);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);
        int queryCountAfterShow = Volatile.Read(ref wifiQueryCount);

        session.SetStateForTesting(SingleViewDeviceSessionState.Starting);
        await polling.LatestOperation!(polling.LatestCancellationToken);

        Assert.IsTrue(viewModel.IsDeviceOnline);
        Assert.IsTrue(Volatile.Read(ref wifiQueryCount) > queryCountAfterShow);
    }

    [TestMethod]
    public async Task ToolsPolling_ShowHideShow_DoesNotCreateDuplicateLoops()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        ConfigureKnownRuntimeState(actions, wifiEnabled: true, screenOn: false);
        var polling = new LifecyclePollingService();
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            polling: polling,
            runtimeVerificationDelay: NoVerificationDelay);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);
        await viewModel.SetToolsVisibilityForTestingAsync(false);
        Assert.AreEqual(0, polling.ActiveLoopCount);
        await viewModel.SetToolsVisibilityForTestingAsync(true);

        Assert.AreEqual(2, polling.RunCount);
        Assert.AreEqual(1, polling.ActiveLoopCount);
    }

    [TestMethod]
    public async Task RuntimePolling_DisposeCancelsLoopAndIgnoresLaterCallback()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        ConfigureKnownRuntimeState(actions, wifiEnabled: true, screenOn: false);
        int wifiQueryCount = 0;
        actions.GetWifiEnabledAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref wifiQueryCount);
                return Task.FromResult(true);
            });
        var polling = new LifecyclePollingService();
        ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            polling: polling,
            runtimeVerificationDelay: NoVerificationDelay);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);
        Func<CancellationToken, Task> callback = polling.LatestOperation!;
        await viewModel.DisposeAsync();

        Assert.AreEqual(0, polling.ActiveLoopCount);
        int queryCountAfterDispose = Volatile.Read(ref wifiQueryCount);
        await callback(CancellationToken.None);
        Assert.AreEqual(queryCountAfterDispose, Volatile.Read(ref wifiQueryCount));
    }

    [TestMethod]
    public async Task RuntimePolling_OfflineStopsQueries()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        int wifiQueryCount = 0;
        actions.GetWifiEnabledAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref wifiQueryCount);
                return Task.FromResult(true);
            });
        actions.GetScreenOnAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));
        actions.GetGooglePackageStateAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(new GooglePackageState(false, false));
        var polling = new LifecyclePollingService();
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            polling: polling,
            runtimeVerificationDelay: NoVerificationDelay);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);
        int queryCountAfterShow = Volatile.Read(ref wifiQueryCount);

        tracker.SetDevice(new AdbDevice(Serial, AdbDeviceStatus.Offline));
        await polling.LatestOperation!(polling.LatestCancellationToken);

        Assert.AreEqual(queryCountAfterShow, Volatile.Read(ref wifiQueryCount));
        Assert.IsNull(viewModel.IsWifiEnabled);
        Assert.IsNull(viewModel.IsGmsEnabled);
        Assert.IsNull(viewModel.IsPlayStoreEnabled);
        Assert.IsNull(viewModel.IsScreenOn);
    }

    [TestMethod]
    public async Task RuntimePolling_ReconnectedDeviceRefreshesImmediately()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        int wifiQueryCount = 0;
        int expectReconnectRefresh = 0;
        var reconnectRefreshStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        actions.GetWifiEnabledAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                int queryCount = Interlocked.Increment(ref wifiQueryCount);
                if (Volatile.Read(ref expectReconnectRefresh) != 0 && queryCount > 1)
                    reconnectRefreshStarted.TrySetResult();
                return Task.FromResult(true);
            });
        actions.GetScreenOnAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));
        actions.GetGooglePackageStateAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(new GooglePackageState(false, false));
        var polling = new LifecyclePollingService();
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            polling: polling,
            runtimeVerificationDelay: NoVerificationDelay);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);
        tracker.SetDevice(new AdbDevice(Serial, AdbDeviceStatus.Offline));
        await polling.LatestOperation!(polling.LatestCancellationToken);
        int queryCountWhileOffline = Volatile.Read(ref wifiQueryCount);

        Volatile.Write(ref expectReconnectRefresh, 1);
        tracker.SetDevice(new AdbDevice(Serial, AdbDeviceStatus.Online));
        await reconnectRefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.IsTrue(Volatile.Read(ref wifiQueryCount) > queryCountWhileOffline);
        Assert.IsTrue(viewModel.IsWifiEnabled);
    }

    [TestMethod]
    public async Task RuntimePolling_DoesNotOverlapRefreshes()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        IDeviceActionService actions = Substitute.For<IDeviceActionService>();
        ConfigureKnownScreenAndPackages(actions);
        int holdQueries = 0;
        int activeQueries = 0;
        int maxActiveQueries = 0;
        var firstQueryStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseQuery = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        actions.GetWifiEnabledAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                if (Volatile.Read(ref holdQueries) == 0)
                    return true;

                int active = Interlocked.Increment(ref activeQueries);
                int observedMaximum = Volatile.Read(ref maxActiveQueries);
                while (active > observedMaximum &&
                       Interlocked.CompareExchange(
                           ref maxActiveQueries,
                           active,
                           observedMaximum) != observedMaximum)
                {
                    observedMaximum = Volatile.Read(ref maxActiveQueries);
                }

                firstQueryStarted.TrySetResult();
                try
                {
                    await releaseQuery.Task.ConfigureAwait(false);
                    return true;
                }
                finally
                {
                    Interlocked.Decrement(ref activeQueries);
                }
            });
        var polling = new LifecyclePollingService();
        await using ViewDeviceViewModel viewModel = CreateViewModel(
            tracker,
            actions: actions,
            polling: polling,
            runtimeVerificationDelay: NoVerificationDelay);

        await viewModel.InitializeAsync(Serial, "Device");
        await viewModel.SetToolsVisibilityForTestingAsync(true);
        Volatile.Write(ref holdQueries, 1);

        Task firstRefresh = polling.LatestOperation!(polling.LatestCancellationToken);
        await firstQueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Task secondRefresh = polling.LatestOperation!(polling.LatestCancellationToken);

        releaseQuery.SetResult();
        await Task.WhenAll(firstRefresh, secondRefresh);

        Assert.AreEqual(1, Volatile.Read(ref maxActiveQueries));
    }

    [TestMethod]
    public async Task PackageDrag_OfflineDeviceShowsNone()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Offline)]);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker);
        await viewModel.InitializeAsync(Serial, "Device");

        Assert.IsFalse(ViewDeviceWindow.CanAcceptPackageDrop(
            viewModel,
            new DataObject(DataFormats.FileDrop, new[] { @"C:\Temp\package.apk" })));
    }

    [TestMethod]
    public async Task PackageDrag_OnlineDeviceShowsCopyOnlyForOneSupportedFile()
    {
        var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
        await using ViewDeviceViewModel viewModel = CreateViewModel(tracker);
        await viewModel.InitializeAsync(Serial, "Device");

        Assert.IsTrue(ViewDeviceWindow.CanAcceptPackageDrop(
            viewModel,
            new DataObject(DataFormats.FileDrop, new[] { @"C:\Temp\package.apk" })));
        Assert.IsTrue(ViewDeviceWindow.CanAcceptPackageDrop(
            viewModel,
            new DataObject(DataFormats.FileDrop, new[] { @"C:\Temp\package.XAPK" })));
        Assert.IsFalse(ViewDeviceWindow.CanAcceptPackageDrop(
            viewModel,
            new DataObject(DataFormats.FileDrop, new[] { @"C:\Temp\package.zip" })));
        Assert.IsFalse(ViewDeviceWindow.CanAcceptPackageDrop(
            viewModel,
            new DataObject(
                DataFormats.FileDrop,
            new[] { @"C:\Temp\one.apk", @"C:\Temp\two.apk" })));
    }

    [TestMethod]
    public async Task Restart_DisabledWhileInstallRunning()
    {
        string apkPath = Path.Combine(Path.GetTempPath(), $"deep-droid-{Guid.NewGuid():N}.apk");
        try
        {
            File.WriteAllText(apkPath, "apk");
            var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
            IPackageInstallService packages = Substitute.For<IPackageInstallService>();
            var installGate = new TaskCompletionSource<InstallPackageResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            packages.InstallAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<InstallPackageOptions>(),
                    Arg.Any<CancellationToken>())
                .Returns(installGate.Task);
            await using ViewDeviceViewModel viewModel = CreateViewModel(
                tracker,
                packages: packages);

            await viewModel.InitializeAsync(Serial, "Device");
            Task installTask = viewModel.InstallPackageCommand.ExecuteAsync(apkPath);

            Assert.IsTrue(viewModel.IsInstallBusy);
            Assert.IsFalse(viewModel.RebootDeviceCommand.CanExecute(null));

            installGate.SetResult(new InstallPackageResult(apkPath, true, "success"));
            await installTask;
        }
        finally
        {
            if (File.Exists(apkPath))
                File.Delete(apkPath);
        }
    }

    [TestMethod]
    public async Task Restart_DisabledWhileShellAndFileTransfersAreRunning()
    {
        string localImportPath = Path.Combine(Path.GetTempPath(), $"deep-droid-{Guid.NewGuid():N}.txt");
        string localExportPath = Path.Combine(Path.GetTempPath(), $"deep-droid-{Guid.NewGuid():N}-export.txt");
        try
        {
            File.WriteAllText(localImportPath, "payload");
            var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
            IAdbCommandService adb = Substitute.For<IAdbCommandService>();
            var shellGate = new TaskCompletionSource<CommandResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var pushGate = new TaskCompletionSource<CommandResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var pullGate = new TaskCompletionSource<CommandResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            adb.RunAdbShellAsync(Serial, "echo test", Arg.Any<CancellationToken>())
                .Returns(shellGate.Task);
            adb.PushFileAsync(
                    Serial,
                    localImportPath,
                    "/sdcard/import.txt",
                    Arg.Any<CancellationToken>())
                .Returns(pushGate.Task);
            adb.PullFileAsync(
                    Serial,
                    "/sdcard/export.txt",
                    localExportPath,
                    Arg.Any<CancellationToken>())
                .Returns(pullGate.Task);
            var filePicker = Substitute.For<IFilePickerDialogService>();
            filePicker.ShowOpenFileDialog(Arg.Any<string>(), Arg.Any<string>())
                .Returns(localImportPath);
            filePicker.ShowSaveFileDialog(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(localExportPath);
            await using ViewDeviceViewModel viewModel = CreateViewModel(
                tracker,
                adb: adb,
                filePicker: filePicker);

            await viewModel.InitializeAsync(Serial, "Device");

            viewModel.AdbShellCommand = "echo test";
            Task shellTask = viewModel.RunAdbShellCommand.ExecuteAsync(null);
            Assert.IsTrue(viewModel.IsAdbShellBusy);
            Assert.IsFalse(viewModel.RebootDeviceCommand.CanExecute(null));
            shellGate.SetResult(new CommandResult(0, "ok", string.Empty));
            await shellTask;

            viewModel.BrowseComputerFileCommand.Execute(null);
            viewModel.AndroidFilePath = "/sdcard/import.txt";
            Task pushTask = viewModel.PushFileCommand.ExecuteAsync(null);
            Assert.IsTrue(viewModel.IsFileTransferBusy);
            Assert.IsFalse(viewModel.RebootDeviceCommand.CanExecute(null));
            pushGate.SetResult(new CommandResult(0, "ok", string.Empty));
            await pushTask;

            viewModel.AndroidFilePath = "/sdcard/export.txt";
            viewModel.ComputerFilePath = localExportPath;
            Task pullTask = viewModel.PullFileCommand.ExecuteAsync(null);
            Assert.IsTrue(viewModel.IsFileTransferBusy);
            Assert.IsFalse(viewModel.RebootDeviceCommand.CanExecute(null));
            pullGate.SetResult(new CommandResult(0, "ok", string.Empty));
            await pullTask;
        }
        finally
        {
            if (File.Exists(localImportPath))
                File.Delete(localImportPath);
            if (File.Exists(localExportPath))
                File.Delete(localExportPath);
        }
    }

    [TestMethod]
    public async Task ShellAndFileTransfers_AreDisabledWhileRestartRunning()
    {
        string localImportPath = Path.Combine(Path.GetTempPath(), $"deep-droid-{Guid.NewGuid():N}.txt");
        string localExportPath = Path.Combine(Path.GetTempPath(), $"deep-droid-{Guid.NewGuid():N}-export.txt");
        try
        {
            File.WriteAllText(localImportPath, "payload");
            var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
            IDeviceActionService actions = Substitute.For<IDeviceActionService>();
            var rebootGate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            actions.RebootAsync(Serial, Arg.Any<CancellationToken>()).Returns(rebootGate.Task);
            var filePicker = Substitute.For<IFilePickerDialogService>();
            filePicker.ShowOpenFileDialog(Arg.Any<string>(), Arg.Any<string>())
                .Returns(localImportPath);
            filePicker.ShowSaveFileDialog(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(localExportPath);
            await using ViewDeviceViewModel viewModel = CreateViewModel(
                tracker,
                actions: actions,
                filePicker: filePicker);

            await viewModel.InitializeAsync(Serial, "Device");
            viewModel.AdbShellCommand = "echo test";
            viewModel.BrowseComputerFileCommand.Execute(null);
            viewModel.AndroidFilePath = "/sdcard/import.txt";
            viewModel.AndroidFilePath = "/sdcard/export.txt";
            viewModel.ComputerFilePath = localExportPath;

            Task restartTask = viewModel.RebootDeviceCommand.ExecuteAsync(null);

            Assert.IsTrue(viewModel.IsRestartBusy);
            Assert.IsFalse(viewModel.RunAdbShellCommand.CanExecute(null));
            Assert.IsFalse(viewModel.PushFileCommand.CanExecute(null));
            Assert.IsFalse(viewModel.PullFileCommand.CanExecute(null));

            rebootGate.SetResult();
            await restartTask;
        }
        finally
        {
            if (File.Exists(localImportPath))
                File.Delete(localImportPath);
            if (File.Exists(localExportPath))
                File.Delete(localExportPath);
        }
    }

    [TestMethod]
    public async Task InstallAndToggle_AreDisabledWhileRestartRunning()
    {
        string apkPath = Path.Combine(Path.GetTempPath(), $"deep-droid-{Guid.NewGuid():N}.apk");
        try
        {
            File.WriteAllText(apkPath, "apk");
            var tracker = new FakeTracker([new AdbDevice(Serial, AdbDeviceStatus.Online)]);
            IDeviceActionService actions = Substitute.For<IDeviceActionService>();
            ConfigureKnownRuntimeState(actions, wifiEnabled: true, screenOn: false);
            var rebootGate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            actions.RebootAsync(Serial, Arg.Any<CancellationToken>()).Returns(rebootGate.Task);
            await using ViewDeviceViewModel viewModel = CreateViewModel(
                tracker,
                actions: actions);

            await viewModel.InitializeAsync(Serial, "Device");
            await viewModel.SetToolsVisibilityForTestingAsync(true);
            viewModel.AdbShellCommand = "echo test";
            Task restartTask = viewModel.RebootDeviceCommand.ExecuteAsync(null);

            Assert.IsTrue(viewModel.IsRestartBusy);
            Assert.IsFalse(viewModel.InstallPackageCommand.CanExecute(apkPath));
            Assert.IsFalse(viewModel.RunAdbShellCommand.CanExecute(null));
            Assert.IsFalse(viewModel.ToggleWifiCommand.CanExecute(null));

            rebootGate.SetResult();
            await restartTask;
        }
        finally
        {
            if (File.Exists(apkPath))
                File.Delete(apkPath);
        }
    }

    private static Task NoVerificationDelay(TimeSpan _, CancellationToken __) =>
        Task.CompletedTask;

    private static void ConfigureKnownRuntimeState(
        IDeviceActionService actions,
        bool wifiEnabled,
        bool screenOn)
    {
        actions.GetWifiEnabledAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(wifiEnabled);
        actions.GetScreenOnAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(screenOn));
        actions.GetGooglePackageStateAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(new GooglePackageState(false, false));
    }

    private static void ConfigureKnownScreenAndPackages(IDeviceActionService actions)
    {
        actions.GetScreenOnAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));
        ConfigureKnownPackages(actions);
    }

    private static void ConfigureKnownWifiAndScreen(IDeviceActionService actions)
    {
        actions.GetWifiEnabledAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(true);
        actions.GetScreenOnAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));
    }

    private static void ConfigureKnownWifiAndPackages(IDeviceActionService actions)
    {
        actions.GetWifiEnabledAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(true);
        ConfigureKnownPackages(actions);
    }

    private static void ConfigureKnownPackages(IDeviceActionService actions)
    {
        actions.GetGooglePackageStateAsync(Serial, Arg.Any<CancellationToken>())
            .Returns(new GooglePackageState(false, false));
    }

    private static ViewDeviceViewModel CreateViewModel(
        IAdbDeviceTrackerService tracker,
        ISingleViewDeviceSessionFactory? factory = null,
        IAdbCommandService? adb = null,
        IDeviceConfigService? config = null,
        IDeviceActionService? actions = null,
        IPackageInstallService? packages = null,
        IPollingService? polling = null,
        Func<TimeSpan, CancellationToken, Task>? runtimeVerificationDelay = null,
        ILocalizationService? localization = null,
        IFilePickerDialogService? filePicker = null)
    {
        adb ??= Substitute.For<IAdbCommandService>();
        adb.RunAdbAsync(Serial, "get-state", Arg.Any<CancellationToken>())
            .Returns(new CommandResult(0, "device", string.Empty));
        config ??= Substitute.For<IDeviceConfigService>();
        actions ??= Substitute.For<IDeviceActionService>();
        packages ??= Substitute.For<IPackageInstallService>();
        polling ??= Substitute.For<IPollingService>();
        factory ??= new TrackingSessionFactory(new TrackingSession(Serial));
        localization ??= new FakeLocalizationService();
        filePicker ??= Substitute.For<IFilePickerDialogService>();

        return new ViewDeviceViewModel(
            factory,
            tracker,
            adb,
            filePicker,
            Substitute.For<IViewDeviceScreenshotService>(),
            localization,
            new ImmediateUiDispatcher(),
            Substitute.For<IViewDeviceClipboardService>(),
            new TestLogger<ViewDeviceViewModel>(),
            config,
            actions,
            packages,
            polling,
            ViewDeviceViewModel.DefaultRestartDelays,
            runtimeVerificationDelay: runtimeVerificationDelay);
    }

    private sealed class LifecyclePollingService : IPollingService
    {
        private int _runCount;
        private int _activeLoopCount;

        public TimeSpan Interval { get; private set; }
        public Func<CancellationToken, Task>? LatestOperation { get; private set; }
        public CancellationToken LatestCancellationToken { get; private set; }
        public int RunCount => Volatile.Read(ref _runCount);
        public int ActiveLoopCount => Volatile.Read(ref _activeLoopCount);

        public Task RunAsync(
            TimeSpan interval,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken)
        {
            Interval = interval;
            LatestOperation = operation;
            LatestCancellationToken = cancellationToken;
            Interlocked.Increment(ref _runCount);
            Interlocked.Increment(ref _activeLoopCount);
            return WaitForCancellationAsync(cancellationToken);
        }

        private async Task WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                Interlocked.Decrement(ref _activeLoopCount);
            }
        }
    }

    private sealed class CapturingPollingService : IPollingService
    {
        public TimeSpan Interval { get; private set; }
        public Func<CancellationToken, Task>? Operation { get; private set; }

        public Task RunAsync(
            TimeSpan interval,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken)
        {
            Interval = interval;
            Operation = operation;
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingSessionFactory(TrackingSession session) : ISingleViewDeviceSessionFactory
    {
        public ISingleViewDeviceSession Create(ViewDeviceLaunchOptions options)
        {
            Assert.AreEqual(options.Serial, session.Serial);
            return session;
        }
    }

    private sealed class TrackingSession(string serial) : ISingleViewDeviceSession
    {
        private readonly Scrcpy _client =
            (Scrcpy)RuntimeHelpers.GetUninitializedObject(typeof(Scrcpy));

        public string Serial { get; } = serial;
        public SingleViewDeviceSessionState State { get; private set; } = SingleViewDeviceSessionState.Created;
        public Scrcpy? Client => _client;
        public int ContentWidth => 720;
        public int ContentHeight => 1280;
        public IReadOnlyList<string> RecentDiagnostics => [];
        public List<string> Actions { get; } = [];

        public event EventHandler<SingleViewDeviceSessionStateChangedEventArgs>? StateChanged;
        public event EventHandler<SingleViewDeviceContentSizeChangedEventArgs>? ContentSizeChanged;
        private EventHandler<ScrcpyClipboardChangedEventArgs>? _clipboardChanged;
        private EventHandler? _exited;

        public event EventHandler<ScrcpyClipboardChangedEventArgs>? ClipboardChanged
        {
            add => _clipboardChanged += value;
            remove => _clipboardChanged -= value;
        }

        public event EventHandler? Exited
        {
            add => _exited += value;
            remove => _exited -= value;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetState(SingleViewDeviceSessionState.Starting);
            SetState(SingleViewDeviceSessionState.Running);
            ContentSizeChanged?.Invoke(
                this,
                new SingleViewDeviceContentSizeChangedEventArgs(ContentWidth, ContentHeight));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetState(SingleViewDeviceSessionState.Closed);
            return Task.CompletedTask;
        }

        public void SetStateForTesting(SingleViewDeviceSessionState state)
        {
            SetState(state);
        }

        public Task FlushControlAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task SendKeyEventAsync(int keyCode, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add($"key:{keyCode}");
            return Task.CompletedTask;
        }

        public Task SendBackOrScreenOnAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add("back");
            return Task.CompletedTask;
        }

        public Task SetScreenPowerModeAsync(AndroidScreenPowerMode mode, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add($"screen:{mode}");
            return Task.CompletedTask;
        }

        public Task RotateDeviceAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add("rotate");
            return Task.CompletedTask;
        }

        public Task PasteHostClipboardAsync(string text, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task PasteHostClipboardWithPasteKeyAsync(string text, CancellationToken cancellationToken = default) =>
            PasteHostClipboardAsync(text, cancellationToken);

        public Task InjectTextAsync(string text, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add($"inject:{text}");
            return Task.CompletedTask;
        }

        public Task RequestClipboardAsync(ScrcpyCopyKey copyKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task ExpandNotificationPanelAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ExpandSettingsPanelAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CollapsePanelsAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        private void SetState(SingleViewDeviceSessionState state)
        {
            SingleViewDeviceSessionState previous = State;
            State = state;
            StateChanged?.Invoke(this, new SingleViewDeviceSessionStateChangedEventArgs(previous, state));
        }
    }
}
