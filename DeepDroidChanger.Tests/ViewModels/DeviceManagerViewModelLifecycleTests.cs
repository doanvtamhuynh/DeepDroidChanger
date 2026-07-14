using DeepDroidChanger.Constants;
using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.ViewModels;

[TestClass]
public sealed class DeviceManagerViewModelLifecycleTests
{
    [TestMethod]
    public async Task InitializeDeactivateInitialize_RestartsPollingLifecycleSafely()
    {
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns([]);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot([], []));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        var viewModel = CreateViewModel(deviceList, carriers);

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.DeactivateAsync();
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.DeactivateAsync();

        await deviceList.Received(2).LoadStoredDevicesAsync(Arg.Any<CancellationToken>());
        await deviceList.Received(2).LoadSnapshotAsync(Arg.Any<CancellationToken>());
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task SelectedDevice_SelectsExactlyOneVisibleRow()
    {
        var storedDevices = new[]
        {
            new StoredDeviceConfig { Serial = "A", Name = "First", Type = "Phone" },
            new StoredDeviceConfig { Serial = "B", Name = "Second", Type = "Phone" }
        };
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, []));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        var viewModel = CreateViewModel(deviceList, carriers);
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.SelectedDevice = viewModel.Devices[1];

        Assert.IsFalse(viewModel.Devices[0].IsSelected);
        Assert.IsTrue(viewModel.Devices[1].IsSelected);
        Assert.AreSame(viewModel.Devices[1], viewModel.SelectedDevice);
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task SelectedDeviceFilter_Online_ShowsOnlyConnectedRows()
    {
        var storedDevices = new[]
        {
            new StoredDeviceConfig { Serial = "A", Name = "Online", Type = "Phone" },
            new StoredDeviceConfig { Serial = "B", Name = "Offline", Type = "Phone" }
        };
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, [new AdbDevice("A", AdbDeviceStatus.Online)]));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        var viewModel = CreateViewModel(deviceList, carriers);
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.SelectedDeviceFilter = "Online";

        Assert.HasCount(1, viewModel.Devices);
        Assert.AreEqual("A", viewModel.Devices[0].Serial);
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task PollingWithUnchangedSelection_DoesNotSaveSettingsAgain()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "A", Name = "Phone", Type = "Phone" }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, [new AdbDevice("A", AdbDeviceStatus.Online)]));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        Func<CancellationToken, Task>? pollOperation = null;
        IPollingService polling = Substitute.For<IPollingService>();
        polling.RunAsync(
                Arg.Any<TimeSpan>(),
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                pollOperation = callInfo.ArgAt<Func<CancellationToken, Task>>(1);
                return Task.CompletedTask;
            });
        var viewModel = CreateViewModel(deviceList, carriers, deviceConfig: deviceConfig, polling: polling);

        await viewModel.InitializeAsync(CancellationToken.None);
        deviceConfig.ClearReceivedCalls();

        Assert.IsNotNull(pollOperation);
        await pollOperation(CancellationToken.None);

        await deviceConfig.DidNotReceive().SaveSettingsAsync(Arg.Any<CancellationToken>());
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task ReplacingRows_UnsubscribesOldRowsBeforeClearingCollection()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "A", Name = "Original", Type = "Phone" }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, []));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        var viewModel = CreateViewModel(deviceList, carriers, deviceConfig: deviceConfig);
        await viewModel.InitializeAsync(CancellationToken.None);
        DeviceRowViewModel oldRow = viewModel.Devices[0];

        viewModel.ApplyDeviceListSnapshot(new DeviceListSnapshot(
            [new StoredDeviceConfig { Serial = "A", Name = "Replacement", Type = "Phone" }],
            []));
        deviceConfig.ClearReceivedCalls();
        oldRow.Name = "Detached row";
        await viewModel.DeactivateAsync();

        await deviceConfig.DidNotReceiveWithAnyArgs().SaveDeviceRowAsync(
            default!, default!, default!, default!, default, default, default, default);
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task NameEdits_AreDebouncedAndPersistOnlyLatestValue()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "A", Name = "Original", Type = "Phone" }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, []));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        var saveObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        deviceConfig.SaveDeviceRowAsync(
                Arg.Any<IList<StoredDeviceConfig>>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CarrierCountryOption?>(),
                Arg.Any<CarrierOption?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                saveObserved.TrySetResult();
                return true;
            });
        var viewModel = CreateViewModel(deviceList, carriers, deviceConfig: deviceConfig);
        await viewModel.InitializeAsync(CancellationToken.None);
        deviceConfig.ClearReceivedCalls();

        viewModel.Devices[0].Name = "First";
        viewModel.Devices[0].Name = "Second";
        viewModel.Devices[0].Name = "Final";
        await saveObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await deviceConfig.Received(1).SaveDeviceRowAsync(
            Arg.Any<IList<StoredDeviceConfig>>(),
            "A",
            "Final",
            Arg.Any<string>(),
            Arg.Any<CarrierCountryOption?>(),
            Arg.Any<CarrierOption?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task DeactivateAsync_FlushesPendingNameEditImmediately()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "A", Name = "Original", Type = "Phone" }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, []));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        deviceConfig.SaveDeviceRowAsync(
                Arg.Any<IList<StoredDeviceConfig>>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CarrierCountryOption?>(),
                Arg.Any<CarrierOption?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var viewModel = CreateViewModel(deviceList, carriers, deviceConfig: deviceConfig);
        await viewModel.InitializeAsync(CancellationToken.None);
        deviceConfig.ClearReceivedCalls();

        viewModel.Devices[0].Name = "Flush me";
        await viewModel.DeactivateAsync();

        await deviceConfig.Received(1).SaveDeviceRowAsync(
            Arg.Any<IList<StoredDeviceConfig>>(),
            "A",
            "Flush me",
            Arg.Any<string>(),
            Arg.Any<CarrierCountryOption?>(),
            Arg.Any<CarrierOption?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task InitializeAsync_RestoresOnlyAllowedMainConfigForSelectedDevice()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new()
            {
                Serial = "A",
                Name = "Phone",
                Type = "Phone",
                Brand = "Samsung",
                AndroidVersion = "Android 15",
                ChangeSimEnabled = false,
                CountryIso = "vn",
                CountryName = "Vietnam",
                Carrier = "Viettel",
                CarrierMcc = "452",
                CarrierMnc = "04"
            }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, []));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns(
            [new CarrierProfile("vn", "84", "Vietnam", "Viettel", "452", "04")]);
        var viewModel = CreateViewModel(deviceList, carriers);

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.AreEqual("Samsung", viewModel.SelectedBrand);
        Assert.AreEqual("Android 15", viewModel.SelectedAndroidVersion);
        Assert.IsFalse(viewModel.IsChangeSimEnabled);
        Assert.AreEqual("Vietnam (VN)", viewModel.SelectedCountry?.DisplayName);
        Assert.AreEqual("Viettel (MCC 452 / MNC 04)", viewModel.SelectedCarrier?.DisplayName);
        Assert.AreEqual(string.Empty, viewModel.DeviceInfo.Name);
        Assert.AreEqual(string.Empty, viewModel.DeviceInfo.Model);
        Assert.AreEqual(string.Empty, viewModel.DeviceInfo.Serial);
        Assert.AreEqual(string.Empty, viewModel.DeviceInfo.Imei);
        Assert.AreEqual(string.Empty, viewModel.DeviceInfo.Iccid);
        Assert.AreEqual(string.Empty, viewModel.DeviceInfo.Imsi);
        Assert.AreEqual(string.Empty, viewModel.DeviceInfo.Operator);
        Assert.AreEqual(string.Empty, viewModel.DeviceInfo.PhoneNumber);
        Assert.AreEqual(string.Empty, viewModel.DeviceInfo.Mac);
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task DeactivateAsync_FlushesLatestProfileChangesForEachEditedDevice()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "A", Name = "First", Type = "Phone" },
            new() { Serial = "B", Name = "Second", Type = "Phone" }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, []));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        var viewModel = CreateViewModel(deviceList, carriers, deviceConfig: deviceConfig);
        await viewModel.InitializeAsync(CancellationToken.None);
        deviceConfig.ClearReceivedCalls();

        viewModel.SelectedBrand = "Samsung";
        viewModel.SelectedDevice = viewModel.Devices[1];
        viewModel.SelectedBrand = "Google";
        await viewModel.DeactivateAsync();

        await deviceConfig.Received(1).SaveDeviceProfileAsync(
            Arg.Any<IList<StoredDeviceConfig>>(),
            "A",
            Arg.Is<DeviceProfileConfig>(profile => profile.Brand == "Samsung"),
            Arg.Any<CancellationToken>());
        await deviceConfig.Received(1).SaveDeviceProfileAsync(
            Arg.Any<IList<StoredDeviceConfig>>(),
            "B",
            Arg.Is<DeviceProfileConfig>(profile => profile.Brand == "Google"),
            Arg.Any<CancellationToken>());
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task RandomDevice_UpdatesSessionFormWithoutPersistingGeneratedValues()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "A", Name = "Phone", Type = "Phone", Brand = "Samsung" }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, [new AdbDevice("A", AdbDeviceStatus.Online)]));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();
        randomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice
                {
                    Name = "Generated device",
                    Model = "SM-S928B",
                    Serial = "GENERATED-SERIAL",
                    Imei = "123456789012345",
                    Iccid = "8984041234567890123",
                    Imsi = "452041234567890",
                    SimOperatorName = "Viettel",
                    SimPhoneNumber = "+84901234567",
                    WifiMacAddress = "00:11:22:33:44:55"
                }));
        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            deviceConfig: deviceConfig,
            randomDevice: randomDevice);
        await viewModel.InitializeAsync(CancellationToken.None);
        deviceConfig.ClearReceivedCalls();

        await viewModel.RandomDeviceCommand.ExecuteAsync(null);
        await viewModel.DeactivateAsync();

        Assert.AreEqual("Generated device", viewModel.DeviceInfo.Name);
        Assert.AreEqual("SM-S928B", viewModel.DeviceInfo.Model);
        Assert.AreEqual("GENERATED-SERIAL", viewModel.DeviceInfo.Serial);
        Assert.AreEqual("123456789012345", viewModel.DeviceInfo.Imei);
        Assert.AreEqual("8984041234567890123", viewModel.DeviceInfo.Iccid);
        Assert.AreEqual("452041234567890", viewModel.DeviceInfo.Imsi);
        Assert.AreEqual("Viettel", viewModel.DeviceInfo.Operator);
        Assert.AreEqual("+84901234567", viewModel.DeviceInfo.PhoneNumber);
        Assert.AreEqual("00:11:22:33:44:55", viewModel.DeviceInfo.Mac);
        await deviceConfig.DidNotReceiveWithAnyArgs().SaveDeviceProfileAsync(
            default!, default!, default!, default);
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task SaveColumnRatios_RefreshesBindingAndPersistsSettings()
    {
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        var settings = new AppSettings();
        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            deviceConfig: deviceConfig,
            settings: settings);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        var ratios = new Dictionary<string, double>
        {
            [DeviceTableColumnSettings.Name] = 0.4,
            [DeviceTableColumnSettings.Process] = 0.6
        };

        await viewModel.SaveColumnRatiosCommand.ExecuteAsync(ratios);

        Assert.AreSame(settings.DeviceTableColumnRatios, viewModel.DeviceTableColumnRatios);
        Assert.AreEqual(0.4, viewModel.DeviceTableColumnRatios[DeviceTableColumnSettings.Name]);
        Assert.AreEqual(0.6, viewModel.DeviceTableColumnRatios[DeviceTableColumnSettings.Process]);
        Assert.Contains(nameof(DeviceManagerViewModel.DeviceTableColumnRatios), changedProperties);
        await deviceConfig.Received(1).SaveSettingsAsync(Arg.Any<CancellationToken>());
        viewModel.Dispose();
    }

    private static DeviceManagerViewModel CreateViewModel(
        IDeviceListService deviceList,
        ICarrierDataService carriers,
        IDeviceConfigService? deviceConfig = null,
        IPollingService? polling = null,
        IRandomDeviceService? randomDevice = null,
        AppSettings? settings = null)
    {
        return new DeviceManagerViewModel(
            Substitute.For<IAddDevicesDialogService>(),
            carriers,
            Substitute.For<IChangeTimezoneDialogService>(),
            Substitute.For<IDeviceTimezoneService>(),
            Substitute.For<IChangeLocationDialogService>(),
            Substitute.For<IDeviceLocationService>(),
            Substitute.For<IFakeProxyDialogService>(),
            Substitute.For<IProxyService>(),
            Substitute.For<IProxyWorkflowService>(),
            Substitute.For<IUpdateIntegrityDialogService>(),
            Substitute.For<IDeviceIntegrityService>(),
            Substitute.For<IInstallPackageDialogService>(),
            Substitute.For<IDeviceViewerDialogService>(),
            Substitute.For<IDeleteDeviceConfirmationDialogService>(),
            deviceList,
            new DeviceSelectionService(),
            deviceConfig ?? Substitute.For<IDeviceConfigService>(),
            randomDevice ?? Substitute.For<IRandomDeviceService>(),
            Substitute.For<IDeviceActionService>(),
            CreateLocalizationService(),
            settings ?? new AppSettings(),
            new ImmediateDispatcherService(),
            polling ?? new PollingService(),
            NullLogger<DeviceManagerViewModel>.Instance);
    }

    private static ILocalizationService CreateLocalizationService()
    {
        ILocalizationService localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>())
            .Returns(callInfo => callInfo.Arg<string>() == "DeviceManager_NewDeviceCount" ? "New: {0}" : callInfo.Arg<string>());
        return localization;
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
}
