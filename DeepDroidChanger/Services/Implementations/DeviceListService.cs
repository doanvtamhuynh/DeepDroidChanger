using DeepDroidChanger.Helpers;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services
{
    public sealed class DeviceListService : IDeviceListService
    {
        private readonly IAdbDeviceService _deviceService;
        private readonly IDeviceStoreService _deviceStoreService;

        public DeviceListService(
            IAdbDeviceService deviceService,
            IDeviceStoreService deviceStoreService)
        {
            _deviceService = deviceService;
            _deviceStoreService = deviceStoreService;
        }

        public async Task<DeviceListSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken)
        {
            var storedDevices = (await _deviceStoreService.LoadAsync(cancellationToken).ConfigureAwait(false)).ToList();
            var connectedDevices = await LoadDetectedDevicesAsync(cancellationToken).ConfigureAwait(false);
            return new DeviceListSnapshot(storedDevices, connectedDevices);
        }

        public async Task<IReadOnlyList<StoredDeviceConfig>> LoadStoredDevicesAsync(CancellationToken cancellationToken)
        {
            return (await _deviceStoreService.LoadAsync(cancellationToken).ConfigureAwait(false)).ToList();
        }

        public async Task<IReadOnlyList<AdbDevice>> LoadDetectedDevicesAsync(CancellationToken cancellationToken)
        {
            var connectedDevices = await _deviceService.GetConnectedDevicesAsync(cancellationToken).ConfigureAwait(false);
            return connectedDevices
                .GroupBy(device => device.Serial, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        public Task<bool> IsDeviceOnlineAsync(string serial, CancellationToken cancellationToken)
        {
            return _deviceService.IsDeviceOnlineAsync(serial, cancellationToken);
        }

        public async Task<DeviceListSnapshot> AddSelectedDevicesAsync(
            IEnumerable<StoredDeviceConfig> selectedDevices,
            CancellationToken cancellationToken)
        {
            var storedDevices = (await _deviceStoreService.MergeAsync(selectedDevices, cancellationToken).ConfigureAwait(false)).ToList();
            var connectedDevices = await LoadDetectedDevicesAsync(cancellationToken).ConfigureAwait(false);
            return new DeviceListSnapshot(storedDevices, connectedDevices);
        }

        public async Task<DeviceDeleteResult> DeleteSavedDeviceAsync(
            string serial,
            CancellationToken cancellationToken)
        {
            bool removed = await _deviceStoreService.RemoveAsync(serial, cancellationToken).ConfigureAwait(false);
            var storedDevices = (await _deviceStoreService.LoadAsync(cancellationToken).ConfigureAwait(false)).ToList();

            var connectedDevices = await LoadDetectedDevicesAsync(cancellationToken).ConfigureAwait(false);
            return new DeviceDeleteResult(
                removed,
                new DeviceListSnapshot(storedDevices, connectedDevices));
        }

        public int CountNewDevices(
            IReadOnlyList<StoredDeviceConfig> storedDevices,
            IReadOnlyList<AdbDevice> connectedDevices)
        {
            return connectedDevices.Count(connectedDevice =>
                connectedDevice.Status == AdbDeviceStatus.Online
                && !DeviceRowFactory.ContainsSerial(storedDevices, connectedDevice.Serial));
        }
    }
}
