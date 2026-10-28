using System.IO;
using System.Text.Json;
using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    public async Task<TwrpSession> RecoverAsync(Guid sessionId, IProgress<TwrpProgress>? progress, CancellationToken cancellationToken)
    {
        var session = await store.LoadAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (session.ExecutionMode == TwrpExecutionMode.ExternalRecovery)
            return await RecoverDataAsync(session, progress, cancellationToken).ConfigureAwait(false);
        using var operation = Start(session.Device.Serial, cancellationToken);
        if (session.IsTerminal)
        {
            if (session.CliRestore && session.State == TwrpSessionState.Completed && session.BootRestored && !session.StopRequested && !session.CleanupCompleted)
                await CleanupCliRestoreAsync(session, progress, cancellationToken).ConfigureAwait(false);
            return session;
        }
        if (session.CliRestore && session.State is TwrpSessionState.Preparing or TwrpSessionState.OriginalSaved or TwrpSessionState.ImagePrepared)
        {
            // Journal precedes FLASH_INTENT. Reopening a prepared session must
            // never replay restore or write boot just to abandon preparation.
            session.State = TwrpSessionState.Failed;
            session.BootRestored = true;
            session.WorkflowError = "Twrp_WorkflowFailed";
            await store.SaveAsync(session, cancellationToken).ConfigureAwait(false);
            return session;
        }
        if (session.Partitions.Contains("media") && (session.State is TwrpSessionState.Preparing or TwrpSessionState.OriginalSaved or TwrpSessionState.ImagePrepared
            || session.BootRestored && (session.WorkflowError == "Twrp_MediaRestoreIncomplete" || session.Partitions.SequenceEqual(new[] { "media" }))))
        {
            // Retain old journals without replaying a removed transfer. Sessions
            // that may have written boot still use the normal recovery path below.
            session.State = TwrpSessionState.Failed;
            session.WorkflowError = "Twrp_MediaUnsupported";
            await store.SaveAsync(session, cancellationToken).ConfigureAwait(false);
            progress?.Report(new(session.Id, session.State, "Twrp_MediaUnsupported"));
            return session;
        }
        string? result = await ReadResultAsync(session).ConfigureAwait(false);
        if (result is not null && GetField(ParseFields(result), "state") is "COMPLETED" or "FAILED" or "NEEDS_RECOVERY")
        {
            await CollectResultAsync(session, progress).ConfigureAwait(false);
            if (session.CliRestore && session.State == TwrpSessionState.Completed && !session.StopRequested)
                await CleanupCliRestoreAsync(session, progress, cancellationToken).ConfigureAwait(false);
            return session;
        }
        await TryRecoverBootAsync(session).ConfigureAwait(false);
        await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
        return session;
    }
    private async Task TryRecoverBootAsync(TwrpSession session)
    {
        try
        {
            if (!File.Exists(store.FileFor(session.Id, "original.img"))) throw new TwrpWorkflowException("Twrp_ManualRecoveryRequired");
            string recovery = (await ShellAsync(session.Device.Serial, "getprop ro.twrp.boot", CancellationToken.None).ConfigureAwait(false)).StandardOutput.Trim();
            if (recovery == "1")
            {
                // The init guard may still be executing. Never race a second writer against it.
                session.State = TwrpSessionState.NeedsRecovery;
                session.BootRestoreError = "Twrp_GuardOwnsRecovery";
                return;
            }
            await StateAsync(session, TwrpSessionState.RestoreBootIntent, null, CancellationToken.None).ConfigureAwait(false);
            await root.ExecuteAsRootAsync(session.Device.Serial, async ct =>
            {
                string remote = TwrpScripts.Remote(session);
                await ShellAsync(session.Device.Serial, $$"""
                    set -eu
                    # A watchdog on this Android boot owns the writer until it exits.
                    # A changed kernel boot ID proves that the old process is gone.
                    if [ -f {{TwrpSafety.Quote(remote + "/flash-owned")}} ] && [ ! -f {{TwrpSafety.Quote(remote + "/flash-finished")}} ]; then
                        test -r {{TwrpSafety.Quote(remote + "/flash.bootid")}}
                        test "$(cat /proc/sys/kernel/random/boot_id)" != "$(cat {{TwrpSafety.Quote(remote + "/flash.bootid")}})"
                    fi
                    """, ct).ConfigureAwait(false);
                await ShellAsync(session.Device.Serial, $"mkdir -p {TwrpSafety.Quote(remote)}", ct).ConfigureAwait(false);
                string original = remote + "/original.img";
                await TransferAsync(session.Device.Serial, store.FileFor(session.Id, "original.img"), original, true, ct).ConfigureAwait(false);
                await ShellAsync(session.Device.Serial, $$"""
                    set -eu
                    test "$(getprop {{PropertyConstants.DeepDroidDevice}})" = {{TwrpSafety.Quote(session.Device.Codename)}}
                    test "$(getprop ro.boot.slot_suffix)" = {{TwrpSafety.Quote(session.Device.Slot)}}
                    test "$(readlink -f {{TwrpSafety.Quote(TwrpSafety.Boot(session.Device.Slot))}})" = {{TwrpSafety.Quote(session.Device.BootDevice)}}
                    dd if={{TwrpSafety.Quote(original)}} of={{TwrpSafety.Quote(TwrpSafety.Boot(session.Device.Slot))}} bs=4194304 conv=fsync
                    """, ct, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            }, CancellationToken.None).ConfigureAwait(false);
            session.BootRestored = true;
            session.State = TwrpSessionState.Failed; // Boot recovered; never replay a backup or a partial restore.
            session.BootRestoreError = null;
        }
        catch (Exception e)
        {
            session.BootRestored = false; session.State = TwrpSessionState.NeedsRecovery;
            session.BootRestoreError = "Twrp_ManualRecoveryRequired";
            logger.LogError(e, "Automatic boot recovery is unavailable for {Operation}. Rescue files remain on the PC.", session.Id);
        }
    }
    public Task<IReadOnlyList<TwrpSession>> ListAsync(string serial, CancellationToken cancellationToken) => store.ListAsync(serial, cancellationToken);
    public async Task DeleteAsync(string serial, Guid sessionId, CancellationToken cancellationToken)
    {
        TwrpSafety.ValidateSerial(serial);
        using var operation = Start(serial, cancellationToken);
        var session = await store.LoadAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var history = await store.ListAsync(serial, cancellationToken).ConfigureAwait(false);
        if (session.ExecutionMode == TwrpExecutionMode.ExternalRecovery)
        {
            if (session.Device.Serial != serial || !session.IsTerminal || !session.CleanupCompleted || history.Any(s => !s.IsTerminal))
                throw new TwrpWorkflowException("Twrp_PendingSession");
            store.DeleteLocal(sessionId);
            return;
        }
        if (session.Device.Serial != serial || !session.IsTerminal || !session.BootRestored || history.Any(item => !item.IsTerminal)) throw new TwrpWorkflowException("Twrp_PendingSession");
        if ((session.CliBackup || session.CliRestore) && !session.CleanupCompleted) throw new TwrpWorkflowException("Twrp_PendingSession");
        if (session.Kind == TwrpOperationKind.Backup && !session.CliBackup)
        {
            string expected = TwrpSafety.Storage(session.StorageRoot) + "/DeepDroidChanger/TWRP/" + session.Id.ToString("N");
            if (session.NativeFolder != expected) throw new TwrpWorkflowException("Twrp_UnsafePath");
            await root.ExecuteAsRootAsync(serial, async ct =>
            {
                // Verify physical identity without trusting the catalog's serial alone.
                await ShellAsync(serial, $$"""
                    set -eu
                    test "$(getprop {{PropertyConstants.DeepDroidDevice}})" = {{TwrpSafety.Quote(session.Device.Codename)}}
                    test "$(getprop ro.boot.slot_suffix)" = {{TwrpSafety.Quote(session.Device.Slot)}}
                    path={{TwrpSafety.Quote(expected)}}
                    if [ -e "$path" ]; then
                        test "$(readlink -f "$path")" = "$path"
                        rm -rf -- "$path"
                    fi
                    """, ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        store.DeleteLocal(sessionId); // Additional user-selected rescue copies deliberately remain.
    }
    private async Task StateAsync(TwrpSession session, TwrpSessionState state, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        TwrpSafety.Transition(session, state);
        await store.SaveAsync(session, token).ConfigureAwait(false);
        bool bootOnly = session.Kind == TwrpOperationKind.Backup && !session.NativeBoot
            && (session.RequestedPartitions.Length > 0 ? session.RequestedPartitions : session.Partitions).SequenceEqual(new[] { "boot" });
        progress?.Report(new(session.Id, state, bootOnly && state is TwrpSessionState.BootRestored or TwrpSessionState.Completed ? "Twrp_OriginalBootSaved"
            : state == TwrpSessionState.Completed && session.Kind == TwrpOperationKind.Backup && session.ArchivePath is not null ? "Twrp_NativeCompleted" : "Twrp_State_" + state,
            store.FileFor(session.Id, "original.img")));
    }
    private Task<CommandResult> WriteRemoteJournalAsync(TwrpSession session, CancellationToken token) => ShellAsync(session.Device.Serial,
        $"printf '%s' {TwrpSafety.Quote(JsonSerializer.Serialize(session, new JsonSerializerOptions(TwrpSessionStore.JsonOptions) { WriteIndented = false }))} > {TwrpSafety.Quote(TwrpScripts.Remote(session) + "/session.json")} && sync", token);
}
