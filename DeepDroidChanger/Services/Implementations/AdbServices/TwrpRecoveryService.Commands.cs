using System.IO;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    private async Task<CommandResult> ShellAsync(string serial, string script, CancellationToken token, TimeSpan? timeout = null)
    {
        token.ThrowIfCancellationRequested();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(timeout ?? CommandTimeout);
        var result = await adb.RunAdbShellScriptAsync(serial, script, limit.Token).ConfigureAwait(false);
        limit.Token.ThrowIfCancellationRequested();
        if (result.ExitCode != 0)
            throw new TwrpWorkflowException("Twrp_CommandFailed",
                new IOException($"Shell command exited with code {result.ExitCode}: {result.StandardOutput.Trim()} {result.StandardError.Trim()}"));
        return result;
    }

    private async Task AdbAsync(TwrpOperation operation, string args, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(CommandTimeout);
        var result = await adb.RunAdbAsync(operation.Device.Serial, args, limit.Token).ConfigureAwait(false);
        LogCommandResult(operation, args, result, includeOutput: true);
        limit.Token.ThrowIfCancellationRequested();
        if (result.ExitCode != 0) throw new TwrpWorkflowException("Twrp_CommandFailed");
    }
}
