using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using DeepDroidChanger.Tests.Fakes;
using DeepDroidChanger.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeepDroidChanger.Tests.ViewModels.Dialogs;

[TestClass]
public sealed class FakeProxyViewModelTests
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
        var viewModel = CreateViewModel(store);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => viewModel.InitializeAsync(cancellation.Token));
    }

    [TestMethod]
    public async Task EditingThenClosing_PersistsProxyDialogConfigWithoutMainViewModel()
    {
        var config = new StoredDeviceConfig { Serial = "ABC" };
        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(config);
        var viewModel = CreateViewModel(store);
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.FullProxyString = "proxy.example:1080:user:password";
        viewModel.ProxyChangeLocationByIp = false;
        viewModel.ProxyChangeTimezoneByIp = false;
        await viewModel.FlushPendingConfigSaveAsync();

        Assert.AreEqual("proxy.example:1080:user:password", config.ProxyFullString);
        Assert.AreEqual("Socks 5", config.ProxyType);
        Assert.IsFalse(config.ProxyChangeLocationByIp);
        Assert.IsFalse(config.ProxyChangeTimezoneByIp);
        await store.Received().UpdateAsync(
            "ABC",
            Arg.Any<Action<StoredDeviceConfig>>(),
            CancellationToken.None);
    }

    [TestMethod]
    public async Task InitializeAsync_RestoresSavedProxyDialogConfig()
    {
        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(new StoredDeviceConfig
        {
            Serial = "ABC",
            ProxyFullString = "proxy.example:1080:user:password",
            ProxyType = "SOCKS5",
            ProxyChangeLocationByIp = false,
            ProxyChangeTimezoneByIp = true
        });
        var viewModel = CreateViewModel(store);

        await viewModel.InitializeAsync(CancellationToken.None);

        Assert.AreEqual("proxy.example", viewModel.ProxyHost);
        Assert.AreEqual("1080", viewModel.ProxyPort);
        Assert.AreEqual("user", viewModel.ProxyUsername);
        Assert.AreEqual("password", viewModel.ProxyPassword);
        Assert.IsFalse(viewModel.ProxyChangeLocationByIp);
        Assert.IsTrue(viewModel.ProxyChangeTimezoneByIp);
    }

    [TestMethod]
    public async Task BuildResult_RequiresPairedCredentialsAndValidPort()
    {
        IDeviceStoreService store = DialogViewModelTestFactory.CreateStore(new StoredDeviceConfig { Serial = "ABC" });
        var viewModel = CreateViewModel(store);
        await viewModel.InitializeAsync(CancellationToken.None);

        viewModel.FullProxyString = "proxy.example:1080:user:password";
        FakeProxyDialogResult? valid = viewModel.BuildResult();
        viewModel.FullProxyString = "proxy.example:1080:user:";
        FakeProxyDialogResult? invalid = viewModel.BuildResult();

        Assert.IsNotNull(valid);
        Assert.AreEqual("proxy.example", valid.Host);
        Assert.AreEqual(1080, valid.Port);
        Assert.IsNull(invalid);
    }

    private static FakeProxyViewModel CreateViewModel(IDeviceStoreService store)
    {
        return new FakeProxyViewModel(
            store,
            DialogViewModelTestFactory.CreateLocalizationService(),
            NullLogger<FakeProxyViewModel>.Instance)
        {
            DeviceSerial = "ABC",
        };
    }
}
