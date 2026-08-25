using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public interface IFakeProxyBatchDialogService
{
    Task<FakeProxyBatchDialogResult?> ShowFakeProxyBatchAsync(
        int targetCount,
        CancellationToken cancellationToken);
}
