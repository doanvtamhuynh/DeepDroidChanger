using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    private async Task RunDirectDataCommandAsync(TwrpOperation operation, string name,
        IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        // A previous CLI crash can leave an ORS command behind. TWRP appends
        // the next CLI command to that file and would otherwise replay both.
        await EnsureNativeCommandQueueEmptyAsync(operation, token).ConfigureAwait(false);
        string command = operation.CliRestore
            ? "twrp restore " + TwrpSafety.Quote(operation.NativeFolder) + " D"
            : "twrp backup D " + TwrpSafety.Quote(name);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(RecoveryTimeout);
        using var timerCancellation = new CancellationTokenSource();
        var elapsed = Stopwatch.StartNew();
        string lastOutput = "";
        var outputTails = new Dictionary<ProcessUpdateKind, string>();
        double? percent = null;
        string processState = "Starting";
        void ReportStatus()
        {
            lock (operation)
                progress?.Report(new(operation.Id, operation.State, "Twrp_NativeStatus",
                    Detail: lastOutput, Percent: percent, Elapsed: elapsed.Elapsed, ProcessStatus: processState));
        }
        async Task TickAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try { while (await timer.WaitForNextTickAsync(timerCancellation.Token).ConfigureAwait(false)) ReportStatus(); }
            catch (OperationCanceledException) when (timerCancellation.IsCancellationRequested) { }
        }
        SetDataState(operation, operation.CliRestore ? TwrpOperationState.NativeRestoreRunning : TwrpOperationState.NativeBackupRunning, progress);
        Log(operation, "process.log", $"{DateTimeOffset.UtcNow:O} adb -s {operation.Device.Serial} shell {command}{Environment.NewLine}");
        Task timerTask = TickAsync();
        try
        {
            limit.Token.ThrowIfCancellationRequested();
            // Mark intent before starting: even failure to receive the start event may mean a lost transport.
            operation.NativeStarted = true;
            var result = await adb.RunAdbShellStreamingAsync(operation.Device.Serial, command, update =>
            {
                lock (operation)
                {
                    processState = update.Kind == ProcessUpdateKind.Exited ? "Exited" : "Running";
                    string label = update.Kind.ToString();
                    Log(operation, "process.log", $"{DateTimeOffset.UtcNow:O} PID={update.ProcessId} {label} {update.ExitCode} {update.Text}{Environment.NewLine}");
                    if (update.Kind is ProcessUpdateKind.StandardOutput or ProcessUpdateKind.StandardError)
                    {
                        Log(operation, "recovery.log", update.Text);
                        lastOutput = update.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? lastOutput;
                        if (lastOutput.Length > 600) lastOutput = lastOutput[^600..];
                        outputTails.TryGetValue(update.Kind, out string? tail);
                        string recent = tail + update.Text;
                        var matches = Regex.Matches(recent, @"(?<![\d.])(100|[0-9]{1,2}(?:\.[0-9]+)?)\s*%", RegexOptions.CultureInvariant);
                        if (matches.Count > 0) percent = double.Parse(matches[^1].Groups[1].Value, CultureInfo.InvariantCulture);
                        outputTails[update.Kind] = recent.Length > 128 ? recent[^128..] : recent;
                    }
                    ReportStatus();
                }
            }, limit.Token).ConfigureAwait(false);
            processState = "Exited";
            LogOperation(operation, "ADB exit code: " + result.ExitCode);
            string output = result.StandardOutput + "\n" + result.StandardError;
            bool complete = HasNativeCompletion(output, operation.CliRestore);
            if (!complete && result.ExitCode == 0)
                complete = await HasTranslatedCompletionAsync(operation, output, token).ConfigureAwait(false);
            bool failed = HasNativeError(output);
            // A terminal native failure also proves writing ended; a bare adb exit does not.
            operation.NativeCompleted = complete || Regex.IsMatch(output,
                @"\[" + (operation.CliRestore ? "RESTORE" : "BACKUP") + @" FAILED[^\]\r\n]*\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (result.ExitCode != 0 || failed) throw new TwrpWorkflowException("Twrp_DataNativeFailed");
            if (!complete) throw new TwrpWorkflowException("Twrp_DirectResultUnknown");
            operation.NativeResultVerified = true;
            SetDataState(operation, TwrpOperationState.NativeCompleted, progress);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            LogOperation(operation, "Native ADB timeout after " + RecoveryTimeout);
            throw new TwrpWorkflowException("Twrp_NativeTimeout");
        }
        finally
        {
            timerCancellation.Cancel();
            await timerTask.ConfigureAwait(false);
        }
    }

    internal static bool HasNativeCompletion(string output, bool restore) => Regex.IsMatch(output,
        @"\[\s*" + (restore ? "RESTORE" : "BACKUP") + @"\s+COMPLETED(?:\s+IN\s+[0-9]+(?:[.,][0-9]+)?\s+SECONDS?)?\s*\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static bool HasNativeError(string output)
    {
        // Some recoveries prefix a missing UI translation with E:, even when
        // native restore succeeds. Keep it in the raw logs, but do not classify
        // this exact diagnostic as a backup/restore error.
        string nativeOutput = Regex.Replace(output,
            @"^[\t ]*E:[\t ]*String resource '[^'\r\n]+' not found\. No default value\.[\t ]*\r?$",
            "", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return Regex.IsMatch(nativeOutput,
            @"(?:^|[\r\n])\s*(?:E:|ERROR\b|(?:Backup|Restore)\s+(?:failed|aborted)\b)|\[(?:BACKUP|RESTORE)\s+FAILED\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private async Task<bool> HasTranslatedCompletionAsync(TwrpOperation operation, string output, CancellationToken token)
    {
        try
        {
            // Optional completion evidence, after process exit. No property or native CLI probe.
            var result = await ShellAsync(operation.Device.Serial, "cat /twres/languages/*.xml 2>/dev/null", token).ConfigureAwait(false);
            string xml = "<languages>" + Regex.Replace(result.StandardOutput, @"<\?xml[^?]*\?>", "") + "</languages>";
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            string key = operation.CliRestore ? "restore_completed" : "backup_completed";
            foreach (var text in XDocument.Load(reader).Descendants("string").Where(e => (string?)e.Attribute("name") == key))
            {
                if (!text.Value.Contains("{1}", StringComparison.Ordinal)) continue;
                string pattern = Regex.Escape(text.Value).Replace(Regex.Escape("{1}"), "[0-9]+(?:[.,][0-9]+)?", StringComparison.Ordinal);
                if (Regex.IsMatch(output, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) return true;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) { LogOperation(operation, "Localized completion evidence unavailable: " + exception.Message); }
        return false;
    }

    private async Task ReserveDirectDataPayloadAsync(TwrpOperation operation, CancellationToken token)
    {
        ValidateDirectDataFolder(operation);
        // TWRP backup rejects an already existing destination. Reserve the path
        // under the exclusive interlock; only restore creates a directory here.
        await ShellAsync(operation.Device.Serial, $$"""
            set -eu
            path={{TwrpSafety.Quote(operation.NativeFolder)}}
            test ! -e "$path"
            test ! -L "$path"
            test "$(cat /tmp/deepdroid-native-active/owner)" = {{TwrpSafety.Quote(operation.Id.ToString("N"))}}
            printf '%s' "$path" > /tmp/deepdroid-native-active/payload
            """, token).ConfigureAwait(false);
        operation.RemotePayloadOwned = true;
        if (operation.CliRestore)
            await ShellAsync(operation.Device.Serial, $$"""
                set -eu
                path={{TwrpSafety.Quote(operation.NativeFolder)}}
                mkdir -p "$path"
                test "$(readlink -f "$path")" = "$path"
                printf '%s' {{TwrpSafety.Quote(operation.Id.ToString("N"))}} > "$path/.deepdroid-operation"
                """, token).ConfigureAwait(false);
    }

    private async Task UploadDirectDataAsync(TwrpOperation operation, TwrpBackupManifest manifest,
        IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        ReportData(operation, progress, "Twrp_Uploading");
        foreach (var artifact in operation.Artifacts)
        {
            string local = Path.Combine(CliPayloadDirectory(operation), manifest.Name, artifact.Name);
            await PayloadTransferAsync(operation.Device.Serial, local, operation.NativeFolder + "/" + artifact.Name, true, token).ConfigureAwait(false);
        }
    }

    private async Task DownloadDirectDataAsync(TwrpOperation operation, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        SetDataState(operation, TwrpOperationState.Downloading, progress);
        string payload = CliPayloadDirectory(operation);
        TwrpArchiveService.CheckDirectory(payload);
        Directory.CreateDirectory(payload);
        await PayloadTransferAsync(operation.Device.Serial, operation.NativeFolder, payload, false, token).ConfigureAwait(false);
        string folder = Path.Combine(payload, TwrpSafety.CliBackupName(operation.ArchivePath!));
        TwrpArchiveService.CheckDirectory(folder);
        if (Directory.EnumerateDirectories(folder).Any()) throw new TwrpWorkflowException("Twrp_InvalidManifest");
        operation.Artifacts = Directory.EnumerateFiles(folder).Where(path => Path.GetFileName(path) != ".deepdroid-operation").Select(path =>
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new TwrpWorkflowException("Twrp_UnsafePath");
            return new TwrpArtifact(Path.GetFileName(path), new FileInfo(path).Length);
        }).OrderBy(file => file.Name, StringComparer.Ordinal).ToArray();
        TwrpArchiveService.ValidateDataArtifacts(operation.Artifacts);
        // Compare a post-completion remote inventory with the downloaded files, catching truncated pulls.
        var inventory = await ShellAsync(operation.Device.Serial, $$"""
            set -eu
            for file in {{TwrpSafety.Quote(operation.NativeFolder)}}/*; do
                test -f "$file"
                test ! -L "$file"
                printf '%s|' "${file##*/}"
                wc -c < "$file"
            done
            """, token).ConfigureAwait(false);
        string[] actual = operation.Artifacts.Select(f => f.Name + "|" + f.Bytes.ToString(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal).ToArray();
        string[] expected = inventory.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => string.Join("|", line.Split('|').Select(field => field.Trim()))).Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expected)) throw new TwrpWorkflowException("Twrp_TransferFailed");
        LogOperation(operation, "Payload inventory and lengths verified.");
    }

    private static void ValidateDirectDataFolder(TwrpOperation operation)
    {
        TwrpSafety.RemoteRoot(operation.NativeFolder);
        string leaf = operation.CliRestore ? "restore_" + operation.Id.ToString("N") : TwrpSafety.CliBackupName(operation.ArchivePath!);
        if (operation.NativeFolder != "/data/media/0/TWRP/BACKUPS/" + operation.Device.Serial + "/" + leaf)
            throw new TwrpWorkflowException("Twrp_UnsafePath");
    }

    private async Task CleanupDataPayloadAsync(TwrpOperation operation, CancellationToken token)
    {
        if (!operation.RemotePayloadOwned) return;
        if (operation.NativeStarted && !operation.NativeCompleted) throw new TwrpWorkflowException("Twrp_DataNativeActive");
        ValidateDirectDataFolder(operation);
        string restoreOwnership = operation.CliRestore ? $$"""
                test ! -L "$path/.deepdroid-operation"
                test "$(cat "$path/.deepdroid-operation")" = {{TwrpSafety.Quote(operation.Id.ToString("N"))}}
            """ : "";
        await ShellAsync(operation.Device.Serial, $$"""
            set -eu
            path={{TwrpSafety.Quote(operation.NativeFolder)}}
            test ! -L /tmp/deepdroid-native-active
            test ! -L /tmp/deepdroid-native-active/owner
            test ! -L /tmp/deepdroid-native-active/payload
            test "$(cat /tmp/deepdroid-native-active/owner)" = {{TwrpSafety.Quote(operation.Id.ToString("N"))}}
            test "$(cat /tmp/deepdroid-native-active/payload)" = "$path"
            if [ -e "$path" ] || [ -L "$path" ]; then
                test "$(readlink -f "$path")" = "$path"
                {{restoreOwnership}}
                rm -rf -- "$path"
            fi
            """, token).ConfigureAwait(false);
        operation.RemotePayloadOwned = false;
    }
}
