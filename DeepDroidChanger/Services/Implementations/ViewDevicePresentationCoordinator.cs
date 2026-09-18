using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepDroidChanger.Services;

public sealed class ViewDevicePresentationCoordinator : IViewDevicePresentationCoordinator
{
    private sealed class MultiViewRegistration(
        ViewDevicePresentationCoordinator owner,
        Func<string, CancellationToken, Task> suspendAsync,
        Func<string, CancellationToken, Task> resumeAsync) : IDisposable
    {
        private int _disposed;

        public Func<string, CancellationToken, Task> SuspendAsync { get; } = suspendAsync;

        public Func<string, CancellationToken, Task> ResumeAsync { get; } = resumeAsync;

        public bool IsActive => Volatile.Read(ref _disposed) == 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Unregister(this);
        }
    }

    private sealed class DedicatedState(MultiViewRegistration? registration)
    {
        public MultiViewRegistration? Registration { get; } = registration;
    }

    private sealed class DedicatedViewLease(
        ViewDevicePresentationCoordinator owner,
        string serial) : IAsyncDisposable
    {
        private readonly ViewDevicePresentationCoordinator _owner = owner;
        private readonly string _serial = serial;
        private readonly object _gate = new();
        private Task? _releaseTask;

        public ValueTask DisposeAsync()
        {
            lock (_gate)
                return new ValueTask(_releaseTask ??= _owner.ReleaseDedicatedViewAsync(_serial));
        }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, DedicatedState> _dedicatedViews =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SemaphoreSlim> _serialGates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ViewDevicePresentationCoordinator> _logger;
    private MultiViewRegistration? _multiViewRegistration;

    public ViewDevicePresentationCoordinator(
        ILogger<ViewDevicePresentationCoordinator>? logger = null)
    {
        _logger = logger ?? NullLogger<ViewDevicePresentationCoordinator>.Instance;
    }

    public IDisposable RegisterMultiView(
        Func<string, CancellationToken, Task> suspendAsync,
        Func<string, CancellationToken, Task> resumeAsync)
    {
        ArgumentNullException.ThrowIfNull(suspendAsync);
        ArgumentNullException.ThrowIfNull(resumeAsync);

        MultiViewRegistration registration = new(this, suspendAsync, resumeAsync);
        lock (_gate)
        {
            MultiViewRegistration? previous = _multiViewRegistration;
            _multiViewRegistration = registration;
            previous?.Dispose();
        }

        return registration;
    }

    public async Task<IAsyncDisposable> AcquireDedicatedViewAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        string normalizedSerial = NormalizeSerial(serial);
        SemaphoreSlim serialGate = GetSerialGate(normalizedSerial);
        await serialGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        MultiViewRegistration? registration = null;
        bool suspendAttempted = false;
        try
        {
            lock (_gate)
            {
                if (_dedicatedViews.ContainsKey(normalizedSerial))
                {
                    throw new InvalidOperationException(
                        $"A dedicated View Device presentation is already active for {normalizedSerial}.");
                }

                registration = _multiViewRegistration is { IsActive: true }
                    ? _multiViewRegistration
                    : null;
                _dedicatedViews.Add(normalizedSerial, new DedicatedState(registration));
            }

            if (registration is not null)
            {
                suspendAttempted = true;
                await registration.SuspendAsync(normalizedSerial, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new DedicatedViewLease(this, normalizedSerial);
        }
        catch
        {
            lock (_gate)
                _dedicatedViews.Remove(normalizedSerial);

            if (suspendAttempted && registration is { IsActive: true })
            {
                try
                {
                    await registration.ResumeAsync(normalizedSerial, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Failed to roll back multi-view suspension for {Serial}.",
                        normalizedSerial);
                }
            }

            throw;
        }
        finally
        {
            serialGate.Release();
        }
    }

    public bool IsDedicatedViewActive(string serial)
    {
        string normalizedSerial = NormalizeSerial(serial);
        lock (_gate)
            return _dedicatedViews.ContainsKey(normalizedSerial);
    }

    private async Task ReleaseDedicatedViewAsync(string serial)
    {
        SemaphoreSlim serialGate = GetSerialGate(serial);
        await serialGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        MultiViewRegistration? registration = null;
        try
        {
            lock (_gate)
            {
                if (!_dedicatedViews.Remove(serial, out DedicatedState? state))
                    return;

                registration = state.Registration is { IsActive: true }
                    ? state.Registration
                    : _multiViewRegistration is { IsActive: true }
                        ? _multiViewRegistration
                        : null;
            }

            if (registration is not null)
            {
                try
                {
                    await registration.ResumeAsync(serial, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Failed to resume multi-view presentation for {Serial}.",
                        serial);
                }
            }
        }
        finally
        {
            serialGate.Release();
        }
    }

    private void Unregister(MultiViewRegistration registration)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_multiViewRegistration, registration))
                _multiViewRegistration = null;
        }
    }

    private SemaphoreSlim GetSerialGate(string serial)
    {
        lock (_gate)
        {
            // Serial gates are intentionally cached for the coordinator lifetime;
            // removing one while a lease is releasing could allow two handoffs
            // for the same device to overlap.
            if (!_serialGates.TryGetValue(serial, out SemaphoreSlim? gate))
            {
                gate = new SemaphoreSlim(1, 1);
                _serialGates.Add(serial, gate);
            }

            return gate;
        }
    }

    private static string NormalizeSerial(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        return serial.Trim();
    }
}
