using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    private readonly TwrpArchiveService _archives = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _confirmedStops = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _unsafeDevices = new(StringComparer.Ordinal);
    private sealed class StopRegistration(System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> stops, Guid id) : IDisposable
    {
        public void Dispose() => stops.TryRemove(id, out _);
    }

    public Task<bool> RequestStopAsync(string serial, Guid operationId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var current = coordinator.GetOperation(serial);
        if (current is not { Kind: DeviceActionKind.TwrpRecovery, CanCancel: true } || current.OperationId != operationId)
            return Task.FromResult(false);
        _confirmedStops.TryAdd(operationId, 0);
        bool requested = coordinator.TryRequestCancellation(serial, operationId);
        if (!requested) _confirmedStops.TryRemove(operationId, out _);
        return Task.FromResult(requested);
    }

    private async Task PayloadTransferAsync(string serial, string from, string to, bool push, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = push ? await adb.PushFileAsync(serial, from, to, token).ConfigureAwait(false)
            : await adb.PullFileAsync(serial, from, to, token).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new TwrpWorkflowException("Twrp_TransferFailed");
    }
}
