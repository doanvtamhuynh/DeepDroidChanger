using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    private async Task<CommandResult> ShellAsync(string serial, string script, CancellationToken token, TimeSpan? timeout = null)
    {
        token.ThrowIfCancellationRequested();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(timeout ?? CommandTimeout);
        var result = await adb.RunAdbShellScriptAsync(serial, script, limit.Token).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new TwrpWorkflowException("Twrp_CommandFailed");
        return result;
    }
    private async Task AdbAsync(string serial, string args, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(CommandTimeout);
        if ((await adb.RunAdbAsync(serial, args, limit.Token).ConfigureAwait(false)).ExitCode != 0) throw new TwrpWorkflowException("Twrp_CommandFailed");
    }
}
