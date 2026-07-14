using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Tests.Helpers
{
    [TestClass]
    public sealed class DeviceRowFactoryTests
    {
        [TestMethod]
        public void MergeSelectedDevices_AddsOnlyMissingSerials()
        {
            var storedDevices = new List<StoredDeviceConfig>
            {
                new()
                {
                    Serial = "ABC123",
                    Name = "Existing",
                    Type = "Phone",
                    CountryIso = "us",
                    CarrierMcc = "310",
                    CarrierMnc = "260"
                }
            };

            var selectedDevices = new[]
            {
                new StoredDeviceConfig { Serial = "abc123", Name = "Duplicate", Type = "Tablet" },
                new StoredDeviceConfig { Serial = "XYZ999", Name = "New", Type = "Phone" }
            };

            DeviceRowFactory.MergeSelectedDevices(storedDevices, selectedDevices);

            Assert.HasCount(2, storedDevices);
            Assert.AreEqual("Existing", storedDevices[0].Name);
            Assert.AreEqual("310", storedDevices[0].CarrierMcc);
            Assert.AreEqual("260", storedDevices[0].CarrierMnc);
            Assert.AreEqual("XYZ999", storedDevices[1].Serial);
        }

        [TestMethod]
        public void CreateDeviceRow_UsesConnectedStatusWhenDeviceIsOnline()
        {
            var storedDevice = new StoredDeviceConfig
            {
                Serial = "SERIAL-1",
                Name = "Pixel",
                Type = "Phone"
            };
            var connectedDevice = new AdbDevice("serial-1", AdbDeviceStatus.Online);

            var row = DeviceRowFactory.CreateDeviceRow(3, storedDevice, connectedDevice, "Online", "Ready");

            Assert.AreEqual(3, row.Index);
            Assert.AreEqual("SERIAL-1", row.Serial);
            Assert.AreEqual("Pixel", row.Name);
            Assert.AreEqual(AdbDeviceStatus.Online, row.ConnectionStatus);
            Assert.AreEqual("Online", row.Status);
            Assert.AreEqual("Ready", row.Process);
        }
    }
}
