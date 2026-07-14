using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IRandomDeviceService
{
    Task<RandomDeviceResult> CreateRandomProfileAsync(
        RandomDeviceRequest request,
        CancellationToken cancellationToken);
}
