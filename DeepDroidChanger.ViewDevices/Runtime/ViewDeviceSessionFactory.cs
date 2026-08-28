using DeepDroidChanger.ViewDevices.Contracts;
using DeepDroidChanger.ViewDevices.Interop;
using DeepDroidChanger.ViewDevices.Models;

namespace DeepDroidChanger.ViewDevices.Runtime;

public sealed class ViewDeviceSessionFactory : IViewDeviceSessionFactory, IDisposable
{
    private readonly ScrcpyRuntimeResolver _runtimeResolver;
    private readonly ProcessJob _processJob = ProcessJob.Create();
    private int _disposed;

    public ViewDeviceSessionFactory(ScrcpyRuntimeResolver runtimeResolver)
    {
        _runtimeResolver = runtimeResolver;
    }

    public IViewDeviceSession Create(ViewDeviceLaunchOptions options)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(options);
        return new ScrcpyProcessSession(options, _runtimeResolver.Resolve(), _processJob);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _processJob.Dispose();
    }
}
