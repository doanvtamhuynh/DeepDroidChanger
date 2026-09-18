using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations;

[TestClass]
public sealed class DeviceConfigRenameTests
{
    [TestMethod]
    public async Task RenameDeviceAsync_TrimsNameAndPreservesOtherStoredConfiguration()
    {
        IDeviceStoreService store = Substitute.For<IDeviceStoreService>();
        StoredDeviceConfig stored = new()
        {
            Serial = "SERIAL",
            Name = "Old name",
            Brand = "Google",
            AndroidVersion = "Android 15",
            ChangeSimEnabled = false,
            LocationLatitude = "10.7626"
        };
        store.UpdateAsync(
                Arg.Any<string>(),
                Arg.Any<Action<StoredDeviceConfig>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.ArgAt<Action<StoredDeviceConfig>>(1)(stored);
                return true;
            });
        var service = new DeviceConfigService(
            store,
            Substitute.For<ISettingsService>(),
            new AppSettings());

        bool updated = await service.RenameDeviceAsync(
            "serial",
            "  New device name  ",
            CancellationToken.None);

        Assert.IsTrue(updated);
        Assert.AreEqual("New device name", stored.Name);
        Assert.AreEqual("Google", stored.Brand);
        Assert.AreEqual("Android 15", stored.AndroidVersion);
        Assert.IsFalse(stored.ChangeSimEnabled);
        Assert.AreEqual("10.7626", stored.LocationLatitude);
        await store.Received(1).UpdateAsync(
            "serial",
            Arg.Any<Action<StoredDeviceConfig>>(),
            CancellationToken.None);
    }

    [TestMethod]
    public async Task RenameDeviceAsync_PublishesMetadataChangeOnlyAfterStorageSucceeds()
    {
        IDeviceStoreService store = Substitute.For<IDeviceStoreService>();
        var notifier = new DeviceMetadataChangeNotifier();
        DeviceNameChangedEventArgs? published = null;
        notifier.DeviceNameChanged += (_, eventArgs) => published = eventArgs;
        StoredDeviceConfig stored = new() { Serial = "SERIAL", Name = "Old" };
        store.UpdateAsync(
                Arg.Any<string>(),
                Arg.Any<Action<StoredDeviceConfig>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.ArgAt<Action<StoredDeviceConfig>>(1)(stored);
                return true;
            });
        var service = new DeviceConfigService(
            store,
            Substitute.For<ISettingsService>(),
            new AppSettings(),
            notifier);

        bool updated = await service.RenameDeviceAsync(
            "SERIAL",
            "New name",
            CancellationToken.None);

        Assert.IsTrue(updated);
        Assert.IsNotNull(published);
        Assert.AreEqual("SERIAL", published!.Serial);
        Assert.AreEqual("New name", published.Name);
    }

    [TestMethod]
    public async Task RenameDeviceAsync_RejectsBlankName()
    {
        var service = new DeviceConfigService(
            Substitute.For<IDeviceStoreService>(),
            Substitute.For<ISettingsService>(),
            new AppSettings());

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            service.RenameDeviceAsync("SERIAL", "  ", CancellationToken.None));
    }
}
