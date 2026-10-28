using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    private async Task<string> ReadTwrpVariableAsync(string serial, string name, CancellationToken token)
    {
        var result = await EntryCommandAsync(ct => adb.RunAdbShellAsync(serial, "twrp get " + name, ct),
            CommandTimeout, token).ConfigureAwait(false);
        return GetField(ParseFields(result.StandardOutput), name);
    }

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
    private async Task TransferAsync(string serial, string from, string to, bool push, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token); limit.CancelAfter(TimeSpan.FromMinutes(2));
        var result = push ? await adb.PushFileAsync(serial, from, to, limit.Token).ConfigureAwait(false) : await adb.PullFileAsync(serial, from, to, limit.Token).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new TwrpWorkflowException("Twrp_TransferFailed");
    }
    private static Dictionary<string, string> ParseFields(string output)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            int separator = line.IndexOf('=');
            if (separator < 0) separator = line.IndexOf(':');
            if (separator < 0) continue;

            fields[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return fields;
    }

    private static string GetField(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out string? value) ? value : "";
}
