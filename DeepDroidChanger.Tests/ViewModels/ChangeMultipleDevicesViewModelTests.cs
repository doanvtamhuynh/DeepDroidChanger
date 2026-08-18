using DeepDroidChanger.Models;
using DeepDroidChanger.Helpers;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using Microsoft.Extensions.Logging;
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
                Arg.Is<LocationOption>(location =>
                    location.CountryCode == selectedLocation.CountryCode
                    && location.CountryName == selectedLocation.CountryName
                    && location.CityName == selectedLocation.CityName
                    && location.Timezone == selectedLocation.Timezone
                    && location.GmtOffset == selectedLocation.GmtOffset
                    && location.Latitude == selectedLocation.Latitude
                    && location.Longitude == selectedLocation.Longitude),
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
            .ApplyCatalogLocationAsync(
                "A",
                Arg.Is<LocationOption>(location =>
                    location.CountryCode == selectedLocation.CountryCode
                    && location.CountryName == selectedLocation.CountryName
                    && location.CityName == selectedLocation.CityName
                    && location.Timezone == selectedLocation.Timezone
                    && location.GmtOffset == selectedLocation.GmtOffset
                    && location.Latitude == selectedLocation.Latitude
                    && location.Longitude == selectedLocation.Longitude),
                Arg.Any<CancellationToken>());
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
                Arg.Is<LocationOption>(location =>
                    location.CountryCode == selectedLocation.CountryCode
                    && location.CountryName == selectedLocation.CountryName
                    && location.CityName == selectedLocation.CityName
                    && location.Timezone == selectedLocation.Timezone
                    && location.GmtOffset == selectedLocation.GmtOffset
                    && location.Latitude == selectedLocation.Latitude
                    && location.Longitude == selectedLocation.Longitude),
                Arg.Any<CancellationToken>())
            .Returns(new DeviceLocationResult("10.7123", "106.6456", "VN", "Ho Chi Minh City"));

        await viewModel.InitializeAsync(CancellationToken.None);
        using IDisposable busyLease = context.DeviceActionGuard.TryStart("A", DeviceActionKind.BatchChangeDevice, canCancel: true)!;
        viewModel.SelectedInfoDevice = viewModel.Devices.Single(device => device.Serial == "B");

        await viewModel.ChangeSelectedLocationsCommand.ExecuteAsync(null);

        await context.LocationDialog.Received(1)
            .ShowChangeLocationBatchAsync(1, Arg.Any<CancellationToken>());
        await context.LocationService.DidNotReceive()
            .ApplyCatalogLocationAsync("A", Arg.Any<LocationOption>(), Arg.Any<CancellationToken>());
        await context.LocationService.Received(1)
            .ApplyCatalogLocationAsync(
                "B",
                Arg.Is<LocationOption>(location =>
                    location.CountryCode == selectedLocation.CountryCode
                    && location.CountryName == selectedLocation.CountryName
                    && location.CityName == selectedLocation.CityName
                    && location.Timezone == selectedLocation.Timezone
                    && location.GmtOffset == selectedLocation.GmtOffset
                    && location.Latitude == selectedLocation.Latitude
                    && location.Longitude == selectedLocation.Longitude),
                Arg.Any<CancellationToken>());
        await context.DeviceList.DidNotReceive()
            .IsDeviceOnlineAsync("A", Arg.Any<CancellationToken>());
        Assert.AreEqual("Log_DeviceActionAlreadyRunningFormat", viewModel.Devices.Single(device => device.Serial == "A").Process);
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
        using IDisposable busyLease = context.DeviceActionGuard.TryStart("A", DeviceActionKind.BatchChangeDevice, canCancel: true)!;
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
        Assert.AreEqual("Log_DeviceActionAlreadyRunningFormat", viewModel.Devices.Single(device => device.Serial == "A").Process);
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
        Assert.IsNull(context.DeviceActionGuard.TryStart("A", DeviceActionKind.BatchChangeDevice, canCancel: true));

        dialogResult.SetResult(null);
        await operation;

        Assert.AreEqual("Log_ChangeLocationCanceled", viewModel.Devices.Single().Process);
        Assert.AreEqual(DeviceProcessState.Canceled, viewModel.Devices.Single().ProcessState);
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("A"));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task StopSelectedDeviceAction_TransitionsToStoppingAndReleasesAfterDialogUnwinds()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Ready" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var dialogOpened = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogResult = new TaskCompletionSource<ChangeLocationDialogResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.LocationDialog.ShowChangeLocationBatchAsync(
                1,
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                CancellationToken token = callInfo.Arg<CancellationToken>();
                token.Register(() => dialogResult.TrySetResult(null));
                dialogOpened.TrySetResult();
                return dialogResult.Task;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task operation = viewModel.ChangeSelectedLocationsCommand.ExecuteAsync(null);
        await dialogOpened.Task;

        Assert.AreEqual(DeviceActionKind.BatchChangeLocation, viewModel.SelectedBatchActionKind);
        Assert.IsTrue(viewModel.HasActiveBatchActionButton);
        Assert.AreEqual(4, viewModel.SelectedBatchActionButtonRow);
        Assert.AreEqual(0, viewModel.SelectedBatchActionButtonColumn);
        Assert.IsTrue(viewModel.StopSelectedDeviceActionCommand.CanExecute(null));
        viewModel.StopSelectedDeviceActionCommand.Execute(null);

        Assert.AreEqual(DeviceActionRuntimeState.Stopping, context.DeviceActionGuard.GetOperation("A")!.State);
        Assert.IsTrue(context.DeviceActionGuard.IsBusy("A"));
        Assert.AreEqual(
            DeviceActionCancellationReason.UserStop,
            context.DeviceActionGuard.GetOperation("A")!.CancellationReason);
        Assert.IsFalse(viewModel.StopSelectedDeviceActionCommand.CanExecute(null));
        dialogResult.TrySetResult(null);

        await operation;

        Assert.IsNull(viewModel.SelectedBatchActionKind);
        Assert.AreEqual("Log_ChangeLocationCanceled", viewModel.Devices.Single().Process);
        Assert.AreEqual(DeviceProcessState.Canceled, viewModel.Devices.Single().ProcessState);
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("A"));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task StopSelectedDeviceAction_RandomDeviceUsesActionSpecificCanceledResult()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Ready" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var workerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workerCompletion = new TaskCompletionSource<RandomDeviceResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                CancellationToken token = callInfo.Arg<CancellationToken>();
                token.Register(() => workerCompletion.TrySetCanceled(token));
                workerStarted.TrySetResult();
                return workerCompletion.Task;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task operation = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await workerStarted.Task;

        Assert.AreEqual(DeviceActionKind.BatchRandomDevice, viewModel.SelectedBatchActionKind);
        Assert.AreEqual(0, viewModel.SelectedBatchActionButtonRow);
        Assert.AreEqual(0, viewModel.SelectedBatchActionButtonColumn);
        viewModel.StopSelectedDeviceActionCommand.Execute(null);
        await operation;

        DeviceRowViewModel device = viewModel.Devices.Single();
        Assert.AreEqual("Log_RandomDeviceCanceled", device.Process);
        Assert.AreEqual(DeviceProcessState.Canceled, device.ProcessState);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task Deactivate_RandomSimUsesActionSpecificReadyResult()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Ready" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var preflightStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preflightCompletion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.DeviceList.IsDeviceOnlineAsync("A", Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                CancellationToken token = callInfo.Arg<CancellationToken>();
                token.Register(() => preflightCompletion.TrySetCanceled(token));
                preflightStarted.TrySetResult();
                return preflightCompletion.Task;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task operation = viewModel.RandomSelectedSimsCommand.ExecuteAsync(null);
        await preflightStarted.Task;

        await viewModel.DeactivateAsync();
        await operation;

        DeviceRowViewModel device = viewModel.Devices.Single();
        Assert.AreEqual("Log_Ready", device.Process);
        Assert.AreEqual(DeviceProcessState.Ready, device.ProcessState);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task StopSelectedDeviceAction_WhileWipeConfirmationIsOpenPersistsCanceledResult()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Ready" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var confirmationOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmationResult = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.ActionConfirmation.ConfirmMultipleAsync(
                MultipleDeviceBatchAction.WipeWithoutChange,
                1,
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                CancellationToken token = callInfo.Arg<CancellationToken>();
                token.Register(() => confirmationResult.TrySetCanceled(token));
                confirmationOpened.TrySetResult();
                return confirmationResult.Task;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task operation = viewModel.WipeSelectedDevicesWithoutChangeCommand.ExecuteAsync(null);
        await confirmationOpened.Task;

        viewModel.StopSelectedDeviceActionCommand.Execute(null);
        Assert.AreEqual(DeviceActionRuntimeState.Stopping, context.DeviceActionGuard.GetOperation("A")!.State);
        Assert.IsTrue(context.DeviceActionGuard.IsBusy("A"));
        confirmationResult.SetResult(false);
        await operation;

        DeviceRowViewModel device = viewModel.Devices.Single();
        Assert.AreEqual("Log_WipeWithoutChangeCanceled", device.Process);
        Assert.AreEqual(DeviceProcessState.Canceled, device.ProcessState);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task StopSelectedDeviceAction_WhileWorkerRuns_PersistsCanceledLog()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Ready" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
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
        var applyStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var applyCompletion = new TaskCompletionSource<DeviceLocationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.LocationService.ApplyCatalogLocationAsync(
                Arg.Any<string>(),
                Arg.Any<LocationOption>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                CancellationToken token = callInfo.Arg<CancellationToken>();
                token.Register(() => applyCompletion.TrySetCanceled(token));
                applyStarted.TrySetResult();
                return applyCompletion.Task;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task operation = viewModel.ChangeSelectedLocationsCommand.ExecuteAsync(null);
        await applyStarted.Task;

        viewModel.StopSelectedDeviceActionCommand.Execute(null);
        await operation;

        DeviceRowViewModel device = viewModel.Devices.Single();
        Assert.AreEqual("Log_ChangeLocationCanceled", device.Process);
        Assert.AreEqual(DeviceProcessState.Canceled, device.ProcessState);
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("A"));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task StopSelectedDeviceAction_CancelsOnlySelectedBatchTarget()
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
        var started = new[]
        {
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var completions = new[]
        {
            new TaskCompletionSource<RandomDeviceResult>(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource<RandomDeviceResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var tokens = new CancellationToken[2];
        int invocationCount = 0;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                int index = Interlocked.Increment(ref invocationCount) - 1;
                tokens[index] = callInfo.Arg<CancellationToken>();
                tokens[index].Register(() => completions[index].TrySetCanceled(tokens[index]));
                started[index].TrySetResult();
                return completions[index].Task;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task batch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await Task.WhenAll(started.Select(source => source.Task));

        DeviceRowViewModel deviceA = viewModel.Devices.Single(device => device.Serial == "A");
        viewModel.SelectedInfoDevice = deviceA;
        Assert.IsTrue(viewModel.StopSelectedDeviceActionCommand.CanExecute(null));
        viewModel.StopSelectedDeviceActionCommand.Execute(null);

        DeviceActionOperationSnapshot stoppedA = context.DeviceActionGuard.GetOperation("A")!;
        DeviceActionOperationSnapshot runningB = context.DeviceActionGuard.GetOperation("B")!;
        Assert.AreEqual(DeviceActionRuntimeState.Stopping, stoppedA.State);
        Assert.AreEqual(DeviceActionCancellationReason.UserStop, stoppedA.CancellationReason);
        Assert.AreEqual(DeviceActionRuntimeState.Running, runningB.State);
        Assert.AreEqual(DeviceActionCancellationReason.None, runningB.CancellationReason);
        Assert.IsTrue(tokens[0].IsCancellationRequested);
        Assert.IsFalse(tokens[1].IsCancellationRequested);

        completions[1].SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile B" }));
        await batch;

        Assert.IsFalse(context.DeviceActionGuard.IsBusy("A"));
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("B"));
        Assert.AreEqual(DeviceProcessState.Succeeded,
            viewModel.Devices.Single(device => device.Serial == "B").ProcessState);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ChangeSelectedLocations_LifecycleCancellationReturnsRunningTargetToReady()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Ready" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
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
        var applyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applyCompletion = new TaskCompletionSource<DeviceLocationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.LocationService.ApplyCatalogLocationAsync(
                "A",
                Arg.Is<LocationOption>(location =>
                    location.CountryCode == selectedLocation.CountryCode
                    && location.CountryName == selectedLocation.CountryName
                    && location.CityName == selectedLocation.CityName
                    && location.Timezone == selectedLocation.Timezone
                    && location.GmtOffset == selectedLocation.GmtOffset
                    && location.Latitude == selectedLocation.Latitude
                    && location.Longitude == selectedLocation.Longitude),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                CancellationToken token = callInfo.Arg<CancellationToken>();
                token.Register(() => applyCompletion.TrySetCanceled(token));
                applyStarted.SetResult();
                return applyCompletion.Task;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task operation = viewModel.ChangeSelectedLocationsCommand.ExecuteAsync(null);
        await applyStarted.Task;
        Assert.AreEqual(DeviceProcessState.InProgress, viewModel.Devices.Single().ProcessState);

        await viewModel.DeactivateAsync();
        await operation;

        Assert.AreEqual("Log_Ready", viewModel.Devices.Single().Process);
        Assert.AreEqual(DeviceProcessState.Ready, viewModel.Devices.Single().ProcessState);
    }

    [TestMethod]
    public async Task LocationAndTimezoneActions_BusySelectedInfoDeviceDoNotBlockOtherFreeSelections()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "Busy" },
                    new StoredDeviceConfig { Serial = "B", Name = "Idle" }
                ],
                [
                    new AdbDevice("A", AdbDeviceStatus.Online),
                    new AdbDevice("B", AdbDeviceStatus.Online)
                ]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        using IDisposable busyLease = context.DeviceActionGuard.TryStart("A", DeviceActionKind.BatchChangeDevice, canCancel: true)!;
        viewModel.SelectedInfoDevice = viewModel.Devices.Single(device => device.Serial == "A");

        Assert.IsTrue(viewModel.ChangeSelectedLocationsCommand.CanExecute(null));
        Assert.IsTrue(viewModel.ChangeSelectedTimezonesCommand.CanExecute(null));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task SelectedInfoDeviceChange_NotifiesAllSharedBatchCommandsInBothDirections()
    {
        IDeviceActionCoordinatorService deviceActionCoordinator = new DeviceActionCoordinatorService();
        using IDeviceActionOperation busyDeviceLease = deviceActionCoordinator.TryStart(
            "A",
            DeviceActionKind.ChangeDevice,
            canCancel: true)!;
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
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] },
            deviceActionCoordinator: deviceActionCoordinator);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        DeviceRowViewModel deviceA = viewModel.Devices.Single(device => device.Serial == "A");
        DeviceRowViewModel deviceB = viewModel.Devices.Single(device => device.Serial == "B");
        var sharedGuardCommands = new (string Name, System.Windows.Input.ICommand Command)[]
        {
            (nameof(viewModel.RandomSelectedDevicesCommand), viewModel.RandomSelectedDevicesCommand),
            (nameof(viewModel.ChangeSelectedDevicesCommand), viewModel.ChangeSelectedDevicesCommand),
            (nameof(viewModel.RandomChangeAndWipeSelectedDevicesCommand), viewModel.RandomChangeAndWipeSelectedDevicesCommand),
            (nameof(viewModel.ChangeSelectedDevicesWithoutWipeCommand), viewModel.ChangeSelectedDevicesWithoutWipeCommand),
            (nameof(viewModel.WipeSelectedDevicesWithoutChangeCommand), viewModel.WipeSelectedDevicesWithoutChangeCommand),
            (nameof(viewModel.RandomSelectedSimsCommand), viewModel.RandomSelectedSimsCommand),
            (nameof(viewModel.ChangeSelectedSimsCommand), viewModel.ChangeSelectedSimsCommand),
            (nameof(viewModel.ChangeSelectedLocationsCommand), viewModel.ChangeSelectedLocationsCommand),
            (nameof(viewModel.ChangeSelectedTimezonesCommand), viewModel.ChangeSelectedTimezonesCommand),
            (nameof(viewModel.InstallSelectedPackagesCommand), viewModel.InstallSelectedPackagesCommand)
        };
        var notifications = sharedGuardCommands.ToDictionary(command => command.Name, _ => 0);
        foreach ((string name, System.Windows.Input.ICommand command) in sharedGuardCommands)
        {
            string commandName = name;
            command.CanExecuteChanged += (_, _) => notifications[commandName]++;
        }

        void AssertSharedGuardState(bool expected, string state)
        {
            foreach ((string name, System.Windows.Input.ICommand command) in sharedGuardCommands)
            {
                Assert.AreEqual(expected, command.CanExecute(null), $"{name} should be {state}.");
            }
        }

        Assert.AreSame(deviceA, viewModel.SelectedInfoDevice);
        AssertSharedGuardState(true, "enabled while another selected device is free");

        viewModel.SelectedInfoDevice = deviceB;

        foreach ((string name, System.Windows.Input.ICommand command) in sharedGuardCommands)
        {
            Assert.IsGreaterThan(0, notifications[name], $"{name} did not notify after selecting a free info device.");
        }
        AssertSharedGuardState(true, "enabled after selecting a free info device");

        var notificationsBeforeBusy = notifications.ToDictionary(pair => pair.Key, pair => pair.Value);
        using IDeviceActionOperation secondBusyDeviceLease = deviceActionCoordinator.TryStart(
            "B",
            DeviceActionKind.ChangeDevice,
            canCancel: true)!;
        AssertSharedGuardState(true, "enabled when every selected device is busy so the action can report the running action");
        foreach ((string name, System.Windows.Input.ICommand command) in sharedGuardCommands)
        {
            Assert.IsGreaterThan(
                notificationsBeforeBusy[name],
                notifications[name],
                $"{name} did not notify when the selected info device became busy.");
        }

        var notificationsBeforeRelease = notifications.ToDictionary(pair => pair.Key, pair => pair.Value);
        secondBusyDeviceLease.Dispose();
        AssertSharedGuardState(true, "enabled after the selected info device becomes free");
        foreach ((string name, System.Windows.Input.ICommand command) in sharedGuardCommands)
        {
            Assert.IsGreaterThan(
                notificationsBeforeRelease[name],
                notifications[name],
                $"{name} did not notify when the selected info device became free.");
        }

        var notificationsBeforeBusySelection = notifications.ToDictionary(pair => pair.Key, pair => pair.Value);
        viewModel.SelectedInfoDevice = deviceA;
        AssertSharedGuardState(true, "enabled after switching to a busy info device while another is free");
        foreach ((string name, System.Windows.Input.ICommand command) in sharedGuardCommands)
        {
            Assert.IsGreaterThan(
                notificationsBeforeBusySelection[name],
                notifications[name],
                $"{name} did not notify after switching back to a busy info device.");
        }

        var notificationsBeforeNullSelection = notifications.ToDictionary(pair => pair.Key, pair => pair.Value);
        viewModel.SelectedInfoDevice = null;
        AssertSharedGuardState(true, "enabled when the info device is cleared while another device is free");
        foreach ((string name, System.Windows.Input.ICommand command) in sharedGuardCommands)
        {
            Assert.IsGreaterThan(
                notificationsBeforeNullSelection[name],
                notifications[name],
                $"{name} did not notify when the info device was cleared.");
        }

        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task Dispose_WithLocationDialogOpen_CancelsAndWaitsForDialogCompletion()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Alpha", Type = "Phone" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]));
        var dialogCompletion = new TaskCompletionSource<ChangeLocationDialogResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken dialogToken = default;
        context.LocationDialog.ShowChangeLocationBatchAsync(
                1,
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                dialogToken = callInfo.Arg<CancellationToken>();
                return dialogCompletion.Task;
            });
        ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.Devices.Single().IsSelected = true;

        Task operation = viewModel.ChangeSelectedLocationsCommand.ExecuteAsync(null);
        await context.LocationDialog.Received(1)
            .ShowChangeLocationBatchAsync(1, Arg.Any<CancellationToken>());

        Task disposeTask = Task.Run(viewModel.Dispose);
        Task firstCompletion = await Task.WhenAny(
            disposeTask,
            Task.Delay(TimeSpan.FromSeconds(1)));
        bool disposedBeforeDialogCompleted = ReferenceEquals(firstCompletion, disposeTask);

        dialogCompletion.TrySetResult(null);
        await Task.WhenAll(disposeTask, operation);

        Assert.IsFalse(disposedBeforeDialogCompleted);
        Assert.IsTrue(dialogToken.IsCancellationRequested);
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("A"));
    }

    [TestMethod]
    public async Task ChangeSelectedLocations_CachedOfflineButLiveOnlineDeviceIsEligible()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Reconnected" }],
                []),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.ChangeSelectedLocationsCommand.ExecuteAsync(null);

        await context.LocationDialog.Received(1)
            .ShowChangeLocationBatchAsync(1, Arg.Any<CancellationToken>());
        Assert.AreEqual("Log_ChangeLocationCanceled", viewModel.Devices.Single().Process);
        await context.LocationService.DidNotReceiveWithAnyArgs()
            .ApplyCatalogLocationAsync(default!, default!, default);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ChangeSelectedLocations_LeaseRaceLostLeavesProcessUnchangedAndDoesNotOpenDialog()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Racing" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var onlineCheckStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var onlineCheckResult = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.DeviceList.IsDeviceOnlineAsync("A", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                onlineCheckStarted.TrySetResult();
                return onlineCheckResult.Task;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task operation = viewModel.ChangeSelectedLocationsCommand.ExecuteAsync(null);
        await onlineCheckStarted.Task;
        using IDisposable competingLease = context.DeviceActionGuard.TryStart("A", DeviceActionKind.BatchChangeDevice, canCancel: true)!;
        onlineCheckResult.SetResult(true);
        await operation;

        await context.LocationDialog.DidNotReceiveWithAnyArgs()
            .ShowChangeLocationBatchAsync(default, default);
        Assert.AreEqual("Log_DeviceActionAlreadyRunningFormat", viewModel.Devices.Single().Process);
        Assert.IsTrue(context.DeviceActionGuard.IsBusy("A"));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ChangeSelectedLocations_MixedEligibilityUsesOnlyReservedOnlineTargets()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new StoredDeviceConfig { Serial = "A", Name = "Offline" },
            new StoredDeviceConfig { Serial = "B", Name = "Busy" },
            new StoredDeviceConfig { Serial = "C", Name = "Online C" },
            new StoredDeviceConfig { Serial = "D", Name = "Online D" }
        ];
        TestContext context = CreateContext(
            CreateSnapshot(
                storedDevices,
                [
                    new AdbDevice("C", AdbDeviceStatus.Online),
                    new AdbDevice("D", AdbDeviceStatus.Online)
                ]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B", "C", "D"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);
        using IDisposable busyLease = context.DeviceActionGuard.TryStart("B", DeviceActionKind.BatchChangeDevice, canCancel: true)!;

        context.DeviceList.IsDeviceOnlineAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(
                callInfo.Arg<string>() is "C" or "D"));
        context.LocationDialog.ShowChangeLocationBatchAsync(
                2,
                Arg.Any<CancellationToken>())
            .Returns((ChangeLocationDialogResult?)null);

        await viewModel.ChangeSelectedLocationsCommand.ExecuteAsync(null);

        await context.LocationDialog.Received(1)
            .ShowChangeLocationBatchAsync(2, Arg.Any<CancellationToken>());
        Assert.AreEqual("Log_DeviceMustBeOnline", viewModel.Devices.Single(device => device.Serial == "A").Process);
        Assert.AreEqual("Log_DeviceActionAlreadyRunningFormat", viewModel.Devices.Single(device => device.Serial == "B").Process);
        Assert.AreEqual("Log_ChangeLocationCanceled", viewModel.Devices.Single(device => device.Serial == "C").Process);
        Assert.AreEqual("Log_ChangeLocationCanceled", viewModel.Devices.Single(device => device.Serial == "D").Process);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task InstallSelectedPackages_OfflineTargetShowsDeviceMustBeOnline()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Offline" }],
                []),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        context.DeviceList.IsDeviceOnlineAsync("A", Arg.Any<CancellationToken>())
            .Returns(false);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);

        await context.InstallPackageDialog.DidNotReceiveWithAnyArgs()
            .ShowInstallPackageBatchAsync(default, default);
        Assert.AreEqual("Log_DeviceMustBeOnline", viewModel.Devices.Single().Process);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task InstallSelectedPackages_CachedOfflineButLiveOnlineIsEligible()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Cached offline" }],
                []),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        context.DeviceList.IsDeviceOnlineAsync("A", Arg.Any<CancellationToken>())
            .Returns(true);
        context.InstallPackageDialog.ShowInstallPackageBatchAsync(1, Arg.Any<CancellationToken>())
            .Returns((InstallPackageBatchRequest?)null);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);

        await context.InstallPackageDialog.Received(1)
            .ShowInstallPackageBatchAsync(1, Arg.Any<CancellationToken>());
        Assert.AreEqual("Log_InstallPackageCanceled", viewModel.Devices.Single().Process);
        Assert.AreEqual(DeviceProcessState.Canceled, viewModel.Devices.Single().ProcessState);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task InstallSelectedPackages_LiveOnlineCheckFailureShowsOfflineFeedbackAndLogsDiagnostic()
    {
        var logger = new DeepDroidChanger.Tests.Fakes.TestLogger<ChangeMultipleDevicesViewModel>();
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Failure" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] },
            logger: logger);
        context.DeviceList.IsDeviceOnlineAsync("A", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("ADB unavailable")));
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);

        await context.InstallPackageDialog.DidNotReceiveWithAnyArgs()
            .ShowInstallPackageBatchAsync(default, default);
        Assert.AreEqual("Log_DeviceMustBeOnline", viewModel.Devices.Single().Process);
        Assert.IsTrue(logger.Messages.Any(message =>
            message.Contains("Live initial online preflight failed", StringComparison.Ordinal)));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task InstallSelectedPackages_LeaseRaceLostLeavesProcessUnchangedAndDoesNotOpenDialog()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Racing" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        var onlineCheckStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var onlineCheckResult = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.DeviceList.IsDeviceOnlineAsync("A", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                onlineCheckStarted.TrySetResult();
                return onlineCheckResult.Task;
            });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);
        Task operation = viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);
        await onlineCheckStarted.Task;
        using IDisposable competingLease = context.DeviceActionGuard.TryStart("A", DeviceActionKind.BatchChangeDevice, canCancel: true)!;
        onlineCheckResult.SetResult(true);

        await operation;

        await context.InstallPackageDialog.DidNotReceiveWithAnyArgs()
            .ShowInstallPackageBatchAsync(default, default);
        Assert.AreEqual("Log_DeviceActionAlreadyRunningFormat", viewModel.Devices.Single().Process);
        Assert.IsTrue(context.DeviceActionGuard.IsBusy("A"));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task InstallSelectedPackages_MixedEligibilityUsesOnlyReservedOnlineTargets()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new StoredDeviceConfig { Serial = "A", Name = "Offline" },
            new StoredDeviceConfig { Serial = "B", Name = "Busy" },
            new StoredDeviceConfig { Serial = "C", Name = "Online C" },
            new StoredDeviceConfig { Serial = "D", Name = "Online D" }
        ];
        TestContext context = CreateContext(
            CreateSnapshot(
                storedDevices,
                [
                    new AdbDevice("C", AdbDeviceStatus.Online),
                    new AdbDevice("D", AdbDeviceStatus.Online)
                ]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B", "C", "D"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);
        using IDisposable busyLease = context.DeviceActionGuard.TryStart("B", DeviceActionKind.BatchChangeDevice, canCancel: true)!;
        context.DeviceList.IsDeviceOnlineAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<string>() is "C" or "D"));
        context.InstallPackageDialog.ShowInstallPackageBatchAsync(
                2,
                Arg.Any<CancellationToken>())
            .Returns((InstallPackageBatchRequest?)null);

        await viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);

        await context.InstallPackageDialog.Received(1)
            .ShowInstallPackageBatchAsync(2, Arg.Any<CancellationToken>());
        Assert.AreEqual("Log_DeviceMustBeOnline", viewModel.Devices.Single(device => device.Serial == "A").Process);
        Assert.AreEqual("Log_DeviceActionAlreadyRunningFormat", viewModel.Devices.Single(device => device.Serial == "B").Process);
        Assert.AreEqual("Log_InstallPackageCanceled", viewModel.Devices.Single(device => device.Serial == "C").Process);
        Assert.AreEqual("Log_InstallPackageCanceled", viewModel.Devices.Single(device => device.Serial == "D").Process);
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("C"));
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("D"));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task InstallSelectedPackages_HoldsReservationsWhileDialogIsOpen()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "C", Name = "C" },
                    new StoredDeviceConfig { Serial = "D", Name = "D" }
                ],
                [new AdbDevice("C", AdbDeviceStatus.Online), new AdbDevice("D", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["C", "D"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var dialogOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogResult = new TaskCompletionSource<InstallPackageBatchRequest?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.InstallPackageDialog.ShowInstallPackageBatchAsync(2, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                dialogOpened.TrySetResult();
                return dialogResult.Task;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task operation = viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);
        await dialogOpened.Task;

        Assert.IsTrue(context.DeviceActionGuard.IsBusy("C"));
        Assert.IsTrue(context.DeviceActionGuard.IsBusy("D"));
        Assert.IsNull(context.DeviceActionGuard.TryStart("C", DeviceActionKind.BatchChangeDevice, canCancel: true));
        Assert.IsNull(context.DeviceActionGuard.TryStart("D", DeviceActionKind.BatchChangeDevice, canCancel: true));

        dialogResult.SetResult(null);
        await operation;

        await context.PackageInstall.DidNotReceiveWithAnyArgs()
            .InstallAsync(default!, default!, default!, default);
        await context.DeviceConfig.DidNotReceiveWithAnyArgs().SaveLocationConfigAsync(
            default!,
            default!,
            default,
            default!,
            default!,
            default!,
            default!,
            default);
        await context.DeviceConfig.DidNotReceiveWithAnyArgs().SaveTimezoneConfigAsync(
            default!,
            default!,
            default,
            default!,
            default);
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("C"));
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("D"));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task Dispose_WithInstallPackageDialogOpen_CancelsAndWaitsForDialogCompletion()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "A" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        var dialogOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogCompletion = new TaskCompletionSource<InstallPackageBatchRequest?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken dialogToken = default;
        context.InstallPackageDialog.ShowInstallPackageBatchAsync(1, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                dialogToken = callInfo.Arg<CancellationToken>();
                dialogOpened.TrySetResult();
                return dialogCompletion.Task;
            });
        ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        Task operation = viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);
        await dialogOpened.Task;

        Task disposeTask = Task.Run(viewModel.Dispose);
        Task firstCompletion = await Task.WhenAny(
            disposeTask,
            Task.Delay(TimeSpan.FromSeconds(1)));
        bool disposedBeforeDialogCompleted = ReferenceEquals(firstCompletion, disposeTask);

        try
        {
            Assert.IsFalse(disposedBeforeDialogCompleted);
            Assert.IsTrue(dialogToken.IsCancellationRequested);
        }
        finally
        {
            dialogCompletion.TrySetResult(null);
        }

        await Task.WhenAll(disposeTask, operation);

        Assert.IsFalse(context.DeviceActionGuard.IsBusy("A"));
    }

    [TestMethod]
    public async Task Deactivate_WithInstallPackageDialogOpen_RequestsCancellation()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "A" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        var dialogOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogCompletion = new TaskCompletionSource<InstallPackageBatchRequest?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken dialogToken = default;
        context.InstallPackageDialog.ShowInstallPackageBatchAsync(1, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                dialogToken = callInfo.Arg<CancellationToken>();
                dialogOpened.TrySetResult();
                dialogToken.Register(() => dialogCompletion.TrySetResult(null));
                return dialogCompletion.Task;
            });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        Task operation = viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);
        await dialogOpened.Task;
        await viewModel.DeactivateAsync();
        await operation;

        Assert.IsTrue(dialogToken.IsCancellationRequested);
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("A"));
    }

    [TestMethod]
    public async Task InstallSelectedPackages_TargetDisconnectsWhileDialogOpen_ThenCancelPreservesOfflineLog()
    {
        DeviceListSnapshot initial = CreateSnapshot(
            [
                new StoredDeviceConfig { Serial = "A", Name = "A" },
                new StoredDeviceConfig { Serial = "B", Name = "B" }
            ],
            [new AdbDevice("A", AdbDeviceStatus.Online), new AdbDevice("B", AdbDeviceStatus.Online)]);
        DeviceListSnapshot disconnected = CreateSnapshot(
            [
                new StoredDeviceConfig { Serial = "A", Name = "A" },
                new StoredDeviceConfig { Serial = "B", Name = "B" }
            ],
            [new AdbDevice("A", AdbDeviceStatus.Online)]);
        TestContext context = CreateContext(
            initial,
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        context.DeviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(initial, initial, disconnected);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var dialogOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogResult = new TaskCompletionSource<InstallPackageBatchRequest?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.InstallPackageDialog.ShowInstallPackageBatchAsync(2, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                dialogOpened.TrySetResult();
                return dialogResult.Task;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task operation = viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);
        await dialogOpened.Task;

        await context.Polling.TickAsync();
        Assert.AreEqual("Log_DeviceMustBeOnline", viewModel.Devices.Single(device => device.Serial == "B").Process);
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("B"));

        dialogResult.SetResult(null);
        await operation;

        Assert.AreEqual("Log_InstallPackageCanceled", viewModel.Devices.Single(device => device.Serial == "A").Process);
        Assert.AreEqual(
            DeviceProcessState.Canceled,
            viewModel.Devices.Single(device => device.Serial == "A").ProcessState);
        Assert.AreEqual("Log_DeviceMustBeOnline", viewModel.Devices.Single(device => device.Serial == "B").Process);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task InstallSelectedPackages_RechecksLiveOnlineBeforeInstallation()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "A" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        context.DeviceList.IsDeviceOnlineAsync("A", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true), Task.FromResult(false));
        context.InstallPackageDialog.ShowInstallPackageBatchAsync(1, Arg.Any<CancellationToken>())
            .Returns(new InstallPackageBatchRequest(
                Array.AsReadOnly(new[] { "one.apk" }),
                new InstallPackageOptions(true, false)));
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);

        await context.PackageInstall.DidNotReceiveWithAnyArgs()
            .InstallAsync(default!, default!, default!, default);
        Assert.AreEqual("Log_DeviceMustBeOnline", viewModel.Devices.Single().Process);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task InstallSelectedPackages_UsesSamePackageSnapshotForEveryEligibleDevice()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "A" },
                    new StoredDeviceConfig { Serial = "B", Name = "B" }
                ],
                [new AdbDevice("A", AdbDeviceStatus.Online), new AdbDevice("B", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        var request = new InstallPackageBatchRequest(
            Array.AsReadOnly(new[] { "one.apk", "two.xapk" }),
            new InstallPackageOptions(false, true));
        context.InstallPackageDialog.ShowInstallPackageBatchAsync(2, Arg.Any<CancellationToken>())
            .Returns(request);
        context.PackageInstall.InstallAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<InstallPackageOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => new InstallPackageResult(
                callInfo.ArgAt<string>(1),
                true,
                "Log_InstallPackageSuccess"));
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);

        await context.PackageInstall.Received(1)
            .InstallAsync(
                "A",
                "one.apk",
                Arg.Is<InstallPackageOptions>(options =>
                    options.GrantPermissions == request.Options.GrantPermissions
                    && options.AllowDowngrade == request.Options.AllowDowngrade),
                Arg.Any<CancellationToken>());
        await context.PackageInstall.Received(1)
            .InstallAsync(
                "A",
                "two.xapk",
                Arg.Is<InstallPackageOptions>(options =>
                    options.GrantPermissions == request.Options.GrantPermissions
                    && options.AllowDowngrade == request.Options.AllowDowngrade),
                Arg.Any<CancellationToken>());
        await context.PackageInstall.Received(1)
            .InstallAsync(
                "B",
                "one.apk",
                Arg.Is<InstallPackageOptions>(options =>
                    options.GrantPermissions == request.Options.GrantPermissions
                    && options.AllowDowngrade == request.Options.AllowDowngrade),
                Arg.Any<CancellationToken>());
        await context.PackageInstall.Received(1)
            .InstallAsync(
                "B",
                "two.xapk",
                Arg.Is<InstallPackageOptions>(options =>
                    options.GrantPermissions == request.Options.GrantPermissions
                    && options.AllowDowngrade == request.Options.AllowDowngrade),
                Arg.Any<CancellationToken>());
        Assert.AreEqual("complete 2/2", viewModel.Devices.Single(device => device.Serial == "A").Process);
        Assert.AreEqual("complete 2/2", viewModel.Devices.Single(device => device.Serial == "B").Process);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task InstallSelectedPackages_InstallsPackagesSequentiallyPerDevice()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "A" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        var request = new InstallPackageBatchRequest(
            Array.AsReadOnly(new[] { "one.apk", "two.apk" }),
            new InstallPackageOptions(true, false));
        var calls = new List<string>();
        var firstPackageStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstPackage = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondPackageStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.InstallPackageDialog.ShowInstallPackageBatchAsync(1, Arg.Any<CancellationToken>())
            .Returns(request);
        context.PackageInstall.InstallAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<InstallPackageOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => InstallPackageAsync(
                callInfo.ArgAt<string>(0),
                callInfo.ArgAt<string>(1)));

        async Task<InstallPackageResult> InstallPackageAsync(string serial, string filePath)
        {
            calls.Add($"{serial}:{filePath}");
            if (filePath == "one.apk")
            {
                firstPackageStarted.TrySetResult();
                await releaseFirstPackage.Task;
            }
            else
            {
                secondPackageStarted.TrySetResult();
            }

            return new InstallPackageResult(filePath, true, "Log_InstallPackageSuccess");
        }

        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        Task operation = viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);
        try
        {
            await firstPackageStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.IsFalse(secondPackageStarted.Task.IsCompleted);
        }
        finally
        {
            releaseFirstPackage.TrySetResult();
        }

        await operation;

        CollectionAssert.AreEqual(new[] { "A:one.apk", "A:two.apk" }, calls);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task InstallSelectedPackages_AllowsDifferentDevicesToRunConcurrently()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "A" },
                    new StoredDeviceConfig { Serial = "B", Name = "B" },
                    new StoredDeviceConfig { Serial = "C", Name = "C" },
                    new StoredDeviceConfig { Serial = "D", Name = "D" },
                    new StoredDeviceConfig { Serial = "E", Name = "E" }
                ],
                [
                    new AdbDevice("A", AdbDeviceStatus.Online),
                    new AdbDevice("B", AdbDeviceStatus.Online),
                    new AdbDevice("C", AdbDeviceStatus.Online),
                    new AdbDevice("D", AdbDeviceStatus.Online),
                    new AdbDevice("E", AdbDeviceStatus.Online)
                ]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B", "C", "D", "E"] });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedBySerial = new Dictionary<string, TaskCompletionSource>(StringComparer.Ordinal)
        {
            ["A"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["B"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["C"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["D"] = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ["E"] = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        int activeWorkers = 0;
        int maximumActiveWorkers = 0;
        var request = new InstallPackageBatchRequest(
            Array.AsReadOnly(new[] { "one.apk" }),
            new InstallPackageOptions(true, false));

        async Task<InstallPackageResult> InstallAndWaitAsync(string serial, string filePath)
        {
            int current = Interlocked.Increment(ref activeWorkers);
            while (current > Volatile.Read(ref maximumActiveWorkers)
                   && Interlocked.CompareExchange(
                       ref maximumActiveWorkers,
                       current,
                       Volatile.Read(ref maximumActiveWorkers)) != current)
            {
            }

            startedBySerial[serial].TrySetResult();

            await release.Task;
            Interlocked.Decrement(ref activeWorkers);
            return new InstallPackageResult(filePath, true, "Log_InstallPackageSuccess");
        }

        context.InstallPackageDialog.ShowInstallPackageBatchAsync(5, Arg.Any<CancellationToken>())
            .Returns(request);
        context.PackageInstall.InstallAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<InstallPackageOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => InstallAndWaitAsync(
                callInfo.ArgAt<string>(0),
                callInfo.ArgAt<string>(1)));
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);
        Task operation = viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);

        async Task<int> WaitForStartedWorkersAsync()
        {
            var pending = startedBySerial.Values
                .Select(started => started.Task)
                .ToList();
            int startedCount = 0;
            while (startedCount < 4)
            {
                Task completed = await Task.WhenAny(pending)
                    .WaitAsync(TimeSpan.FromSeconds(1));
                pending.Remove(completed);
                startedCount++;
            }

            Assert.IsFalse(pending.Single().IsCompleted);
            return startedCount;
        }

        try
        {
            int startedCount = await WaitForStartedWorkersAsync();
            Assert.AreEqual(4, startedCount);
            Assert.IsGreaterThan(1, maximumActiveWorkers);
            Assert.IsLessThanOrEqualTo(4, maximumActiveWorkers);
        }
        finally
        {
            release.TrySetResult();
        }

        await operation;
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task InstallSelectedPackages_PackageFailureDoesNotStopRemainingPackages()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "A" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        var request = new InstallPackageBatchRequest(
            Array.AsReadOnly(new[] { "one.apk", "two.apk" }),
            new InstallPackageOptions(true, false));
        context.InstallPackageDialog.ShowInstallPackageBatchAsync(1, Arg.Any<CancellationToken>())
            .Returns(request);
        context.PackageInstall.InstallAsync(
                "A",
                "one.apk",
                Arg.Any<InstallPackageOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(new InstallPackageResult("one.apk", false, "Log_InstallPackageNoMatchingAbis"));
        context.PackageInstall.InstallAsync(
                "A",
                "two.apk",
                Arg.Any<InstallPackageOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(new InstallPackageResult("two.apk", true, "Log_InstallPackageSuccess"));
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);

        await context.PackageInstall.Received(1)
            .InstallAsync(
                "A",
                "two.apk",
                Arg.Is<InstallPackageOptions>(options =>
                    options.GrantPermissions == request.Options.GrantPermissions
                    && options.AllowDowngrade == request.Options.AllowDowngrade),
                Arg.Any<CancellationToken>());
        Assert.AreEqual("partial 1/2", viewModel.Devices.Single().Process);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task InstallSelectedPackages_AllPackagesFailedUsesFailedSummary()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "A" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        var request = new InstallPackageBatchRequest(
            Array.AsReadOnly(new[] { "one.apk", "two.apk" }),
            new InstallPackageOptions(true, false));
        context.InstallPackageDialog.ShowInstallPackageBatchAsync(1, Arg.Any<CancellationToken>())
            .Returns(request);
        context.PackageInstall.InstallAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<InstallPackageOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => new InstallPackageResult(
                callInfo.ArgAt<string>(1),
                false,
                "Log_InstallPackageAdbFailure"));
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);

        Assert.AreEqual("failed 0/2", viewModel.Devices.Single().Process);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task InstallSelectedPackages_OneDeviceFailureDoesNotStopOtherDevices()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "A", Name = "A" },
                    new StoredDeviceConfig { Serial = "B", Name = "B" }
                ],
                [new AdbDevice("A", AdbDeviceStatus.Online), new AdbDevice("B", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        var request = new InstallPackageBatchRequest(
            Array.AsReadOnly(new[] { "one.apk", "two.apk" }),
            new InstallPackageOptions(true, false));
        context.InstallPackageDialog.ShowInstallPackageBatchAsync(2, Arg.Any<CancellationToken>())
            .Returns(request);
        context.PackageInstall.InstallAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<InstallPackageOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => new InstallPackageResult(
                callInfo.ArgAt<string>(1),
                callInfo.ArgAt<string>(0) == "B",
                callInfo.ArgAt<string>(0) == "B"
                    ? "Log_InstallPackageSuccess"
                    : "Log_InstallPackageAdbFailure"));
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);

        await context.PackageInstall.Received(1)
            .InstallAsync(
                "A",
                "two.apk",
                Arg.Is<InstallPackageOptions>(options =>
                    options.GrantPermissions == request.Options.GrantPermissions
                    && options.AllowDowngrade == request.Options.AllowDowngrade),
                Arg.Any<CancellationToken>());
        await context.PackageInstall.Received(1)
            .InstallAsync(
                "B",
                "two.apk",
                Arg.Is<InstallPackageOptions>(options =>
                    options.GrantPermissions == request.Options.GrantPermissions
                    && options.AllowDowngrade == request.Options.AllowDowngrade),
                Arg.Any<CancellationToken>());
        Assert.AreEqual("failed 0/2", viewModel.Devices.Single(device => device.Serial == "A").Process);
        Assert.AreEqual("complete 2/2", viewModel.Devices.Single(device => device.Serial == "B").Process);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task InstallSelectedPackages_SinglePackageFailurePreservesDetailedFailureMessage()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "A" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        var request = new InstallPackageBatchRequest(
            Array.AsReadOnly(new[] { "one.apk" }),
            new InstallPackageOptions(true, false));
        context.InstallPackageDialog.ShowInstallPackageBatchAsync(1, Arg.Any<CancellationToken>())
            .Returns(request);
        context.PackageInstall.InstallAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<InstallPackageOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(new InstallPackageResult(
                "one.apk",
                false,
                "Log_InstallPackageNoMatchingAbis"));
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);

        Assert.AreEqual("Log_InstallPackageNoMatchingAbis", viewModel.Devices.Single().Process);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task InstallSelectedPackages_FormattedSinglePackageFailureUsesMessageArguments()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "A" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        var request = new InstallPackageBatchRequest(
            Array.AsReadOnly(new[] { "one.apk" }),
            new InstallPackageOptions(true, false));
        context.InstallPackageDialog.ShowInstallPackageBatchAsync(1, Arg.Any<CancellationToken>())
            .Returns(request);
        context.PackageInstall.InstallAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<InstallPackageOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(new InstallPackageResult(
                "one.apk",
                false,
                "Log_InstallPackageAdbFailureCodeFormat",
                "INSTALL_FAILED_TEST",
                "INSTALL_FAILED_TEST"));
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.InstallSelectedPackagesCommand.ExecuteAsync(null);

        Assert.AreEqual("ADB failed: INSTALL_FAILED_TEST", viewModel.Devices.Single().Process);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ChangeSelectedTimezones_LiveOnlineExceptionShowsOfflineFeedbackAndLogsDiagnostic()
    {
        var logger = new DeepDroidChanger.Tests.Fakes.TestLogger<ChangeMultipleDevicesViewModel>();
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Failure" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] },
            logger: logger);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        context.DeviceList.IsDeviceOnlineAsync("A", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("ADB unavailable")));

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.ChangeSelectedTimezonesCommand.ExecuteAsync(null);

        await context.TimezoneDialog.DidNotReceiveWithAnyArgs()
            .ShowChangeTimezoneBatchAsync(default, default);
        Assert.AreEqual("Log_DeviceMustBeOnline", viewModel.Devices.Single().Process);
        Assert.IsTrue(logger.Messages.Any(message =>
            message.Contains("Live initial online preflight failed", StringComparison.Ordinal)));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ChangeSelectedTimezones_HoldsLeaseUntilDialogCompletesAndCancelLogsTarget()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Ready" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var dialogOpened = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogResult = new TaskCompletionSource<ChangeTimezoneDialogResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.TimezoneDialog.ShowChangeTimezoneBatchAsync(
                1,
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                dialogOpened.TrySetResult();
                return dialogResult.Task;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task operation = viewModel.ChangeSelectedTimezonesCommand.ExecuteAsync(null);
        await dialogOpened.Task;

        Assert.IsTrue(context.DeviceActionGuard.IsBusy("A"));
        dialogResult.SetResult(null);
        await operation;

        Assert.AreEqual("Log_ChangeTimezoneCanceled", viewModel.Devices.Single().Process);
        Assert.AreEqual(DeviceProcessState.Canceled, viewModel.Devices.Single().ProcessState);
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("A"));
        await context.TimezoneService.DidNotReceiveWithAnyArgs()
            .ApplyAsync(default!, default!, default);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ChangeSelectedLocations_DisconnectThenCancelPreservesOfflineFeedback()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new StoredDeviceConfig { Serial = "A", Name = "Still online" },
            new StoredDeviceConfig { Serial = "B", Name = "Disconnects" }
        ];
        TestContext context = CreateContext(
            CreateSnapshot(
                storedDevices,
                [
                    new AdbDevice("A", AdbDeviceStatus.Online),
                    new AdbDevice("B", AdbDeviceStatus.Online)
                ]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var dialogOpened = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogResult = new TaskCompletionSource<ChangeLocationDialogResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.LocationDialog.ShowChangeLocationBatchAsync(
                2,
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                dialogOpened.TrySetResult();
                return dialogResult.Task;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task operation = viewModel.ChangeSelectedLocationsCommand.ExecuteAsync(null);
        await dialogOpened.Task;

        viewModel.ApplyDeviceListSnapshot(CreateSnapshot(
            storedDevices,
            [new AdbDevice("A", AdbDeviceStatus.Online)]));
        Assert.AreEqual("Log_DeviceMustBeOnline", viewModel.Devices.Single(device => device.Serial == "B").Process);

        dialogResult.SetResult(null);
        await operation;

        Assert.AreEqual("Log_ChangeLocationCanceled", viewModel.Devices.Single(device => device.Serial == "A").Process);
        Assert.AreEqual(
            DeviceProcessState.Canceled,
            viewModel.Devices.Single(device => device.Serial == "A").ProcessState);
        Assert.AreEqual("Log_DeviceMustBeOnline", viewModel.Devices.Single(device => device.Serial == "B").Process);
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("A"));
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("B"));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ChangeSelectedTimezones_DisconnectThenCancelPreservesOfflineFeedback()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new StoredDeviceConfig { Serial = "A", Name = "Still online" },
            new StoredDeviceConfig { Serial = "B", Name = "Disconnects" }
        ];
        TestContext context = CreateContext(
            CreateSnapshot(
                storedDevices,
                [
                    new AdbDevice("A", AdbDeviceStatus.Online),
                    new AdbDevice("B", AdbDeviceStatus.Online)
                ]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var dialogOpened = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogResult = new TaskCompletionSource<ChangeTimezoneDialogResult?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.TimezoneDialog.ShowChangeTimezoneBatchAsync(
                2,
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                dialogOpened.TrySetResult();
                return dialogResult.Task;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task operation = viewModel.ChangeSelectedTimezonesCommand.ExecuteAsync(null);
        await dialogOpened.Task;

        viewModel.ApplyDeviceListSnapshot(CreateSnapshot(
            storedDevices,
            [new AdbDevice("A", AdbDeviceStatus.Online)]));
        Assert.AreEqual("Log_DeviceMustBeOnline", viewModel.Devices.Single(device => device.Serial == "B").Process);

        dialogResult.SetResult(null);
        await operation;

        Assert.AreEqual("Log_ChangeTimezoneCanceled", viewModel.Devices.Single(device => device.Serial == "A").Process);
        Assert.AreEqual(
            DeviceProcessState.Canceled,
            viewModel.Devices.Single(device => device.Serial == "A").ProcessState);
        Assert.AreEqual("Log_DeviceMustBeOnline", viewModel.Devices.Single(device => device.Serial == "B").Process);
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("A"));
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("B"));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task ChangeSelectedLocations_ExecutionRecheckOfflineSkipsApplyAndPersistence()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Goes offline" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
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
        int onlineCheckCount = 0;
        context.DeviceList.IsDeviceOnlineAsync("A", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Interlocked.Increment(ref onlineCheckCount) == 1));

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.ChangeSelectedLocationsCommand.ExecuteAsync(null);

        Assert.AreEqual(2, onlineCheckCount);
        Assert.AreEqual("Log_DeviceMustBeOnline", viewModel.Devices.Single().Process);
        await context.LocationService.DidNotReceiveWithAnyArgs()
            .ApplyCatalogLocationAsync(default!, default!, default);
        await context.DeviceConfig.DidNotReceiveWithAnyArgs().SaveLocationConfigAsync(
            default!,
            default!,
            default,
            default!,
            default!,
            default!,
            default!,
            default);
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
        Assert.AreEqual("Log_RebootDeviceSuccess", clicked.Process);
        Assert.AreEqual(DeviceProcessState.Succeeded, clicked.ProcessState);
        Assert.IsTrue(viewModel.Devices.Single(device => device.Serial == "A").IsSelected);
        Assert.IsTrue(viewModel.Devices.Single(device => device.Serial == "B").IsSelected);
        Assert.IsFalse(clicked.IsSelected);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RebootContextCommand_FailurePersistsFailedResult()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Alpha" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]));
        context.DeviceAction.RebootAsync("A", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("reboot failed")));
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        DeviceRowViewModel device = viewModel.Devices.Single();
        await viewModel.RebootDeviceCommand.ExecuteAsync(device);

        Assert.AreEqual("Log_RebootDeviceFailed", device.Process);
        Assert.AreEqual(DeviceProcessState.Failed, device.ProcessState);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task DeleteContextCommand_CanceledConfirmationPersistsCanceledResult()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Alpha" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]));
        context.ActionConfirmation.ConfirmDeleteDeviceAsync(
                "Alpha",
                "A",
                Arg.Any<CancellationToken>())
            .Returns(false);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        DeviceRowViewModel device = viewModel.Devices.Single();
        await viewModel.DeleteDeviceCommand.ExecuteAsync(device);

        Assert.AreEqual("Log_DeleteDeviceCanceled", device.Process);
        Assert.AreEqual(DeviceProcessState.Canceled, device.ProcessState);
        Assert.HasCount(1, viewModel.Devices);
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
        Assert.AreEqual(DeviceProcessState.Failed, device.ProcessState);
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
    public async Task ContextMenuActions_BusyOnlineDeviceShowsRunningLogAndDisablesDelete()
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
        IDisposable busyLease = context.DeviceActionGuard.TryStart("A", DeviceActionKind.BatchChangeDevice, canCancel: true)!;

        Assert.IsTrue(viewModel.ViewDeviceInfoCommand.CanExecute(device));
        Assert.IsTrue(viewModel.CopySerialCommand.CanExecute(device));
        Assert.IsTrue(viewModel.ToggleGmsCommand.CanExecute(device));
        Assert.IsTrue(viewModel.TogglePlayStoreCommand.CanExecute(device));
        Assert.IsTrue(viewModel.ToggleWifiCommand.CanExecute(device));
        Assert.IsTrue(viewModel.RebootDeviceCommand.CanExecute(device));
        Assert.IsFalse(viewModel.DeleteDeviceCommand.CanExecute(device));

        await viewModel.RefreshContextMenuStateCommand.ExecuteAsync(device);
        await viewModel.RebootDeviceCommand.ExecuteAsync(device);
        await viewModel.ToggleGmsCommand.ExecuteAsync(device);
        await viewModel.TogglePlayStoreCommand.ExecuteAsync(device);
        await viewModel.ToggleWifiCommand.ExecuteAsync(device);

        await context.DeviceAction.DidNotReceive()
            .RebootAsync("A", Arg.Any<CancellationToken>());
        await context.DeviceAction.DidNotReceive()
            .SetGmsEnabledAsync("A", Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await context.DeviceAction.DidNotReceive()
            .SetPlayStoreEnabledAsync("A", Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await context.DeviceAction.DidNotReceive()
            .SetWifiEnabledAsync("A", Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await context.DeviceAction.DidNotReceive()
            .GetGooglePackageStateAsync("A", Arg.Any<CancellationToken>());
        await context.DeviceAction.DidNotReceive()
            .GetWifiEnabledAsync("A", Arg.Any<CancellationToken>());
        Assert.IsTrue(context.DeviceActionGuard.IsBusy("A"));
        Assert.AreEqual("Log_DeviceActionAlreadyRunningFormat", device.Process);

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
        using IDisposable busyLease = context.DeviceActionGuard.TryStart("A", DeviceActionKind.BatchChangeDevice, canCancel: true)!;
        Task refresh = viewModel.RefreshContextMenuStateCommand.ExecuteAsync(device);

        Assert.IsFalse(device.IsContextMenuStateLoading);
        Assert.IsTrue(device.CanToggleContextMenuActions);
        Assert.IsTrue(viewModel.CopySerialCommand.CanExecute(device));
        Assert.IsTrue(viewModel.RebootDeviceCommand.CanExecute(device));
        Assert.IsFalse(viewModel.DeleteDeviceCommand.CanExecute(device));

        await refresh;

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
        using IDisposable busyLease = context.DeviceActionGuard.TryStart("A", DeviceActionKind.BatchChangeDevice, canCancel: true)!;

        viewModel.ToggleSelectAllDevicesCommand.Execute(null);

        Assert.IsTrue(viewModel.Devices.Single(device => device.Serial == "A").IsSelected);
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
        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
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
    public async Task OverlappingBatchWorkflowsUseIndependentConfigurationSnapshots()
    {
        StoredDeviceConfig[] devices =
        [
            new() { Serial = "A", Name = "Alpha" },
            new() { Serial = "B", Name = "Beta" }
        ];
        var initialOptions = new DeviceChangeOptions
        {
            UseDefaultMode = false,
            ChangeAndroidId = true,
            ChangeMacAddress = false
        };
        var updatedOptions = new DeviceChangeOptions
        {
            UseDefaultMode = false,
            ChangeAndroidId = false,
            ChangeMacAddress = true
        };
        TestContext context = CreateContext(
            CreateSnapshot(
                devices,
                devices.Select(device => new AdbDevice(device.Serial, AdbDeviceStatus.Online)).ToArray()),
            new AppSettings { SelectedMultipleDeviceSerials = ["B"] },
            new MultipleDeviceConfiguration { ChangeOptions = initialOptions });
        context.MultipleConfig.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(
                new MultipleDeviceConfiguration { ChangeOptions = initialOptions },
                new MultipleDeviceConfiguration { ChangeOptions = initialOptions },
                new MultipleDeviceConfiguration { ChangeOptions = updatedOptions });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var deviceBStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deviceBCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capturedOptions = new Dictionary<string, DeviceChangeOptions>(StringComparer.OrdinalIgnoreCase);
        context.DeviceChange.WipeWithoutChangeAsync(
                Arg.Any<string>(),
                Arg.Any<DeviceChangeOptions>(),
                Arg.Any<IProgress<DeviceChangeStage>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                string serial = callInfo.Arg<string>();
                capturedOptions[serial] = DeviceChangeOptionsHelper.CreateNormalizedCopy(
                    callInfo.Arg<DeviceChangeOptions>());
                return serial == "B"
                    ? StartBatchAction(deviceBStarted, deviceBCompletion.Task)
                    : Task.CompletedTask;
            });
        context.AdvancedDialog.ShowAdvancedChangeConfigAsync(
                Arg.Any<string>(),
                Arg.Any<DeviceChangeOptions>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(new AdvancedChangeConfigDialogResult(updatedOptions, useIntegritySecurityPatch: false));

        await viewModel.InitializeAsync(CancellationToken.None);
        Task firstWorkflow = viewModel.WipeSelectedDevicesWithoutChangeCommand.ExecuteAsync(null);
        await deviceBStarted.Task;

        DeviceRowViewModel deviceA = viewModel.Devices.Single(device => device.Serial == "A");
        viewModel.ToggleDeviceSelectionCommand.Execute(deviceA);
        viewModel.SelectedInfoDevice = deviceA;
        Assert.IsTrue(viewModel.OpenAdvancedChangeConfigCommand.CanExecute(null));
        await viewModel.OpenAdvancedChangeConfigCommand.ExecuteAsync(null);

        Task secondWorkflow = viewModel.WipeSelectedDevicesWithoutChangeCommand.ExecuteAsync(null);
        await secondWorkflow;
        deviceBCompletion.SetResult();
        await firstWorkflow;
        Assert.IsTrue(capturedOptions["A"].ChangeMacAddress);
        Assert.IsFalse(capturedOptions["A"].ChangeAndroidId);
        Assert.IsTrue(viewModel.WipeSelectedDevicesWithoutChangeCommand.CanExecute(null));

        Assert.IsFalse(capturedOptions["B"].ChangeMacAddress);
        Assert.IsTrue(capturedOptions["B"].ChangeAndroidId);
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
        context.DeviceActionGuard.OperationStateChanged += snapshot =>
        {
            string serial = snapshot.Serial;
            bool isBusy = snapshot.State != DeviceActionRuntimeState.Idle;
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
        Assert.AreEqual(
            DeviceActionKind.BatchRandomChangeAndWipe,
            context.DeviceActionGuard.GetOperation("A")!.Kind);
        Assert.AreEqual(DeviceActionKind.BatchRandomChangeAndWipe, viewModel.SelectedBatchActionKind);
        Assert.AreEqual(3, viewModel.SelectedBatchActionButtonRow);
        Assert.AreEqual(0, viewModel.SelectedBatchActionButtonColumn);

        viewModel.ToggleDeviceSelectionCommand.Execute(deviceB);
        viewModel.SelectedInfoDevice = deviceB;
        Assert.IsTrue(viewModel.RandomChangeAndWipeSelectedDevicesCommand.CanExecute(null));
        Assert.IsTrue(deviceA.IsActionBusy);
        _ = context.DeviceChange.Received(1).ChangeAsync(
            "A",
            Arg.Any<DeviceInfoApiDevice>(),
            Arg.Any<bool>(),
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());

        deviceAChangeCompletion.SetResult();
        await runningAction;

        Assert.AreEqual(1, deviceAGuardReleasedCount);
        Assert.IsFalse(deviceA.IsActionBusy);

        viewModel.ToggleDeviceSelectionCommand.Execute(deviceA);
        Assert.IsTrue(viewModel.RandomChangeAndWipeSelectedDevicesCommand.CanExecute(null));
        Task readyDeviceAction = viewModel.RandomChangeAndWipeSelectedDevicesCommand.ExecuteAsync(null);
        await deviceBChangeStarted.Task;
        Assert.AreEqual(2, randomInvocationCount);
        Assert.IsFalse(deviceA.IsActionBusy);
        await readyDeviceAction;
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
    public async Task RandomChangeAndWipeSelectedDevices_SelectedBusyStateTracksCoordinator()
    {
        var coordinator = new ControllableDeviceActionCoordinator();
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
            deviceActionCoordinator: coordinator);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = "Random profile" })));
        var deviceAStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deviceACompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
                _ => throw new InvalidOperationException("Unexpected device serial.")
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        Task runningAction = viewModel.ChangeSelectedDevicesCommand.ExecuteAsync(null);
        await deviceAStarted.Task;

        DeviceRowViewModel deviceA = viewModel.Devices.Single(device => device.Serial == "A");
        DeviceRowViewModel deviceB = viewModel.Devices.Single(device => device.Serial == "B");
        Assert.IsTrue(deviceA.IsActionBusy);
        coordinator.ForceRelease("A");
        Assert.IsFalse(deviceA.IsActionBusy);
        viewModel.ToggleDeviceSelectionCommand.Execute(deviceB);
        viewModel.SelectedInfoDevice = deviceB;
        Assert.IsTrue(viewModel.RandomChangeAndWipeSelectedDevicesCommand.CanExecute(null));

        Assert.IsFalse(deviceA.IsActionBusy);
        Assert.AreEqual(1, deviceAInvocationCount);
        _ = context.DeviceChange.Received(1).ChangeAsync(
            "A",
            Arg.Any<DeviceInfoApiDevice>(),
            Arg.Any<bool>(),
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());
        deviceACompletion.SetResult();
        await runningAction;
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
        Assert.IsTrue(viewModel.ChangeSelectedDevicesCommand.CanExecute(null));
        viewModel.DeviceInfo.Model = "Blocked edit";
        viewModel.ToggleDeviceSelectionCommand.Execute(deviceC);
        viewModel.SelectedInfoDevice = deviceC;
        Assert.IsTrue(viewModel.CanInteractWithSelectedInfoDevice);
        Assert.IsTrue(viewModel.ChangeSelectedDevicesCommand.CanExecute(null));

        completions["A"].SetResult();
        completions["B"].SetResult();
        await firstBatch;
        Assert.IsTrue(viewModel.ChangeSelectedDevicesCommand.CanExecute(null));

        Task secondBatch = viewModel.ChangeSelectedDevicesCommand.ExecuteAsync(null);
        await started["C"].Task;
        Assert.IsTrue(deviceC.IsActionBusy);
        completions["C"].SetResult();
        await secondBatch;

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
        Assert.IsTrue(viewModel.ChangeSelectedDevicesCommand.CanExecute(null));
        Assert.IsTrue(viewModel.ChangeSelectedDevicesWithoutWipeCommand.CanExecute(null));
        Assert.IsTrue(viewModel.WipeSelectedDevicesWithoutChangeCommand.CanExecute(null));
        Assert.IsTrue(viewModel.ChangeSelectedSimsCommand.CanExecute(null));
        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Assert.IsTrue(viewModel.RandomSelectedSimsCommand.CanExecute(null));

        DeviceRowViewModel offlineDevice = viewModel.Devices.Single(device => device.Serial == "A");
        viewModel.SelectedInfoDevice = offlineDevice;
        Assert.IsTrue(viewModel.ChangeSelectedDevicesCommand.CanExecute(null));
        completion.SetResult();
        await firstAction;
        viewModel.ToggleDeviceSelectionCommand.Execute(onlineDevice);
        Assert.IsFalse(viewModel.ChangeSelectedDevicesCommand.CanExecute(null));
        Assert.IsFalse(viewModel.ChangeSelectedDevicesWithoutWipeCommand.CanExecute(null));
        Assert.IsFalse(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Assert.IsFalse(viewModel.RandomSelectedSimsCommand.CanExecute(null));
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
        Assert.AreEqual(DeviceProcessState.Failed, offlineDevice.ProcessState);

        Assert.IsFalse(viewModel.ChangeSelectedDevicesCommand.CanExecute(null));
        Assert.IsFalse(viewModel.ChangeSelectedDevicesWithoutWipeCommand.CanExecute(null));
        Assert.IsFalse(viewModel.WipeSelectedDevicesWithoutChangeCommand.CanExecute(null));
        Assert.IsFalse(viewModel.ChangeSelectedSimsCommand.CanExecute(null));
        Assert.IsFalse(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Assert.IsFalse(viewModel.RandomSelectedSimsCommand.CanExecute(null));
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
        var deviceAReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.DeviceActionGuard.OperationStateChanged += snapshot =>
        {
            if (snapshot.Serial == "A" && snapshot.State == DeviceActionRuntimeState.Idle)
                deviceAReleased.TrySetResult();
        };
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
        Task randomBatch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await randomBatch;

        Task firstBatch = viewModel.ChangeSelectedDevicesCommand.ExecuteAsync(null);
        await Task.WhenAll(deviceBStarted.Task, deviceAReleased.Task);

        DeviceRowViewModel deviceA = viewModel.Devices.Single(device => device.Serial == "A");
        DeviceRowViewModel deviceB = viewModel.Devices.Single(device => device.Serial == "B");
        Assert.IsFalse(deviceA.IsActionBusy);
        Assert.IsTrue(deviceB.IsActionBusy);
        Assert.IsTrue(viewModel.ChangeSelectedDevicesCommand.CanExecute(null));

        using (IDeviceActionOperation reusableOperation =
               context.DeviceActionGuard.TryStart(
                   "A",
                   DeviceActionKind.BatchChangeSim,
                   canCancel: true)!)
        {
            Assert.IsNotNull(reusableOperation);
        }

        Task retryBatch = viewModel.ChangeSelectedDevicesCommand.ExecuteAsync(null);
        await retryBatch;
        Assert.IsTrue(deviceB.IsActionBusy);
        Assert.IsTrue(viewModel.ChangeSelectedDevicesCommand.CanExecute(null));

        deviceBCompletion.SetResult();
        await firstBatch;

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
        context.DeviceActionGuard.OperationStateChanged += snapshot =>
        {
            string serial = snapshot.Serial;
            bool isBusy = snapshot.State != DeviceActionRuntimeState.Idle;
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

        deviceBCompletion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile B" }));
        await firstBatch;
        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        viewModel.ToggleDeviceSelectionCommand.Execute(deviceB);
        await viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
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
        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Assert.IsTrue(deviceA.IsActionBusy);
        Assert.IsFalse(deviceB.IsActionBusy);

        deviceACompletion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile A" }));
        await firstBatch;
        Assert.IsFalse(deviceA.IsActionBusy);
        Assert.IsFalse(deviceB.IsActionBusy);

        viewModel.ToggleDeviceSelectionCommand.Execute(deviceA);
        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Task secondBatch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await deviceBStarted.Task;
        Assert.IsFalse(deviceBToken.IsCancellationRequested);
        Assert.IsTrue(deviceB.IsActionBusy);

        deviceBCompletion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile B" }));
        await secondBatch;
        Assert.IsFalse(deviceB.IsActionBusy);

        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task RandomBatchActions_OfflineSelectionIsNotRunnableUntilOnline()
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

        Assert.IsFalse(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Assert.IsFalse(viewModel.RandomSelectedSimsCommand.CanExecute(null));
        await context.RandomDevice.DidNotReceiveWithAnyArgs()
            .CreateRandomProfileAsync(default!, default);
        context.SimProfile.DidNotReceive().CreateRandomProfile(
            Arg.Any<CarrierCountryOption>(),
            Arg.Any<CarrierOption>());
        Assert.AreEqual("Log_Ready", viewModel.Devices[0].Process);
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

        firstCompletion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile A" }));
        secondCompletion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile B" }));
        await initialBatch;

        viewModel.ToggleDeviceSelectionCommand.Execute(deviceA);
        viewModel.ToggleDeviceSelectionCommand.Execute(deviceB);
        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Task newDeviceBatch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await thirdStarted.Task;
        Assert.IsTrue(deviceC.IsActionBusy);

        thirdCompletion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile C" }));
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
    public async Task RandomSelectedDevices_QueuesFifthTargetAtBatchThrottleLimit()
    {
        StoredDeviceConfig[] initialDevices = Enumerable.Range(1, 5)
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
        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Assert.AreEqual(5, invocationCount);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task StopRandomSelectedDevices_CancelsQueuedTargetBeforeItStarts()
    {
        StoredDeviceConfig[] devices = Enumerable.Range(1, 5)
            .Select(index => new StoredDeviceConfig { Serial = $"D{index}", Name = $"Device {index}" })
            .ToArray();
        TestContext context = CreateContext(
            CreateSnapshot(
                devices,
                devices.Select(device => new AdbDevice(device.Serial, AdbDeviceStatus.Online)).ToArray()),
            new AppSettings { SelectedMultipleDeviceSerials = devices.Select(device => device.Serial).ToList() });
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
            .Returns(callInfo =>
            {
                int index = Interlocked.Increment(ref invocationCount) - 1;
                CancellationToken token = callInfo.Arg<CancellationToken>();
                token.Register(() => completions[index].TrySetCanceled(token));
                started[index].TrySetResult();
                return completions[index].Task;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task batch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await Task.WhenAll(started.Select(source => source.Task));
        Assert.AreEqual(4, invocationCount);

        viewModel.SelectedInfoDevice = viewModel.Devices.Single(device => device.Serial == "D5");
        viewModel.StopSelectedDeviceActionCommand.Execute(null);
        foreach (TaskCompletionSource<RandomDeviceResult> completion in completions)
            completion.TrySetCanceled();
        await batch;

        Assert.AreEqual(4, invocationCount);
        DeviceRowViewModel queued = viewModel.Devices.Single(device => device.Serial == "D5");
        Assert.AreEqual("Log_RandomDeviceCanceled", queued.Process);
        Assert.AreEqual(DeviceProcessState.Canceled, queued.ProcessState);
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("D5"));
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task StopRandomSelectedDevices_PreservesAlreadyCompletedTargetSuccess()
    {
        StoredDeviceConfig[] devices =
        [
            new() { Serial = "A", Name = "Completed" },
            new() { Serial = "B", Name = "Running" }
        ];
        TestContext context = CreateContext(
            CreateSnapshot(
                devices,
                devices.Select(device => new AdbDevice(device.Serial, AdbDeviceStatus.Online)).ToArray()),
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var runningStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runningCompletion = new TaskCompletionSource<RandomDeviceResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int invocationCount = 0;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                if (Interlocked.Increment(ref invocationCount) == 1)
                {
                    return Task.FromResult(new RandomDeviceResult(
                        RandomDeviceStatus.Created,
                        new DeviceInfoApiDevice { Model = "Completed profile" }));
                }

                CancellationToken token = callInfo.Arg<CancellationToken>();
                token.Register(() => runningCompletion.TrySetCanceled(token));
                runningStarted.TrySetResult();
                return runningCompletion.Task;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task batch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await runningStarted.Task;

        DeviceRowViewModel completed = viewModel.Devices.Single(device => device.Serial == "A");
        Assert.AreEqual(DeviceProcessState.Succeeded, completed.ProcessState);
        viewModel.SelectedInfoDevice = viewModel.Devices.Single(device => device.Serial == "B");
        viewModel.StopSelectedDeviceActionCommand.Execute(null);
        await batch;

        Assert.AreEqual("Log_RandomDeviceSuccess", completed.Process);
        Assert.AreEqual(DeviceProcessState.Succeeded, completed.ProcessState);
        DeviceRowViewModel stopped = viewModel.Devices.Single(device => device.Serial == "B");
        Assert.AreEqual(DeviceProcessState.Canceled, stopped.ProcessState);
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
        context.DeviceActionGuard.OperationStateChanged += snapshot =>
        {
            string serial = snapshot.Serial;
            bool isBusy = snapshot.State != DeviceActionRuntimeState.Idle;
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
        Assert.IsFalse(viewModel.ViewRandomDeviceInfoCommand.CanExecute(null));
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
        Assert.IsFalse(viewModel.ViewRandomDeviceInfoCommand.CanExecute(null));

        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task MultipleView_DoesNotExposeRandomDeviceInfoDialog()
    {
        StoredDeviceConfig stored = new() { Serial = "A", Name = "Alpha" };
        TestContext context = CreateContext(
            CreateSnapshot([stored], [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = "Original", Serial = "A" })));
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        Assert.IsFalse(viewModel.ViewRandomDeviceInfoCommand.CanExecute(null));
        await viewModel.ViewRandomDeviceInfoCommand.ExecuteAsync(null);
        await context.RandomInfoDialog.DidNotReceiveWithAnyArgs()
            .ShowRandomDeviceInfoAsync(default!, default);

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
        using IDisposable busyLease = context.DeviceActionGuard.TryStart("A", DeviceActionKind.BatchChangeDevice, canCancel: true)!;
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

        Assert.AreEqual("Log_DeviceActionAlreadyRunningFormat", viewModel.Devices.Single(device => device.Serial == "A").Process);
        Assert.AreEqual("Log_RandomDeviceSuccess", viewModel.Devices.Single(device => device.Serial == "B").Process);
        Assert.AreEqual(
            DeviceProcessState.InProgress,
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
    public async Task PollRefresh_OnlineStatusChangeEnablesRandomCommands()
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

        Assert.IsFalse(viewModel.RandomSelectedDevicesCommand.CanExecute(null));
        Assert.IsFalse(viewModel.RandomSelectedSimsCommand.CanExecute(null));

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
        TestContext context = CreateContext(
            CreateSnapshot([stored], []),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
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
    public async Task AdvancedConfig_UsesAllSelectedOnlineDevicesAndFlushesPreset()
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
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<DeviceChangeOptions>(),
                Arg.Any<bool>(),
                true,
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
            Arg.Is<IReadOnlyList<string>>(serials =>
                serials.Count == 2 && serials[0] == "B" && serials[1] == "A"),
            Arg.Is<DeviceChangeOptions>(options => !options.UseDefaultMode),
            true,
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
    public async Task AdvancedConfig_IncludesSelectedDeviceRunningAnotherAction()
    {
        DeviceListSnapshot snapshot = CreateSnapshot(
            [
                new StoredDeviceConfig { Serial = "A", Name = "Alpha", Type = "sargo" },
                new StoredDeviceConfig { Serial = "B", Name = "Beta", Type = "starlte" }
            ],
            [
                new AdbDevice("A", AdbDeviceStatus.Online),
                new AdbDevice("B", AdbDeviceStatus.Online)
            ]);
        var settings = new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] };
        var configuration = new MultipleDeviceConfiguration
        {
            ChangeOptions = new DeviceChangeOptions { UseDefaultMode = false }
        };
        var coordinator = new DeviceActionCoordinatorService();
        using IDeviceActionOperation busyOperation = coordinator.TryStart(
            "B",
            DeviceActionKind.BatchChangeDevice,
            canCancel: true)!;
        TestContext context = CreateContext(
            snapshot,
            settings,
            configuration,
            deviceActionCoordinator: coordinator);
        context.AdvancedDialog.ShowAdvancedChangeConfigAsync(
                Arg.Is<IReadOnlyList<string>>(serials =>
                    serials.Count == 2 && serials[0] == "A" && serials[1] == "B"),
                Arg.Any<DeviceChangeOptions>(),
                Arg.Any<bool>(),
                true,
                Arg.Any<CancellationToken>())
            .Returns(new AdvancedChangeConfigDialogResult(
                new DeviceChangeOptions { UseDefaultMode = false },
                useIntegritySecurityPatch: true));
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.OpenAdvancedChangeConfigCommand.CanExecute(null));
        await viewModel.OpenAdvancedChangeConfigCommand.ExecuteAsync(null);

        await context.AdvancedDialog.Received(1).ShowAdvancedChangeConfigAsync(
            Arg.Is<IReadOnlyList<string>>(serials =>
                serials.Count == 2 && serials[0] == "A" && serials[1] == "B"),
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<bool>(),
            true,
            Arg.Any<CancellationToken>());
        await context.AdvancedDialog.DidNotReceive().ShowAdvancedChangeConfigAsync(
            Arg.Any<string>(),
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());

        busyOperation.Dispose();
        await viewModel.DeactivateAsync();
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

    [TestMethod]
    public async Task SuspendAsync_DoesNotCancelRunningBatchOrResetBatchPresentation()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Phone" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var workerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workerCompletion = new TaskCompletionSource<RandomDeviceResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken workerToken = default;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                workerToken = callInfo.Arg<CancellationToken>();
                workerToken.Register(() => workerCompletion.TrySetCanceled(workerToken));
                workerStarted.TrySetResult();
                return workerCompletion.Task;
            });
        await viewModel.InitializeAsync(CancellationToken.None);

        Task batch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await workerStarted.Task;
        Guid operationId = context.DeviceActionGuard.GetOperation("A")!.OperationId;
        await viewModel.SuspendAsync();

        DeviceActionOperationSnapshot operation = context.DeviceActionGuard.GetOperation("A")!;
        Assert.AreEqual(DeviceActionKind.BatchRandomDevice, viewModel.SelectedBatchActionKind);
        Assert.IsTrue(viewModel.HasActiveBatchActionButton);
        Assert.AreEqual(DeviceActionRuntimeState.Running, operation.State);
        Assert.AreEqual(DeviceActionCancellationReason.None, operation.CancellationReason);
        Assert.IsFalse(workerToken.IsCancellationRequested);
        Assert.IsTrue(context.DeviceActionGuard.IsBusy("A"));
        Assert.AreEqual("Log_RandomDevice", viewModel.Devices.Single().Process);

        await viewModel.InitializeAsync(CancellationToken.None);
        Assert.AreEqual(operationId, context.DeviceActionGuard.GetOperation("A")!.OperationId);
        Assert.AreEqual(DeviceActionKind.BatchRandomDevice, viewModel.SelectedBatchActionKind);
        Assert.IsTrue(viewModel.HasActiveBatchActionButton);
        Assert.IsTrue(viewModel.CanStopSelectedDeviceAction);
        Assert.IsFalse(workerToken.IsCancellationRequested);

        viewModel.StopSelectedDeviceActionCommand.Execute(null);
        await batch;
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task DeactivateAsync_CancelsAndAwaitsRunningBatchAsExternal()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Phone" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var workerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workerCompletion = new TaskCompletionSource<RandomDeviceResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken workerToken = default;
        DeviceActionOperationSnapshot? stoppingOperation = null;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                workerToken = callInfo.Arg<CancellationToken>();
                workerToken.Register(() => workerCompletion.TrySetCanceled(workerToken));
                workerStarted.TrySetResult();
                return workerCompletion.Task;
            });
        context.DeviceActionGuard.OperationStateChanged += snapshot =>
        {
            if (snapshot.Serial == "A" && snapshot.State == DeviceActionRuntimeState.Stopping)
                stoppingOperation = snapshot;
        };
        await viewModel.InitializeAsync(CancellationToken.None);

        Task batch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await workerStarted.Task;
        await viewModel.DeactivateAsync();
        await batch;

        Assert.IsTrue(workerToken.IsCancellationRequested);
        Assert.IsNotNull(stoppingOperation);
        Assert.AreEqual(DeviceActionCancellationReason.External, stoppingOperation.CancellationReason);
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("A"));
        Assert.AreEqual("Log_Ready", viewModel.Devices.Single().Process);
        Assert.AreEqual(DeviceProcessState.Ready, viewModel.Devices.Single().ProcessState);
    }

    [TestMethod]
    public async Task DeactivateAsync_CancelsAllConcurrentBatchWorkflowsAndReleasesTargets()
    {
        StoredDeviceConfig[] devices =
        [
            new() { Serial = "A", Name = "Phone A" },
            new() { Serial = "B", Name = "Phone B" }
        ];
        TestContext context = CreateContext(
            CreateSnapshot(
                devices,
                devices.Select(device => new AdbDevice(device.Serial, AdbDeviceStatus.Online)).ToArray()),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var started = new[]
        {
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var completions = new[]
        {
            new TaskCompletionSource<RandomDeviceResult>(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource<RandomDeviceResult>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var tokens = new CancellationToken[2];
        int invocationCount = 0;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                int index = Interlocked.Increment(ref invocationCount) - 1;
                tokens[index] = callInfo.Arg<CancellationToken>();
                tokens[index].Register(() => completions[index].TrySetCanceled(tokens[index]));
                started[index].TrySetResult();
                return completions[index].Task;
            });

        await viewModel.InitializeAsync(CancellationToken.None);
        Task firstWorkflow = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await started[0].Task;

        DeviceRowViewModel deviceB = viewModel.Devices.Single(device => device.Serial == "B");
        viewModel.ToggleDeviceSelectionCommand.Execute(deviceB);
        Task secondWorkflow = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await started[1].Task;

        await viewModel.DeactivateAsync();
        await Task.WhenAll(firstWorkflow, secondWorkflow);

        Assert.IsTrue(tokens[0].IsCancellationRequested);
        Assert.IsTrue(tokens[1].IsCancellationRequested);
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("A"));
        Assert.IsFalse(context.DeviceActionGuard.IsBusy("B"));
        Assert.AreEqual(DeviceProcessState.Ready, viewModel.Devices.Single(device => device.Serial == "A").ProcessState);
        Assert.AreEqual(DeviceProcessState.Ready, viewModel.Devices.Single(device => device.Serial == "B").ProcessState);
    }

    [TestMethod]
    public async Task SingleOwnedDeviceAndMultipleBatch_ShowStopOnlyForMultipleOwnedTargets()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "A", Name = "Alpha" },
            new() { Serial = "B", Name = "Beta" },
            new() { Serial = "C", Name = "Gamma" }
        ];
        DeviceListSnapshot snapshot = CreateSnapshot(
            storedDevices,
            [
                new AdbDevice("A", AdbDeviceStatus.Online),
                new AdbDevice("B", AdbDeviceStatus.Online),
                new AdbDevice("C", AdbDeviceStatus.Online)
            ]);
        var coordinator = new DeviceActionCoordinatorService();
        using IDeviceActionOperation singleOperation = coordinator.TryStart(
            "A",
            DeviceActionKind.ChangeDevice,
            canCancel: true)!;
        var processState = new DeviceProcessStateService();
        processState.SetProcess("A", "Changing A from Single", "Log_ChangeDevice");
        TestContext context = CreateContext(
            snapshot,
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B", "C"] },
            deviceActionCoordinator: coordinator,
            processState: processState);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var started = Enumerable.Range(0, 2)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var completions = Enumerable.Range(0, 2)
            .Select(_ => new TaskCompletionSource<RandomDeviceResult>(
                TaskCreationOptions.RunContinuationsAsynchronously))
            .ToArray();
        var batchStopping = new List<DeviceActionOperationSnapshot>();
        coordinator.OperationStateChanged += snapshotState =>
        {
            if (snapshotState.Serial is "B" or "C"
                && snapshotState.State == DeviceActionRuntimeState.Stopping)
            {
                lock (batchStopping)
                    batchStopping.Add(snapshotState);
            }
        };
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
        DeviceRowViewModel deviceA = viewModel.Devices.Single(device => device.Serial == "A");
        DeviceRowViewModel deviceB = viewModel.Devices.Single(device => device.Serial == "B");
        DeviceRowViewModel deviceC = viewModel.Devices.Single(device => device.Serial == "C");
        viewModel.SelectedInfoDevice = deviceB;

        Task batch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await Task.WhenAll(started.Select(source => source.Task));

        try
        {
        Guid singleOperationId = singleOperation.OperationId;
        Assert.AreEqual(2, invocationCount);
        Assert.AreEqual(DeviceActionKind.ChangeDevice, coordinator.GetOperation("A")!.Kind);
        Assert.AreEqual(DeviceActionKind.BatchRandomDevice, coordinator.GetOperation("B")!.Kind);
        Assert.AreEqual(DeviceActionKind.BatchRandomDevice, coordinator.GetOperation("C")!.Kind);
        Assert.IsTrue(viewModel.CanStopSelectedDeviceAction);

        viewModel.SelectedInfoDevice = deviceB;
        Assert.IsTrue(viewModel.IsSelectedInfoDeviceActiveBatchTarget);
        Assert.IsTrue(viewModel.HasSelectedInfoDeviceBatchStopButton);
        Assert.IsTrue(viewModel.ShowSelectedDeviceBatchStop);
        Assert.IsFalse(viewModel.HasExternalSelectedDeviceAction);

        viewModel.SelectedInfoDevice = deviceC;
        Assert.IsTrue(viewModel.IsSelectedInfoDeviceActiveBatchTarget);
        Assert.IsTrue(viewModel.ShowSelectedDeviceBatchStop);

        viewModel.SelectedInfoDevice = deviceA;
        Assert.IsFalse(viewModel.IsSelectedInfoDeviceActiveBatchTarget);
        Assert.IsFalse(viewModel.HasSelectedInfoDeviceBatchStopButton);
        Assert.IsFalse(viewModel.ShowSelectedDeviceBatchStop);
        Assert.IsFalse(viewModel.HasActiveBatchActionButton);
        Assert.IsTrue(viewModel.HasExternalSelectedDeviceAction);
        Assert.AreEqual(DeviceActionKind.ChangeDevice, viewModel.DisplayedSelectedDeviceActionKind);
        StringAssert.Contains(viewModel.ExternalSelectedDeviceActionText, "Single Device");
        Assert.AreEqual("Log_DeviceActionAlreadyRunningFormat", deviceA.Process);

        viewModel.SelectedInfoDevice = deviceB;
        viewModel.StopSelectedDeviceActionCommand.Execute(null);
        Assert.AreEqual(DeviceActionCancellationReason.None, coordinator.GetOperation("A")!.CancellationReason);
        Assert.AreEqual(singleOperationId, coordinator.GetOperation("A")!.OperationId);
        Assert.AreEqual(DeviceActionRuntimeState.Running, coordinator.GetOperation("A")!.State);
        lock (batchStopping)
        {
            Assert.IsTrue(batchStopping.Any(operation =>
                operation.Serial == "B"
                && operation.CancellationReason == DeviceActionCancellationReason.UserStop));
            Assert.IsFalse(batchStopping.Any(operation =>
                operation.Serial == "C"
                && operation.CancellationReason == DeviceActionCancellationReason.UserStop));
        }

        completions[0].TrySetCanceled();
        completions[1].TrySetCanceled();
        await batch;
        Assert.IsFalse(coordinator.IsBusy("B"));
        Assert.IsFalse(coordinator.IsBusy("C"));
        Assert.IsTrue(coordinator.IsBusy("A"));
        viewModel.SelectedInfoDevice = deviceA;
        Assert.IsTrue(viewModel.HasExternalSelectedDeviceAction);
        Assert.AreEqual(DeviceActionRuntimeState.Running, coordinator.GetOperation("A")!.State);
        Assert.AreEqual(DeviceActionCancellationReason.None, coordinator.GetOperation("A")!.CancellationReason);
        await viewModel.DeactivateAsync();
        }
        finally
        {
            completions[0].TrySetCanceled();
            completions[1].TrySetCanceled();
            try
            {
                await batch;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [TestMethod]
    public async Task SkippedSingleOwnedDeviceDoesNotJoinBatchAfterItCompletes()
    {
        DeviceListSnapshot snapshot = CreateSnapshot(
            [
                new StoredDeviceConfig { Serial = "A", Name = "Alpha" },
                new StoredDeviceConfig { Serial = "B", Name = "Beta" }
            ],
            [
                new AdbDevice("A", AdbDeviceStatus.Online),
                new AdbDevice("B", AdbDeviceStatus.Online)
            ]);
        var coordinator = new DeviceActionCoordinatorService();
        using IDeviceActionOperation singleOperation = coordinator.TryStart(
            "A",
            DeviceActionKind.ChangeDevice,
            canCancel: true)!;
        var processState = new DeviceProcessStateService();
        processState.SetProcess("A", "Changing A from Single", "Log_ChangeDevice");
        TestContext context = CreateContext(
            snapshot,
            new AppSettings { SelectedMultipleDeviceSerials = ["A", "B"] },
            deviceActionCoordinator: coordinator,
            processState: processState);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var batchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var batchCompletion = new TaskCompletionSource<RandomDeviceResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => StartRandom(batchStarted, batchCompletion.Task));
        await viewModel.InitializeAsync(CancellationToken.None);
        DeviceRowViewModel deviceA = viewModel.Devices.Single(device => device.Serial == "A");
        DeviceRowViewModel deviceB = viewModel.Devices.Single(device => device.Serial == "B");
        viewModel.SelectedInfoDevice = deviceB;

        Task batch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        Task startedSignal = await Task.WhenAny(batchStarted.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.AreSame(
            batchStarted.Task,
            startedSignal,
            $"Batch did not start. selected={string.Join(',', viewModel.SelectedDevices.Select(device => device.Serial))}; "
            + $"canExecute={viewModel.RandomSelectedDevicesCommand.CanExecute(null)}; "
            + $"batchCompleted={batch.IsCompleted}");
        Assert.IsTrue(viewModel.IsSelectedInfoDeviceActiveBatchTarget);
        Assert.AreEqual(DeviceActionKind.ChangeDevice, coordinator.GetOperation("A")!.Kind);
        Assert.AreEqual(DeviceActionKind.BatchRandomDevice, coordinator.GetOperation("B")!.Kind);

        singleOperation.Dispose();
        viewModel.SelectedInfoDevice = deviceA;
        Assert.IsFalse(viewModel.IsSelectedInfoDeviceActiveBatchTarget);
        Assert.IsFalse(viewModel.ShowSelectedDeviceBatchStop);
        Assert.AreEqual("Log_Ready", deviceA.Process);

        batchCompletion.TrySetCanceled();
        await batch;
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task CompletedBatchTargetLosesStopOverlayWhileAnotherTargetRuns()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [
                    new StoredDeviceConfig { Serial = "B", Name = "Beta" },
                    new StoredDeviceConfig { Serial = "C", Name = "Gamma" }
                ],
                [
                    new AdbDevice("B", AdbDeviceStatus.Online),
                    new AdbDevice("C", AdbDeviceStatus.Online)
                ]),
            new AppSettings { SelectedMultipleDeviceSerials = ["B", "C"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var bStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bCompletion = new TaskCompletionSource<RandomDeviceResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cCompletion = new TaskCompletionSource<RandomDeviceResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var bIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.DeviceActionGuard.OperationStateChanged += snapshot =>
        {
            if (snapshot.Serial == "B" && snapshot.State == DeviceActionRuntimeState.Idle)
                bIdle.TrySetResult();
        };
        int invocationCount = 0;
        context.RandomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref invocationCount) switch
            {
                1 => StartRandom(bStarted, bCompletion.Task),
                2 => StartRandom(cStarted, cCompletion.Task),
                _ => throw new InvalidOperationException("Unexpected Random Device invocation.")
            });
        await viewModel.InitializeAsync(CancellationToken.None);
        DeviceRowViewModel deviceB = viewModel.Devices.Single(device => device.Serial == "B");
        DeviceRowViewModel deviceC = viewModel.Devices.Single(device => device.Serial == "C");
        viewModel.SelectedInfoDevice = deviceB;

        Task batch = viewModel.RandomSelectedDevicesCommand.ExecuteAsync(null);
        await Task.WhenAll(bStarted.Task, cStarted.Task);
        Assert.IsTrue(viewModel.ShowSelectedDeviceBatchStop);

        bCompletion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Completed B", Serial = "B" }));
        Task completionSignal = await Task.WhenAny(
            bIdle.Task,
            Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.AreSame(bIdle.Task, completionSignal, "B did not leave the active batch target set.");

        viewModel.SelectedInfoDevice = deviceB;
        Assert.IsFalse(viewModel.IsSelectedInfoDeviceActiveBatchTarget);
        Assert.IsFalse(viewModel.ShowSelectedDeviceBatchStop);
        Assert.IsFalse(viewModel.CanStopSelectedDeviceAction);

        viewModel.SelectedInfoDevice = deviceC;
        Assert.IsTrue(viewModel.IsSelectedInfoDeviceActiveBatchTarget);
        Assert.IsTrue(viewModel.ShowSelectedDeviceBatchStop);
        Assert.IsTrue(viewModel.CanStopSelectedDeviceAction);

        viewModel.StopSelectedDeviceActionCommand.Execute(null);
        cCompletion.TrySetCanceled();
        await batch;
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task SingleOwnedAction_IsReadOnlyInMultipleAndCompletesWhileSuspended()
    {
        TestContext context = CreateContext(
            CreateSnapshot(
                [new StoredDeviceConfig { Serial = "A", Name = "Phone" }],
                [new AdbDevice("A", AdbDeviceStatus.Online)]),
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] });
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        await viewModel.InitializeAsync(CancellationToken.None);

        using IDeviceActionOperation operation = context.DeviceActionGuard.TryStart(
            "A",
            DeviceActionKind.ChangeDevice,
            canCancel: true)!;
        context.ProcessState.SetProcess("A", "Changing from Single", "Log_ChangeDevice");

        Assert.IsTrue(viewModel.Devices.Single().IsActionBusy);
        Assert.AreEqual(DeviceActionKind.ChangeDevice, viewModel.DisplayedSelectedDeviceActionKind);
        Assert.IsTrue(viewModel.HasExternalSelectedDeviceAction);
        StringAssert.Contains(viewModel.ExternalSelectedDeviceActionText, "Single Device");
        Assert.AreEqual("Changing from Single", viewModel.Devices.Single().Process);
        Assert.IsFalse(viewModel.HasActiveBatchActionButton);
        Assert.IsTrue(viewModel.RandomSelectedDevicesCommand.CanExecute(null));

        await viewModel.SuspendAsync();
        Assert.AreEqual(DeviceActionCancellationReason.None, operation.CancellationReason);
        context.ProcessState.SetProcess("A", "Changed successfully", "Log_ChangeDeviceSuccess");
        operation.Dispose();

        Assert.AreEqual("Changed successfully", viewModel.Devices.Single().Process);
        Assert.AreEqual(DeviceProcessState.Succeeded, viewModel.Devices.Single().ProcessState);
        Assert.IsFalse(viewModel.Devices.Single().IsActionBusy);
        Assert.IsFalse(viewModel.HasExternalSelectedDeviceAction);
        await viewModel.DeactivateAsync();
    }

    [TestMethod]
    public async Task SharedProcessState_ProjectsSameLogAndTerminalStateIntoBothScreens()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "A", Name = "Phone", Type = "Phone" }
        ];
        var snapshot = CreateSnapshot(
            storedDevices,
            [new AdbDevice("A", AdbDeviceStatus.Online)]);
        var processState = new DeviceProcessStateService();
        var coordinator = new DeviceActionCoordinatorService();
        TestContext multipleContext = CreateContext(
            snapshot,
            new AppSettings { SelectedMultipleDeviceSerials = ["A"] },
            deviceActionCoordinator: coordinator,
            processState: processState);
        using ChangeMultipleDevicesViewModel multiple = multipleContext.ViewModel;

        IDeviceListService singleDeviceList = Substitute.For<IDeviceListService>();
        singleDeviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        singleDeviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>()).Returns(snapshot);
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        using ChangeSingleDeviceViewModel single =
            ChangeSingleDeviceViewModelLifecycleTests.CreateViewModel(
                singleDeviceList,
                carriers,
                deviceActionCoordinator: coordinator,
                processState: processState);

        processState.SetProcess("A", "Changing on both screens", "Log_ChangeDevice");
        await single.InitializeAsync(CancellationToken.None);
        await multiple.InitializeAsync(CancellationToken.None);

        Assert.AreEqual("Changing on both screens", single.Devices.Single().Process);
        Assert.AreEqual("Changing on both screens", multiple.Devices.Single().Process);
        Assert.AreEqual(DeviceProcessState.InProgress, single.Devices.Single().ProcessState);
        Assert.AreEqual(DeviceProcessState.InProgress, multiple.Devices.Single().ProcessState);

        processState.SetProcess("A", "Success on both screens", "Log_ChangeDeviceSuccess");
        Assert.AreEqual("Success on both screens", single.Devices.Single().Process);
        Assert.AreEqual("Success on both screens", multiple.Devices.Single().Process);
        Assert.AreEqual(DeviceProcessState.Succeeded, single.Devices.Single().ProcessState);
        Assert.AreEqual(DeviceProcessState.Succeeded, multiple.Devices.Single().ProcessState);

        await single.DeactivateAsync();
        await multiple.DeactivateAsync();
    }

    private static TestContext CreateContext(
        DeviceListSnapshot snapshot,
        AppSettings? settings = null,
        MultipleDeviceConfiguration? configuration = null,
        IDeviceActionCoordinatorService? deviceActionCoordinator = null,
        ILogger<ChangeMultipleDevicesViewModel>? logger = null,
        IDeviceProcessStateService? processState = null)
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
        IInstallPackageDialogService installPackageDialog = Substitute.For<IInstallPackageDialogService>();
        IPackageInstallService packageInstall = Substitute.For<IPackageInstallService>();
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
        deviceActionCoordinator ??= new DeviceActionCoordinatorService();
        processState ??= new DeviceProcessStateService();
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
                "InstallPackage_BatchDeviceInfo" => "Install packages on {0} devices",
                "Log_InstallPackageCompleteFormat" => "complete {0}/{1}",
                "Log_InstallPackagePartialFormat" => "partial {0}/{1}",
                "Log_InstallPackageFailedFormat" => "failed {0}/{1}",
                "Log_InstallPackageAdbFailureCodeFormat" => "ADB failed: {0}",
                "ChangeMultipleDevices_ExternalActionRunningFormat" => "{0} • Running in Single Device",
                "ChangeMultipleDevices_ExternalActionStoppingFormat" => "{0} • Stopping in Single Device",
                "DeviceAction_Name_RandomDevice" => "Random Device",
                "DeviceAction_Name_ChangeDevice" => "Change & Wipe Device",
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
            deviceActionCoordinator,
            processState,
            localization,
            multipleConfig,
            randomInfoDialog,
            randomDevice,
            simProfile,
            settingsService,
            new ImmediateDispatcherService(),
            polling,
            sharedSettings,
            logger ?? NullLogger<ChangeMultipleDevicesViewModel>.Instance,
            deviceAction,
            locationService,
            timezoneService,
            locationDialog,
            timezoneDialog,
            installPackageDialog,
            packageInstall);
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
            deviceActionCoordinator,
            processState,
            deviceAction,
            locationService,
            timezoneService,
            locationDialog,
            timezoneDialog,
            installPackageDialog,
            packageInstall);
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
        IDeviceActionCoordinatorService DeviceActionGuard,
        IDeviceProcessStateService ProcessState,
        IDeviceActionService DeviceAction,
        IDeviceLocationService LocationService,
        IDeviceTimezoneService TimezoneService,
        IChangeLocationDialogService LocationDialog,
        IChangeTimezoneDialogService TimezoneDialog,
        IInstallPackageDialogService InstallPackageDialog,
        IPackageInstallService PackageInstall);

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

    private sealed class ControllableDeviceActionCoordinator : IDeviceActionCoordinatorService
    {
        private readonly IDeviceActionCoordinatorService _inner = new DeviceActionCoordinatorService();
        private readonly Dictionary<string, IDeviceActionOperation> _manualOperations =
            new(StringComparer.OrdinalIgnoreCase);

        public event Action<DeviceActionOperationSnapshot>? OperationStateChanged
        {
            add => _inner.OperationStateChanged += value;
            remove => _inner.OperationStateChanged -= value;
        }

        public bool IsBusy(string serial) => _inner.IsBusy(serial);

        public DeviceActionOperationSnapshot? GetOperation(string serial) => _inner.GetOperation(serial);

        public IReadOnlyList<DeviceActionSessionSnapshot> GetActiveSessions() => _inner.GetActiveSessions();

        public IDeviceActionOperation? TryStart(
            string serial,
            DeviceActionKind kind,
            bool canCancel,
            CancellationToken externalCancellationToken = default,
            Guid? sessionId = null)
        {
            IDeviceActionOperation? operation = _inner.TryStart(
                serial,
                kind,
                canCancel,
                externalCancellationToken,
                sessionId);
            if (operation != null)
            {
                lock (_manualOperations)
                    _manualOperations[serial] = operation;
            }

            return operation;
        }

        public bool TryRequestCancellation(string serial) => _inner.TryRequestCancellation(serial);

        public bool TryRequestSessionCancellation(Guid sessionId) => _inner.TryRequestSessionCancellation(sessionId);

        public void ForceRelease(string serial)
        {
            IDeviceActionOperation? operation;
            lock (_manualOperations)
            {
                _manualOperations.Remove(serial, out operation);
            }

            operation?.Dispose();
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
