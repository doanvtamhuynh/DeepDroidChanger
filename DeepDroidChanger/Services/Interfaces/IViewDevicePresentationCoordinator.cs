namespace DeepDroidChanger.Services;

public interface IViewDevicePresentationCoordinator
{
    IDisposable RegisterMultiView(
        Func<string, CancellationToken, Task> suspendAsync,
        Func<string, CancellationToken, Task> resumeAsync);

    Task<IAsyncDisposable> AcquireDedicatedViewAsync(
        string serial,
        CancellationToken cancellationToken = default);

    bool IsDedicatedViewActive(string serial);
}
