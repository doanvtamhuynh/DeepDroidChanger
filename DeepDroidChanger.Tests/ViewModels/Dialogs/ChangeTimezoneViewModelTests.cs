using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Fakes;
using DeepDroidChanger.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.ViewModels.Dialogs;

[TestClass]
public sealed class ChangeTimezoneViewModelTests
{
    [TestMethod]
    public async Task InitializeAsync_CanceledDeviceLoad_PropagatesCancellation()
    {
        IDeviceStoreService store = Substitute.For<IDeviceStoreService>();
        using var cancellation = new CancellationTokenSource();
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<StoredDeviceConfig>>>(
            _ =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            });
        var viewModel = CreateViewModel(store, Substitute.For<ILocationDataService>());

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => viewModel.InitializeAsync(cancellation.Token));
    }

    [TestMethod]
    public async Task EditingThenClosing_PersistsDialogConfigWithoutMainViewModel()
    {
        var config = new StoredDeviceConfig { Serial = "ABC" };
        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(config);
        ILocationDataService timezones = Substitute.For<ILocationDataService>();
        var option = new TimezoneOption("VN", "Vietnam", "Asia/Ho_Chi_Minh", "+07:00");
        timezones.GetTimezonesAsync(Arg.Any<CancellationToken>()).Returns([option]);
        var viewModel = CreateViewModel(store, timezones);
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.SelectedTimezone = option;
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.AreEqual(nameof(ChangeTimezoneMode.Data), config.TimezoneMode);
        Assert.AreEqual("Asia/Ho_Chi_Minh", config.Timezone);
    }

    [TestMethod]
    public async Task Initialize_RestoresSavedTimezoneAndFiltersByCountry()
    {
        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(new StoredDeviceConfig
        {
            Serial = "ABC",
            TimezoneMode = ChangeTimezoneMode.Data.ToString(),
            Timezone = "Asia/Ho_Chi_Minh",
        });
        ILocationDataService timezones = Substitute.For<ILocationDataService>();
        var vietnam = new TimezoneOption("VN", "Vietnam", "Asia/Ho_Chi_Minh", "+07:00");
        var unitedStates = new TimezoneOption("US", "United States", "America/New_York", "-05:00");
        timezones.GetTimezonesAsync(Arg.Any<CancellationToken>()).Returns([vietnam, unitedStates]);
        var viewModel = CreateViewModel(store, timezones);

        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.TimezoneSearchText = "Vietnam";

        Assert.AreSame(vietnam, viewModel.SelectedTimezone);
        Assert.HasCount(1, viewModel.FilteredCountries);
        Assert.AreEqual("Vietnam", viewModel.FilteredCountries[0].CountryName);
    }

    [TestMethod]
    public async Task SelectingUnitedStates_PopulatesOnlyUsTimezonesAndDefaultsToNewYork()
    {
        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(new StoredDeviceConfig { Serial = "ABC" });
        ILocationDataService timezones = Substitute.For<ILocationDataService>();
        var vietnam = new TimezoneOption("VN", "Vietnam", "Asia/Ho_Chi_Minh", "+07:00");
        var usNewYork = new TimezoneOption("US", "United States", "America/New_York", "-04:00");
        var usHonolulu = new TimezoneOption("US", "United States", "Pacific/Honolulu", "-10:00");
        timezones.GetTimezonesAsync(Arg.Any<CancellationToken>()).Returns([vietnam, usHonolulu, usNewYork]);

        var viewModel = CreateViewModel(store, timezones);
        await viewModel.InitializeAsync(CancellationToken.None);

        var usCountry = viewModel.Countries.First(c => c.CountryCode == "US");
        viewModel.SelectedCountry = usCountry;

        Assert.HasCount(2, viewModel.CountryTimezones);
        Assert.IsTrue(viewModel.CountryTimezones.All(tz => tz.CountryCode == "US"));
        Assert.AreEqual("America/New_York", viewModel.SelectedTimezone?.Timezone);
    }

    private static ChangeTimezoneViewModel CreateViewModel(
        IDeviceStoreService store,
        ILocationDataService timezones)
    {
        return new ChangeTimezoneViewModel(
            timezones,
            store,
            DialogViewModelTestFactory.CreateLocalizationService(),
            NullLogger<ChangeTimezoneViewModel>.Instance)
        {
            DeviceSerial = "abc",
        };
    }
}
