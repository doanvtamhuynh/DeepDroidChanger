using System.IO;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    private void CleanupCliPayload(TwrpSession session)
    {
        CheckCliCache(session);
        string payload = CliPayloadDirectory(session);
        if (Directory.Exists(payload)) DeleteCheckedDirectory(payload);
        if (session.CacheLayoutVersion == 1)
        {
            string image = store.FileFor(session.Id, "recovery.img");
            if (File.Exists(image)) File.Delete(image);
        }
    }
    private async Task CleanupCliRestoreAsync(TwrpSession session, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (session.State != TwrpSessionState.Completed || !session.BootRestored || session.StopRequested) throw new TwrpWorkflowException("Twrp_PendingSession");
        CheckCliCache(session);
        string name = session.NativeFolder[(session.NativeFolder.LastIndexOf('/') + 1)..];
        if (TwrpSafety.CliBackupName(name + ".zip") != name || session.NativeFolder != "/data/media/0/TWRP/BACKUPS/" + session.Device.Serial + "/" + name) throw new TwrpWorkflowException("Twrp_UnsafePath");
        progress?.Report(new(session.Id, session.State, "Twrp_CleaningRestore", session.CacheDirectory));
        await root.ExecuteAsRootAsync(session.Device.Serial, async ct =>
            await ShellAsync(session.Device.Serial, $$"""
                set -eu
                test "$(getprop ro.deepdroid.device)" = {{TwrpSafety.Quote(session.Device.Codename)}}
                path={{TwrpSafety.Quote(session.NativeFolder)}}
                if [ -e "$path" ]; then
                    test "$(readlink -f "$path")" = "$path"
                    test ! -L "$path/.deepdroid-operation"
                    test "$(cat "$path/.deepdroid-operation")" = {{TwrpSafety.Quote(session.Id.ToString("N"))}}
                    rm -rf -- "$path"
                fi
                """, ct).ConfigureAwait(false), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        CleanupCliPayload(session);
        session.CleanupCompleted = true;
        await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
    }
}
