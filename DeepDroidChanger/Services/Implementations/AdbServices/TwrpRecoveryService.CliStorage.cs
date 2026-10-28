using System.IO;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    private static string CliPayloadDirectory(TwrpOperation operation) => Path.Combine(operation.CacheDirectory!, "payload");

    private static void Log(TwrpOperation operation, string file, string text)
    {
        // Both native streams and the elapsed timer share these writers.
        lock (operation)
        {
            TwrpSafety.ValidateLocalDirectory(Path.Combine(operation.CacheDirectory!, "logs"));
            Directory.CreateDirectory(Path.Combine(operation.CacheDirectory!, "logs"));
            string path = Path.Combine(operation.CacheDirectory!, "logs", file);
            if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new TwrpWorkflowException("Twrp_UnsafePath");
            File.AppendAllText(path, text);
        }
    }

    private void LogOperation(TwrpOperation operation, string text)
    {
        try
        {
            lock (operation)
            {
                string line = $"{DateTimeOffset.UtcNow:O} {text}{Environment.NewLine}";
                if (operation.CacheDirectory is null) { operation.PreparationLog.Add(line); return; }
                Log(operation, "operation.log", string.Concat(operation.PreparationLog) + line);
                operation.PreparationLog.Clear();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TwrpWorkflowException)
        {
            logger.LogWarning(exception, "Cannot write operation.log for {Operation}: {Message}", operation.Id, text);
        }
    }

    private const string NativeQueueCheck = """
        set -eu
        for script in /tmp/openrecoveryscript /cache/recovery/openrecoveryscript; do
            if [ -s "$script" ] || [ -L "$script" ]; then
                printf '%s\n' DDC_PENDING_NATIVE_COMMAND
                exit 73
            fi
        done
        """;

    private Task EnsureNativeCommandQueueEmptyAsync(TwrpOperation operation, CancellationToken token) =>
        RunNativeSafetyScriptAsync(operation, NativeQueueCheck, token);

    private async Task RunNativeSafetyScriptAsync(TwrpOperation operation, string script, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(CommandTimeout);
        var result = await adb.RunAdbShellScriptAsync(operation.Device.Serial, script, limit.Token).ConfigureAwait(false);
        if (result.ExitCode == 0) return;
        LogOperation(operation, $"Native safety command failed: exit={result.ExitCode}; {result.StandardOutput.Trim()} {result.StandardError.Trim()}");
        throw new TwrpWorkflowException(result.ExitCode switch
        {
            73 => "Twrp_NativeCommandPending",
            74 => "Twrp_DataNativeActive",
            _ => "Twrp_CommandFailed"
        });
    }

    private async Task AcquireNativeOwnershipAsync(TwrpOperation operation, CancellationToken token)
    {
        // This interlock stores ownership only. After a crash/disconnect it prevents
        // another native command until recovery has been deliberately restarted.
        await RunNativeSafetyScriptAsync(operation, NativeQueueCheck + "\n" + $$"""
            set -eu
            for cmdline in /proc/[0-9]*/cmdline; do
                command=$(tr '\000' ' ' < "$cmdline" 2>/dev/null || true)
                case "$command" in twrp\ backup*|*/twrp\ backup*|twrp\ restore*|*/twrp\ restore*)
                    printf '%s\n' DDC_NATIVE_PROCESS_ACTIVE
                    exit 74
                    ;;
                esac
            done
            if ! mkdir /tmp/deepdroid-native-active; then
                printf '%s\n' DDC_NATIVE_INTERLOCK_EXISTS
                exit 74
            fi
            printf '%s' {{TwrpSafety.Quote(operation.Id.ToString("N"))}} > /tmp/deepdroid-native-active/owner
            """, token).ConfigureAwait(false);
        operation.RemoteLockOwned = true;
    }

    private async Task ReleaseNativeOwnershipAsync(TwrpOperation operation, CancellationToken token)
    {
        if (!operation.RemoteLockOwned) return;
        await ShellAsync(operation.Device.Serial, $$"""
            set -eu
            test ! -L /tmp/deepdroid-native-active
            test ! -L /tmp/deepdroid-native-active/owner
            test "$(cat /tmp/deepdroid-native-active/owner)" = {{TwrpSafety.Quote(operation.Id.ToString("N"))}}
            rm -f /tmp/deepdroid-native-active/payload
            rm /tmp/deepdroid-native-active/owner
            rmdir /tmp/deepdroid-native-active
            """, token).ConfigureAwait(false);
        operation.RemoteLockOwned = false;
    }
}
