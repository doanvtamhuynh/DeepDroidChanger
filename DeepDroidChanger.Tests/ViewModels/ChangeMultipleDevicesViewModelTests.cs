using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.ViewModels;

[TestClass]
public sealed class ChangeMultipleDevicesViewModelTests
{
    [TestMethod]
    public async Task ChangeSelectedLocations_PreflightsLiveOnlineAndUsesStableInitialTargets()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "Alpha" },
                    new StoredDeviceConfig { Serial = "B", Name = "Beta" }
                ],
                [
                    new AdbDevice("A", AdbDeviceStatus.Online),
                    new AdbDevice("B", AdbDeviceStatus.Online)
                ]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var selectedLocation = new LocationOption(
            "VN",
            "Vietnam",
            "Ho Chi Minh City",
            "Asia/Ho_Chi_Minh",
            "+07:00",
            10.75,
            106.6667);
        context.LocationDialog.ShowChangeLocationBatchAsync(
                1,
                Arg.Any<CancellationToken>())
            .Returns(new ChangeLocationDialogResult(
                ChangeLocationMode.Config,
                string.Empty,
                string.Empty,
                selectedLocation));
        context.DeviceConfig.SaveLocationConfigAsync(
                Arg.Any<IList<StoredDeviceConfig>>(),
                Arg.Any<string>(),
                Arg.Any<ChangeLocationMode>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        context.LocationService.ApplyCatalogLocationAsync(
                "A",
                selectedLocation,
                Arg.Any<CancellationToken>())
            .Returns(new DeviceLocationResult("10.7123", "106.6456", "VN", "Ho Chi Minh City"));

        int onlineChecksForA = 0;
        int onlineChecksForB = 0;
        context.DeviceList.IsDeviceOnlineAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                string serial = callInfo.Arg<string>();
                if (serial == "A")
                    Interlocked.Increment(ref onlineChecksForA);
                else
                    Interlocked.Increment(ref onlineChecksForB);

                return Task.FromResult(serial == "A");
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.ChangeSelectedLocationsCommand.ExecuteAsync(null);

        await context.LocationDialog.Received(1)
            .ShowChangeLocationBatchAsync(1, Arg.Any<CancellationToken>());
        await context.LocationService.Received(1)
            .ApplyCatalogLocationAsync("A", selectedLocation, Arg.Any<CancellationToken>());
        await context.LocationService.DidNotReceive()
            .ApplyCatalogLocationAsync("B", Arg.Any<LocationOption>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(2, onlineChecksForA);
        Assert.AreEqual(1, onlineChecksForB);
        Assert.AreEqual("Log_ChangeLocationSuccess", viewModel.Devices.Single(device => device.Serial == "A").Process);
        Assert.AreEqual("Log_DeviceMustBeOnline", viewModel.Devices.Single(device => device.Serial == "B").Process);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ChangeSelectedTimezones_WhenInitialPreflightFindsNoOnlineDevice_DoesNotOpenDialogOrApply()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "Alpha" },
                    new StoredDeviceConfig { Serial = "B", Name = "Beta" }
                ],
                []),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        context.DeviceList.IsDeviceOnlineAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.ChangeSelectedTimezonesCommand.ExecuteAsync(null);

        await context.TimezoneDialog.DidNotReceiveWithAnyArgs()
            .ShowChangeTimezoneBatchAsync(default, default);
        await context.TimezoneService.DidNotReceiveWithAnyArgs()
            .ApplyAsync(default!, default!, default);
        Assert.IsTrue(viewModel.Devices.All(device => device.Process == "Log_DeviceMustBeOnline"));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ChangeSelectedLocations_SkipsBusyDeviceBeforeOpeningDialog()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "Busy" },
                    new StoredDeviceConfig { Serial = "B", Name = "Ready" }
                ],
                [
                    new AdbDevice("A", AdbDeviceStatus.Online),
                    new AdbDevice("B", AdbDeviceStatus.Online)
                ]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var selectedLocation = new LocationOption(
            "VN",
            "Vietnam",
            "Ho Chi Minh City",
            "Asia/Ho_Chi_Minh",
            "+07:00",
            10.75,
            106.6667);
        context.LocationDialog.ShowChangeLocationBatchAsync(
                1,
                Arg.Any<CancellationToken>())
            .Returns(new ChangeLocationDialogResult(
                ChangeLocationMode.Config,
                string.Empty,
                string.Empty,
                selectedLocation));
        context.LocationService.ApplyCatalogLocationAsync(
                "B",
                selectedLocation,
                Arg.Any<CancellationToken>())
            .Returns(new DeviceLocationResult("10.7123", "106.6456", "VN", "Ho Chi Minh City"));

        await viewModel.InitializeAsync(CancellationToken.None);
        using IDisposable busyLease = context.DeviceActionGuard.TryAcquire("A")!;
        viewModel.SelectedInfoDevice = viewModel.Devices.Single(device => device.Serial == "B");

        await viewModel.ChangeSelectedLocationsCommand.ExecuteAsync(null);

        await context.LocationDialog.Received(1)
            .ShowChangeLocationBatchAsync(1, Arg.Any<CancellationToken>());
        await context.LocationService.DidNotReceive()
            .ApplyCatalogLocationAsync("A", Arg.Any<LocationOption>(), Arg.Any<CancellationToken>());
        await context.LocationService.Received(1)
            .ApplyCatalogLocationAsync("B", selectedLocation, Arg.Any<CancellationToken>());
        await context.DeviceList.DidNotReceive()
            .IsDeviceOnlineAsync("A", Arg.Any<CancellationToken>());
        Assert.AreEqual("Log_Ready", viewModel.Devices.Single(device => device.Serial == "A").Process);
        Assert.AreEqual("Log_ChangeLocationSuccess", viewModel.Devices.Single(device => device.Serial == "B").Process);
        Assert.IsTrue(context.DeviceActionGuard.IsBusy("A"));
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("B"));

        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ChangeSelectedTimezones_SkipsBusyDeviceBeforeOpeningDialog()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "Busy" },
                    new StoredDeviceConfig { Serial = "B", Name = "Ready" }
                ],
                [
                    new AdbDevice("A", AdbDeviceStatus.Online),
                    new AdbDevice("B", AdbDeviceStatus.Online)
                ]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        context.TimezoneDialog.ShowChangeTimezoneBatchAsync(
                1,
                Arg.Any<CancellationToken>())
            .Returns(new ChangeTimezoneDialogResult(ChangeTimezoneMode.Data, "Asia/Ho_Chi_Minh"));
        context.TimezoneService.ApplyAsync(
                "B",
                Arg.Any<ChangeTimezoneDialogResult>(),
                Arg.Any<CancellationToken>())
            .Returns("Asia/Ho_Chi_Minh");

        await viewModel.InitializeAsync(CancellationToken.None);
        using IDisposable busyLease = context.DeviceActionGuard.TryAcquire("A")!;
        viewModel.SelectedInfoDevice = viewModel.Devices.Single(device => device.Serial == "B");

        await viewModel.ChangeSelectedTimezonesCommand.ExecuteAsync(null);

        await context.TimezoneDialog.Received(1)
            .ShowChangeTimezoneBatchAsync(1, Arg.Any<CancellationToken>());
        await context.TimezoneService.DidNotReceive()
            .ApplyAsync("A", Arg.Any<ChangeTimezoneDialogResult>(), Arg.Any<CancellationToken>());
        await context.TimezoneService.Received(1)
            .ApplyAsync("B", Arg.Any<ChangeTimezoneDialogResult>(), Arg.Any<CancellationToken>());
        await context.DeviceList.DidNotReceive()
            .IsDeviceOnlineAsync("A", Arg.Any<CancellationToken>());
        Assert.AreEqual("Log_Ready", viewModel.Devices.Single(device => device.Serial == "A").Process);
        Assert.AreEqual("Log_ChangeTimezoneSuccess", viewModel.Devices.Single(device => device.Serial == "B").Process);
        Assert.IsTrue(context.DeviceActionGuard.IsBusy("A"));
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("B"));

        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ChangeSelectedLocations_HoldsLeaseUntilDialogCompletes()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Ready" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var dialogOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogResult = new TaskCompletionSource<ChangeLocationDialogResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.LocationDialog.ShowChangeLocationBatchAsync(
                1,
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                dialogOpened.SetResult();
                return dialogResult.Task;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task operation = viewModel.ChangeSelectedLocationsCommand.ExecuteAsync(null);
        await dialogOpened.Task;

        Assert.IsTrue(context.DeviceActionGuard.IsBusy("A"));
        Assert.IsNull(context.DeviceActionGuard.TryAcquire("A"));

        dialogResult.SetResult(null);
        await operation;

        Assert.IsFalse(context.DeviceActionGuard.IsBusy("A"));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RebootContextCommand_UsesClickedRowWithoutChangingBatchSelection()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "Alpha" },
                    new StoredDeviceConfig { Serial = "B", Name = "Beta" },
                    new StoredDeviceConfig { Serial = "C", Name = "Gamma" }
                ],
                [
                    new AdbDevice("A", AdbDeviceStatus.Online),
                    new AdbDevice("B", AdbDeviceStatus.Online),
                    new AdbDevice("C", AdbDeviceStatus.Online)
                ]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        DeviceRowViewModel clicked = viewModel.Devices.Single(device => device.Serial == "C");
        await viewModel.RebootDeviceCommand.ExecuteAsync(clicked);

        await context.DeviceAction.Received(1).RebootAsync("C", Arg.Any<CancellationToken>());
        await context.DeviceAction.DidNotReceive().RebootAsync("A", Arg.Any<CancellationToken>());
        await context.DeviceAction.DidNotReceive().RebootAsync("B", Arg.Any<CancellationToken>());
        Assert.IsTrue(viewModel.Devices.Single(device => device.Serial == "A").IsSelected);
        Assert.IsTrue(viewModel.Devices.Single(device => device.Serial == "B").IsSelected);
        Assert.IsFalse(clicked.IsSelected);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RefreshContextMenuState_QueriesClickedRow()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "Alpha" },
                    new StoredDeviceConfig { Serial = "B", Name = "Beta" }
                ],
                [
                    new AdbDevice("A", AdbDeviceStatus.Online),
                    new AdbDevice("B", AdbDeviceStatus.Online)
                ]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        context.DeviceAction.GetGooglePackageStateAsync("B", Arg.Any<CancellationToken>())
            .Returns(new GooglePackageState(true, false));
        context.DeviceAction.GetWifiEnabledAsync("B", Arg.Any<CancellationToken>())
            .Returns(true);

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RefreshContextMenuStateCommand.ExecuteAsync(
            viewModel.Devices.Single(device => device.Serial == "B"));

        await context.DeviceAction.Received(1)
            .GetGooglePackageStateAsync("B", Arg.Any<CancellationToken>());
        await context.DeviceAction.Received(1)
            .GetWifiEnabledAsync("B", Arg.Any<CancellationToken>());
        await context.DeviceAction.DidNotReceive()
            .GetGooglePackageStateAsync("A", Arg.Any<CancellationToken>());
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ContextMenuActions_OfflineDeviceMatchesSingleGuards()
    {
        StoredDeviceConfig storedDevice = new() { Serial = "A", Name = "Offline", Type = "Phone" };
        TestContext context = CreateContext(CreateSnapshot([storedDevice], []));
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        DeviceRowViewModel device = viewModel.Devices.Single();

        Assert.IsTrue(viewModel.ViewDeviceCommand.CanExecute(device));
        Assert.IsTrue(viewModel.ViewDeviceInfoCommand.CanExecute(device));
        Assert.IsTrue(viewModel.CopySerialCommand.CanExecute(device));
        Assert.IsTrue(viewModel.ToggleGmsCommand.CanExecute(device));
        Assert.IsTrue(viewModel.TogglePlayStoreCommand.CanExecute(device));
        Assert.IsTrue(viewModel.ToggleWifiCommand.CanExecute(device));
        Assert.IsTrue(viewModel.RebootDeviceCommand.CanExecute(device));
        Assert.IsTrue(viewModel.DeleteDeviceCommand.CanExecute(device));

        await viewModel.ViewDeviceInfoCommand.ExecuteAsync(device);
        await viewModel.RebootDeviceCommand.ExecuteAsync(device);
        await viewModel.ToggleGmsCommand.ExecuteAsync(device);
        await viewModel.TogglePlayStoreCommand.ExecuteAsync(device);
        await viewModel.ToggleWifiCommand.ExecuteAsync(device);

        Assert.AreEqual("Log_DeviceMustBeOnline", device.Process);
        await context.DeviceList.DidNotReceive()
            .IsDeviceOnlineAsync("A", Arg.Any<CancellationToken>());
        await context.DeviceAction.DidNotReceive()
            .RebootAsync("A", Arg.Any<CancellationToken>());
        await context.DeviceAction.DidNotReceive()
            .SetGmsEnabledAsync("A", Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await context.DeviceAction.DidNotReceive()
            .SetPlayStoreEnabledAsync("A", Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await context.DeviceAction.DidNotReceive()
            .SetWifiEnabledAsync("A", Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ContextMenuActions_BusyOnlineDeviceAllowsMenuActionsAndDisablesDelete()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Busy", Type = "Phone" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]));
        context.DeviceAction.GetGooglePackageStateAsync("A", Arg.Any<CancellationToken>())
            .Returns(new GooglePackageState(false, false));
        context.DeviceAction.GetWifiEnabledAsync("A", Arg.Any<CancellationToken>())
            .Returns(true);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        DeviceRowViewModel device = viewModel.Devices.Single();
        IDisposable busyLease = context.DeviceActionGuard.TryAcquire("A")!;

        Assert.IsTrue(viewModel.ViewDeviceCommand.CanExecute(device));
        Assert.IsTrue(viewModel.ViewDeviceInfoCommand.CanExecute(device));
        Assert.IsTrue(viewModel.CopySerialCommand.CanExecute(device));
        Assert.IsTrue(viewModel.ToggleGmsCommand.CanExecute(device));
        Assert.IsTrue(viewModel.TogglePlayStoreCommand.CanExecute(device));
        Assert.IsTrue(viewModel.ToggleWifiCommand.CanExecute(device));
        Assert.IsTrue(viewModel.RebootDeviceCommand.CanExecute(device));
        Assert.IsFalse(viewModel.DeleteDeviceCommand.CanExecute(device));

        await viewModel.ViewDeviceCommand.ExecuteAsync(device);
        await viewModel.RefreshContextMenuStateCommand.ExecuteAsync(device);
        await viewModel.RebootDeviceCommand.ExecuteAsync(device);
        await viewModel.ToggleGmsCommand.ExecuteAsync(device);
        await viewModel.TogglePlayStoreCommand.ExecuteAsync(device);
        await viewModel.ToggleWifiCommand.ExecuteAsync(device);

        await context.ViewerDialog.Received(1)
            .ShowDeviceViewerAsync("A", "Busy", Arg.Any<CancellationToken>());
        await context.DeviceAction.Received(1)
            .RebootAsync("A", Arg.Any<CancellationToken>());
        await context.DeviceAction.Received(1)
            .SetGmsEnabledAsync("A", Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await context.DeviceAction.Received(1)
            .SetPlayStoreEnabledAsync("A", Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await context.DeviceAction.Received(1)
            .SetWifiEnabledAsync("A", Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await context.DeviceAction.Received(3)
            .GetGooglePackageStateAsync("A", Arg.Any<CancellationToken>());
        await context.DeviceAction.Received(2)
            .GetWifiEnabledAsync("A", Arg.Any<CancellationToken>());
        Assert.IsTrue(context.DeviceActionGuard.IsBusy("A"));
        Assert.AreEqual("Log_Ready", device.Process);

        busyLease.Dispose();

        Assert.IsTrue(viewModel.DeleteDeviceCommand.CanExecute(device));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RefreshContextMenuState_BusyDeviceDisablesOnlyToggleActionsWhileLoading()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Busy", Type = "Phone" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]));
        var packageState = new TaskCompletionSource<GooglePackageState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.DeviceAction.GetGooglePackageStateAsync("A", Arg.Any<CancellationToken>())
            .Returns(packageState.Task);
        context.DeviceAction.GetWifiEnabledAsync("A", Arg.Any<CancellationToken>())
            .Returns(true);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        DeviceRowViewModel device = viewModel.Devices.Single();
        using IDisposable busyLease = context.DeviceActionGuard.TryAcquire("A")!;
        Task refresh = viewModel.RefreshContextMenuStateCommand.ExecuteAsync(device);

        Assert.IsTrue(device.IsContextMenuStateLoading);
        Assert.IsFalse(device.CanToggleContextMenuActions);
        Assert.IsTrue(viewModel.ViewDeviceCommand.CanExecute(device));
        Assert.IsTrue(viewModel.CopySerialCommand.CanExecute(device));
        Assert.IsTrue(viewModel.RebootDeviceCommand.CanExecute(device));
        Assert.IsFalse(viewModel.DeleteDeviceCommand.CanExecute(device));

        packageState.SetResult(new GooglePackageState(false, false));
        await refresh;

        Assert.IsFalse(device.IsContextMenuStateLoading);
        Assert.IsTrue(device.CanToggleContextMenuActions);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ToggleSelectAllDevices_WithUnselectedBusyDevice_CanSelectThenClearEditableRows()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "Alpha" },
                    new StoredDeviceConfig { Serial = "B", Name = "Beta" },
                    new StoredDeviceConfig { Serial = "C", Name = "Gamma" }
                ],
                []));
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);
        Assert.IsTrue(viewModel.Devices.All(device => device.Process == "Log_Ready"));
        using IDisposable busyLease = context.DeviceActionGuard.TryAcquire("A")!;

        viewModel.ToggleSelectAllDevicesCommand.Execute(null);

        Assert.IsFalse(viewModel.Devices.Single(device => device.Serial == "A").IsSelected);
        Assert.IsTrue(viewModel.Devices.Single(device => device.Serial == "B").IsSelected);
        Assert.IsTrue(viewModel.Devices.Single(device => device.Serial == "C").IsSelected);

        viewModel.ToggleSelectAllDevicesCommand.Execute(null);

        Assert.IsTrue(viewModel.Devices.All(device => !device.IsSelected));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task Refresh_RemovedInfoDeviceFallsBackToFirstRemainingSelectedDevice()
    {
        var settings = new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] };
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "Alpha" },
                    new StoredDeviceConfig { Serial = "B", Name = "Beta" }
                ],
                []),
            settings);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);
        Assert.AreEqual("A", viewModel.SelectedInfoDevice?.Serial);

        viewModel.ApplyDeviceListSnapshot(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "B", Name = "Beta" }],
                []));

        Assert.AreEqual("B", viewModel.SelectedInfoDevice?.Serial);
        Assert.HasCount(1, viewModel.SelectedDevices);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ChangeSelectedDevices_UsesStableSnapshotAndSavedChangeOptions()
    {
        DeviceListSnapshot snapshot = CreateSnapshot(
            [
                new StoredDeviceConfig { Serial = "A", Name = "Alpha" },
                new StoredDeviceConfig { Serial = "B", Name = "Beta" },
                new StoredDeviceConfig { Serial = "C", Name = "Gamma" }
            ],
            [
                new AdbDevice("A", AdbDeviceStatus.Online),
                new AdbDevice("B", AdbDeviceStatus.Online),
                new AdbDevice("C", AdbDeviceStatus.Online)
            ]);
        var settings = new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] };
        var configuration = new MultipleDeviceConfiguration
        {
            ChangeConfig = new MultipleDeviceChangeConfig { ChangeSimEnabled = false },
            ChangeOptions = new DeviceChangeOptions
            {
                UseDefaultMode = false,
                ChangeAndroidId = true,
                ChangeMacAddress = false
            }
        };
        TestContext context = CreateContext(snapshot, settings, configuration);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice
                {
                    Model = "Profile",
                    Iccid = "8901000000000000000",
                    Imsi = "310260123456789",
                    SimOperatorCountry = "us",
                    SimOperatorNumeric = "310260"
                })));
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int invocationCount = 0;
        context.DeviceChange.ChangeAsync(
                Arg.Any<string>(),
                Arg.Any<DeviceInfoApiDevice>(),
                Arg.Any<bool>(),
                Arg.Any<DeviceChangeOptions>(),
                Arg.Any<IProgress<DeviceChangeStage>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref invocationCount) switch
            {
                1 => StartBatchAction(firstStarted, completion.Task),
                2 => StartBatchAction(secondStarted, completion.Task),
                _ => Task.CompletedTask
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);

        Task batch = viewModel.ChangeSelectedDevicesCommand.ExecuteAsync(null);
        await Task.WhenAll(firstStarted.Task, secondStarted.Task);
        Assert.IsFalse(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        viewModel.ToggleDeviceSelectionCommand.Execute(
            viewModel.Devices.Single(device => device.Serial == "C"));
        Assert.IsTrue(viewModel.Devices.Single(device => device.Serial == "C").IsSelected);

        completion.SetResult();
        await batch;

        await context.ActionConfirmation.Received(1).ConfirmMultipleAsync(
            MultipleDeviceBatchAction.ChangeAndWipe,
            2,
            Arg.Any<CancellationToken>());
        await context.DeviceChange.Received(2).ChangeAsync(
            Arg.Is<string>(serial => serial == "A" || serial == "B"),
            Arg.Any<DeviceInfoApiDevice>(),
            false,
            Arg.Is<DeviceChangeOptions>(options =>
                !options.UseDefaultMode && options.ChangeAndroidId && !options.ChangeMacAddress),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());
        await context.DeviceChange.DidNotReceive().ChangeAsync(
            "C",
            Arg.Any<DeviceInfoApiDevice>(),
            Arg.Any<bool>(),
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RandomChangeAndWipeSelectedDevices_RandomizesThenChangesEachSelectedOnlineDevice()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "Alpha" },
                    new StoredDeviceConfig { Serial = "B", Name = "Beta" }
                ],
                [
                    new AdbDevice("A", AdbDeviceStatus.Online),
                    new AdbDevice("B", AdbDeviceStatus.Online)
                ]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        int profileNumber = 0;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = $"Profile {Interlocked.Increment(ref profileNumber)}" })));

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RandomChangeAndWipeSelectedDevicesCommand.ExecuteAsync(null);

        await context.ActionConfirmation.Received(1).ConfirmMultipleAsync(
            MultipleDeviceBatchAction.ChangeAndWipe,
            2,
            Arg.Any<CancellationToken>());
        await context.RandomDevice.Received(2).CreateRandomProfileAsync(
            Arg.Any<RandomDeviceRequest>(),
            Arg.Any<CancellationToken>());
        await context.DeviceChange.Received(2).ChangeAsync(
            Arg.Is<string>(serial => serial == "A" || serial == "B"),
            Arg.Is<DeviceInfoApiDevice>(profile => profile.Model == "Profile 1" || profile.Model == "Profile 2"),
            Arg.Any<bool>(),
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());
        Assert.IsFalse(viewModel.Devices.Any(device => device.IsActionBusy));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RandomChangeAndWipeSelectedDevices_KeepsGuardAcrossRandomAndChangeStages()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "Running" },
                    new StoredDeviceConfig { Serial = "B", Name = "Ready" }
                ],
                [
                    new AdbDevice("A", AdbDeviceStatus.Online),
                    new AdbDevice("B", AdbDeviceStatus.Online)
                ]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        int randomInvocationCount = 0;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice
                {
                    Model = $"Profile {Interlocked.Increment(ref randomInvocationCount)}"
                })));
        var deviceAChangeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deviceAChangeCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deviceBChangeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.DeviceChange.ChangeAsync(
                Arg.Any<string>(),
                Arg.Any<DeviceInfoApiDevice>(),
                Arg.Any<bool>(),
                Arg.Any<DeviceChangeOptions>(),
                Arg.Any<IProgress<DeviceChangeStage>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<string>() switch
            {
                "A" => StartBatchAction(deviceAChangeStarted, deviceAChangeCompletion.Task),
                "B" => StartBatchAction(deviceBChangeStarted, Task.CompletedTask),
                _ => throw new InvalidOperationException("Unexpected device serial.")
            });
        int deviceAGuardAcquiredCount = 0;
        int deviceAGuardReleasedCount = 0;
        context.DeviceActionGuard.BusyStateChanged += (serial, isBusy) =>
        {
            if (!string.Equals(serial, "A", StringComparison.OrdinalIgnoreCase))
                return;

            if (isBusy)
                Interlocked.Increment(ref deviceAGuardAcquiredCount);
            else
                Interlocked.Increment(ref deviceAGuardReleasedCount);
        };

        await viewModel.InitializeAsync(CancellationToken.None);
        Task runningAction = viewModel.RandomChangeAndWipeSelectedDevicesCommand.ExecuteAsync(null);
        await deviceAChangeStarted.Task;

        DeviceRowViewModel deviceA = viewModel.Devices.Single(device => device.Serial == "A");
        DeviceRowViewModel deviceB = viewModel.Devices.Single(device => device.Serial == "B");
        Assert.AreEqual(1, randomInvocationCount);
        Assert.AreEqual(1, deviceAGuardAcquiredCount);
        Assert.AreEqual(0, deviceAGuardReleasedCount);
        Assert.IsTrue(deviceA.IsActionBusy);

        viewModel.ToggleDeviceSelectionCommand.Execute(deviceB);
        viewModel.SelectedInfoDevice = deviceB;
        Task readyDeviceAction = viewModel.RandomChangeAndWipeSelectedDevicesCommand.ExecuteAsync(null);
        await deviceBChangeStarted.Task;

        Assert.AreEqual(2, randomInvocationCount);
        Assert.AreEqual(1, deviceAGuardAcquiredCount);
        Assert.AreEqual(0, deviceAGuardReleasedCount);
        Assert.IsTrue(deviceA.IsActionBusy);
        await context.DeviceChange.Received(1).ChangeAsync(
            "A",
            Arg.Any<DeviceInfoApiDevice>(),
            Arg.Any<bool>(),
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());

        deviceAChangeCompletion.SetResult();
        await runningAction;
        await readyDeviceAction;

        Assert.AreEqual(1, deviceAGuardReleasedCount);
        Assert.IsFalse(deviceA.IsActionBusy);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RandomChangeAndWipeSelectedDevices_IgnoresProgressLogAfterTargetCompletes()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Alpha" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = "Profile" })));
        IProgress<DeviceChangeStage>? capturedProgress = null;
        context.DeviceChange.ChangeAsync(
                "A",
                Arg.Any<DeviceInfoApiDevice>(),
                Arg.Any<bool>(),
                Arg.Any<DeviceChangeOptions>(),
                Arg.Any<IProgress<DeviceChangeStage>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedProgress = callInfo.ArgAt<IProgress<DeviceChangeStage>>(4);
                return Task.CompletedTask;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RandomChangeAndWipeSelectedDevicesCommand.ExecuteAsync(null);

        DeviceRowViewModel device = viewModel.Devices.Single();
        Assert.AreEqual("Log_ChangeDeviceSuccess", device.Process);
        Assert.IsNotNull(capturedProgress);
        capturedProgress.Report(DeviceChangeStage.Preparing);
        await Task.Delay(200);

        Assert.AreEqual("Log_ChangeDeviceSuccess", device.Process);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RandomChangeAndWipeSelectedDevices_DoesNotReleaseExistingChangeGuard()
    {
        var guard = new ControllableDeviceActionGuard();
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "Running" },
                    new StoredDeviceConfig { Serial = "B", Name = "Ready" }
                ],
                [
                    new AdbDevice("A", AdbDeviceStatus.Online),
                    new AdbDevice("B", AdbDeviceStatus.Online)
                ]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] },
            deviceActionGuard: guard);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = "Random profile" })));
        var deviceAStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deviceACompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deviceBStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int deviceAInvocationCount = 0;
        context.DeviceChange.ChangeAsync(
                Arg.Any<string>(),
                Arg.Any<DeviceInfoApiDevice>(),
                Arg.Any<bool>(),
                Arg.Any<DeviceChangeOptions>(),
                Arg.Any<IProgress<DeviceChangeStage>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<string>() switch
            {
                "A" => StartCountedBatchAction(
                    deviceAStarted,
                    deviceACompletion.Task,
                    () => Interlocked.Increment(ref deviceAInvocationCount)),
                "B" => StartBatchAction(deviceBStarted, Task.CompletedTask),
                _ => throw new InvalidOperationException("Unexpected device serial.")
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        Task runningAction = viewModel.ChangeSelectedDevicesCommand.ExecuteAsync(null);
        await deviceAStarted.Task;

        DeviceRowViewModel deviceA = viewModel.Devices.Single(device => device.Serial == "A");
        DeviceRowViewModel deviceB = viewModel.Devices.Single(device => device.Serial == "B");
        Assert.IsTrue(deviceA.IsActionBusy);
        guard.ForceRelease("A");
        Assert.IsTrue(deviceA.IsActionBusy);
        viewModel.ToggleDeviceSelectionCommand.Execute(deviceB);
        viewModel.SelectedInfoDevice = deviceB;

        Task readyDeviceAction = viewModel.RandomChangeAndWipeSelectedDevicesCommand.ExecuteAsync(null);
        await deviceBStarted.Task;

        Assert.IsTrue(deviceA.IsActionBusy);
        Assert.AreEqual(1, deviceAInvocationCount);
        await context.DeviceChange.Received(1).ChangeAsync(
            "A",
            Arg.Any<DeviceInfoApiDevice>(),
            Arg.Any<bool>(),
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());
        await context.DeviceChange.Received(1).ChangeAsync(
            "B",
            Arg.Any<DeviceInfoApiDevice>(),
            Arg.Any<bool>(),
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());

        deviceACompletion.SetResult();
        await runningAction;
        await readyDeviceAction;
        Assert.IsFalse(deviceA.IsActionBusy);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ChangeSelectedDevices_SelectedRunningDeviceDisablesActionsButReadyDeviceCanRun()
    {
        StoredDeviceConfig[] devices =
        [
            new() { Serial = "A", Name = "Alpha" },
            new() { Serial = "B", Name = "Beta" },
            new() { Serial = "C", Name = "Gamma" }
        ];
        TestContext context = CreateContext(
            CreateSnapshot(
                devices,
                devices.Select(device => new AdbDevice(device.Serial, AdbDeviceStatus.Online)).ToArray()),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B", "C"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = "Random profile" })));

        var started = new Dictionary<string, TaskCompletionSource>
        {
            ["A"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["B"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["C"] = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var completions = new Dictionary<string, TaskCompletionSource>
        {
            ["A"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["B"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["C"] = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        context.DeviceChange.ChangeAsync(
                Arg.Any<string>(),
                Arg.Any<DeviceInfoApiDevice>(),
                Arg.Any<bool>(),
                Arg.Any<DeviceChangeOptions>(),
                Arg.Any<IProgress<DeviceChangeStage>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                string serial = callInfo.Arg<string>();
                return StartBatchAction(started[serial], completions[serial].Task);
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        DeviceRowViewModel deviceA = viewModel.Devices.Single(device => device.Serial == "A");
        DeviceRowViewModel deviceB = viewModel.Devices.Single(device => device.Serial == "B");
        DeviceRowViewModel deviceC = viewModel.Devices.Single(device => device.Serial == "C");
        viewModel.ToggleDeviceSelectionCommand.Execute(deviceC);

        Task firstBatch = viewModel.ChangeSelectedDevicesCommand.ExecuteAsync(null);
        await Task.WhenAll(started["A"].Task, started["B"].Task);
        Assert.IsTrue(deviceA.IsActionBusy);
        Assert.IsTrue(deviceB.IsActionBusy);
        Assert.IsFalse(deviceC.IsSelected);

        viewModel.SelectedInfoDevice = deviceA;
        Assert.IsFalse(viewModel.CanInteractWithSelectedInfoDevice);
        Assert.IsFalse(viewModel.ChangeSelectedDevicesCommand.CanExecute(null));
        viewModel.DeviceInfo.Model = "Blocked edit";
        viewModel.ToggleDeviceSelectionCommand.Execute(deviceC);
        viewModel.SelectedInfoDevice = deviceC;
        Assert.IsTrue(viewModel.CanInteractWithSelectedInfoDevice);
        Assert.IsTrue(viewModel.ChangeSelectedDevicesCommand.CanExecute(null));

        Task secondBatch = viewModel.ChangeSelectedDevicesCommand.ExecuteAsync(null);
        await started["C"].Task;
        await context.DeviceChange.Received(1).ChangeAsync(
            "C",
            Arg.Any<DeviceInfoApiDevice>(),
            Arg.Any<bool>(),
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());
        Assert.IsTrue(deviceC.IsActionBusy);
        Assert.IsTrue(deviceA.IsActionBusy);
        Assert.IsTrue(deviceB.IsActionBusy);

        completions["A"].SetResult();
        completions["B"].SetResult();
        completions["C"].SetResult();
        await firstBatch;
        await secondBatch;
        Assert.IsTrue(viewModel.ChangeSelectedDevicesCommand.CanExecute(null));

        viewModel.SelectedInfoDevice = deviceA;
        Assert.AreEqual("Random profile", viewModel.DeviceInfo.Model);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task BatchActions_SelectedRunningDeviceDisablesActionsAndOfflineReadyDeviceLogs()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "Offline" },
                    new StoredDeviceConfig { Serial = "B", Name = "Online" }
                ],
                [new AdbDevice("B", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = "Profile" })));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.DeviceChange.ChangeAsync(
                "B",
                Arg.Any<DeviceInfoApiDevice>(),
                Arg.Any<bool>(),
                Arg.Any<DeviceChangeOptions>(),
                Arg.Any<IProgress<DeviceChangeStage>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => StartBatchAction(started, completion.Task));

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        Task firstAction = viewModel.ChangeSelectedDevicesCommand.ExecuteAsync(null);
        await started.Task;

        DeviceRowViewModel onlineDevice = viewModel.Devices.Single(device => device.Serial == "B");
        Assert.IsTrue(onlineDevice.IsActionBusy);
        viewModel.SelectedInfoDevice = onlineDevice;
        Assert.IsFalse(viewModel.ChangeSelectedDevicesCommand.CanExecute(null));
        Assert.IsFalse(viewModel.ChangeSelectedDevicesWithoutWipeCommand.CanExecute(null));
        Assert.IsFalse(viewModel.WipeSelectedDevicesWithoutChangeCommand.CanExecute(null));
        Assert.IsFalse(viewModel.ChangeSelectedSimsCommand.CanExecute(null));
        Assert.IsFalse(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Assert.IsFalse(viewModel.RandomSelectedSimsCommand.CanExecute(null));

        DeviceRowViewModel offlineDevice = viewModel.Devices.Single(device => device.Serial == "A");
        viewModel.SelectedInfoDevice = offlineDevice;
        Assert.IsTrue(viewModel.ChangeSelectedDevicesCommand.CanExecute(null));
        await viewModel.ChangeSelectedDevicesWithoutWipeCommand.ExecuteAsync(null);
        await viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await viewModel.RandomSelectedSimsCommand.ExecuteAsync(null);
        await context.DeviceChange.DidNotReceiveWithAnyArgs().ChangeWithoutWipeAsync(
            default!,
            default!,
            default,
            default!,
            default,
            default);
        await context.DeviceChange.Received(1).ChangeAsync(
            "B",
            Arg.Any<DeviceInfoApiDevice>(),
            Arg.Any<bool>(),
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());
        await context.RandomDevice.Received(1).CreateRandomProfileAsync(
            Arg.Any<RandomDeviceRequest>(),
            Arg.Any<CancellationToken>());
        context.SimProfile.DidNotReceive().CreateRandomProfile(
            Arg.Any<CarrierCountryOption>(),
            Arg.Any<CarrierOption>());
        Assert.AreEqual(
            "Log_DeviceMustBeOnline", offlineDevice.Process);

        completion.SetResult();
        await firstAction;
        Assert.IsTrue(viewModel.ChangeSelectedDevicesCommand.CanExecute(null));
        Assert.IsTrue(viewModel.ChangeSelectedDevicesWithoutWipeCommand.CanExecute(null));
        Assert.IsTrue(viewModel.WipeSelectedDevicesWithoutChangeCommand.CanExecute(null));
        Assert.IsTrue(viewModel.ChangeSelectedSimsCommand.CanExecute(null));
        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Assert.IsTrue(viewModel.RandomSelectedSimsCommand.CanExecute(null));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ChangeSelectedDevices_FailedTargetCanRetryWhileAnotherTargetRuns()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "A", Name = "Fails once" },
            new() { Serial = "B", Name = "Keeps running" }
        ];
        TestContext context = CreateContext(
            CreateSnapshot(
                storedDevices,
                storedDevices
                    .Select(device => new AdbDevice(device.Serial, AdbDeviceStatus.Online))
                    .ToArray()),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = "Profile" })));
        var deviceBStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deviceBCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int deviceAInvocationCount = 0;
        context.DeviceChange.ChangeAsync(
                Arg.Any<string>(),
                Arg.Any<DeviceInfoApiDevice>(),
                Arg.Any<bool>(),
                Arg.Any<DeviceChangeOptions>(),
                Arg.Any<IProgress<DeviceChangeStage>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<string>() switch
            {
                "A" when Interlocked.Increment(ref deviceAInvocationCount) == 1 =>
                    Task.FromException(new InvalidOperationException("First A attempt failed.")),
                "A" => Task.CompletedTask,
                "B" => StartBatchAction(deviceBStarted, deviceBCompletion.Task),
                _ => throw new InvalidOperationException("Unexpected device serial.")
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        var deviceAReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.DeviceActionGuard.BusyStateChanged += (serial, isBusy) =>
        {
            if (serial == "A" && !isBusy)
                deviceAReleased.TrySetResult();
        };

        Task firstBatch = viewModel.ChangeSelectedDevicesCommand.ExecuteAsync(null);
        await Task.WhenAll(deviceBStarted.Task, deviceAReleased.Task);

        DeviceRowViewModel deviceA = viewModel.Devices.Single(device => device.Serial == "A");
        DeviceRowViewModel deviceB = viewModel.Devices.Single(device => device.Serial == "B");
        Assert.IsFalse(deviceA.IsActionBusy);
        Assert.IsTrue(deviceB.IsActionBusy);
        Assert.IsTrue(viewModel.ChangeSelectedDevicesCommand.CanExecute(null));

        await viewModel.ChangeSelectedDevicesCommand.ExecuteAsync(null);

        await context.DeviceChange.Received(2).ChangeAsync(
            "A",
            Arg.Any<DeviceInfoApiDevice>(),
            Arg.Any<bool>(),
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());
        await context.DeviceChange.Received(1).ChangeAsync(
            "B",
            Arg.Any<DeviceInfoApiDevice>(),
            Arg.Any<bool>(),
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());
        Assert.IsFalse(deviceA.IsActionBusy);
        Assert.IsTrue(deviceB.IsActionBusy);

        deviceBCompletion.SetResult();
        await firstBatch;
        Assert.IsTrue(viewModel.ChangeSelectedDevicesCommand.CanExecute(null));
        viewModel.ToggleDeviceSelectionCommand.Execute(deviceB);
        Assert.IsFalse(deviceB.IsSelected);

        await viewModel.ChangeSelectedDevicesCommand.ExecuteAsync(null);

        await context.DeviceChange.Received(3).ChangeAsync(
            "A",
            Arg.Any<DeviceInfoApiDevice>(),
            Arg.Any<bool>(),
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());
        await context.DeviceChange.Received(1).ChangeAsync(
            "B",
            Arg.Any<DeviceInfoApiDevice>(),
            Arg.Any<bool>(),
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());
        Assert.IsFalse(deviceA.IsActionBusy);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RandomSelectedDevices_FailedTargetCanRetryWhileAnotherTargetRuns()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "A", Name = "Fails once" },
            new() { Serial = "B", Name = "Keeps running" }
        ];
        TestContext context = CreateContext(
            CreateSnapshot(
                storedDevices,
                storedDevices
                    .Select(device => new AdbDevice(device.Serial, AdbDeviceStatus.Online))
                    .ToArray()),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var deviceBStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deviceBCompletion = new TaskCompletionSource<RandomDeviceResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int invocationCount = 0;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref invocationCount) switch
            {
                1 => Task.FromResult(new RandomDeviceResult(RandomDeviceStatus.Failed, null)),
                2 => StartRandom(deviceBStarted, deviceBCompletion.Task),
                3 => Task.FromResult(new RandomDeviceResult(
                    RandomDeviceStatus.Created,
                    new DeviceInfoApiDevice { Model = "Retried profile" })),
                _ => throw new InvalidOperationException("Unexpected random-device invocation.")
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        var deviceAReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.DeviceActionGuard.BusyStateChanged += (serial, isBusy) =>
        {
            if (serial == "A" && !isBusy)
                deviceAReleased.TrySetResult();
        };

        Task firstBatch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await Task.WhenAll(deviceBStarted.Task, deviceAReleased.Task);

        DeviceRowViewModel deviceA = viewModel.Devices.Single(device => device.Serial == "A");
        DeviceRowViewModel deviceB = viewModel.Devices.Single(device => device.Serial == "B");
        Assert.IsFalse(deviceA.IsActionBusy);
        Assert.IsTrue(deviceB.IsActionBusy);
        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));

        await viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);

        Assert.AreEqual(3, invocationCount);
        Assert.IsFalse(deviceA.IsActionBusy);
        Assert.IsTrue(deviceB.IsActionBusy);

        deviceBCompletion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile B" }));
        await firstBatch;
        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Assert.AreEqual(3, invocationCount);
        Assert.IsFalse(deviceA.IsActionBusy);
        viewModel.SelectedInfoDevice = deviceA;
        Assert.AreEqual("Retried profile", viewModel.DeviceInfo.Model);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RandomSelectedDevices_StartingSecondBatch_DoesNotCancelFirstBatch()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "A", Name = "Phone A" },
            new() { Serial = "B", Name = "Phone B" }
        ];
        TestContext context = CreateContext(
            CreateSnapshot(
                storedDevices,
                [
                    new AdbDevice("A", AdbDeviceStatus.Online),
                    new AdbDevice("B", AdbDeviceStatus.Online)
                ]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        var deviceAStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deviceBStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deviceACompletion = new TaskCompletionSource<RandomDeviceResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var deviceBCompletion = new TaskCompletionSource<RandomDeviceResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken deviceAToken = default;
        CancellationToken deviceBToken = default;
        int invocationCount = 0;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                CancellationToken token = callInfo.ArgAt<CancellationToken>(1);
                int invocation = Interlocked.Increment(ref invocationCount);
                if (invocation == 1)
                {
                    deviceAToken = token;
                    deviceAStarted.TrySetResult();
                    return deviceACompletion.Task;
                }

                if (invocation == 2)
                {
                    deviceBToken = token;
                    deviceBStarted.TrySetResult();
                    return deviceBCompletion.Task;
                }

                throw new InvalidOperationException("Unexpected random-device invocation.");
            });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        DeviceRowViewModel deviceA = viewModel.Devices.Single(device => device.Serial == "A");
        DeviceRowViewModel deviceB = viewModel.Devices.Single(device => device.Serial == "B");
        Task firstBatch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await deviceAStarted.Task;

        viewModel.ToggleDeviceSelectionCommand.Execute(deviceB);
        viewModel.SelectedInfoDevice = deviceB;
        Task secondBatch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await deviceBStarted.Task;

        Assert.IsFalse(deviceAToken.IsCancellationRequested);
        Assert.IsFalse(deviceBToken.IsCancellationRequested);
        Assert.IsFalse(firstBatch.IsCompleted);
        Assert.IsTrue(deviceA.IsActionBusy);
        Assert.IsTrue(deviceB.IsActionBusy);

        deviceACompletion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile A" }));
        await firstBatch;
        Assert.IsFalse(deviceA.IsActionBusy);
        Assert.IsTrue(deviceB.IsActionBusy);

        deviceBCompletion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile B" }));
        await secondBatch;
        Assert.IsFalse(deviceB.IsActionBusy);

        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RandomBatchActions_OfflineSelectionRemainsEnabledAndLogsOnlineRequirement()
    {
        StoredDeviceConfig storedDevice = new() { Serial = "A", Name = "Phone" };
        TestContext context = CreateContext(
            CreateSnapshot([storedDevice], []),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = "Profile" })));

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Assert.IsTrue(viewModel.RandomSelectedSimsCommand.CanExecute(null));
        await viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await viewModel.RandomSelectedSimsCommand.ExecuteAsync(null);
        await context.RandomDevice.DidNotReceiveWithAnyArgs()
            .CreateRandomProfileAsync(default!, default);
        context.SimProfile.DidNotReceive().CreateRandomProfile(
            Arg.Any<CarrierCountryOption>(),
            Arg.Any<CarrierOption>());
        Assert.AreEqual("Log_DeviceMustBeOnline", viewModel.Devices[0].Process);
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("A"));

        viewModel.ApplyDeviceListSnapshot(CreateSnapshot(
            [storedDevice],
            [new AdbDevice("A", AdbDeviceStatus.Online)]));

        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Assert.IsTrue(viewModel.RandomSelectedSimsCommand.CanExecute(null));
        await viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await viewModel.RandomSelectedSimsCommand.ExecuteAsync(null);
        await context.RandomDevice.Received(1).CreateRandomProfileAsync(
            Arg.Any<RandomDeviceRequest>(),
            Arg.Any<CancellationToken>());
        context.SimProfile.Received(1).CreateRandomProfile(
            Arg.Any<CarrierCountryOption>(),
            Arg.Any<CarrierOption>());

        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task AddNewDevices_DuringRandomBatch_AllowsReadyNewDeviceToRun()
    {
        DeviceListSnapshot initial = CreateSnapshot(
            [
                new StoredDeviceConfig { Serial = "A", Name = "Alpha" },
                new StoredDeviceConfig { Serial = "B", Name = "Beta" }
            ],
            [
                new AdbDevice("A", AdbDeviceStatus.Online),
                new AdbDevice("B", AdbDeviceStatus.Online)
            ]);
        DeviceListSnapshot added = CreateSnapshot(
            [
                new StoredDeviceConfig { Serial = "A", Name = "Alpha" },
                new StoredDeviceConfig { Serial = "B", Name = "Beta" },
                new StoredDeviceConfig { Serial = "C", Name = "Gamma" }
            ],
            [
                new AdbDevice("A", AdbDeviceStatus.Online),
                new AdbDevice("B", AdbDeviceStatus.Online),
                new AdbDevice("C", AdbDeviceStatus.Online)
            ]);
        TestContext context = CreateContext(
            initial,
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        context.DeviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(initial, initial, added);
        context.AddDialog.ShowAddDevicesAsync(Arg.Any<CancellationToken>())
            .Returns([new StoredDeviceConfig { Serial = "C", Name = "Gamma" }]);
        context.DeviceList.AddSelectedDevicesAsync(
                Arg.Any<IEnumerable<StoredDeviceConfig>>(),
                Arg.Any<CancellationToken>())
            .Returns(added);

        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCompletion = new TaskCompletionSource<RandomDeviceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompletion = new TaskCompletionSource<RandomDeviceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdCompletion = new TaskCompletionSource<RandomDeviceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int invocationCount = 0;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref invocationCount) switch
            {
                1 => StartRandom(firstStarted, firstCompletion.Task),
                2 => StartRandom(secondStarted, secondCompletion.Task),
                3 => StartRandom(thirdStarted, thirdCompletion.Task),
                _ => throw new InvalidOperationException("Unexpected random-device invocation.")
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        DeviceRowViewModel deviceA = viewModel.Devices.Single(device => device.Serial == "A");
        DeviceRowViewModel deviceB = viewModel.Devices.Single(device => device.Serial == "B");
        Task initialBatch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await Task.WhenAll(firstStarted.Task, secondStarted.Task);

        Assert.IsTrue(viewModel.AddNewDevicesCommand.CanExecute(null));
        await viewModel.AddNewDevicesCommand.ExecuteAsync(null);
        DeviceRowViewModel deviceC = viewModel.Devices.Single(device => device.Serial == "C");
        Assert.AreSame(deviceA, viewModel.Devices.Single(device => device.Serial == "A"));
        Assert.AreSame(deviceB, viewModel.Devices.Single(device => device.Serial == "B"));
        Assert.IsTrue(deviceA.IsActionBusy);
        Assert.IsTrue(deviceB.IsActionBusy);
        Assert.IsTrue(deviceC.CanEdit);

        viewModel.ToggleDeviceSelectionCommand.Execute(deviceC);
        viewModel.SelectedInfoDevice = deviceC;
        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Task newDeviceBatch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await thirdStarted.Task;
        Assert.IsTrue(deviceC.IsActionBusy);
        Assert.IsTrue(deviceA.IsActionBusy);
        Assert.IsTrue(deviceB.IsActionBusy);

        thirdCompletion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile C" }));
        firstCompletion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile A" }));
        secondCompletion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile B" }));
        await initialBatch;
        await newDeviceBatch;
        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Assert.IsFalse(deviceC.IsActionBusy);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RandomSelectedDevices_RemovedAndReaddedTargetDoesNotReceiveStaleProfile()
    {
        DeviceListSnapshot initial = CreateSnapshot(
            [new StoredDeviceConfig { Serial = "A", Name = "Before" }],
            [new AdbDevice("A", AdbDeviceStatus.Online)]);
        TestContext context = CreateContext(
            initial,
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<RandomDeviceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => StartRandom(started, completion.Task));

        await viewModel.InitializeAsync(CancellationToken.None);
        DeviceRowViewModel original = viewModel.Devices.Single();
        Task batch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await started.Task;

        viewModel.ApplyDeviceListSnapshot(CreateSnapshot([], []));
        viewModel.ApplyDeviceListSnapshot(CreateSnapshot(
            [new StoredDeviceConfig { Serial = "A", Name = "Replacement" }],
            [new AdbDevice("A", AdbDeviceStatus.Online)]));
        DeviceRowViewModel replacement = viewModel.Devices.Single();
        Assert.AreNotSame(original, replacement);
        Assert.IsTrue(replacement.IsActionBusy);

        completion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Stale profile" }));
        await batch;

        Assert.IsFalse(replacement.IsActionBusy);
        viewModel.SelectedInfoDevice = replacement;
        Assert.AreEqual(string.Empty, viewModel.DeviceInfo.Model);
        Assert.IsFalse(viewModel.ViewRandomDeviceInfoCommand.CanExecute(null));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RandomSelectedDevices_QueuesReadyTargetWhileOtherTargetsAreBusy()
    {
        StoredDeviceConfig[] initialDevices = Enumerable.Range(1, 4)
            .Select(index => new StoredDeviceConfig { Serial = $"D{index}", Name = $"Device {index}" })
            .ToArray();
        TestContext context = CreateContext(
            CreateSnapshot(
                initialDevices,
                initialDevices
                    .Select(device => new AdbDevice(device.Serial, AdbDeviceStatus.Online))
                    .ToArray()),
            new AppSettings { SelectedMultipleDeviceSerials = initialDevices.Select(device => device.Serial).ToList() });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        TaskCompletionSource[] started = Enumerable.Range(0, 5)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        TaskCompletionSource<RandomDeviceResult>[] completions = Enumerable.Range(0, 5)
            .Select(_ => new TaskCompletionSource<RandomDeviceResult>(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        int invocationCount = 0;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                int index = Interlocked.Increment(ref invocationCount) - 1;
                return StartRandom(started[index], completions[index].Task);
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task firstBatch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await Task.WhenAll(started.Take(4).Select(source => source.Task));

        viewModel.ApplyDeviceListSnapshot(CreateSnapshot(
            [.. initialDevices, new StoredDeviceConfig { Serial = "D5", Name = "Device 5" }],
            [
                .. initialDevices.Select(device => new AdbDevice(device.Serial, AdbDeviceStatus.Online)),
                new AdbDevice("D5", AdbDeviceStatus.Online)
            ]));
        viewModel.ToggleDeviceSelectionCommand.Execute(
            viewModel.Devices.Single(device => device.Serial == "D5"));
        viewModel.SelectedInfoDevice = viewModel.Devices.Single(device => device.Serial == "D5");
        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Task secondBatch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        Assert.IsFalse(started[4].Task.IsCompleted);
        Assert.AreEqual(4, invocationCount);

        completions[0].SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile" }));
        await started[4].Task;
        Assert.AreEqual(5, invocationCount);

        foreach (TaskCompletionSource<RandomDeviceResult> completion in completions.Skip(1))
        {
            completion.TrySetResult(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = "Profile" }));
        }

        await firstBatch;
        await secondBatch;
        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Assert.AreEqual(5, invocationCount);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RandomSelectedDevices_QueuedTargetGoingOfflineLeavesGuardWithoutStarting()
    {
        StoredDeviceConfig[] storedDevices = Enumerable.Range(1, 5)
            .Select(index => new StoredDeviceConfig { Serial = $"D{index}", Name = $"Device {index}" })
            .ToArray();
        AdbDevice[] allOnline = storedDevices
            .Select(device => new AdbDevice(device.Serial, AdbDeviceStatus.Online))
            .ToArray();
        TestContext context = CreateContext(
            CreateSnapshot(storedDevices, allOnline),
            new AppSettings { SelectedMultipleDeviceSerials = storedDevices.Select(device => device.Serial).ToList() });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        TaskCompletionSource[] started = Enumerable.Range(0, 4)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        TaskCompletionSource<RandomDeviceResult>[] completions = Enumerable.Range(0, 4)
            .Select(_ => new TaskCompletionSource<RandomDeviceResult>(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        int invocationCount = 0;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                int index = Interlocked.Increment(ref invocationCount) - 1;
                return StartRandom(started[index], completions[index].Task);
            });
        var queuedTargetReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.DeviceActionGuard.BusyStateChanged += (serial, isBusy) =>
        {
            if (serial == "D5" && !isBusy)
                queuedTargetReleased.TrySetResult();
        };

        await viewModel.InitializeAsync(CancellationToken.None);
        Task batch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await Task.WhenAll(started.Select(source => source.Task));
        Assert.IsTrue(context.DeviceActionGuard.IsBusy("D5"));

        viewModel.ApplyDeviceListSnapshot(CreateSnapshot(
            storedDevices,
            allOnline.Where(device => device.Serial != "D5").ToArray()));
        await queuedTargetReleased.Task;

        DeviceRowViewModel queuedTarget = viewModel.Devices.Single(device => device.Serial == "D5");
        Assert.AreEqual(4, invocationCount);
        Assert.IsFalse(queuedTarget.IsActionBusy);
        Assert.AreEqual("Log_DeviceMustBeOnline", queuedTarget.Process);

        completions[0].SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile D1" }));

        foreach (TaskCompletionSource<RandomDeviceResult> completion in completions.Skip(1))
        {
            completion.SetResult(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = "Profile" }));
        }

        await batch;
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ChangeSelectedDevices_RunsAtMostFourDevicesConcurrently()
    {
        StoredDeviceConfig[] devices = Enumerable.Range(1, 5)
            .Select(index => new StoredDeviceConfig { Serial = $"D{index}", Name = $"Device {index}" })
            .ToArray();
        AdbDevice[] connected = devices
            .Select(device => new AdbDevice(device.Serial, AdbDeviceStatus.Online))
            .ToArray();
        TestContext context = CreateContext(
            CreateSnapshot(devices, connected),
            new AppSettings { SelectedMultipleDeviceSerials = devices.Select(device => device.Serial).ToList() });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = "Profile" })));
        TaskCompletionSource[] started = Enumerable.Range(0, 5)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        TaskCompletionSource[] completions = Enumerable.Range(0, 5)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        int invocationCount = 0;
        context.DeviceChange.ChangeAsync(
                Arg.Any<string>(),
                Arg.Any<DeviceInfoApiDevice>(),
                Arg.Any<bool>(),
                Arg.Any<DeviceChangeOptions>(),
                Arg.Any<IProgress<DeviceChangeStage>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                int index = Interlocked.Increment(ref invocationCount) - 1;
                return StartBatchAction(started[index], completions[index].Task);
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);

        Task batch = viewModel.ChangeSelectedDevicesCommand.ExecuteAsync(null);
        await Task.WhenAll(started.Take(4).Select(source => source.Task));
        Assert.IsFalse(started[4].Task.IsCompleted);
        completions[0].SetResult();
        await started[4].Task;
        foreach (TaskCompletionSource completion in completions.Skip(1))
            completion.TrySetResult();
        await batch;

        Assert.AreEqual(5, invocationCount);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RandomAndChangeSelectedSims_SkipsOfflineDeviceAndKeepsPerDeviceProfile()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "Alpha" },
                    new StoredDeviceConfig { Serial = "B", Name = "Beta" }
                ],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RandomSelectedSimsCommand.ExecuteAsync(null);
        await viewModel.ChangeSelectedSimsCommand.ExecuteAsync(null);

        await context.ActionConfirmation.Received(1).ConfirmMultipleAsync(
            MultipleDeviceBatchAction.ChangeSim,
            1,
            Arg.Any<CancellationToken>());
        await context.DeviceChange.Received(1).ChangeSimAsync(
            "A",
            Arg.Is<SimProfile>(profile => profile.Iccid == "8901000000000000000"),
            Arg.Any<CancellationToken>());
        Assert.AreEqual("Log_DeviceMustBeOnline", viewModel.Devices.Single(device => device.Serial == "B").Process);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RandomSelectedDevices_StoresProfilesAndKeepsRunningSnapshotStable()
    {
        DeviceListSnapshot snapshot = CreateSnapshot(
            [
                new StoredDeviceConfig { Serial = "A", Name = "Alpha" },
                new StoredDeviceConfig { Serial = "B", Name = "Beta" },
                new StoredDeviceConfig { Serial = "C", Name = "Gamma" }
            ],
            [
                new AdbDevice("A", AdbDeviceStatus.Online),
                new AdbDevice("B", AdbDeviceStatus.Online),
                new AdbDevice("C", AdbDeviceStatus.Online)
            ]);
        var settings = new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] };
        TestContext context = CreateContext(snapshot, settings);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCompletion = new TaskCompletionSource<RandomDeviceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompletion = new TaskCompletionSource<RandomDeviceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int invocationCount = 0;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                return Interlocked.Increment(ref invocationCount) switch
                {
                    1 => StartRandom(firstStarted, firstCompletion.Task),
                    2 => StartRandom(secondStarted, secondCompletion.Task),
                    _ => Task.FromResult(new RandomDeviceResult(
                        RandomDeviceStatus.Created,
                        new DeviceInfoApiDevice { Model = "Unexpected" }))
                };
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        DeviceRowViewModel deviceA = viewModel.Devices.Single(device => device.Serial == "A");
        DeviceRowViewModel deviceB = viewModel.Devices.Single(device => device.Serial == "B");
        DeviceRowViewModel deviceC = viewModel.Devices.Single(device => device.Serial == "C");

        Task batch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await Task.WhenAll(firstStarted.Task, secondStarted.Task);

        viewModel.ToggleDeviceSelectionCommand.Execute(deviceA);
        viewModel.ToggleDeviceSelectionCommand.Execute(deviceC);
        Assert.IsTrue(deviceA.IsSelected);
        Assert.IsTrue(deviceB.IsSelected);
        Assert.IsTrue(deviceC.IsSelected);
        Assert.IsTrue(deviceA.IsActionBusy);
        Assert.IsTrue(deviceB.IsActionBusy);

        firstCompletion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile A", Serial = "A" }));
        secondCompletion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile B", Serial = "B" }));
        await batch;

        Assert.IsFalse(deviceA.IsActionBusy);
        Assert.IsFalse(deviceB.IsActionBusy);
        viewModel.SelectedInfoDevice = deviceA;
        string firstProfileModel = viewModel.DeviceInfo.Model;
        viewModel.SelectedInfoDevice = deviceB;
        string secondProfileModel = viewModel.DeviceInfo.Model;
        CollectionAssert.AreEquivalent(
            new[] { "Profile A", "Profile B" },
            new[] { firstProfileModel, secondProfileModel });
        viewModel.SelectedInfoDevice = deviceA;
        Assert.IsTrue(viewModel.ViewRandomDeviceInfoCommand.CanExecute(null));
        Assert.AreEqual(2, invocationCount);

        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RandomSelectedDevices_LeavesProfileEmptyAndReportsIndependentFailures()
    {
        DeviceListSnapshot snapshot = CreateSnapshot(
            [
                new StoredDeviceConfig { Serial = "A", Name = "Alpha" },
                new StoredDeviceConfig { Serial = "B", Name = "Beta" },
                new StoredDeviceConfig { Serial = "C", Name = "Gamma" }
            ],
            [
                new AdbDevice("A", AdbDeviceStatus.Online),
                new AdbDevice("B", AdbDeviceStatus.Online),
                new AdbDevice("C", AdbDeviceStatus.Online)
            ]);
        var settings = new AppSettings { SelectedMultipleDeviceSerials = ["A", "B", "C"] };
        TestContext context = CreateContext(snapshot, settings);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        int invocationCount = 0;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref invocationCount) switch
            {
                1 => Task.FromResult(new RandomDeviceResult(
                    RandomDeviceStatus.Created,
                    new DeviceInfoApiDevice { Model = "Profile A", Serial = "A" })),
                2 => Task.FromResult(new RandomDeviceResult(RandomDeviceStatus.Failed, null)),
                _ => Task.FromResult(new RandomDeviceResult(RandomDeviceStatus.LoginRequired, null))
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        DeviceRowViewModel deviceA = viewModel.Devices.Single(device => device.Serial == "A");
        DeviceRowViewModel deviceB = viewModel.Devices.Single(device => device.Serial == "B");
        DeviceRowViewModel deviceC = viewModel.Devices.Single(device => device.Serial == "C");

        Assert.IsFalse(viewModel.ViewRandomDeviceInfoCommand.CanExecute(null));
        await viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);

        Assert.AreEqual(3, invocationCount);
        Assert.IsFalse(deviceA.IsActionBusy);
        Assert.IsFalse(deviceB.IsActionBusy);
        Assert.IsFalse(deviceC.IsActionBusy);
        string[] processStates = [deviceA.Process, deviceB.Process, deviceC.Process];
        Assert.IsTrue(processStates.Contains("Log_RandomDeviceFailed"));
        Assert.IsTrue(processStates.Contains("Log_RandomDeviceLoginRequired"));
        int profileCount = 0;
        DeviceRowViewModel? profileDevice = null;
        foreach (DeviceRowViewModel device in viewModel.SelectedDevices)
        {
            viewModel.SelectedInfoDevice = device;
            if (!string.IsNullOrWhiteSpace(viewModel.DeviceInfo.Model))
            {
                profileCount++;
                profileDevice = device;
            }
        }
        Assert.AreEqual(1, profileCount);
        viewModel.SelectedInfoDevice = profileDevice;
        Assert.IsTrue(viewModel.ViewRandomDeviceInfoCommand.CanExecute(null));

        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ViewRandomDeviceInfo_UsesTheSelectedProfileAndReflectsDialogEdits()
    {
        StoredDeviceConfig stored = new() { Serial = "A", Name = "Alpha" };
        TestContext context = CreateContext(
            CreateSnapshot([stored], [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var dialogModels = new List<string?>();
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = "Original", Serial = "A" })));
        context.RandomInfoDialog.ShowRandomDeviceInfoAsync(
                Arg.Any<DeviceInfoApiDevice>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                DeviceInfoApiDevice profile = callInfo.Arg<DeviceInfoApiDevice>();
                dialogModels.Add(profile.Model);
                profile.Model = "Edited in dialog";
                return Task.FromResult(true);
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        Assert.AreEqual("Original", viewModel.DeviceInfo.Model);

        await viewModel.ViewRandomDeviceInfoCommand.ExecuteAsync(null);

        Assert.AreEqual("Edited in dialog", viewModel.DeviceInfo.Model);

        viewModel.DeviceInfo.Model = "Edited inline";
        await viewModel.ViewRandomDeviceInfoCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(
            new[] { "Original", "Edited inline" },
            dialogModels);

        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RandomSelectedDevices_SkipsDeviceBusyInAnotherWorkflow()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "Alpha" },
                    new StoredDeviceConfig { Serial = "B", Name = "Beta" }
                ],
                [
                    new AdbDevice("A", AdbDeviceStatus.Online),
                    new AdbDevice("B", AdbDeviceStatus.Online)
                ]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = "Profile B", Serial = "B" })));

        await viewModel.InitializeAsync(CancellationToken.None);
        using IDisposable busyLease = context.DeviceActionGuard.TryAcquire("A")!;
        await viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);

        viewModel.ApplyDeviceListSnapshot(CreateSnapshot(
            [
                new StoredDeviceConfig { Serial = "A", Name = "Alpha" },
                new StoredDeviceConfig { Serial = "B", Name = "Beta" }
            ],
            [
                new AdbDevice("A", AdbDeviceStatus.Online),
                new AdbDevice("B", AdbDeviceStatus.Online)
            ]));

        Assert.AreEqual("Log_Ready", viewModel.Devices.Single(device => device.Serial == "A").Process);
        Assert.AreEqual("Log_RandomDeviceSuccess", viewModel.Devices.Single(device => device.Serial == "B").Process);
        Assert.AreEqual(
            DeviceProcessState.Ready,
            viewModel.Devices.Single(device => device.Serial == "A").ProcessState);
        Assert.AreEqual(
            DeviceProcessState.Succeeded,
            viewModel.Devices.Single(device => device.Serial == "B").ProcessState);
        Assert.IsTrue(context.DeviceActionGuard.IsBusy("A"));

        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task InitializeAndRefresh_PreservesValidSelectionsAndRemovesDeletedSerials()
    {
        DeviceListSnapshot initial = CreateSnapshot(
            [
                new StoredDeviceConfig { Serial = "A", Name = "Alpha", Type = "sargo" },
                new StoredDeviceConfig { Serial = "B", Name = "Beta", Type = "starlte" },
                new StoredDeviceConfig { Serial = "C", Name = "Gamma", Type = "tissot" }
            ],
            [
                new AdbDevice("A", AdbDeviceStatus.Online),
                new AdbDevice("B", AdbDeviceStatus.Offline)
            ]);
        var settings = new AppSettings
        {
            SelectedMultipleDeviceSerials = ["B", "MISSING"]
        };
        TestContext context = CreateContext(initial, settings);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.HasCount(3, viewModel.Devices);
        Assert.IsTrue(viewModel.Devices.Single(device => device.Serial == "B").IsSelected);
        CollectionAssert.AreEqual(new[] { "B" }, settings.SelectedMultipleDeviceSerials);

        DeviceRowViewModel deviceA =
            viewModel.Devices.Single(device => device.Serial == "A");
        viewModel.ToggleDeviceSelectionCommand.Execute(deviceA);
        Assert.IsTrue(deviceA.IsSelected);
        Assert.IsNull(viewModel.AllDevicesSelectionState);
        viewModel.SelectedDeviceFilter = "Online";
        Assert.HasCount(1, viewModel.Devices);
        Assert.AreEqual("A", viewModel.Devices[0].Serial);
        viewModel.ToggleSelectAllDevicesCommand.Execute(null);
        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, settings.SelectedMultipleDeviceSerials);
        Assert.IsTrue(viewModel.AllDevicesSelectionState);

        viewModel.ApplyDeviceListSnapshot(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "Alpha", Type = "sargo" },
                    new StoredDeviceConfig { Serial = "C", Name = "Gamma", Type = "tissot" }
                ],
                [new AdbDevice("A", AdbDeviceStatus.Online)]));
        CollectionAssert.AreEqual(new[] { "A", "C" }, settings.SelectedMultipleDeviceSerials);

        await viewModel.DeactivateAsync();
        await context.SettingsService.Received().SaveAsync(settings, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Initialize_LoadedPresetDoesNotQueueDefaultOverwrite()
    {
        var configuration = new MultipleDeviceConfiguration
        {
            ChangeConfig = new MultipleDeviceChangeConfig
            {
                Brand = "Samsung",
                AndroidVersion = "Android 15",
                Model = "SM-S918B",
                CountryIso = "vn",
                CountryName = "Vietnam",
                Carrier = "Viettel",
                CarrierMcc = "452",
                CarrierMnc = "04"
            },
            ChangeOptions = new DeviceChangeOptions { UseDefaultMode = false }
        };
        TestContext context = CreateContext(
            CreateSnapshot([], []),
            configuration: configuration);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;

        await viewModel.InitializeAsync(CancellationToken.None);
        await Task.Delay(450);
        await viewModel.DeactivateAsync();

        Assert.AreEqual("Samsung", viewModel.SelectedBrand);
        Assert.AreEqual("Android 15", viewModel.SelectedAndroidVersion);
        Assert.AreEqual("SM-S918B", viewModel.SelectedModel);
        Assert.AreEqual("vn", viewModel.SelectedCountry?.CountryIso);
        await context.MultipleConfig.DidNotReceiveWithAnyArgs()
            .SaveAsync(default!, default);
    }

    [TestMethod]
    public async Task PollRefresh_RemovesDeletedSelectedSerialFromSettings()
    {
        DeviceListSnapshot initial = CreateSnapshot(
            [
                new StoredDeviceConfig { Serial = "A" },
                new StoredDeviceConfig { Serial = "B" }
            ],
            [new AdbDevice("A", AdbDeviceStatus.Online)]);
        DeviceListSnapshot updated = CreateSnapshot(
            [new StoredDeviceConfig { Serial = "A" }],
            [new AdbDevice("A", AdbDeviceStatus.Online)]);
        var settings = new AppSettings { SelectedMultipleDeviceSerials = ["B"] };
        TestContext context = CreateContext(initial, settings);
        context.DeviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(initial, initial, updated);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        await context.Polling.TickAsync();

        Assert.HasCount(1, viewModel.Devices);
        Assert.AreEqual("A", viewModel.Devices[0].Serial);
        Assert.IsEmpty(settings.SelectedMultipleDeviceSerials);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task PollRefresh_OnlineStatusChangeKeepsRandomCommandsEnabled()
    {
        StoredDeviceConfig storedDevice = new() { Serial = "A", Name = "Phone" };
        DeviceListSnapshot offline = CreateSnapshot([storedDevice], []);
        DeviceListSnapshot online = CreateSnapshot(
            [storedDevice],
            [new AdbDevice("A", AdbDeviceStatus.Online)]);
        TestContext context = CreateContext(
            offline,
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        context.DeviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(offline, offline, online);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);
        int randomDeviceCanExecuteChanged = 0;
        int randomSimCanExecuteChanged = 0;
        viewModel.RandomSelectedDevicesCommand.CanExecuteChanged +=
            (_, _) => randomDeviceCanExecuteChanged++;
        viewModel.RandomSelectedSimsCommand.CanExecuteChanged +=
            (_, _) => randomSimCanExecuteChanged++;

        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Assert.IsTrue(viewModel.RandomSelectedSimsCommand.CanExecute(null));

        await context.Polling.TickAsync();

        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Assert.IsTrue(viewModel.RandomSelectedSimsCommand.CanExecute(null));
        Assert.IsGreaterThan(0, randomDeviceCanExecuteChanged);
        Assert.IsGreaterThan(0, randomSimCanExecuteChanged);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task AddNewDevices_UsesExistingDialogAndDeviceListWorkflow()
    {
        DeviceListSnapshot initial = CreateSnapshot([], []);
        var addedDevice = new StoredDeviceConfig
        {
            Serial = "NEW",
            Name = "New phone",
            Type = "sargo"
        };
        DeviceListSnapshot added = CreateSnapshot(
            [addedDevice],
            [new AdbDevice("NEW", AdbDeviceStatus.Online)]);
        TestContext context = CreateContext(initial);
        context.AddDialog.ShowAddDevicesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { addedDevice });
        context.DeviceList.AddSelectedDevicesAsync(
                Arg.Any<IEnumerable<StoredDeviceConfig>>(),
                Arg.Any<CancellationToken>())
            .Returns(added);
        context.DeviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(initial, initial, added);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.AddNewDevicesCommand.ExecuteAsync(null);

        Assert.HasCount(1, viewModel.Devices);
        Assert.AreEqual("NEW", viewModel.Devices[0].Serial);
        await context.DeviceList.Received(1).AddSelectedDevicesAsync(
            Arg.Is<IEnumerable<StoredDeviceConfig>>(devices =>
                devices.Single().Serial == "NEW"),
            Arg.Any<CancellationToken>());
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task DeviceRowEdits_SaveNameAndTypeWithoutBatchCarrierProfile()
    {
        StoredDeviceConfig stored = new()
        {
            Serial = "A",
            Name = "Before",
            Type = "sargo"
        };
        TestContext context = CreateContext(CreateSnapshot([stored], []));
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.Devices[0].Name = "After";
        viewModel.Devices[0].Type = "starlte";
        await viewModel.DeactivateAsync();

        await context.DeviceConfig.Received().SaveDeviceRowAsync(
            Arg.Any<IList<StoredDeviceConfig>>(),
            "A",
            "After",
            "starlte",
            null,
            null,
            false,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AdvancedConfig_UsesFirstSelectedOnlineDeviceAndFlushesPreset()
    {
        DeviceListSnapshot snapshot = CreateSnapshot(
            [
                new StoredDeviceConfig { Serial = "B", Name = "Beta", Type = "starlte" },
                new StoredDeviceConfig { Serial = "A", Name = "Alpha", Type = "sargo" }
            ],
            [
                new AdbDevice("B", AdbDeviceStatus.Online),
                new AdbDevice("A", AdbDeviceStatus.Online)
            ]);
        var settings = new AppSettings
        {
            SelectedMultipleDeviceSerials = ["B", "A"]
        };
        var initialConfig = new MultipleDeviceConfiguration
        {
            ChangeOptions = new DeviceChangeOptions { UseDefaultMode = false }
        };
        TestContext context = CreateContext(snapshot, settings, initialConfig);
        context.AdvancedDialog.ShowAdvancedChangeConfigAsync(
                Arg.Any<string>(),
                Arg.Any<DeviceChangeOptions>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(new AdvancedChangeConfigDialogResult(
                new DeviceChangeOptions
                {
                    UseDefaultMode = false,
                    ChangeAndroidId = true,
                    ClearAllPackages = false
                },
                useIntegritySecurityPatch: false));
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.OpenAdvancedChangeConfigCommand.CanExecute(null));
        await viewModel.OpenAdvancedChangeConfigCommand.ExecuteAsync(null);
        viewModel.SelectedBrand = "Samsung";
        viewModel.SelectedModel = "SM-S918B";
        await viewModel.DeactivateAsync();

        await context.AdvancedDialog.Received(1).ShowAdvancedChangeConfigAsync(
            "B",
            Arg.Is<DeviceChangeOptions>(options => !options.UseDefaultMode),
            true,
            Arg.Any<CancellationToken>());
        await context.MultipleConfig.Received().SaveAsync(
            Arg.Is<MultipleDeviceConfiguration>(configuration =>
                configuration.ChangeConfig.Brand == "Samsung"
                && configuration.ChangeConfig.Model == "SM-S918B"
                && !configuration.ChangeConfig.UseIntegritySecurityPatch
                && configuration.ChangeOptions.ChangeAndroidId),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task DeactivateThenInitialize_RestartsPollingSafely()
    {
        TestContext context = CreateContext(CreateSnapshot([], []));
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.DeactivateAsync();
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.DeactivateAsync();

        Assert.AreEqual(2, context.Polling.StartCount);
    }

    [TestMethod]
    public async Task SaveMultipleDeviceColumnRatios_UpdatesSharedLayoutInPlace()
    {
        var settings = new AppSettings();
        Dictionary<string, double> sharedBefore = settings.DeviceTableColumnRatios;
        TestContext context = CreateContext(CreateSnapshot([], []), settings);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var ratios = new Dictionary<string, double>
        {
            ["Name"] = 0.4,
            ["Process"] = 0.6
        };

        await viewModel.SaveMultipleDeviceColumnRatiosCommand.ExecuteAsync(ratios);

        Assert.AreSame(sharedBefore, settings.DeviceTableColumnRatios);
        Assert.AreSame(settings.DeviceTableColumnRatios, viewModel.DeviceTableColumnRatios);
        Assert.AreEqual(0.4, viewModel.DeviceTableColumnRatios["Name"]);
        await context.SettingsService.Received(1).SaveAsync(
            settings,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task DeviceSearchText_MatchesSerialNameAndTypeOnly()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "SERIAL-MATCH", Name = "Name-Match", Type = "Type-Match" },
            new() { Serial = "OTHER", Name = "Other device", Type = "Other type" }
        ];
        var settings = new AppSettings();
        TestContext context = CreateContext(
            CreateSnapshot(storedDevices, [new AdbDevice("SERIAL-MATCH", AdbDeviceStatus.Online)]),
            settings);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.Devices.Single(device => device.Serial == "SERIAL-MATCH").Process = "Process-Match";

        foreach (string search in new[] { "serial-match", "NAME-match", "type-MATCH" })
        {
            viewModel.DeviceSearchText = search;
            Assert.HasCount(1, viewModel.Devices, search);
            Assert.AreEqual("SERIAL-MATCH", viewModel.Devices[0].Serial, search);
        }

        viewModel.DeviceSearchText = "statusonline";
        Assert.IsEmpty(viewModel.Devices);
        viewModel.DeviceSearchText = "process-match";
        Assert.IsEmpty(viewModel.Devices);

        viewModel.SelectedDeviceFilter = "Online";
        viewModel.DeviceSearchText = "name-match";
        Assert.HasCount(1, viewModel.Devices);
        viewModel.SelectedDeviceFilter = "Offline";
        Assert.IsEmpty(viewModel.Devices);
        viewModel.DeviceSearchText = "other";
        Assert.HasCount(1, viewModel.Devices);
        Assert.AreEqual("OTHER", viewModel.Devices[0].Serial);
        viewModel.DeviceSearchText = "  ";
        Assert.HasCount(1, viewModel.Devices);

        viewModel.SelectedDeviceFilter = "All";
        viewModel.DeviceSearchText = string.Empty;
        DeviceRowViewModel matchingRow = viewModel.Devices.Single(device => device.Serial == "SERIAL-MATCH");
        DeviceRowViewModel hiddenRow = viewModel.Devices.Single(device => device.Serial == "OTHER");
        viewModel.DeviceSearchText = "name-match";
        matchingRow.Name = "Changed";
        Assert.IsEmpty(viewModel.Devices);
        hiddenRow.Name = "Name-Match";
        Assert.HasCount(1, viewModel.Devices);
        Assert.AreSame(hiddenRow, viewModel.Devices[0]);

        viewModel.DeviceSearchText = string.Empty;
        viewModel.ToggleDeviceSelectionCommand.Execute(
            matchingRow);
        CollectionAssert.AreEqual(new[] { "SERIAL-MATCH" }, settings.SelectedMultipleDeviceSerials);
        viewModel.ApplyDeviceListSnapshot(
            CreateSnapshot(storedDevices, [new AdbDevice("SERIAL-MATCH", AdbDeviceStatus.Online)]));
        Assert.IsTrue(viewModel.Devices.Single(device => device.Serial == "SERIAL-MATCH").IsSelected);
        CollectionAssert.AreEqual(new[] { "SERIAL-MATCH" }, settings.SelectedMultipleDeviceSerials);

        await viewModel.DeactivateAsync();
    }

    private static TestContext CreateContext(
        DeviceListSnapshot snapshot,
        AppSettings? settings = null,
        MultipleDeviceConfiguration? configuration = null,
        IDeviceActionGuardService? deviceActionGuard = null)
    {
        IAddDevicesDialogService addDialog = Substitute.For<IAddDevicesDialogService>();
        IAdvancedChangeConfigDialogService advancedDialog =
            Substitute.For<IAdvancedChangeConfigDialogService>();
        ICarrierDataService carrierData = Substitute.For<ICarrierDataService>();
        IDeviceActionConfirmationDialogService actionConfirmation =
            Substitute.For<IDeviceActionConfirmationDialogService>();
        actionConfirmation.ConfirmMultipleAsync(
                Arg.Any<MultipleDeviceBatchAction>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        IDeviceChangeService deviceChange = Substitute.For<IDeviceChangeService>();
        IDeviceActionService deviceAction = Substitute.For<IDeviceActionService>();
        IDeviceLocationService locationService = Substitute.For<IDeviceLocationService>();
        IDeviceTimezoneService timezoneService = Substitute.For<IDeviceTimezoneService>();
        IChangeLocationDialogService locationDialog = Substitute.For<IChangeLocationDialogService>();
        IChangeTimezoneDialogService timezoneDialog = Substitute.For<IChangeTimezoneDialogService>();
        IDeviceViewerDialogService viewerDialog = Substitute.For<IDeviceViewerDialogService>();
        carrierData.GetCarrierProfilesAsync(Arg.Any<CancellationToken>())
            .Returns(
            [
                new CarrierProfile("us", "1", "United States", "AT&T", "310", "410"),
                new CarrierProfile("vn", "84", "Vietnam", "Viettel", "452", "04")
            ]);
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        deviceConfig.SaveLocationConfigAsync(
                Arg.Any<IList<StoredDeviceConfig>>(),
                Arg.Any<string>(),
                Arg.Any<ChangeLocationMode>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        deviceConfig.SaveTimezoneConfigAsync(
                Arg.Any<IList<StoredDeviceConfig>>(),
                Arg.Any<string>(),
                Arg.Any<ChangeTimezoneMode>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.IsDeviceOnlineAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        deviceActionGuard ??= new DeviceActionGuardService();
        IRandomDeviceInfoDialogService randomInfoDialog =
            Substitute.For<IRandomDeviceInfoDialogService>();
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();
        ISimProfileService simProfile = Substitute.For<ISimProfileService>();
        simProfile.CreateRandomProfile(Arg.Any<CarrierCountryOption>(), Arg.Any<CarrierOption>())
            .Returns(new SimProfile
            {
                Iccid = "8901000000000000000",
                Imsi = "310260123456789",
                PhoneNumber = "+15551234567",
                OperatorCountry = "us",
                OperatorNumeric = "310260",
                OperatorName = "T-Mobile"
            });
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>()).Returns(snapshot);
        deviceList.CountNewDevices(
                Arg.Any<IReadOnlyList<StoredDeviceConfig>>(),
                Arg.Any<IReadOnlyList<AdbDevice>>())
            .Returns(0);
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(callInfo =>
            callInfo.Arg<string>() switch
            {
                "ChangeMultipleDevices_NewDeviceCount" => "New devices: {0}",
                "ChangeMultipleDevices_NotAvailable" => "N/A",
                _ => callInfo.Arg<string>()
            });
        IMultipleDeviceConfigService multipleConfig =
            Substitute.For<IMultipleDeviceConfigService>();
        multipleConfig.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(configuration ?? new MultipleDeviceConfiguration());
        ISettingsService settingsService = Substitute.For<ISettingsService>();
        var polling = new BlockingPollingService();
        AppSettings sharedSettings = settings ?? new AppSettings();
        var viewModel = new ChangeMultipleDevicesViewModel(
            addDialog,
            advancedDialog,
            carrierData,
            actionConfirmation,
            deviceChange,
            deviceConfig,
            deviceList,
            deviceActionGuard,
            localization,
            multipleConfig,
            randomInfoDialog,
            randomDevice,
            simProfile,
            settingsService,
            new ImmediateDispatcherService(),
            polling,
            sharedSettings,
            NullLogger<ChangeMultipleDevicesViewModel>.Instance,
            deviceAction,
            locationService,
            timezoneService,
            locationDialog,
            timezoneDialog,
            viewerDialog);
        return new TestContext(
            viewModel,
            addDialog,
            advancedDialog,
            actionConfirmation,
            deviceChange,
            deviceConfig,
            deviceList,
            multipleConfig,
            settingsService,
            polling,
            randomDevice,
            randomInfoDialog,
            simProfile,
            deviceActionGuard,
            deviceAction,
            locationService,
            timezoneService,
            locationDialog,
            timezoneDialog,
            viewerDialog);
    }

    private static DeviceListSnapshot CreateSnapshot(
        IReadOnlyList<StoredDeviceConfig> storedDevices,
        IReadOnlyList<AdbDevice> connectedDevices)
    {
        return new DeviceListSnapshot(storedDevices, connectedDevices);
    }

    private sealed record TestContext(
        ChangeMultipleDevicesViewModel ViewModel,
        IAddDevicesDialogService AddDialog,
        IAdvancedChangeConfigDialogService AdvancedDialog,
        IDeviceActionConfirmationDialogService ActionConfirmation,
        IDeviceChangeService DeviceChange,
        IDeviceConfigService DeviceConfig,
        IDeviceListService DeviceList,
        IMultipleDeviceConfigService MultipleConfig,
        ISettingsService SettingsService,
        BlockingPollingService Polling,
        IRandomDeviceService RandomDevice,
        IRandomDeviceInfoDialogService RandomInfoDialog,
        ISimProfileService SimProfile,
        IDeviceActionGuardService DeviceActionGuard,
        IDeviceActionService DeviceAction,
        IDeviceLocationService LocationService,
        IDeviceTimezoneService TimezoneService,
        IChangeLocationDialogService LocationDialog,
        IChangeTimezoneDialogService TimezoneDialog,
        IDeviceViewerDialogService ViewerDialog);

    private static Task<RandomDeviceResult> StartRandom(
        TaskCompletionSource started,
        Task<RandomDeviceResult> completion)
    {
        started.TrySetResult();
        return completion;
    }

    private static Task StartBatchAction(TaskCompletionSource started, Task completion)
    {
        started.TrySetResult();
        return completion;
    }

    private static Task StartCountedBatchAction(
        TaskCompletionSource started,
        Task completion,
        Action countInvocation)
    {
        countInvocation();
        return StartBatchAction(started, completion);
    }

    private sealed class ImmediateDispatcherService : IUiDispatcherService
    {
        public bool CheckAccess() => true;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class ControllableDeviceActionGuard : IDeviceActionGuardService
    {
        private readonly object _syncRoot = new();
        private readonly HashSet<string> _busySerials = new(StringComparer.OrdinalIgnoreCase);

        public event Action<string, bool>? BusyStateChanged;

        public bool IsBusy(string serial)
        {
            lock (_syncRoot)
                return _busySerials.Contains(serial);
        }

        public IDisposable? TryAcquire(string serial)
        {
            lock (_syncRoot)
            {
                if (!_busySerials.Add(serial))
                    return null;
            }

            BusyStateChanged?.Invoke(serial, true);
            return new CallbackDisposable(() => Release(serial));
        }

        public void ForceRelease(string serial)
        {
            Release(serial);
        }

        private void Release(string serial)
        {
            bool removed;
            lock (_syncRoot)
                removed = _busySerials.Remove(serial);

            if (removed)
                BusyStateChanged?.Invoke(serial, false);
        }

        private sealed class CallbackDisposable(Action callback) : IDisposable
        {
            private Action? _callback = callback;

            public void Dispose()
            {
                Interlocked.Exchange(ref _callback, null)?.Invoke();
            }
        }
    }

    private sealed class BlockingPollingService : IPollingService
    {
        private Func<CancellationToken, Task>? _operation;
        private CancellationToken _cancellationToken;

        public int StartCount { get; private set; }

        public Task TickAsync()
        {
            return (_operation ?? throw new InvalidOperationException("Polling has not started."))(
                _cancellationToken);
        }

        public async Task RunAsync(
            TimeSpan interval,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken)
        {
            StartCount++;
            _operation = operation;
            _cancellationToken = cancellationToken;
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }
}
