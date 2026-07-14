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
        var viewModel = CreateViewModel(store, Substitute.For<ITimezoneDataService>());

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => viewModel.InitializeAsync(cancellation.Token));
    }

    [TestMethod]
    public async Task EditingThenClosing_PersistsDialogAndDataPathConfigWithoutMainViewModel()
    {
        var config = new StoredDeviceConfig { Serial = "ABC" };
        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(config);
        ITimezoneDataService timezones = Substitute.For<ITimezoneDataService>();
        ISettingsService settingsService = Substitute.For<ISettingsService>();
        var appSettings = new AppSettings();
        var option = new TimezoneOption("VN", "Vietnam", "Asia/Ho_Chi_Minh", "+07:00", "Vietnam — Asia/Ho_Chi_Minh (+07:00)");
        timezones.GetTimezonesAsync(Arg.Any<CancellationToken>()).Returns([option]);
        var viewModel = CreateViewModel(store, timezones, settingsService, appSettings);
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.SelectedTimezone = option;
        viewModel.DeviceDataFilePath = "device-data.json";
        await viewModel.FlushPendingConfigSaveAsync();

        Assert.AreEqual(nameof(ChangeTimezoneMode.Data), config.TimezoneMode);
        Assert.AreEqual("Asia/Ho_Chi_Minh", config.Timezone);
        Assert.AreEqual("device-data.json", appSettings.DeviceDataFilePath);
        await settingsService.Received().SaveAsync(appSettings, CancellationToken.None);
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
        ITimezoneDataService timezones = Substitute.For<ITimezoneDataService>();
        var vietnam = new TimezoneOption("VN", "Vietnam", "Asia/Ho_Chi_Minh", "+07:00", "Vietnam — Asia/Ho_Chi_Minh (+07:00)");
        var unitedStates = new TimezoneOption("US", "United States", "America/New_York", "-05:00", "United States — America/New_York (-05:00)");
        timezones.GetTimezonesAsync(Arg.Any<CancellationToken>()).Returns([vietnam, unitedStates]);
        var viewModel = CreateViewModel(store, timezones);

        await viewModel.InitializeAsync(CancellationToken.None);
        viewModel.TimezoneSearchText = "Vietnam";

        Assert.AreSame(vietnam, viewModel.SelectedTimezone);
        Assert.HasCount(1, viewModel.FilteredTimezones);
        Assert.AreSame(vietnam, viewModel.FilteredTimezones[0]);
    }

    private static ChangeTimezoneViewModel CreateViewModel(
        IDeviceStoreService store,
        ITimezoneDataService timezones,
        ISettingsService? settingsService = null,
        AppSettings? appSettings = null)
    {
        return new ChangeTimezoneViewModel(
            timezones,
            store,
            settingsService ?? Substitute.For<ISettingsService>(),
            DialogViewModelTestFactory.CreateLocalizationService(),
            appSettings ?? new AppSettings(),
            NullLogger<ChangeTimezoneViewModel>.Instance)
        {
            DeviceSerial = "abc",
        };
    }
}
