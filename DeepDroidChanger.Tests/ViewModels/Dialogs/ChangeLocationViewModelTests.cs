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
        using var cancellation = new CancellationTokenSource();
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<StoredDeviceConfig>>>(
            _ =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            });
        var viewModel = new ChangeLocationViewModel(
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
        var viewModel = new ChangeLocationViewModel(
            store,
            DialogViewModelTestFactory.CreateLocalizationService(),
            NullLogger<ChangeLocationViewModel>.Instance)
        {
            DeviceSerial = "abc",
        };
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.Latitude = "10.1234";
        viewModel.Longitude = "106.1234";
        await viewModel.FlushPendingConfigSaveAsync();

        Assert.AreEqual(nameof(ChangeLocationMode.Config), config.LocationMode);
        Assert.AreEqual("10.1234", config.LocationLatitude);
        Assert.AreEqual("106.1234", config.LocationLongitude);
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
        var viewModel = new ChangeLocationViewModel(
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
    public async Task Save_PersistsOnlyConfirmedValues()
    {
        var config = new StoredDeviceConfig { Serial = "ABC" };
        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(config);
        var viewModel = new ChangeLocationViewModel(
            store,
            DialogViewModelTestFactory.CreateLocalizationService(),
            NullLogger<ChangeLocationViewModel>.Instance)
        {
            DeviceSerial = "abc",
        };
        await viewModel.InitializeAsync(CancellationToken.None);
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
}
