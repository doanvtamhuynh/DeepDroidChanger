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
            []);
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
            CreateSnapshot([stored], []),
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
                []),
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
            []));

        Assert.AreEqual("Log_ActionAlreadyInProgress", viewModel.Devices.Single(device => device.Serial == "A").Process);
        Assert.AreEqual("Log_RandomDeviceSuccess", viewModel.Devices.Single(device => device.Serial == "B").Process);
        Assert.AreEqual(
            DeviceProcessState.Failed,
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
        MultipleDeviceConfiguration? configuration = null)
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
        carrierData.GetCarrierProfilesAsync(Arg.Any<CancellationToken>())
            .Returns(
            [
                new CarrierProfile("us", "1", "United States", "AT&T", "310", "410"),
                new CarrierProfile("vn", "84", "Vietnam", "Viettel", "452", "04")
            ]);
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        IDeviceActionGuardService deviceActionGuard = new DeviceActionGuardService();
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
            NullLogger<ChangeMultipleDevicesViewModel>.Instance);
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
            deviceActionGuard);
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
        IDeviceActionGuardService DeviceActionGuard);

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
