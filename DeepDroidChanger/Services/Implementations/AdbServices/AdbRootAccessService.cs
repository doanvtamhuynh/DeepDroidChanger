using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepDroidChanger.Services;

public sealed class AdbRootAccessService : IAdbRootAccessService
{
    internal static readonly TimeSpan RootCleanupTimeout = TimeSpan.FromSeconds(30);

    private static readonly ConditionalWeakTable<IAdbCommandService, AdbRootAccessService> SharedFallbacks = new();

    private readonly ConcurrentDictionary<string, RootState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly IAdbCommandService _adb;
    private readonly ILogger<AdbRootAccessService> _logger;

    internal static IAdbRootAccessService GetShared(IAdbCommandService adb)
    {
        ArgumentNullException.ThrowIfNull(adb);
        return SharedFallbacks.GetValue(adb, static commandService => new AdbRootAccessService(commandService));
    }

    public AdbRootAccessService(
        IAdbCommandService adb,
        ILogger<AdbRootAccessService>? logger = null)
    {
        _adb = adb;
        _logger = logger ?? NullLogger<AdbRootAccessService>.Instance;
    }

    public Task ExecuteAsRootAsync(
        string serial,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(action);

        return ExecuteAsRootCoreAsync(serial, action, cancellationToken);
    }

    public Task<T> ExecuteAsRootAsync<T>(
        string serial,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(action);

        return ExecuteAsRootCoreAsync(serial, action, cancellationToken);
    }

    private async Task ExecuteAsRootCoreAsync(
        string serial,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        RootLease lease = await AcquireAsync(serial, cancellationToken).ConfigureAwait(false);
        Exception? actionException = null;
        Exception? cleanupException = null;

        try
        {
            await action(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            actionException = exception;
        }

        try
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            cleanupException = exception;
        }

        if (actionException is not null)
        {
            if (cleanupException is not null)
            {
                _logger.LogError(
                    cleanupException,
                    "Unable to restore ADB shell mode for device {Serial} after the root action failed.",
                    serial);
            }

            ExceptionDispatchInfo.Capture(actionException).Throw();
        }

        if (cleanupException is not null)
        {
            _logger.LogError(
                cleanupException,
                "Action succeeded but ADB shell could not be restored for device {Serial}.",
                serial);
            ExceptionDispatchInfo.Capture(cleanupException).Throw();
        }
    }

    private async Task<T> ExecuteAsRootCoreAsync<T>(
        string serial,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        RootLease lease = await AcquireAsync(serial, cancellationToken).ConfigureAwait(false);
        T result = default!;
        Exception? actionException = null;
        Exception? cleanupException = null;

        try
        {
            result = await action(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            actionException = exception;
        }

        try
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            cleanupException = exception;
        }

        if (actionException is not null)
        {
            if (cleanupException is not null)
            {
                _logger.LogError(
                    cleanupException,
                    "Unable to restore ADB shell mode for device {Serial} after the root action failed.",
                    serial);
            }

            ExceptionDispatchInfo.Capture(actionException).Throw();
        }

        if (cleanupException is not null)
        {
            _logger.LogError(
                cleanupException,
                "Action succeeded but ADB shell could not be restored for device {Serial}.",
                serial);
            ExceptionDispatchInfo.Capture(cleanupException).Throw();
        }

        return result;
    }

    private async Task<RootLease> AcquireAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        RootState state = _states.GetOrAdd(serial, static _ => new RootState());
        await state.TransitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        bool rootAttempted = false;
        try
        {
            rootAttempted = true;
            await VerifyRootAsync(serial, cancellationToken).ConfigureAwait(false);
            state.ActiveLeaseCount++;
            return new RootLease(this, serial, state);
        }
        catch (Exception exception)
        {
            if (rootAttempted && state.ActiveLeaseCount == 0)
            {
                try
                {
                    using var cleanupCancellation = CreateCleanupCancellationSource();
                    await RestoreShellAsync(serial, cleanupCancellation.Token).ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogError(
                        cleanupException,
                        "Unable to restore ADB shell mode for device {Serial} after root verification failed.",
                        serial);
                }
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
        finally
        {
            state.TransitionGate.Release();
        }
    }

    private async Task ReleaseAsync(RootLease lease)
    {
        using var cleanupCancellation = CreateCleanupCancellationSource();
        bool gateAcquired = false;
        try
        {
            await lease.State.TransitionGate
                .WaitAsync(cleanupCancellation.Token)
                .ConfigureAwait(false);
            gateAcquired = true;
        }
        catch (OperationCanceledException) when (cleanupCancellation.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out waiting to release ADB root access for device {lease.Serial}.");
        }

        try
        {
            if (lease.State.ActiveLeaseCount <= 0)
            {
                lease.MarkReleaseApplied();
                return;
            }

            lease.State.ActiveLeaseCount--;
            lease.MarkReleaseApplied();
            if (lease.State.ActiveLeaseCount > 0)
                return;

            await RestoreShellAsync(lease.Serial, cleanupCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            if (gateAcquired)
                lease.State.TransitionGate.Release();
        }
    }

    private async Task VerifyRootAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Requesting ADB root for device {Serial}.", serial);

        CommandResult? rootResult = null;
        try
        {
            rootResult = await _adb
                .RunAdbAsync(serial, "root", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "ADB root command failed for device {Serial}; verifying the actual shell identity.",
                serial);
        }

        if (rootResult is not null && rootResult.ExitCode != 0)
        {
            _logger.LogDebug(
                "ADB root returned exit code {ExitCode} for device {Serial}; verifying the actual shell identity.",
                rootResult.ExitCode,
                serial);
        }

        CommandResult? waitResult = null;
        try
        {
            waitResult = await _adb
                .RunAdbAsync(serial, "wait-for-device", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "ADB reconnect wait failed for device {Serial}; verifying the actual shell identity.",
                serial);
        }

        if (waitResult is not null && waitResult.ExitCode != 0)
        {
            _logger.LogDebug(
                "ADB reconnect wait returned exit code {ExitCode} for device {Serial}; verifying the actual shell identity.",
                waitResult.ExitCode,
                serial);
        }

        CommandResult identityResult;
        try
        {
            identityResult = await _adb
                .RunAdbShellAsync(serial, "whoami", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Unable to verify ADB root access for device {serial}.",
                exception);
        }

        if (identityResult is null || identityResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Unable to verify ADB root access for device {serial}.");
        }

        string identity = identityResult.StandardOutput.Trim();
        if (!string.Equals(identity, "root", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Device {serial} does not provide ADB root access; whoami returned '{identity}'.");
        }

        _logger.LogInformation("ADB root verified for device {Serial}.", serial);
    }

    private async Task RestoreShellAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Releasing ADB root for device {Serial}.", serial);

        await RunCleanupCommandAsync(
                serial,
                "wait-for-device",
                cancellationToken)
            .ConfigureAwait(false);
        await RunCleanupCommandAsync(
                serial,
                "unroot",
                cancellationToken)
            .ConfigureAwait(false);
        await RunCleanupCommandAsync(
                serial,
                "wait-for-device",
                cancellationToken)
            .ConfigureAwait(false);

        CommandResult? identityResult = await RunCleanupShellCommandAsync(
                serial,
                "whoami",
                cancellationToken)
            .ConfigureAwait(false);
        if (identityResult is null || identityResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Unable to verify that ADB shell mode was restored for device {serial}.");
        }

        string identity = identityResult.StandardOutput.Trim();
        if (!string.Equals(identity, "shell", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"ADB shell mode was not restored for device {serial}; whoami returned '{identity}'.");
        }

        _logger.LogInformation("ADB shell mode restored for device {Serial}.", serial);
    }

    private async Task RunCleanupCommandAsync(
        string serial,
        string arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            CommandResult? result = await _adb
                .RunAdbAsync(serial, arguments, cancellationToken)
                .ConfigureAwait(false);
            if (result is not null && result.ExitCode != 0)
            {
                _logger.LogWarning(
                    "ADB cleanup command {Arguments} returned exit code {ExitCode} for device {Serial}.",
                    arguments,
                    result.ExitCode,
                    serial);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"ADB root cleanup timed out while running '{arguments}' for device {serial}.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "ADB cleanup command {Arguments} failed for device {Serial}; continuing cleanup.",
                arguments,
                serial);
        }
    }

    private async Task<CommandResult?> RunCleanupShellCommandAsync(
        string serial,
        string command,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _adb
                .RunAdbShellAsync(serial, command, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"ADB root cleanup timed out while running '{command}' for device {serial}.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "ADB cleanup shell command {Command} failed for device {Serial}.",
                command,
                serial);
            return null;
        }
    }

    private static CancellationTokenSource CreateCleanupCancellationSource()
    {
        var cancellation = new CancellationTokenSource(RootCleanupTimeout);
        return cancellation;
    }

    private sealed class RootState
    {
        internal SemaphoreSlim TransitionGate { get; } = new(1, 1);
        internal int ActiveLeaseCount { get; set; }
    }

    private sealed class RootLease
    {
        private readonly AdbRootAccessService _owner;
        private readonly object _releaseSync = new();
        private TaskCompletionSource<object?>? _releaseCompletion;
        private bool _releaseApplied;

        internal RootLease(
            AdbRootAccessService owner,
            string serial,
            RootState state)
        {
            _owner = owner;
            Serial = serial;
            State = state;
        }

        internal string Serial { get; }
        internal RootState State { get; }

        internal ValueTask DisposeAsync()
        {
            TaskCompletionSource<object?> completion;
            bool startRelease = false;
            lock (_releaseSync)
            {
                if (_releaseCompletion is not null)
                    return new ValueTask(_releaseCompletion.Task);

                if (_releaseApplied)
                    return ValueTask.CompletedTask;

                completion = new TaskCompletionSource<object?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _releaseCompletion = completion;
                startRelease = true;
            }

            if (startRelease)
                _ = CompleteReleaseAsync(completion);

            return new ValueTask(completion.Task);
        }

        internal void MarkReleaseApplied()
        {
            lock (_releaseSync)
            {
                _releaseApplied = true;
            }
        }

        private async Task CompleteReleaseAsync(
            TaskCompletionSource<object?> completion)
        {
            try
            {
                await _owner.ReleaseAsync(this).ConfigureAwait(false);
                MarkReleaseApplied();
                completion.TrySetResult(null);
            }
            catch (Exception exception)
            {
                lock (_releaseSync)
                {
                    if (!_releaseApplied &&
                        ReferenceEquals(_releaseCompletion, completion))
                    {
                        _releaseCompletion = null;
                    }
                }

                completion.TrySetException(exception);
            }
        }
    }
}
