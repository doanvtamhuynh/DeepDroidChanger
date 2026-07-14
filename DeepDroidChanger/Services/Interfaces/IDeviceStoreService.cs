using DeepDroidChanger.Models;
namespace DeepDroidChanger.Services
{
    public interface IDeviceStoreService
    {
        Task<IReadOnlyList<StoredDeviceConfig>> LoadAsync(CancellationToken cancellationToken);
        Task SaveAsync(IEnumerable<StoredDeviceConfig> devices, CancellationToken cancellationToken);
        Task<bool> UpdateAsync(
            string serial,
            Action<StoredDeviceConfig> update,
            CancellationToken cancellationToken);
        Task<IReadOnlyList<StoredDeviceConfig>> MergeAsync(
            IEnumerable<StoredDeviceConfig> devices,
            CancellationToken cancellationToken);
        Task<bool> RemoveAsync(string serial, CancellationToken cancellationToken);
    }
}
