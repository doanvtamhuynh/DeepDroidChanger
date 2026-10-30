using DeepDroidChanger.ViewDevices.Contracts;

namespace DeepDroidChanger.ViewModels;

public sealed partial class ViewMultipleDeviceItemViewModel
{
    private int _keepScreenOff;
    private int _keepConnectedWhileSelected;
    private int _sessionToolUsers;
    private int _isInVisiblePage;
    private int _viewRotationQuarterTurns;
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    internal bool IsInVisiblePage
    {
        get => Volatile.Read(ref _isInVisiblePage) != 0;
        set => Volatile.Write(ref _isInVisiblePage, value ? 1 : 0);
    }

    internal bool KeepSessionInBackground => Volatile.Read(ref _keepScreenOff) != 0 ||
        (Volatile.Read(ref _keepConnectedWhileSelected) != 0 && Volatile.Read(ref _isSelected)) ||
        Volatile.Read(ref _sessionToolUsers) != 0;

    public int ViewRotationAngle => _viewRotationQuarterTurns * 90;
    public double ViewAspectRatio => _viewRotationQuarterTurns % 2 == 0
        ? DeviceAspectRatio : 1 / DeviceAspectRatio;

    internal void RotateView()
    {
        _viewRotationQuarterTurns = (_viewRotationQuarterTurns + 1) % 4;
        OnPropertyChanged(nameof(ViewRotationAngle));
        OnPropertyChanged(nameof(ViewAspectRatio));
    }

    internal async Task StartMaintainedSessionAsync(CancellationToken token)
    {
        await _sessionGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (IsInVisiblePage || KeepSessionInBackground)
                await StartCoreAsync(token).ConfigureAwait(false);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    internal async Task StopUnusedSessionAsync()
    {
        await _sessionGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (IsInVisiblePage || KeepSessionInBackground || IsDisposed)
                return;
            ResetRetryState();
            await StopCurrentSessionCoreAsync().ConfigureAwait(false);
            await SetDisplayStateAsync(DisplayState.Idle, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    // Handoffs and disposal use the same gate. An ADB tool must finish before a
    // dedicated viewer can take ownership, without creating a streaming session.
    internal async Task<T> ExecuteExternalToolAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        await _sessionGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            token.ThrowIfCancellationRequested();
            if (!IsTrackerOnline() || IsDedicatedViewActive())
                throw new InvalidOperationException("ViewMultipleDevices_DeviceUnavailable");
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    internal Task SendToolKeyAsync(int keyCode, Func<CancellationToken, Task> adbFallback, CancellationToken token) =>
        ExecuteExternalToolAsync(async () =>
        {
            ISingleViewDeviceSession? session;
            lock (_sessionStateGate) session = _session;
            if (session is not null && IsReadyToPublish(session))
            {
                await session.SendKeyEventAsync(keyCode, token).ConfigureAwait(false);
                await session.FlushControlAsync(token).ConfigureAwait(false);
            }
            else await adbFallback(token).ConfigureAwait(false);
            return true;
        }, token);

    internal async Task ExecuteSessionToolAsync(
        Func<ISingleViewDeviceSession, CancellationToken, Task> action,
        CancellationToken cancellationToken,
        bool reconnect = false,
        bool? screenOn = null)
    {
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsTrackerOnline() || IsDedicatedViewActive())
                throw new InvalidOperationException("ViewMultipleDevices_DeviceUnavailable");

            Interlocked.Increment(ref _sessionToolUsers);
            try
            {
                if (reconnect)
                {
                    ResetRetryState();
                    await StopCurrentSessionCoreAsync().ConfigureAwait(false);
                }
                await StartCoreAsync(cancellationToken).ConfigureAwait(false);
                ISingleViewDeviceSession? session;
                lock (_sessionStateGate) session = _session;
                if (session is null || !IsReadyToPublish(session) || !IsTrackerOnline() || IsDedicatedViewActive())
                    throw new InvalidOperationException("ViewMultipleDevices_DeviceUnavailable");

                await action(session, cancellationToken).ConfigureAwait(false);
                await session.FlushControlAsync(cancellationToken).ConfigureAwait(false);
                if (reconnect) Volatile.Write(ref _keepConnectedWhileSelected, Volatile.Read(ref _isSelected) ? 1 : 0);
                if (screenOn.HasValue) Volatile.Write(ref _keepScreenOff, screenOn.Value ? 0 : 1);
            }
            finally
            {
                Interlocked.Decrement(ref _sessionToolUsers);
                if (!IsInVisiblePage && !KeepSessionInBackground)
                {
                    ResetRetryState();
                    await StopCurrentSessionCoreAsync().ConfigureAwait(false);
                    await SetDisplayStateAsync(DisplayState.Idle, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _sessionGate.Release();
        }
    }
}
