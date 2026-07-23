using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Fakes;
using DeepDroidChanger.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.ViewModels.Dialogs;

[TestClass]
public sealed class ChangeLocationViewModelTests
{
    [TestMethod]
    public async Task InitializeAsync_CanceledLoad_PropagatesCancellation()
    {
        IDeviceStoreService store = Substitute.For<IDeviceStoreService>();
        ILocationDataService locationService = Substitute.For<ILocationDataService>();
        using var cancellation = new CancellationTokenSource();
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<StoredDeviceConfig>>>(
            _ =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            });
        var viewModel = new ChangeLocationViewModel(
            locationService,
            store,
            DialogViewModelTestFactory.CreateLocalizationService(),
            NullLogger<ChangeLocationViewModel>.Instance);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => viewModel.InitializeAsync(cancellation.Token));
    }

    [TestMethod]
    public async Task EditingThenClosing_PersistsDialogConfigWithoutMainViewModel()
    {
        var config = new StoredDeviceConfig { Serial = "ABC" };
        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(config);
        ILocationDataService locationService = CreateMockLocationDataService();
        var viewModel = new ChangeLocationViewModel(
            locationService,
            store,
            DialogViewModelTestFactory.CreateLocalizationService(),
            NullLogger<ChangeLocationViewModel>.Instance)
        {
            DeviceSerial = "abc",
        };
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.IsConfigMode = true;
        viewModel.Latitude = "10.1234";
        viewModel.Longitude = "106.1234";
        await viewModel.FlushPendingConfigSaveAsync();

        Assert.AreEqual(nameof(ChangeLocationMode.Config), config.LocationMode);
        Assert.AreEqual("10.1234", config.LocationLatitude);
        Assert.AreEqual("106.1234", config.LocationLongitude);
        Assert.IsFalse(string.IsNullOrEmpty(config.LocationCountryCode));
        Assert.IsFalse(string.IsNullOrEmpty(config.LocationCityName));
        await store.Received().UpdateAsync(
            "abc",
            Arg.Any<Action<StoredDeviceConfig>>(),
            CancellationToken.None);
    }

    [TestMethod]
    public async Task InitializeAsync_RestoresSavedLocationDialogConfig()
    {
        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(new StoredDeviceConfig
        {
            Serial = "ABC",
            LocationMode = nameof(ChangeLocationMode.Config),
            LocationLatitude = "21.0285",
            LocationLongitude = "105.8542"
        });
        ILocationDataService locationService = CreateMockLocationDataService();
        var viewModel = new ChangeLocationViewModel(
            locationService,
            store,
            DialogViewModelTestFactory.CreateLocalizationService(),
            NullLogger<ChangeLocationViewModel>.Instance)
        {
            DeviceSerial = "abc",
        };

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(viewModel.IsConfigMode);
        Assert.IsFalse(viewModel.IsDeviceIpMode);
        Assert.AreEqual("21.0285", viewModel.Latitude);
        Assert.AreEqual("105.8542", viewModel.Longitude);
    }

    [TestMethod]
    public async Task InitializeAsync_RestoresCountryAndCityFromSavedConfig()
    {
        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(new StoredDeviceConfig
        {
            Serial = "ABC",
            LocationMode = nameof(ChangeLocationMode.Config),
            LocationLatitude = "21.0285",
            LocationLongitude = "105.8542",
            LocationCountryCode = "VN",
            LocationCityName = "Hanoi"
        });
        ILocationDataService locationService = CreateMockLocationDataService();
        var viewModel = new ChangeLocationViewModel(
            locationService,
            store,
            DialogViewModelTestFactory.CreateLocalizationService(),
            NullLogger<ChangeLocationViewModel>.Instance)
        {
            DeviceSerial = "abc",
        };

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsNotNull(viewModel.SelectedCountry);
        Assert.AreEqual("VN", viewModel.SelectedCountry.CountryCode);
        Assert.IsNotNull(viewModel.SelectedLocation);
        Assert.AreEqual("Hanoi", viewModel.SelectedLocation.CityName);
    }

    [TestMethod]
    public async Task Save_PersistsCountryCodeAndCityName()
    {
        var config = new StoredDeviceConfig { Serial = "ABC" };
        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(config);
        ILocationDataService locationService = CreateMockLocationDataService();
        var viewModel = new ChangeLocationViewModel(
            locationService,
            store,
            DialogViewModelTestFactory.CreateLocalizationService(),
            NullLogger<ChangeLocationViewModel>.Instance)
        {
            DeviceSerial = "abc",
        };
        await viewModel.InitializeAsync(CancellationToken.None);

        var vietnam = viewModel.Countries.FirstOrDefault(c => c.CountryName == "Vietnam");
        Assert.IsNotNull(vietnam);
        viewModel.SelectedCountry = vietnam;
        await viewModel.FlushPendingConfigSaveAsync();

        Assert.AreEqual("VN", config.LocationCountryCode);
        Assert.AreEqual("Hanoi", config.LocationCityName);
    }

    [TestMethod]
    public async Task SelectingCountryLocation_UpdatesLatitudeAndLongitudeInputs()
    {
        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(new StoredDeviceConfig { Serial = "ABC" });
        ILocationDataService locationService = CreateMockLocationDataService();
        var viewModel = new ChangeLocationViewModel(
            locationService,
            store,
            DialogViewModelTestFactory.CreateLocalizationService(),
            NullLogger<ChangeLocationViewModel>.Instance)
        {
            DeviceSerial = "abc",
        };

        await viewModel.InitializeAsync(CancellationToken.None);

        var vietnam = viewModel.Countries.FirstOrDefault(c => c.CountryName == "Vietnam");
        Assert.IsNotNull(vietnam);
        viewModel.SelectedCountry = vietnam;

        Assert.IsNotNull(viewModel.SelectedLocation);
        Assert.AreEqual("21.0285", viewModel.Latitude);
        Assert.AreEqual("105.8542", viewModel.Longitude);
    }

    [TestMethod]
    public async Task Save_PersistsOnlyConfirmedValues()
    {
        var config = new StoredDeviceConfig { Serial = "ABC" };
        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(config);
        ILocationDataService locationService = CreateMockLocationDataService();
        var viewModel = new ChangeLocationViewModel(
            locationService,
            store,
            DialogViewModelTestFactory.CreateLocalizationService(),
            NullLogger<ChangeLocationViewModel>.Instance)
        {
            DeviceSerial = "abc",
        };
        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.IsConfigMode = true;
        viewModel.Latitude = "10.1234";
        viewModel.Longitude = "106.1234";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.AreEqual("10.1234", config.LocationLatitude);
        Assert.AreEqual("106.1234", config.LocationLongitude);
        await store.Received().UpdateAsync(
            "abc",
            Arg.Any<Action<StoredDeviceConfig>>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task InitializeAsync_RestoresCountryAndCityEvenWhenCoordinatesWereManuallyEdited()
    {
        // Scenario: User selected Vietnam > Hanoi, then manually changed lat/lon to custom values.
        // On reopen, country and city should still restore to Vietnam > Hanoi,
        // and the custom lat/lon should be preserved (not overwritten).
        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(new StoredDeviceConfig
        {
            Serial = "ABC",
            LocationMode = nameof(ChangeLocationMode.Config),
            LocationLatitude = "10.5000",    // custom value, not Hanoi's 21.0285
            LocationLongitude = "106.5000",  // custom value, not Hanoi's 105.8542
            LocationCountryCode = "VN",
            LocationCityName = "Hanoi"
        });
        ILocationDataService locationService = CreateMockLocationDataService();
        var viewModel = new ChangeLocationViewModel(
            locationService,
            store,
            DialogViewModelTestFactory.CreateLocalizationService(),
            NullLogger<ChangeLocationViewModel>.Instance)
        {
            DeviceSerial = "abc",
        };

        await viewModel.InitializeAsync(CancellationToken.None);

        // Country and city should be restored from saved config
        Assert.IsNotNull(viewModel.SelectedCountry);
        Assert.AreEqual("VN", viewModel.SelectedCountry.CountryCode);
        Assert.IsNotNull(viewModel.SelectedLocation);
        Assert.AreEqual("Hanoi", viewModel.SelectedLocation.CityName);

        // Custom lat/lon should be preserved, not overwritten by Hanoi's coordinates
        Assert.AreEqual("10.5000", viewModel.Latitude);
        Assert.AreEqual("106.5000", viewModel.Longitude);
    }

    [TestMethod]
    public async Task InitializeAsync_RestoresCountryAndCityDirectlyFromSavedConfig()
    {
        // Scenario: Saved config has country code VN, city name Ho Chi Minh City, and custom coordinates.
        // Restoration directly loads VN and Ho Chi Minh City without any coordinate guard checks.
        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(new StoredDeviceConfig
        {
            Serial = "ABC",
            LocationMode = nameof(ChangeLocationMode.Config),
            LocationLatitude = "10.7599",
            LocationLongitude = "106.6667",
            LocationCountryCode = "VN",
            LocationCityName = "Ho Chi Minh City"
        });
        ILocationDataService locationService = CreateMockLocationDataService();
        var viewModel = new ChangeLocationViewModel(
            locationService,
            store,
            DialogViewModelTestFactory.CreateLocalizationService(),
            NullLogger<ChangeLocationViewModel>.Instance)
        {
            DeviceSerial = "abc",
        };

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.IsNotNull(viewModel.SelectedCountry);
        Assert.AreEqual("VN", viewModel.SelectedCountry.CountryCode);
        Assert.IsNotNull(viewModel.SelectedLocation);
        Assert.AreEqual("Ho Chi Minh City", viewModel.SelectedLocation.CityName);
        Assert.AreEqual("10.7599", viewModel.Latitude);
        Assert.AreEqual("106.6667", viewModel.Longitude);
    }

    [TestMethod]
    public async Task FullFlow_SelectCountryEditLatSaveReopen_RestoresCountryAndCity()
    {
        // === PHASE 1: First open — select Vietnam > Ho Chi Minh City, then edit latitude ===
        var config = new StoredDeviceConfig { Serial = "ABC" };
        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(config);
        ILocationDataService locationService = CreateMockLocationDataService();

        var vm1 = new ChangeLocationViewModel(
            locationService,
            store,
            DialogViewModelTestFactory.CreateLocalizationService(),
            NullLogger<ChangeLocationViewModel>.Instance)
        {
            DeviceSerial = "abc",
        };
        await vm1.InitializeAsync(CancellationToken.None);

        // Select Vietnam
        var vietnam = vm1.Countries.FirstOrDefault(c => c.CountryName == "Vietnam");
        Assert.IsNotNull(vietnam);
        vm1.SelectedCountry = vietnam;

        // Select Ho Chi Minh City
        var hcm = vm1.CountryLocations.FirstOrDefault(loc => loc.CityName == "Ho Chi Minh City");
        Assert.IsNotNull(hcm);
        vm1.SelectedLocation = hcm;
        Assert.AreEqual("10.7500", vm1.Latitude);
        Assert.AreEqual("106.6667", vm1.Longitude);

        // User manually edits latitude
        vm1.Latitude = "10.7599";

        // Save and close
        await vm1.SaveCommand.ExecuteAsync(null);
        await vm1.FlushPendingConfigSaveAsync();

        // Verify config was saved correctly
        Assert.AreEqual("VN", config.LocationCountryCode, "CountryCode should be saved after manual lat edit");
        Assert.AreEqual("Ho Chi Minh City", config.LocationCityName, "CityName should be saved after manual lat edit");
        Assert.AreEqual("10.7599", config.LocationLatitude, "Manual latitude should be saved");
        Assert.AreEqual("106.6667", config.LocationLongitude, "Longitude should be saved");

        // === PHASE 2: Reopen dialog — should restore VN / Ho Chi Minh City with custom lat ===
        var vm2 = new ChangeLocationViewModel(
            locationService,
            store,
            DialogViewModelTestFactory.CreateLocalizationService(),
            NullLogger<ChangeLocationViewModel>.Instance)
        {
            DeviceSerial = "abc",
        };
        await vm2.InitializeAsync(CancellationToken.None);

        Assert.IsNotNull(vm2.SelectedCountry, "Country should be restored on reopen");
        Assert.AreEqual("VN", vm2.SelectedCountry.CountryCode, "Country should be Vietnam on reopen");
        Assert.IsNotNull(vm2.SelectedLocation, "Location should be restored on reopen");
        Assert.AreEqual("Ho Chi Minh City", vm2.SelectedLocation.CityName, "City should be Ho Chi Minh City on reopen");
        Assert.AreEqual("10.7599", vm2.Latitude, "Custom latitude should be preserved on reopen");
        Assert.AreEqual("106.6667", vm2.Longitude, "Longitude should be preserved on reopen");
    }

    [TestMethod]
    public async Task DeviceIpMode_DoesNotOverwriteCountryAndCityInStoredConfig()
    {
        var config = new StoredDeviceConfig
        {
            Serial = "ABC",
            LocationMode = nameof(ChangeLocationMode.Config),
            LocationCountryCode = "VN",
            LocationCityName = "Ho Chi Minh City",
            LocationLatitude = "10.7500",
            LocationLongitude = "106.6667"
        };

        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(config);
        ILocationDataService locationService = CreateMockLocationDataService();

        var viewModel = new ChangeLocationViewModel(
            locationService,
            store,
            DialogViewModelTestFactory.CreateLocalizationService(),
            NullLogger<ChangeLocationViewModel>.Instance)
        {
            DeviceSerial = "abc",
        };
        await viewModel.InitializeAsync(CancellationToken.None);

        // Switch to Device IP Mode
        viewModel.IsDeviceIpMode = true;
        await viewModel.FlushPendingConfigSaveAsync();

        // Country and City in config should NOT be overwritten (remain VN / Ho Chi Minh City)
        Assert.AreEqual(nameof(ChangeLocationMode.DeviceIp), config.LocationMode);
        Assert.AreEqual("VN", config.LocationCountryCode, "Device IP mode should not clear saved CountryCode");
        Assert.AreEqual("Ho Chi Minh City", config.LocationCityName, "Device IP mode should not clear saved CityName");
    }

    [TestMethod]
    public async Task ApplySelectedLocationCoordinates_ReloadsDefaultCoordinatesForSameSelectedCity()
    {
        // Scenario: User opens dialog with restored US > New York and custom latitude 17.0000.
        // User clicks New York in the dropdown again.
        // Executing ApplySelectedLocationCoordinatesCommand should reload New York's default coordinates (40.7128, -74.0060).
        var config = new StoredDeviceConfig
        {
            Serial = "ABC",
            LocationMode = nameof(ChangeLocationMode.Config),
            LocationCountryCode = "US",
            LocationCityName = "New York",
            LocationLatitude = "17.0000",
            LocationLongitude = "-74.0060"
        };

        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(config);
        ILocationDataService locationService = CreateMockLocationDataService();

        var viewModel = new ChangeLocationViewModel(
            locationService,
            store,
            DialogViewModelTestFactory.CreateLocalizationService(),
            NullLogger<ChangeLocationViewModel>.Instance)
        {
            DeviceSerial = "abc",
        };
        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.AreEqual("New York", viewModel.SelectedLocation?.CityName);
        Assert.AreEqual("17.0000", viewModel.Latitude);

        // User re-selects New York from dropdown (triggers ApplySelectedLocationCoordinatesCommand)
        viewModel.ApplySelectedLocationCoordinatesCommand.Execute(null);

        Assert.AreEqual("40.7128", viewModel.Latitude, "Latitude should be reloaded from New York's default coordinates");
        Assert.AreEqual("-74.0060", viewModel.Longitude, "Longitude should be reloaded from New York's default coordinates");
    }

    private static ILocationDataService CreateMockLocationDataService()
    {
        ILocationDataService service = Substitute.For<ILocationDataService>();
        var locations = new List<LocationOption>
        {
            new LocationOption("AF", "Afghanistan", "Kabul", "Asia/Kabul", "UTC +04:30", 34.5167, 69.2),
            new LocationOption("US", "United States", "New York", "America/New_York", "UTC -04:00", 40.7128, -74.0060),
            new LocationOption("VN", "Vietnam", "Hanoi", "Asia/Ho_Chi_Minh", "UTC +07:00", 21.0285, 105.8542),
            new LocationOption("VN", "Vietnam", "Ho Chi Minh City", "Asia/Ho_Chi_Minh", "UTC +07:00", 10.75, 106.666667)
        };
        service.GetLocationsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<LocationOption>>(locations));
        return service;
    }
}
