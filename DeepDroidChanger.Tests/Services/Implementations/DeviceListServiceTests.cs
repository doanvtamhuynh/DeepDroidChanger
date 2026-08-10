using DeepDroidChanger.Models;
using DeepDroidChanger.Services;
using NSubstitute;

namespace DeepDroidChanger.Tests.Services.Implementations
{
    [TestClass]
    public sealed class DeviceListServiceTests
    {
        [TestMethod]
        public async Task AddSelectedDevicesAsync_MergesMissingDevicesAndPreservesCarrierFields()
        {
            var deviceService = Substitute.For<IAdbDeviceService>();
            var store = Substitute.For<IDeviceStoreService>();
            var existing = new StoredDeviceConfig
            {
                Serial = "ABC123",
                Name = "Existing",
                Type = "Phone",
                CarrierMcc = "310",
                CarrierMnc = "260"
            };
            store.MergeAsync(Arg.Any<IEnumerable<StoredDeviceConfig>>(), CancellationToken.None)
                .Returns(callInfo =>
                {
                    var merged = new List<StoredDeviceConfig> { existing };
                    DeepDroidChanger.Helpers.DeviceRowFactory.MergeSelectedDevices(
                        merged,
                        callInfo.ArgAt<IEnumerable<StoredDeviceConfig>>(0));
                    return merged;
                });
            deviceService.GetConnectedDevicesAsync(CancellationToken.None).Returns([new AdbDevice("XYZ999", AdbDeviceStatus.Online)]);
            var coordinator = new DeviceListService(deviceService, store);

            var snapshot = await coordinator.AddSelectedDevicesAsync(
                [new StoredDeviceConfig { Serial = "abc123", Name = "Duplicate", Type = "Tablet" },
                 new StoredDeviceConfig { Serial = "XYZ999", Name = "New", Type = "Phone" }],
                CancellationToken.None);

            Assert.HasCount(2, snapshot.StoredDevices);
            Assert.AreEqual("310", snapshot.StoredDevices[0].CarrierMcc);
            Assert.AreEqual("260", snapshot.StoredDevices[0].CarrierMnc);
            await store.Received(1).MergeAsync(
                Arg.Is<IEnumerable<StoredDeviceConfig>>(devices => devices.Count() == 2),
                CancellationToken.None);
        }

        [TestMethod]
        public async Task DeleteSavedDeviceAsync_RemovesOnlyMatchingSerial()
        {
            var deviceService = Substitute.For<IAdbDeviceService>();
            var store = Substitute.For<IDeviceStoreService>();
            store.RemoveAsync("a", CancellationToken.None).Returns(true);
            store.LoadAsync(CancellationToken.None).Returns(
                [new StoredDeviceConfig { Serial = "B", Name = "Second" }]);
            deviceService.GetConnectedDevicesAsync(CancellationToken.None).Returns([]);
            var coordinator = new DeviceListService(deviceService, store);

            var result = await coordinator.DeleteSavedDeviceAsync("a", CancellationToken.None);

            Assert.IsTrue(result.Removed);
            Assert.HasCount(1, result.Snapshot.StoredDevices);
            Assert.AreEqual("B", result.Snapshot.StoredDevices[0].Serial);
        }

        [TestMethod]
        public void CountNewDevices_IgnoresAlreadySavedSerials()
        {
            var coordinator = new DeviceListService(
                Substitute.For<IAdbDeviceService>(),
                Substitute.For<IDeviceStoreService>());
            var storedDevices = new[] { new StoredDeviceConfig { Serial = "abc" } };
            var connectedDevices = new[]
            {
                new AdbDevice("ABC", AdbDeviceStatus.Online),
                new AdbDevice("XYZ", AdbDeviceStatus.Online)
            };

            var count = coordinator.CountNewDevices(storedDevices, connectedDevices);

            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public async Task LoadSnapshotAsync_PreservesOfflineAndUnauthorizedStates()
        {
            var deviceService = Substitute.For<IAdbDeviceService>();
            var store = Substitute.For<IDeviceStoreService>();
            store.LoadAsync(CancellationToken.None).Returns(
                [new StoredDeviceConfig { Serial = "ONLINE" }]);
            deviceService.GetConnectedDevicesAsync(CancellationToken.None).Returns(
            [
                new AdbDevice("ONLINE", AdbDeviceStatus.Online),
                new AdbDevice("OFFLINE", AdbDeviceStatus.Offline),
                new AdbDevice("UNAUTHORIZED", AdbDeviceStatus.Unauthorized)
            ]);
            var service = new DeviceListService(deviceService, store);

            DeviceListSnapshot snapshot = await service.LoadSnapshotAsync(CancellationToken.None);

            Assert.HasCount(3, snapshot.ConnectedDevices);
            Assert.AreEqual(
                AdbDeviceStatus.Unauthorized,
                snapshot.ConnectedDevices.Single(device => device.Serial == "UNAUTHORIZED").Status);
        }

        [TestMethod]
        public void CountNewDevices_OfflineOrUnauthorizedDevices_AreNotAddable()
        {
            var service = new DeviceListService(
                Substitute.For<IAdbDeviceService>(),
                Substitute.For<IDeviceStoreService>());
            AdbDevice[] detectedDevices =
            [
                new AdbDevice("ONLINE", AdbDeviceStatus.Online),
                new AdbDevice("OFFLINE", AdbDeviceStatus.Offline),
                new AdbDevice("UNAUTHORIZED", AdbDeviceStatus.Unauthorized)
            ];

            int count = service.CountNewDevices([], detectedDevices);

            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public void FindSelectionSerial_RestoresHiddenMatchingSerial()
        {
            var service = new DeviceListService(
                Substitute.For<IAdbDeviceService>(),
                Substitute.For<IDeviceStoreService>());

            string? selected = service.FindSelectionSerial(
                "target",
                ["VISIBLE"],
                ["VISIBLE", "TARGET"]);

            Assert.AreEqual("TARGET", selected);
        }

        [TestMethod]
        public void FindSelectionSerial_WithoutOrMissingTarget_DoesNotSelectFirstVisibleDevice()
        {
            var service = new DeviceListService(
                Substitute.For<IAdbDeviceService>(),
                Substitute.For<IDeviceStoreService>());

            string? noTarget = service.FindSelectionSerial(
                string.Empty,
                ["FIRST", "SECOND"],
                ["FIRST", "SECOND"]);
            string? missingTarget = service.FindSelectionSerial(
                "MISSING",
                ["FIRST", "SECOND"],
                ["FIRST", "SECOND"]);

            Assert.IsNull(noTarget);
            Assert.IsNull(missingTarget);
        }
    }
}
