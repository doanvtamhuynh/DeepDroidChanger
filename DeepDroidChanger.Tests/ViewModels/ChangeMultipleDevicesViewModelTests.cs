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
    public async Task SaveMultipleDeviceColumnRatios_UpdatesOnlyMultipleLayout()
    {
        var settings = new AppSettings();
        Dictionary<string, double> singleBefore = settings.SingleDeviceTableColumnRatios;
        TestContext context = CreateContext(CreateSnapshot([], []), settings);
        using ChangeMultipleDevicesViewModel viewModel = context.ViewModel;
        var ratios = new Dictionary<string, double>
        {
            ["Name"] = 0.4,
            ["Process"] = 0.6
        };

        await viewModel.SaveMultipleDeviceColumnRatiosCommand.ExecuteAsync(ratios);

        Assert.AreSame(singleBefore, settings.SingleDeviceTableColumnRatios);
        Assert.AreSame(
            settings.MultipleDeviceTableColumnRatios,
            viewModel.MultipleDeviceTableColumnRatios);
        Assert.AreEqual(0.4, viewModel.MultipleDeviceTableColumnRatios["Name"]);
        await context.SettingsService.Received(1).SaveAsync(
            settings,
            Arg.Any<CancellationToken>());
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
        carrierData.GetCarrierProfilesAsync(Arg.Any<CancellationToken>())
            .Returns(
            [
                new CarrierProfile("us", "1", "United States", "AT&T", "310", "410"),
                new CarrierProfile("vn", "84", "Vietnam", "Viettel", "452", "04")
            ]);
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
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
            deviceConfig,
            deviceList,
            localization,
            multipleConfig,
            settingsService,
            new ImmediateDispatcherService(),
            polling,
            sharedSettings,
            NullLogger<ChangeMultipleDevicesViewModel>.Instance);
        return new TestContext(
            viewModel,
            addDialog,
            advancedDialog,
            deviceConfig,
            deviceList,
            multipleConfig,
            settingsService,
            polling);
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
        IDeviceConfigService DeviceConfig,
        IDeviceListService DeviceList,
        IMultipleDeviceConfigService MultipleConfig,
        ISettingsService SettingsService,
        BlockingPollingService Polling);

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
