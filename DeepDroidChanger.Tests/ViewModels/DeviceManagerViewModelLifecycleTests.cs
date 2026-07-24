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
                UseIntegritySecurityPatch = true,
                ChangeOptions = new DeviceChangeOptions
                {
                    UseDefaultMode = false,
                    ClearAllPackages = false,
                    ClearSelectedPackages = true,
                    SelectedPackages = ["com.example.app"]
                },
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
        Assert.IsFalse(viewModel.UseDefaultChangeMode);
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
        viewModel.IsChangeSimEnabled = false;
        viewModel.UseDefaultChangeMode = false;
        viewModel.SelectedDevice = viewModel.Devices[1];
        viewModel.SelectedBrand = "Google";
        await viewModel.DeactivateAsync();

        await deviceConfig.Received(1).SaveDeviceProfileAsync(
            Arg.Any<IList<StoredDeviceConfig>>(),
            "A",
            Arg.Is<DeviceProfileConfig>(profile =>
                profile.Brand == "Samsung"
                && !profile.ChangeSimEnabled
                && profile.UseIntegritySecurityPatch
                && !profile.ChangeOptions.UseDefaultMode),
            Arg.Any<CancellationToken>());
        await deviceConfig.Received(1).SaveDeviceProfileAsync(
            Arg.Any<IList<StoredDeviceConfig>>(),
            "B",
            Arg.Is<DeviceProfileConfig>(profile =>
                profile.Brand == "Google"
                && profile.ChangeSimEnabled
                && profile.UseIntegritySecurityPatch
                && profile.ChangeOptions.UseDefaultMode),
            Arg.Any<CancellationToken>());
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task RandomDevice_UpdatesSessionFormWithoutPersistingGeneratedValues()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new()
            {
                Serial = "A",
                Name = "Phone",
                Type = "Phone",
                Brand = "Samsung",
                UseIntegritySecurityPatch = false
            }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, [new AdbDevice("A", AdbDeviceStatus.Online)]));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();
        IRandomDeviceInfoDialogService randomDeviceInfoDialog = Substitute.For<IRandomDeviceInfoDialogService>();
        var generatedProfile = new DeviceInfoApiDevice
        {
            Name = "Generated device",
            Hardware = "qcom",
            Fingerprint = "samsung/e3qxxx/e3q:15/AP3A/test:user/release-keys",
            Model = "SM-S928B",
            Brand = "samsung",
            Release = "15",
            Serial = "GENERATED-SERIAL",
            Imei = "123456789012345",
            Iccid = "8984041234567890123",
            Imsi = "452041234567890",
            SimOperatorName = "Viettel",
            SimPhoneNumber = "+84901234567",
            WifiMacAddress = "00:11:22:33:44:55"
        };
        randomDeviceInfoDialog.ShowRandomDeviceInfoAsync(
                generatedProfile,
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                generatedProfile.Name = "Edited device";
                generatedProfile.Model = "Edited model";
                generatedProfile.Brand = "edited brand";
                generatedProfile.Release = "14";
                return true;
            });
        randomDevice.CreateRandomProfileAsync(
                Arg.Is<RandomDeviceRequest>(request => request.UseIntegritySecurityPatch),
                Arg.Any<CancellationToken>())
            .Returns(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                generatedProfile));
        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            deviceConfig: deviceConfig,
            randomDevice: randomDevice,
            randomDeviceInfoDialog: randomDeviceInfoDialog);
        await viewModel.InitializeAsync(CancellationToken.None);
        deviceConfig.ClearReceivedCalls();
        Assert.IsFalse(viewModel.ViewRandomDeviceInfoCommand.CanExecute(null));

        await viewModel.RandomDeviceCommand.ExecuteAsync(null);
        Assert.IsTrue(viewModel.ViewRandomDeviceInfoCommand.CanExecute(null));
        await viewModel.ViewRandomDeviceInfoCommand.ExecuteAsync(null);
        await viewModel.DeactivateAsync();

        Assert.AreEqual("Edited device", viewModel.DeviceInfo.Name);
        Assert.AreEqual("qcom", viewModel.DeviceInfo.Hardware);
        Assert.AreEqual(
            "samsung/e3qxxx/e3q:15/AP3A/test:user/release-keys",
            viewModel.DeviceInfo.Fingerprint);
        Assert.AreEqual("Edited model", viewModel.DeviceInfo.Model);
        Assert.AreEqual("edited brand", viewModel.DeviceInfo.Brand);
        Assert.AreEqual("Android 14", viewModel.DeviceInfo.AndroidVersion);
        Assert.AreEqual("GENERATED-SERIAL", viewModel.DeviceInfo.Serial);
        Assert.AreEqual("123456789012345", viewModel.DeviceInfo.Imei);
        Assert.AreEqual("8984041234567890123", viewModel.DeviceInfo.Iccid);
        Assert.AreEqual("452041234567890", viewModel.DeviceInfo.Imsi);
        Assert.AreEqual("Viettel", viewModel.DeviceInfo.Operator);
        Assert.AreEqual("+84901234567", viewModel.DeviceInfo.PhoneNumber);
        Assert.AreEqual("00:11:22:33:44:55", viewModel.DeviceInfo.Mac);
        await deviceConfig.DidNotReceiveWithAnyArgs().SaveDeviceProfileAsync(
            default!, default!, default!, default);
        await randomDeviceInfoDialog.Received(1).ShowRandomDeviceInfoAsync(
            generatedProfile,
            Arg.Any<CancellationToken>());
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task DeviceInfoFormEdits_AutomaticallyUpdatePreparedRandomProfile()
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
        var generatedProfile = new DeviceInfoApiDevice
        {
            Name = "Generated device",
            Hardware = "generated-hardware",
            Fingerprint = "generated/fingerprint",
            Model = "Generated model",
            Brand = "generated-brand",
            Release = "15",
            Product = "preserved-product",
            Serial = "GENERATED-SERIAL",
            Imei = "123456789012345",
            Iccid = "8984041234567890123",
            Imsi = "452041234567890",
            SimOperatorName = "Generated operator",
            SimOperatorNumeric = "45204",
            SimOperatorCountry = "vn",
            SimPhoneNumber = "+84901234567",
            WifiMacAddress = "00:11:22:33:44:55"
        };
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();
        randomDevice.CreateRandomProfileAsync(
                Arg.Any<RandomDeviceRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new RandomDeviceResult(RandomDeviceStatus.Created, generatedProfile));
        DeviceInfoApiDevice? dialogProfile = null;
        IRandomDeviceInfoDialogService randomDeviceInfoDialog = Substitute.For<IRandomDeviceInfoDialogService>();
        randomDeviceInfoDialog.ShowRandomDeviceInfoAsync(
                Arg.Any<DeviceInfoApiDevice>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                dialogProfile = callInfo.ArgAt<DeviceInfoApiDevice>(0);
                return false;
            });
        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            randomDevice: randomDevice,
            randomDeviceInfoDialog: randomDeviceInfoDialog);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RandomDeviceCommand.ExecuteAsync(null);

        viewModel.DeviceInfo.Name = "Edited device";
        viewModel.DeviceInfo.Hardware = "edited-hardware";
        viewModel.DeviceInfo.Fingerprint = "edited/fingerprint";
        viewModel.DeviceInfo.Model = "Edited model";
        viewModel.DeviceInfo.Brand = "edited-brand";
        viewModel.DeviceInfo.AndroidVersion = "Android 14";
        viewModel.DeviceInfo.Serial = "EDITED-SERIAL";
        viewModel.DeviceInfo.Imei = "543210987654321";
        viewModel.DeviceInfo.Iccid = "8984049876543210987";
        viewModel.DeviceInfo.Imsi = "452049876543210";
        viewModel.DeviceInfo.Operator = "Edited operator";
        viewModel.DeviceInfo.PhoneNumber = "+84987654321";
        viewModel.DeviceInfo.Mac = "aa:bb:cc:dd:ee:ff";

        await viewModel.ViewRandomDeviceInfoCommand.ExecuteAsync(null);

        Assert.IsNotNull(dialogProfile);
        Assert.AreEqual("Edited device", dialogProfile.Name);
        Assert.AreEqual("edited-hardware", dialogProfile.Hardware);
        Assert.AreEqual("edited/fingerprint", dialogProfile.Fingerprint);
        Assert.AreEqual("Edited model", dialogProfile.Model);
        Assert.AreEqual("edited-brand", dialogProfile.Brand);
        Assert.AreEqual("14", dialogProfile.Release);
        Assert.AreEqual("preserved-product", dialogProfile.Product);
        Assert.AreEqual("EDITED-SERIAL", dialogProfile.Serial);
        Assert.AreEqual("543210987654321", dialogProfile.Imei);
        Assert.AreEqual("8984049876543210987", dialogProfile.Iccid);
        Assert.AreEqual("452049876543210", dialogProfile.Imsi);
        Assert.AreEqual("Edited operator", dialogProfile.SimOperatorName);
        Assert.AreEqual("45204", dialogProfile.SimOperatorNumeric);
        Assert.AreEqual("vn", dialogProfile.SimOperatorCountry);
        Assert.AreEqual("+84987654321", dialogProfile.SimPhoneNumber);
        Assert.AreEqual("aa:bb:cc:dd:ee:ff", dialogProfile.WifiMacAddress);
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task RandomDevice_OfflineSelection_StillPreparesProfileForLaterChange()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "A", Name = "Offline phone", Type = "Phone" }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, [new AdbDevice("A", AdbDeviceStatus.Offline)]));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();
        randomDevice.CreateRandomProfileAsync(Arg.Any<RandomDeviceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = "Pixel 8", Name = "husky" }));
        var viewModel = CreateViewModel(deviceList, carriers, randomDevice: randomDevice);
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.RandomDeviceCommand.ExecuteAsync(null);

        Assert.AreEqual("Pixel 8", viewModel.DeviceInfo.Model);
        Assert.IsTrue(viewModel.ViewRandomDeviceInfoCommand.CanExecute(null));
        await randomDevice.Received(1).CreateRandomProfileAsync(
            Arg.Any<RandomDeviceRequest>(),
            Arg.Any<CancellationToken>());
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task ChangeDevice_OnlinePreparedDevice_ConfirmsAndRunsWorkflow()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new()
            {
                Serial = "A",
                Name = "Phone",
                Type = "Phone",
                ChangeSimEnabled = false,
                ChangeOptions = new DeviceChangeOptions
                {
                    UseDefaultMode = false,
                    ChangeAndroidId = true
                }
            }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, [new AdbDevice("A", AdbDeviceStatus.Online)]));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();
        var profile = new DeviceInfoApiDevice
        {
            Brand = "samsung",
            Model = "SM-S928B",
            Code = "e3q",
            Name = "e3qxxx",
            Fingerprint = "samsung/e3qxxx/e3q:15/AP3A/test:user/release-keys",
            Serial = "NEW-SERIAL"
        };
        randomDevice.CreateRandomProfileAsync(Arg.Any<RandomDeviceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RandomDeviceResult(RandomDeviceStatus.Created, profile));
        IChangeDeviceConfirmationDialogService confirmation = Substitute.For<IChangeDeviceConfirmationDialogService>();
        DeviceChangeOptions? confirmedOptions = null;
        confirmation.ShowChangeDeviceConfirmationAsync(
            "Phone",
            "A",
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<CancellationToken>()).Returns(callInfo =>
            {
                confirmedOptions = callInfo.ArgAt<DeviceChangeOptions>(2);
                return true;
            });
        IDeviceChangeService deviceChange = Substitute.For<IDeviceChangeService>();
        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            randomDevice: randomDevice,
            changeDeviceConfirmation: confirmation,
            deviceChange: deviceChange);
        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsFalse(viewModel.IsChangeSimEnabled);
        Assert.IsTrue(viewModel.ChangeDeviceCommand.CanExecute(null));
        await viewModel.RandomDeviceCommand.ExecuteAsync(null);
        Assert.IsTrue(viewModel.ChangeDeviceCommand.CanExecute(null));
        await viewModel.ChangeDeviceCommand.ExecuteAsync(null);

        await confirmation.Received(1).ShowChangeDeviceConfirmationAsync(
            "Phone",
            "A",
            Arg.Is<DeviceChangeOptions>(options =>
                !options.UseDefaultMode
                && options.ChangeAndroidId),
            Arg.Any<CancellationToken>());
        await deviceChange.Received(1).ChangeAsync(
            "A",
            profile,
            false,
            Arg.Is<DeviceChangeOptions>(options =>
                ReferenceEquals(options, confirmedOptions)
                && !options.UseDefaultMode
                && options.ChangeAndroidId
                && options.ChangeMacAddress
                && options.ClearAllPackages
                && options.ClearGoogleAccounts),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task ChangeWithoutWipe_OnlinePreparedDevice_ConfirmsAndRunsIdentityWorkflow()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new()
            {
                Serial = "A",
                Name = "Phone",
                Type = "Phone",
                ChangeSimEnabled = false,
                ChangeOptions = new DeviceChangeOptions
                {
                    UseDefaultMode = false,
                    ChangeAndroidId = true
                }
            }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, [new AdbDevice("A", AdbDeviceStatus.Online)]));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        var profile = new DeviceInfoApiDevice
        {
            Brand = "samsung",
            Model = "SM-S928B",
            Name = "e3qxxx",
            Fingerprint = "samsung/e3qxxx/e3q:15/AP3A/test:user/release-keys",
            Serial = "NEW-SERIAL"
        };
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();
        randomDevice.CreateRandomProfileAsync(Arg.Any<RandomDeviceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RandomDeviceResult(RandomDeviceStatus.Created, profile));
        IChangeDeviceConfirmationDialogService confirmation = Substitute.For<IChangeDeviceConfirmationDialogService>();
        IDeviceActionConfirmationDialogService actionConfirmation = CreateDeviceActionConfirmationDialogService();
        IDeviceChangeService deviceChange = Substitute.For<IDeviceChangeService>();
        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            randomDevice: randomDevice,
            changeDeviceConfirmation: confirmation,
            deviceActionConfirmationDialog: actionConfirmation,
            deviceChange: deviceChange);
        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.ChangeWithoutWipeCommand.CanExecute(null));
        await viewModel.RandomDeviceCommand.ExecuteAsync(null);
        Assert.IsTrue(viewModel.ChangeWithoutWipeCommand.CanExecute(null));
        await viewModel.ChangeWithoutWipeCommand.ExecuteAsync(null);

        await confirmation.DidNotReceiveWithAnyArgs().ShowChangeDeviceConfirmationAsync(
            default!, default!, default!, default);
        await actionConfirmation.Received(1).ConfirmChangeWithoutWipeAsync(
            "Phone",
            "A",
            Arg.Any<CancellationToken>());
        await deviceChange.Received(1).ChangeWithoutWipeAsync(
            "A",
            profile,
            false,
            Arg.Is<DeviceChangeOptions>(options =>
                !options.UseDefaultMode
                && options.ChangeAndroidId),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());
        await deviceChange.DidNotReceiveWithAnyArgs().ChangeAsync(
            default!, default!, default, default!, default, default);
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task WipeWithoutChange_OnlineDevice_UsesSelectedDeviceCleanupConfigWithoutProfile()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new()
            {
                Serial = "A",
                Name = "Phone",
                Type = "Phone",
                ChangeOptions = new DeviceChangeOptions
                {
                    UseDefaultMode = false,
                    ChangeAndroidId = true,
                    ClearAllPackages = false,
                    ClearSelectedPackages = true,
                    SelectedPackages = ["com.example.app"],
                    ClearGoogleAccounts = false
                }
            }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, [new AdbDevice("A", AdbDeviceStatus.Online)]));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        IDeviceActionConfirmationDialogService actionConfirmation = CreateDeviceActionConfirmationDialogService();
        IDeviceChangeService deviceChange = Substitute.For<IDeviceChangeService>();
        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            deviceActionConfirmationDialog: actionConfirmation,
            deviceChange: deviceChange);
        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.WipeWithoutChangeCommand.CanExecute(null));
        Assert.IsTrue(viewModel.ChangeDeviceCommand.CanExecute(null));
        await viewModel.WipeWithoutChangeCommand.ExecuteAsync(null);

        await actionConfirmation.Received(1).ConfirmWipeWithoutChangeAsync(
            "Phone",
            "A",
            Arg.Any<CancellationToken>());
        await deviceChange.Received(1).WipeWithoutChangeAsync(
            "A",
            Arg.Is<DeviceChangeOptions>(options =>
                !options.UseDefaultMode
                && options.ChangeAndroidId
                && !options.ClearAllPackages
                && options.ClearSelectedPackages
                && !options.ClearGoogleAccounts
                && options.SelectedPackages.SequenceEqual(new[] { "com.example.app" })),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());
        await deviceChange.DidNotReceiveWithAnyArgs().ChangeAsync(
            default!, default!, default, default!, default, default);
        await deviceChange.DidNotReceiveWithAnyArgs().ChangeWithoutWipeAsync(
            default!, default!, default, default!, default, default);
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task ChangeDevice_ConfirmationCanceled_DoesNotRunWorkflow()
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
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();
        randomDevice.CreateRandomProfileAsync(Arg.Any<RandomDeviceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = "Pixel 9" }));
        IChangeDeviceConfirmationDialogService confirmation = Substitute.For<IChangeDeviceConfirmationDialogService>();
        confirmation.ShowChangeDeviceConfirmationAsync(
            "Phone",
            "A",
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<CancellationToken>()).Returns(false);
        IDeviceChangeService deviceChange = Substitute.For<IDeviceChangeService>();
        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            randomDevice: randomDevice,
            changeDeviceConfirmation: confirmation,
            deviceChange: deviceChange);
        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.RandomDeviceCommand.ExecuteAsync(null);

        await viewModel.ChangeDeviceCommand.ExecuteAsync(null);

        await confirmation.Received(1).ShowChangeDeviceConfirmationAsync(
            "Phone",
            "A",
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<CancellationToken>());
        await deviceChange.DidNotReceiveWithAnyArgs().ChangeAsync(
            default!, default!, default, default!, default, default);
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task ChangeDevice_OfflinePreparedDevice_LogsOnlineRequirementAndDoesNotRun()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "A", Name = "Phone", Type = "Phone" }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, []));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();
        randomDevice.CreateRandomProfileAsync(Arg.Any<RandomDeviceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = "Pixel 8" }));
        IChangeDeviceConfirmationDialogService confirmation = Substitute.For<IChangeDeviceConfirmationDialogService>();
        IDeviceChangeService deviceChange = Substitute.For<IDeviceChangeService>();
        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            randomDevice: randomDevice,
            changeDeviceConfirmation: confirmation,
            deviceChange: deviceChange);
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.RandomDeviceCommand.ExecuteAsync(null);
        var processLogs = new List<string>();
        Assert.IsNotNull(viewModel.SelectedDevice);
        viewModel.SelectedDevice.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DeviceRowViewModel.Process))
                processLogs.Add(viewModel.SelectedDevice.Process);
        };

        Assert.IsTrue(viewModel.ChangeDeviceCommand.CanExecute(null));
        await viewModel.ChangeDeviceCommand.ExecuteAsync(null);

        Assert.Contains(DeviceLogResourceKeys.DeviceMustBeOnline, processLogs);
        await confirmation.DidNotReceiveWithAnyArgs().ShowChangeDeviceConfirmationAsync(
            default!, default!, default!, default);
        await deviceChange.DidNotReceiveWithAnyArgs().ChangeAsync(
            default!, default!, default, default!, default, default);
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task ChangeDeviceActions_OnlineWithoutRandomDevice_LogRequirementAndDoNotRun()
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
        IChangeDeviceConfirmationDialogService confirmation = Substitute.For<IChangeDeviceConfirmationDialogService>();
        IDeviceChangeService deviceChange = Substitute.For<IDeviceChangeService>();
        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            changeDeviceConfirmation: confirmation,
            deviceChange: deviceChange);
        await viewModel.InitializeAsync(CancellationToken.None);
        var processLogs = new List<string>();
        Assert.IsNotNull(viewModel.SelectedDevice);
        viewModel.SelectedDevice.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DeviceRowViewModel.Process))
                processLogs.Add(viewModel.SelectedDevice.Process);
        };

        Assert.IsTrue(viewModel.ChangeDeviceCommand.CanExecute(null));
        Assert.IsTrue(viewModel.ChangeWithoutWipeCommand.CanExecute(null));
        await viewModel.ChangeDeviceCommand.ExecuteAsync(null);
        await viewModel.ChangeWithoutWipeCommand.ExecuteAsync(null);

        Assert.AreEqual(
            2,
            processLogs.Count(log => log == DeviceLogResourceKeys.RandomDeviceRequired));
        await confirmation.DidNotReceiveWithAnyArgs().ShowChangeDeviceConfirmationAsync(
            default!, default!, default!, default);
        await deviceChange.DidNotReceiveWithAnyArgs().ChangeAsync(
            default!, default!, default, default!, default, default);
        await deviceChange.DidNotReceiveWithAnyArgs().ChangeWithoutWipeAsync(
            default!, default!, default, default!, default, default);
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task ChangeSim_OnlineWithoutRandomSim_LogsRequirementAndDoesNotRun()
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
        IDeviceChangeService deviceChange = Substitute.For<IDeviceChangeService>();
        var viewModel = CreateViewModel(deviceList, carriers, deviceChange: deviceChange);
        await viewModel.InitializeAsync(CancellationToken.None);
        var processLogs = new List<string>();
        Assert.IsNotNull(viewModel.SelectedDevice);
        viewModel.SelectedDevice.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DeviceRowViewModel.Process))
                processLogs.Add(viewModel.SelectedDevice.Process);
        };

        Assert.IsTrue(viewModel.ChangeSimCommand.CanExecute(null));
        await viewModel.ChangeSimCommand.ExecuteAsync(null);

        Assert.Contains(DeviceLogResourceKeys.RandomSimRequired, processLogs);
        await deviceChange.DidNotReceiveWithAnyArgs().ChangeSimAsync(
            default!, default!, default);
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task AdvancedChangeConfig_DefaultModeDisabled_OpensImmediatelyAndPersistsDialogResult()
    {
        var settings = new AppSettings();
        StoredDeviceConfig[] storedDevices =
        [
            new()
            {
                Serial = "A",
                Name = "Phone",
                Type = "Phone",
                UseIntegritySecurityPatch = false,
                ChangeOptions = new DeviceChangeOptions
                {
                    UseDefaultMode = false,
                    ClearAllPackages = false,
                    ClearGoogleAccounts = true
                }
            }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, [new AdbDevice("A", AdbDeviceStatus.Online)]));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        IAdvancedChangeConfigDialogService dialog = Substitute.For<IAdvancedChangeConfigDialogService>();
        dialog.ShowAdvancedChangeConfigAsync(
                "A",
                Arg.Any<DeviceChangeOptions>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(new AdvancedChangeConfigDialogResult(
                new DeviceChangeOptions
                {
                    UseDefaultMode = false,
                    ChangeAndroidId = true,
                    ClearAllPackages = false,
                    ClearSelectedPackages = true,
                    SelectedPackages = ["com.example.app"],
                    ChangeMacAddress = false,
                    UseRmRfForPackageCleanup = true,
                    ClearGooglePackages = true,
                    ClearGoogleAccounts = true
                },
                false));
        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();
        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            deviceConfig: deviceConfig,
            advancedChangeConfig: dialog,
            settings: settings);
        await viewModel.InitializeAsync(CancellationToken.None);
        deviceConfig.ClearReceivedCalls();

        Assert.IsFalse(viewModel.UseDefaultChangeMode);
        Assert.IsTrue(viewModel.OpenAdvancedChangeConfigCommand.CanExecute(null));

        await viewModel.OpenAdvancedChangeConfigCommand.ExecuteAsync(null);

        await dialog.Received(1).ShowAdvancedChangeConfigAsync(
            "A",
            Arg.Is<DeviceChangeOptions>(options => !options.UseDefaultMode),
            Arg.Is<bool>(useIntegritySecurityPatch => !useIntegritySecurityPatch),
            Arg.Any<CancellationToken>());
        await viewModel.DeactivateAsync();
        await deviceConfig.Received(1).SaveDeviceProfileAsync(
            Arg.Any<IList<StoredDeviceConfig>>(),
            "A",
            Arg.Is<DeviceProfileConfig>(profile =>
                !profile.ChangeOptions.UseDefaultMode
                && profile.ChangeOptions.ChangeAndroidId
                && !profile.UseIntegritySecurityPatch
                && profile.ChangeOptions.ClearSelectedPackages
                && !profile.ChangeOptions.ChangeMacAddress
                && profile.ChangeOptions.ClearGooglePackages
                && profile.ChangeOptions.ClearGoogleAccounts
                && profile.ChangeOptions.SelectedPackages.SequenceEqual(new[] { "com.example.app" })),
            Arg.Any<CancellationToken>());
        await deviceConfig.DidNotReceive().SaveSettingsAsync(Arg.Any<CancellationToken>());
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task AdvancedChangeConfig_DefaultModeSelected_KeepsButtonDisabled()
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
        var viewModel = CreateViewModel(deviceList, carriers);
        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.UseDefaultChangeMode);
        Assert.IsFalse(viewModel.OpenAdvancedChangeConfigCommand.CanExecute(null));

        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task RandomSim_UpdatesOnlySimFieldsForSelectedCarrier()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new()
            {
                Serial = "A",
                Name = "Phone",
                Type = "Phone",
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
            .Returns(new DeviceListSnapshot(storedDevices, [new AdbDevice("A", AdbDeviceStatus.Online)]));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns(
            [new CarrierProfile("vn", "84", "Vietnam", "Viettel", "452", "04")]);
        ISimProfileService simProfileService = Substitute.For<ISimProfileService>();
        simProfileService.CreateRandomProfile(Arg.Any<CarrierCountryOption?>(), Arg.Any<CarrierOption?>())
            .Returns(new SimProfile
            {
                Imsi = "452041234567890",
                Iccid = "8984041234567890123",
                PhoneNumber = "+84901234567",
                OperatorNumeric = "45204",
                OperatorCountry = "vn",
                OperatorName = "Viettel"
            });
        var viewModel = CreateViewModel(deviceList, carriers, simProfileService: simProfileService);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.DeviceInfo.Model = "Existing model";

        await viewModel.RandomSimCommand.ExecuteAsync(null);

        Assert.AreEqual("452041234567890", viewModel.DeviceInfo.Imsi);
        Assert.AreEqual("8984041234567890123", viewModel.DeviceInfo.Iccid);
        Assert.AreEqual("+84901234567", viewModel.DeviceInfo.PhoneNumber);
        Assert.AreEqual("Viettel", viewModel.DeviceInfo.Operator);
        Assert.AreEqual("Existing model", viewModel.DeviceInfo.Model);
        simProfileService.Received(1).CreateRandomProfile(
            Arg.Is<CarrierCountryOption>(country => country.CountryIso == "vn"),
            Arg.Is<CarrierOption>(carrier => carrier.Mcc == "452" && carrier.Mnc == "04"));
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task RandomSim_OfflineDevice_StillGeneratesSimFields()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "A", Name = "Phone", Type = "Phone" }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, []));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        ISimProfileService simProfileService = Substitute.For<ISimProfileService>();
        simProfileService.CreateRandomProfile(Arg.Any<CarrierCountryOption?>(), Arg.Any<CarrierOption?>())
            .Returns(new SimProfile
            {
                Imsi = "generated-imsi",
                Iccid = "generated-iccid",
                PhoneNumber = "generated-phone",
                OperatorName = "Generated operator"
            });
        var viewModel = CreateViewModel(deviceList, carriers, simProfileService: simProfileService);
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.DeviceInfo.Imsi = "existing-imsi";
        viewModel.DeviceInfo.Iccid = "existing-iccid";
        viewModel.DeviceInfo.PhoneNumber = "existing-phone";
        viewModel.DeviceInfo.Operator = "existing-operator";

        await viewModel.RandomSimCommand.ExecuteAsync(null);

        simProfileService.Received(1).CreateRandomProfile(
            Arg.Any<CarrierCountryOption?>(),
            Arg.Any<CarrierOption?>());
        Assert.AreEqual("generated-imsi", viewModel.DeviceInfo.Imsi);
        Assert.AreEqual("generated-iccid", viewModel.DeviceInfo.Iccid);
        Assert.AreEqual("generated-phone", viewModel.DeviceInfo.PhoneNumber);
        Assert.AreEqual("Generated operator", viewModel.DeviceInfo.Operator);
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task OfflineDevice_AllGuardedActionsRemainExecutableAcrossStatusChanges()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new()
            {
                Serial = "A",
                Name = "Phone",
                Type = "Phone",
                ChangeOptions = new DeviceChangeOptions { UseDefaultMode = false }
            }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, []));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();
        randomDevice.CreateRandomProfileAsync(Arg.Any<RandomDeviceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice { Model = "Generated model" }));
        ISimProfileService simProfileService = Substitute.For<ISimProfileService>();
        simProfileService.CreateRandomProfile(Arg.Any<CarrierCountryOption?>(), Arg.Any<CarrierOption?>())
            .Returns(new SimProfile
            {
                Imsi = "generated-imsi",
                Iccid = "generated-iccid"
            });
        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            randomDevice: randomDevice,
            simProfileService: simProfileService);
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.RandomDeviceCommand.ExecuteAsync(null);
        await viewModel.RandomSimCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.RandomDeviceCommand.CanExecute(null));
        Assert.IsTrue(viewModel.RandomSimCommand.CanExecute(null));
        Dictionary<string, bool> offlineActionStates = GetGuardedActionStates(viewModel);
        Assert.IsTrue(
            offlineActionStates.All(pair => pair.Value),
            CreateActionStateMessage(offlineActionStates));

        viewModel.ApplyDeviceListSnapshot(new DeviceListSnapshot(
            storedDevices,
            [new AdbDevice("A", AdbDeviceStatus.Online)]));

        Dictionary<string, bool> onlineActionStates = GetGuardedActionStates(viewModel);
        Assert.IsTrue(onlineActionStates.All(pair => pair.Value), CreateActionStateMessage(onlineActionStates));
        await randomDevice.Received(1).CreateRandomProfileAsync(
            Arg.Any<RandomDeviceRequest>(),
            Arg.Any<CancellationToken>());
        simProfileService.Received(1).CreateRandomProfile(
            Arg.Any<CarrierCountryOption?>(),
            Arg.Any<CarrierOption?>());
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task ContextMenuActions_RemainExecutableOfflineAndLogOnlineRequirement()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "A", Name = "Phone", Type = "Phone" }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, []));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        var viewModel = CreateViewModel(deviceList, carriers);
        await viewModel.InitializeAsync(CancellationToken.None);

        Dictionary<string, bool> offlineMenuStates = GetContextMenuActionStates(viewModel);
        Assert.IsTrue(offlineMenuStates.All(pair => pair.Value), CreateActionStateMessage(offlineMenuStates));
        var processLogs = new List<string>();
        Assert.IsNotNull(viewModel.SelectedDevice);
        viewModel.SelectedDevice.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DeviceRowViewModel.Process))
                processLogs.Add(viewModel.SelectedDevice.Process);
        };

        await viewModel.ViewDeviceInfoCommand.ExecuteAsync(viewModel.SelectedDevice);

        Assert.Contains(DeviceLogResourceKeys.DeviceMustBeOnline, processLogs);

        viewModel.ApplyDeviceListSnapshot(new DeviceListSnapshot(
            storedDevices,
            [new AdbDevice("A", AdbDeviceStatus.Online)]));

        Dictionary<string, bool> onlineMenuStates = GetContextMenuActionStates(viewModel);
        Assert.IsTrue(onlineMenuStates.All(pair => pair.Value), CreateActionStateMessage(onlineMenuStates));
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task DeleteDevice_OfflineDevice_ConfirmsAndDeletesSavedEntry()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "A", Name = "Offline phone", Type = "Phone" }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, []));
        deviceList.DeleteSavedDeviceAsync("A", Arg.Any<CancellationToken>())
            .Returns(new DeviceDeleteResult(true, new DeviceListSnapshot([], [])));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        IDeleteDeviceConfirmationDialogService confirmation = Substitute.For<IDeleteDeviceConfirmationDialogService>();
        confirmation.ShowDeleteDeviceConfirmationAsync(
                "Offline phone",
                "A",
                Arg.Any<CancellationToken>())
            .Returns(true);
        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            deleteDeviceConfirmation: confirmation);
        await viewModel.InitializeAsync(CancellationToken.None);
        var processLogs = new List<string>();
        Assert.IsNotNull(viewModel.SelectedDevice);
        DeviceRowViewModel device = viewModel.SelectedDevice;
        device.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DeviceRowViewModel.Process))
                processLogs.Add(device.Process);
        };

        Assert.IsTrue(viewModel.DeleteDeviceCommand.CanExecute(device));
        await viewModel.DeleteDeviceCommand.ExecuteAsync(device);

        Assert.DoesNotContain(DeviceLogResourceKeys.DeviceMustBeOnline, processLogs);
        await confirmation.Received(1).ShowDeleteDeviceConfirmationAsync(
            "Offline phone",
            "A",
            Arg.Any<CancellationToken>());
        await deviceList.Received(1).DeleteSavedDeviceAsync(
            "A",
            Arg.Any<CancellationToken>());
        Assert.IsEmpty(viewModel.Devices);
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task ChangeSim_AfterRandomSim_AppliesEditedSimOnlyToSelectedOnlineDevice()
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
        ISimProfileService simProfileService = Substitute.For<ISimProfileService>();
        simProfileService.CreateRandomProfile(Arg.Any<CarrierCountryOption?>(), Arg.Any<CarrierOption?>())
            .Returns(new SimProfile
            {
                Imsi = "452041234567890",
                Iccid = "8984041234567890123",
                PhoneNumber = "+84901234567",
                OperatorNumeric = "45204",
                OperatorCountry = "vn",
                OperatorName = "Viettel"
            });
        IDeviceActionConfirmationDialogService actionConfirmation = CreateDeviceActionConfirmationDialogService();
        IDeviceChangeService deviceChange = Substitute.For<IDeviceChangeService>();
        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            simProfileService: simProfileService,
            deviceActionConfirmationDialog: actionConfirmation,
            deviceChange: deviceChange);
        await viewModel.InitializeAsync(CancellationToken.None);
        Assert.IsTrue(viewModel.ChangeSimCommand.CanExecute(null));

        await viewModel.RandomSimCommand.ExecuteAsync(null);
        viewModel.DeviceInfo.PhoneNumber = "+84909999999";
        Assert.IsTrue(viewModel.ChangeSimCommand.CanExecute(null));
        await viewModel.ChangeSimCommand.ExecuteAsync(null);

        await actionConfirmation.Received(1).ConfirmChangeSimAsync(
            "Phone",
            "A",
            Arg.Any<CancellationToken>());
        await deviceChange.Received(1).ChangeSimAsync(
            "A",
            Arg.Is<SimProfile>(profile =>
                profile.Iccid == "8984041234567890123"
                && profile.Imsi == "452041234567890"
                && profile.PhoneNumber == "+84909999999"
                && profile.OperatorNumeric == "45204"
                && profile.OperatorCountry == "vn"
                && profile.OperatorName == "Viettel"),
            Arg.Any<CancellationToken>());
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task DeviceActionConfirmations_Declined_DoNotRunWorkflows()
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
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();
        randomDevice.CreateRandomProfileAsync(Arg.Any<RandomDeviceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RandomDeviceResult(
                RandomDeviceStatus.Created,
                new DeviceInfoApiDevice
                {
                    Brand = "samsung",
                    Model = "SM-S928B",
                    Name = "e3qxxx",
                    Fingerprint = "samsung/e3qxxx/e3q:15/AP3A/test:user/release-keys"
                }));
        ISimProfileService simProfileService = Substitute.For<ISimProfileService>();
        simProfileService.CreateRandomProfile(Arg.Any<CarrierCountryOption?>(), Arg.Any<CarrierOption?>())
            .Returns(new SimProfile
            {
                Imsi = "452041234567890",
                Iccid = "8984041234567890123",
                PhoneNumber = "+84901234567",
                OperatorNumeric = "45204",
                OperatorCountry = "vn",
                OperatorName = "Viettel"
            });
        IDeviceActionConfirmationDialogService actionConfirmation = Substitute.For<IDeviceActionConfirmationDialogService>();
        actionConfirmation.ConfirmChangeWithoutWipeAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        actionConfirmation.ConfirmWipeWithoutChangeAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        actionConfirmation.ConfirmChangeSimAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        IDeviceChangeService deviceChange = Substitute.For<IDeviceChangeService>();
        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            randomDevice: randomDevice,
            simProfileService: simProfileService,
            deviceActionConfirmationDialog: actionConfirmation,
            deviceChange: deviceChange);
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.RandomDeviceCommand.ExecuteAsync(null);
        await viewModel.RandomSimCommand.ExecuteAsync(null);
        await viewModel.ChangeWithoutWipeCommand.ExecuteAsync(null);
        await viewModel.WipeWithoutChangeCommand.ExecuteAsync(null);
        await viewModel.ChangeSimCommand.ExecuteAsync(null);

        await actionConfirmation.Received(1).ConfirmChangeWithoutWipeAsync(
            "Phone", "A", Arg.Any<CancellationToken>());
        await actionConfirmation.Received(1).ConfirmWipeWithoutChangeAsync(
            "Phone", "A", Arg.Any<CancellationToken>());
        await actionConfirmation.Received(1).ConfirmChangeSimAsync(
            "Phone", "A", Arg.Any<CancellationToken>());
        await deviceChange.DidNotReceiveWithAnyArgs().ChangeWithoutWipeAsync(
            default!, default!, default, default!, default, default);
        await deviceChange.DidNotReceiveWithAnyArgs().WipeWithoutChangeAsync(
            default!, default!, default, default);
        await deviceChange.DidNotReceiveWithAnyArgs().ChangeSimAsync(
            default!, default!, default);
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task RandomSim_AfterRandomDevice_KeepsFullProfileInSync()
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
        var generatedDevice = new DeviceInfoApiDevice { Model = "Generated model" };
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();
        randomDevice.CreateRandomProfileAsync(Arg.Any<RandomDeviceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RandomDeviceResult(RandomDeviceStatus.Created, generatedDevice));
        ISimProfileService simProfileService = Substitute.For<ISimProfileService>();
        simProfileService.CreateRandomProfile(Arg.Any<CarrierCountryOption?>(), Arg.Any<CarrierOption?>())
            .Returns(new SimProfile
            {
                Imsi = "452041234567890",
                Iccid = "8984041234567890123",
                PhoneNumber = "+84901234567",
                OperatorNumeric = "45204",
                OperatorCountry = "vn",
                OperatorName = "Viettel"
            });
        IRandomDeviceInfoDialogService randomDeviceInfoDialog = Substitute.For<IRandomDeviceInfoDialogService>();
        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            randomDevice: randomDevice,
            randomDeviceInfoDialog: randomDeviceInfoDialog,
            simProfileService: simProfileService);
        await viewModel.InitializeAsync(CancellationToken.None);

        await viewModel.RandomDeviceCommand.ExecuteAsync(null);
        await viewModel.RandomSimCommand.ExecuteAsync(null);
        await viewModel.ViewRandomDeviceInfoCommand.ExecuteAsync(null);

        await randomDeviceInfoDialog.Received(1).ShowRandomDeviceInfoAsync(
            Arg.Is<DeviceInfoApiDevice>(device =>
                device.Imsi == "452041234567890"
                && device.Iccid == "8984041234567890123"
                && device.SimPhoneNumber == "+84901234567"
                && device.SimOperatorNumeric == "45204"
                && device.SimOperatorCountry == "vn"
                && device.SimOperatorName == "Viettel"),
            Arg.Any<CancellationToken>());
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task RandomSim_WhileRandomDeviceIsRunning_IsDisabledUntilActionCompletes()
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
        var randomDeviceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var randomDeviceCompletion = new TaskCompletionSource<RandomDeviceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();
        randomDevice.CreateRandomProfileAsync(Arg.Any<RandomDeviceRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                randomDeviceStarted.TrySetResult();
                return randomDeviceCompletion.Task;
            });
        ISimProfileService simProfileService = Substitute.For<ISimProfileService>();
        simProfileService.CreateRandomProfile(Arg.Any<CarrierCountryOption?>(), Arg.Any<CarrierOption?>())
            .Returns(new SimProfile
            {
                Imsi = "sim-imsi",
                Iccid = "sim-iccid",
                PhoneNumber = "sim-phone",
                OperatorName = "SIM operator"
            });
        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            randomDevice: randomDevice,
            simProfileService: simProfileService);
        await viewModel.InitializeAsync(CancellationToken.None);

        Task randomDeviceTask = viewModel.RandomDeviceCommand.ExecuteAsync(null);
        await randomDeviceStarted.Task;

        Assert.IsFalse(viewModel.CanInteractWithSelectedDevice);
        Assert.IsTrue(viewModel.SelectedDevice!.IsActionBusy);
        Assert.IsFalse(viewModel.SelectedDevice.CanEdit);
        Assert.IsFalse(viewModel.RandomSimCommand.CanExecute(null));
        await viewModel.RandomSimCommand.ExecuteAsync(null);
        simProfileService.DidNotReceiveWithAnyArgs().CreateRandomProfile(default, default);

        randomDeviceCompletion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice
            {
                Model = "Generated model",
                Imsi = "device-imsi",
                Iccid = "device-iccid",
                SimPhoneNumber = "device-phone",
                SimOperatorName = "Device operator"
            }));
        await randomDeviceTask;

        Assert.IsTrue(viewModel.CanInteractWithSelectedDevice);
        Assert.IsFalse(viewModel.SelectedDevice.IsActionBusy);
        Assert.IsTrue(viewModel.SelectedDevice.CanEdit);
        Assert.IsTrue(viewModel.RandomSimCommand.CanExecute(null));
        await viewModel.RandomSimCommand.ExecuteAsync(null);

        simProfileService.Received(1).CreateRandomProfile(
            Arg.Any<CarrierCountryOption?>(),
            Arg.Any<CarrierOption?>());
        Assert.AreEqual("sim-imsi", viewModel.DeviceInfo.Imsi);
        Assert.AreEqual("sim-iccid", viewModel.DeviceInfo.Iccid);
        Assert.AreEqual("sim-phone", viewModel.DeviceInfo.PhoneNumber);
        Assert.AreEqual("SIM operator", viewModel.DeviceInfo.Operator);
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task DeviceActionGuard_BlocksOnlyBusySerialAndKeepsRandomInfoIsolatedPerDevice()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new()
            {
                Serial = "A",
                Name = "Phone A",
                Type = "Phone",
                ChangeOptions = new DeviceChangeOptions { UseDefaultMode = false }
            },
            new()
            {
                Serial = "B",
                Name = "Phone B",
                Type = "Phone",
                ChangeOptions = new DeviceChangeOptions { UseDefaultMode = false }
            }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(
                storedDevices,
                [
                    new AdbDevice("A", AdbDeviceStatus.Online),
                    new AdbDevice("B", AdbDeviceStatus.Online)
                ]));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        var firstRandomStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRandomCompletion = new TaskCompletionSource<RandomDeviceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();
        randomDevice.CreateRandomProfileAsync(Arg.Any<RandomDeviceRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref invocationCount) == 1)
                {
                    firstRandomStarted.TrySetResult();
                    return firstRandomCompletion.Task;
                }

                return Task.FromResult(new RandomDeviceResult(
                    RandomDeviceStatus.Created,
                    new DeviceInfoApiDevice { Model = "Profile B" }));
            });
        var viewModel = CreateViewModel(deviceList, carriers, randomDevice: randomDevice);
        await viewModel.InitializeAsync(CancellationToken.None);

        DeviceRowViewModel deviceA = viewModel.Devices.Single(device => device.Serial == "A");
        DeviceRowViewModel deviceB = viewModel.Devices.Single(device => device.Serial == "B");
        viewModel.SelectedDevice = deviceA;
        Task deviceAAction = viewModel.RandomDeviceCommand.ExecuteAsync(null);
        await firstRandomStarted.Task;

        Assert.IsTrue(deviceA.IsActionBusy);
        Assert.IsFalse(deviceA.CanEdit);
        Assert.IsFalse(viewModel.CanInteractWithSelectedDevice);
        Assert.IsFalse(viewModel.RandomDeviceCommand.CanExecute(null));
        Assert.IsFalse(viewModel.RandomSimCommand.CanExecute(null));
        Dictionary<string, bool> busyDeviceActionStates = GetGuardedActionStates(viewModel);
        Assert.IsTrue(
            busyDeviceActionStates.All(pair => !pair.Value),
            CreateActionStateMessage(busyDeviceActionStates));
        Dictionary<string, bool> busyContextMenuStates = GetContextMenuActionStates(viewModel);
        Assert.IsTrue(
            busyContextMenuStates.All(pair => !pair.Value),
            CreateActionStateMessage(busyContextMenuStates));

        viewModel.SelectedDevice = deviceB;

        Assert.IsFalse(deviceB.IsActionBusy);
        Assert.IsTrue(deviceB.CanEdit);
        Assert.IsTrue(viewModel.CanInteractWithSelectedDevice);
        Assert.IsTrue(viewModel.RandomDeviceCommand.CanExecute(null));
        Dictionary<string, bool> otherDeviceActionStates = GetGuardedActionStates(viewModel);
        Assert.IsTrue(
            otherDeviceActionStates.All(pair => pair.Value),
            CreateActionStateMessage(otherDeviceActionStates));

        await viewModel.RandomDeviceCommand.ExecuteAsync(null);
        Assert.AreEqual("Profile B", viewModel.DeviceInfo.Model);

        firstRandomCompletion.SetResult(new RandomDeviceResult(
            RandomDeviceStatus.Created,
            new DeviceInfoApiDevice { Model = "Profile A" }));
        await deviceAAction;

        Assert.AreEqual("Profile B", viewModel.DeviceInfo.Model);
        Assert.IsFalse(deviceA.IsActionBusy);
        Assert.IsTrue(deviceA.CanEdit);

        viewModel.SelectedDevice = deviceA;
        Assert.AreEqual("Profile A", viewModel.DeviceInfo.Model);
        Assert.IsTrue(viewModel.CanInteractWithSelectedDevice);

        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public void SelectedBrand_FiltersAndroidVersionsAndClearsIncompatibleSelection()
    {
        var viewModel = CreateViewModel(
            Substitute.For<IDeviceListService>(),
            Substitute.For<ICarrierDataService>());

        viewModel.SelectedAndroidVersion = "Android 15";
        viewModel.SelectedBrand = "OPPO";

        CollectionAssert.AreEqual(
            new[] { "Random", "Android 14" },
            viewModel.AndroidVersions.ToArray());
        Assert.AreEqual("Random", viewModel.SelectedAndroidVersion);

        var expectedVersions = new Dictionary<string, string[]>
        {
            ["Google"] = ["Random", "Android 13", "Android 14", "Android 15"],
            ["Samsung"] = ["Random", "Android 13", "Android 14", "Android 15"],
            ["Xiaomi"] = ["Random", "Android 13", "Android 14", "Android 15"],
            ["OnePlus"] = ["Random", "Android 13"],
            ["OPPO"] = ["Random", "Android 14"],
            ["vivo"] = ["Random", "Android 14"]
        };

        foreach ((string brand, string[] versions) in expectedVersions)
        {
            viewModel.SelectedBrand = brand;
            CollectionAssert.AreEqual(versions, viewModel.AndroidVersions.ToArray(), brand);
        }

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

    [TestMethod]
    public async Task RandomChangeAndWipeDeviceCommand_DeviceOffline_SetsDeviceMustBeOnlineLog()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new() { Serial = "A", Name = "Phone", Type = "Phone" }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, [new AdbDevice("A", AdbDeviceStatus.Offline)]));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        IChangeDeviceConfirmationDialogService confirmation = Substitute.For<IChangeDeviceConfirmationDialogService>();
        IDeviceChangeService deviceChange = Substitute.For<IDeviceChangeService>();
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();

        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            randomDevice: randomDevice,
            changeDeviceConfirmation: confirmation,
            deviceChange: deviceChange);
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.SelectedDevice = viewModel.Devices[0];
        await viewModel.RandomChangeAndWipeDeviceCommand.ExecuteAsync(null);

        await randomDevice.DidNotReceiveWithAnyArgs().CreateRandomProfileAsync(default!, default);
        await confirmation.DidNotReceiveWithAnyArgs().ShowChangeDeviceConfirmationAsync(default!, default!, default!, default);
        await deviceChange.DidNotReceiveWithAnyArgs().ChangeAsync(default!, default!, default, default!, default, default);
        Assert.AreEqual(DeviceLogResourceKeys.Ready, viewModel.Devices[0].Process);
        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task RandomChangeAndWipeDeviceCommand_DeviceOnline_CreatesProfileConfirmsAndExecutesChangeAsync()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new()
            {
                Serial = "A",
                Name = "Phone",
                Type = "Phone",
                ChangeOptions = new DeviceChangeOptions
                {
                    UseDefaultMode = false,
                    ChangeAndroidId = true
                }
            }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, [new AdbDevice("A", AdbDeviceStatus.Online)]));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        var randomProfile = new DeviceInfoApiDevice
        {
            Brand = "google",
            Model = "Pixel 8",
            Name = "shiba",
            Fingerprint = "google/shiba/shiba:14/UD1A.230803.041/10808077:user/release-keys"
        };
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();
        randomDevice.CreateRandomProfileAsync(Arg.Any<RandomDeviceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RandomDeviceResult(RandomDeviceStatus.Created, randomProfile));
        IChangeDeviceConfirmationDialogService confirmation = Substitute.For<IChangeDeviceConfirmationDialogService>();
        confirmation.ShowChangeDeviceConfirmationAsync(
            "Phone",
            "A",
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(true);
        IDeviceChangeService deviceChange = Substitute.For<IDeviceChangeService>();

        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            randomDevice: randomDevice,
            changeDeviceConfirmation: confirmation,
            deviceChange: deviceChange);
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.SelectedDevice = viewModel.Devices[0];
        await viewModel.RandomChangeAndWipeDeviceCommand.ExecuteAsync(null);

        await randomDevice.Received(1).CreateRandomProfileAsync(
            Arg.Any<RandomDeviceRequest>(),
            Arg.Any<CancellationToken>());
        await confirmation.Received(1).ShowChangeDeviceConfirmationAsync(
            "Phone",
            "A",
            Arg.Is<DeviceChangeOptions>(options =>
                !options.UseDefaultMode
                && options.ChangeAndroidId),
            Arg.Any<CancellationToken>());
        await deviceChange.Received(1).ChangeAsync(
            "A",
            Arg.Is<DeviceInfoApiDevice>(p => p.Model == "Pixel 8" && p.Brand == "google"),
            Arg.Any<bool>(),
            Arg.Is<DeviceChangeOptions>(options =>
                !options.UseDefaultMode
                && options.ChangeAndroidId),
            Arg.Any<IProgress<DeviceChangeStage>>(),
            Arg.Any<CancellationToken>());

        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task RandomChangeAndWipeDeviceCommand_UserCancelsConfirmation_DoesNotExecuteRandomOrChangeAsync()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new()
            {
                Serial = "A",
                Name = "Phone",
                Type = "Phone",
                ChangeOptions = new DeviceChangeOptions
                {
                    UseDefaultMode = false,
                    ChangeAndroidId = true
                }
            }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, [new AdbDevice("A", AdbDeviceStatus.Online)]));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();
        IChangeDeviceConfirmationDialogService confirmation = Substitute.For<IChangeDeviceConfirmationDialogService>();
        confirmation.ShowChangeDeviceConfirmationAsync(
            "Phone",
            "A",
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(false);
        IDeviceChangeService deviceChange = Substitute.For<IDeviceChangeService>();

        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            randomDevice: randomDevice,
            changeDeviceConfirmation: confirmation,
            deviceChange: deviceChange);
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.SelectedDevice = viewModel.Devices[0];
        await viewModel.RandomChangeAndWipeDeviceCommand.ExecuteAsync(null);

        await confirmation.Received(1).ShowChangeDeviceConfirmationAsync(
            "Phone",
            "A",
            Arg.Is<DeviceChangeOptions>(options =>
                !options.UseDefaultMode
                && options.ChangeAndroidId),
            Arg.Any<CancellationToken>());
        await randomDevice.DidNotReceiveWithAnyArgs().CreateRandomProfileAsync(default!, default);
        await deviceChange.DidNotReceiveWithAnyArgs().ChangeAsync(default!, default!, default, default!, default, default);

        Assert.AreEqual(DeviceLogResourceKeys.Ready, viewModel.Devices[0].Process);

        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    [TestMethod]
    public async Task RandomChangeAndWipeDeviceCommand_RandomFailed_OpensConfirmationButDoesNotExecuteChangeAsync()
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
        IRandomDeviceService randomDevice = Substitute.For<IRandomDeviceService>();
        randomDevice.CreateRandomProfileAsync(Arg.Any<RandomDeviceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RandomDeviceResult(RandomDeviceStatus.Failed, null));
        IChangeDeviceConfirmationDialogService confirmation = Substitute.For<IChangeDeviceConfirmationDialogService>();
        confirmation.ShowChangeDeviceConfirmationAsync(
            "Phone",
            "A",
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(true);
        IDeviceChangeService deviceChange = Substitute.For<IDeviceChangeService>();

        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            randomDevice: randomDevice,
            changeDeviceConfirmation: confirmation,
            deviceChange: deviceChange);
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.SelectedDevice = viewModel.Devices[0];
        await viewModel.RandomChangeAndWipeDeviceCommand.ExecuteAsync(null);

        await confirmation.Received(1).ShowChangeDeviceConfirmationAsync(
            "Phone",
            "A",
            Arg.Any<DeviceChangeOptions>(),
            Arg.Any<CancellationToken>());
        await randomDevice.Received(1).CreateRandomProfileAsync(
            Arg.Any<RandomDeviceRequest>(),
            Arg.Any<CancellationToken>());
        await deviceChange.DidNotReceiveWithAnyArgs().ChangeAsync(default!, default!, default, default!, default, default);

        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    private static Dictionary<string, bool> GetGuardedActionStates(DeviceManagerViewModel viewModel)
    {
        return new Dictionary<string, bool>
        {
            [nameof(DeviceManagerViewModel.ChangeDeviceCommand)] = viewModel.ChangeDeviceCommand.CanExecute(null),
            [nameof(DeviceManagerViewModel.ChangeWithoutWipeCommand)] = viewModel.ChangeWithoutWipeCommand.CanExecute(null),
            [nameof(DeviceManagerViewModel.WipeWithoutChangeCommand)] = viewModel.WipeWithoutChangeCommand.CanExecute(null),
            [nameof(DeviceManagerViewModel.OpenAdvancedChangeConfigCommand)] = viewModel.OpenAdvancedChangeConfigCommand.CanExecute(null),
            [nameof(DeviceManagerViewModel.RandomChangeAndWipeDeviceCommand)] = viewModel.RandomChangeAndWipeDeviceCommand.CanExecute(null),
            [nameof(DeviceManagerViewModel.ChangeSimCommand)] = viewModel.ChangeSimCommand.CanExecute(null),
            [nameof(DeviceManagerViewModel.ChangeLocationCommand)] = viewModel.ChangeLocationCommand.CanExecute(null),
            [nameof(DeviceManagerViewModel.ChangeTimezoneCommand)] = viewModel.ChangeTimezoneCommand.CanExecute(null),
            [nameof(DeviceManagerViewModel.UpdateIntegrityCommand)] = viewModel.UpdateIntegrityCommand.CanExecute(null),
            [nameof(DeviceManagerViewModel.InstallApkCommand)] = viewModel.InstallApkCommand.CanExecute(null),
            [nameof(DeviceManagerViewModel.FakeProxyCommand)] = viewModel.FakeProxyCommand.CanExecute(null),
            [nameof(DeviceManagerViewModel.StopFakeProxyCommand)] = viewModel.StopFakeProxyCommand.CanExecute(null)
        };
    }

    private static Dictionary<string, bool> GetContextMenuActionStates(DeviceManagerViewModel viewModel)
    {
        DeviceRowViewModel? targetDevice = viewModel.SelectedDevice;
        return new Dictionary<string, bool>
        {
            [nameof(DeviceManagerViewModel.ViewDeviceCommand)] = viewModel.ViewDeviceCommand.CanExecute(targetDevice),
            [nameof(DeviceManagerViewModel.ViewDeviceInfoCommand)] = viewModel.ViewDeviceInfoCommand.CanExecute(targetDevice),
            [nameof(DeviceManagerViewModel.RebootDeviceCommand)] = viewModel.RebootDeviceCommand.CanExecute(targetDevice),
            [nameof(DeviceManagerViewModel.DeleteDeviceCommand)] = viewModel.DeleteDeviceCommand.CanExecute(targetDevice)
        };
    }

    private static string CreateActionStateMessage(IReadOnlyDictionary<string, bool> actionStates)
    {
        return string.Join(", ", actionStates.Select(pair => $"{pair.Key}={pair.Value}"));
    }

    [TestMethod]
    public async Task FakeProxyCommand_ExecutesProxyWorkflow_DoesNotUpdateLocationOrTimezoneConfig()
    {
        StoredDeviceConfig[] storedDevices =
        [
            new()
            {
                Serial = "A",
                Name = "Phone",
                Type = "Phone",
                LocationMode = nameof(ChangeLocationMode.Config),
                LocationLatitude = "1.23",
                LocationLongitude = "4.56",
                TimezoneMode = nameof(ChangeTimezoneMode.Data),
                Timezone = "America/New_York"
            }
        ];
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, [new AdbDevice("A", AdbDeviceStatus.Online)]));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);

        IFakeProxyDialogService fakeProxyDialog = Substitute.For<IFakeProxyDialogService>();
        var fakeProxyResult = new FakeProxyDialogResult(
            "127.0.0.1",
            8080,
            "user",
            "pass",
            "SOCKS5",
            proxyChangeLocationByIp: true,
            proxyChangeTimezoneByIp: true);
        fakeProxyDialog.ShowFakeProxyDialogAsync("A", "Phone", Arg.Any<CancellationToken>())
            .Returns(fakeProxyResult);

        IProxyWorkflowService proxyWorkflowService = Substitute.For<IProxyWorkflowService>();
        var workflowResult = new ProxyWorkflowResult(
            locationUpdateFailed: false,
            timezoneUpdateFailed: false,
            appliedLatitude: "10.0",
            appliedLongitude: "106.0",
            appliedTimezone: "Asia/Ho_Chi_Minh");
        proxyWorkflowService.ApplyAsync("A", fakeProxyResult, Arg.Any<CancellationToken>())
            .Returns(workflowResult);

        IDeviceConfigService deviceConfig = Substitute.For<IDeviceConfigService>();

        var viewModel = CreateViewModel(
            deviceList,
            carriers,
            deviceConfig: deviceConfig,
            fakeProxyDialog: fakeProxyDialog,
            proxyWorkflowService: proxyWorkflowService);
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.SelectedDevice = viewModel.Devices[0];
        await viewModel.FakeProxyCommand.ExecuteAsync(null);

        await proxyWorkflowService.Received(1).ApplyAsync("A", fakeProxyResult, Arg.Any<CancellationToken>());
        await deviceConfig.DidNotReceiveWithAnyArgs().SaveLocationConfigAsync(
            default!, default!, default!, default!, default!, default!);
        await deviceConfig.DidNotReceiveWithAnyArgs().SaveLocationConfigAsync(
            default!, default!, default!, default!, default!, default!, default!, default!);
        await deviceConfig.DidNotReceiveWithAnyArgs().SaveTimezoneConfigAsync(
            default!, default!, default!, default!, default!);

        Assert.AreEqual(nameof(ChangeLocationMode.Config), storedDevices[0].LocationMode);
        Assert.AreEqual("1.23", storedDevices[0].LocationLatitude);
        Assert.AreEqual("4.56", storedDevices[0].LocationLongitude);
        Assert.AreEqual(nameof(ChangeTimezoneMode.Data), storedDevices[0].TimezoneMode);
        Assert.AreEqual("America/New_York", storedDevices[0].Timezone);
    }

    [TestMethod]
    public async Task CopySerial_CanExecuteAndExecutesWithoutThrowing()
    {
        var storedDevices = new[]
        {
            new StoredDeviceConfig { Serial = "SERIAL_12345", Name = "TestDevice", Type = "Phone" }
        };
        IDeviceListService deviceList = Substitute.For<IDeviceListService>();
        deviceList.LoadStoredDevicesAsync(Arg.Any<CancellationToken>()).Returns(storedDevices);
        deviceList.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(new DeviceListSnapshot(storedDevices, []));
        ICarrierDataService carriers = Substitute.For<ICarrierDataService>();
        carriers.GetCarrierProfilesAsync(Arg.Any<CancellationToken>()).Returns([]);

        var viewModel = CreateViewModel(deviceList, carriers);
        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsNotNull(viewModel.CopySerialCommand);
        Assert.IsTrue(viewModel.CopySerialCommand.CanExecute(viewModel.Devices[0]));

        await viewModel.CopySerialCommand.ExecuteAsync(viewModel.Devices[0]);

        await viewModel.DeactivateAsync();
        viewModel.Dispose();
    }

    private static DeviceManagerViewModel CreateViewModel(
        IDeviceListService deviceList,
        ICarrierDataService carriers,
        IDeviceConfigService? deviceConfig = null,
        IPollingService? polling = null,
        IRandomDeviceService? randomDevice = null,
        IRandomDeviceInfoDialogService? randomDeviceInfoDialog = null,
        ISimProfileService? simProfileService = null,
        IDeleteDeviceConfirmationDialogService? deleteDeviceConfirmation = null,
        IChangeDeviceConfirmationDialogService? changeDeviceConfirmation = null,
        IDeviceActionConfirmationDialogService? deviceActionConfirmationDialog = null,
        IAdvancedChangeConfigDialogService? advancedChangeConfig = null,
        IDeviceChangeService? deviceChange = null,
        IDeviceActionGuardService? deviceActionGuard = null,
        IFakeProxyDialogService? fakeProxyDialog = null,
        IProxyWorkflowService? proxyWorkflowService = null,
        AppSettings? settings = null)
    {
        return new DeviceManagerViewModel(
            Substitute.For<IAddDevicesDialogService>(),
            carriers,
            Substitute.For<IChangeTimezoneDialogService>(),
            Substitute.For<IDeviceTimezoneService>(),
            Substitute.For<IChangeLocationDialogService>(),
            Substitute.For<IDeviceLocationService>(),
            fakeProxyDialog ?? Substitute.For<IFakeProxyDialogService>(),
            Substitute.For<IProxyService>(),
            proxyWorkflowService ?? Substitute.For<IProxyWorkflowService>(),
            Substitute.For<IUpdateIntegrityDialogService>(),
            Substitute.For<IDeviceIntegrityService>(),
            Substitute.For<IInstallPackageDialogService>(),
            Substitute.For<IDeviceViewerDialogService>(),
            deleteDeviceConfirmation ?? Substitute.For<IDeleteDeviceConfirmationDialogService>(),
            changeDeviceConfirmation ?? Substitute.For<IChangeDeviceConfirmationDialogService>(),
            deviceActionConfirmationDialog ?? CreateDeviceActionConfirmationDialogService(),
            advancedChangeConfig ?? Substitute.For<IAdvancedChangeConfigDialogService>(),
            randomDeviceInfoDialog ?? Substitute.For<IRandomDeviceInfoDialogService>(),
            deviceList,
            new DeviceSelectionService(),
            deviceConfig ?? Substitute.For<IDeviceConfigService>(),
            randomDevice ?? Substitute.For<IRandomDeviceService>(),
            simProfileService ?? Substitute.For<ISimProfileService>(),
            deviceActionGuard ?? new DeviceActionGuardService(),
            Substitute.For<IDeviceActionService>(),
            deviceChange ?? Substitute.For<IDeviceChangeService>(),
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

    private static IDeviceActionConfirmationDialogService CreateDeviceActionConfirmationDialogService()
    {
        IDeviceActionConfirmationDialogService confirmation = Substitute.For<IDeviceActionConfirmationDialogService>();
        confirmation.ConfirmChangeWithoutWipeAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        confirmation.ConfirmWipeWithoutChangeAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        confirmation.ConfirmChangeSimAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        return confirmation;
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
