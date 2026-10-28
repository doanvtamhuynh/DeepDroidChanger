using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    private async Task UploadDirectDataAsync(TwrpSession session, TwrpBackupManifest manifest,
        IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        ReportData(session, progress, "Twrp_Uploading");
        await OwnDirectDataPayloadAsync(session, create: true, token).ConfigureAwait(false);
        foreach (var artifact in session.Artifacts)
        {
            string local = Path.Combine(CliPayloadDirectory(session), manifest.Name, artifact.Name);
            await PayloadTransferAsync(session.Device.Serial, local, session.NativeFolder + "/" + artifact.Name, true, token).ConfigureAwait(false);
        }
    }

    private async Task RunDirectDataCommandAsync(TwrpSession session, string name,
        IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        string command = session.CliRestore
            ? "twrp restore " + TwrpSafety.Quote(session.NativeFolder) + " D"
            : "twrp backup D " + TwrpSafety.Quote(name);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(RecoveryTimeout);
        string completion = await DirectCompletionPatternAsync(session, token).ConfigureAwait(false);
        // Keep the native command direct; its FIFO exit code alone does not
        // prove success (including when recovery crashes and closes the FIFO).
        var result = await adb.RunAdbShellAsync(session.Device.Serial, command, limit.Token).ConfigureAwait(false);
        session.RecoveryLog = result.StandardOutput + Environment.NewLine + result.StandardError;
        await File.WriteAllTextAsync(Path.Combine(session.CacheDirectory!, "recovery.log"), session.RecoveryLog, CancellationToken.None).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            session.State = TwrpSessionState.Failed;
            throw new TwrpWorkflowException("Twrp_DataNativeFailed");
        }
        if (!Regex.IsMatch(result.StandardOutput, completion, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new TwrpWorkflowException("Twrp_DirectResultUnknown");
        session.NativeResultVerified = true;
        session.NativeCompleted = true;
        session.NativeBackupCompleted = session.CliBackup;
        await DataStateAsync(session, TwrpSessionState.NativeCompleted, progress, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<string> DirectCompletionPatternAsync(TwrpSession session, CancellationToken token)
    {
        string key = session.CliRestore ? "restore_completed" : "backup_completed";
        string template = session.CliRestore ? "[RESTORE COMPLETED IN {1} SECONDS]" : "[BACKUP COMPLETED IN {1} SECONDS]";
        try
        {
            string language = await ReadTwrpVariableAsync(session.Device.Serial, "tw_language", token).ConfigureAwait(false);
            if (language != "en" && Regex.IsMatch(language, "^[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant))
            {
                var xml = await ShellAsync(session.Device.Serial, "cat " + TwrpSafety.Quote("/twres/languages/" + language + ".xml"), token).ConfigureAwait(false);
                using var reader = XmlReader.Create(new StringReader(xml.StandardOutput), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                string? translated = XDocument.Load(reader).Descendants("string").FirstOrDefault(e => (string?)e.Attribute("name") == key)?.Value;
                if (!string.IsNullOrWhiteSpace(translated) && translated.Contains("{1}", StringComparison.Ordinal)) template = translated;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) { logger.LogDebug(exception, "Using the default native completion message for {Operation}.", session.Id); }
        return Regex.Escape(template).Replace(Regex.Escape("{1}"), "[0-9]+", StringComparison.Ordinal);
    }

    private async Task RebootDirectDataAsync(TwrpSession session, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        if (session.RebootRequested) return;
        ReportData(session, progress, "Twrp_State_ReturningAndroid");
        await AdbAsync(session.Device.Serial, "reboot", token).ConfigureAwait(false);
        session.RebootRequested = true;
        // Record command acceptance without polling Android properties.
        await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task DownloadDirectDataAsync(TwrpSession session, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        await OwnDirectDataPayloadAsync(session, create: false, token).ConfigureAwait(false);
        ReportData(session, progress, "Twrp_Downloading");
        session.ExportState = TwrpExportState.Downloading;
        string payload = CliPayloadDirectory(session);
        TwrpArchiveService.CheckDirectory(payload);
        Directory.CreateDirectory(payload);
        string folder = Path.Combine(payload, TwrpSafety.CliBackupName(session.ArchivePath!));
        TwrpArchiveService.CheckDirectory(folder);
        if (Directory.Exists(folder)) DeleteCheckedDirectory(folder);
        await store.SaveAsync(session, token).ConfigureAwait(false);
        // adb pull copies the named native folder into payload. All
        // payload validation happens locally, with no device queries.
        await PayloadTransferAsync(session.Device.Serial, session.NativeFolder, payload, false, token).ConfigureAwait(false);
        TwrpArchiveService.CheckDirectory(folder);
        if (Directory.EnumerateDirectories(folder).Any()) throw new TwrpWorkflowException("Twrp_InvalidManifest");
        session.Artifacts = Directory.EnumerateFiles(folder)
            .Where(path => Path.GetFileName(path) != ".deepdroid-operation").Select(path =>
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new TwrpWorkflowException("Twrp_UnsafePath");
            return new TwrpArtifact(Path.GetFileName(path), new FileInfo(path).Length);
        }).OrderBy(file => file.Name, StringComparer.Ordinal).ToArray();
        TwrpArchiveService.ValidateDataArtifacts(session.Artifacts);
        session.PayloadDownloaded = true;
        await store.SaveAsync(session, token).ConfigureAwait(false);
    }

    private async Task FinishDirectDataAsync(TwrpSession session, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        if (!session.NativeCompleted || !session.NativeResultVerified) throw new TwrpWorkflowException("Twrp_DirectResultUnknown");
        CheckCliCache(session);
        if (session.CliBackup && !session.PayloadDownloaded)
            await DownloadDirectDataAsync(session, progress, token).ConfigureAwait(false);
        if (session.CliBackup && !session.CleanupCompleted)
        {
            await PackageDataBackupAsync(session, progress, token).ConfigureAwait(false);
            session.ExportState = TwrpExportState.Completed;
        }
        if (!session.CleanupCompleted)
        {
            ReportData(session, progress, session.CliBackup ? "Twrp_CleaningBackup" : "Twrp_CleaningRestore");
            ValidateDirectDataFolder(session);
            await CleanupDataPayloadAsync(session, token).ConfigureAwait(false);
            session.CleanupCompleted = true;
            await store.SaveAsync(session, token).ConfigureAwait(false);
        }
        string localPayload = CliPayloadDirectory(session);
        if (Directory.Exists(localPayload)) DeleteCheckedDirectory(localPayload);
        await RestoreDirectScreenAsync(session, token).ConfigureAwait(false);
        await RebootDirectDataAsync(session, progress, token).ConfigureAwait(false);
        session.WorkflowError = null;
        session.ExportError = null;
        session.ExportState = session.CliBackup ? TwrpExportState.Completed : TwrpExportState.None;
        await DataStateAsync(session, TwrpSessionState.Completed, progress, token).ConfigureAwait(false);
        CheckCliCache(session);
        DeleteCheckedDirectory(session.CacheDirectory!);
    }

    private static void ValidateDirectDataFolder(TwrpSession session)
    {
        TwrpSafety.ValidateSerial(session.Device.Serial);
        string leaf = session.CliRestore ? "restore_" + session.Id.ToString("N") : TwrpSafety.CliBackupName(session.ArchivePath!);
        string expected = "/data/media/0/TWRP/BACKUPS/" + session.Device.Serial + "/" + leaf;
        if (!session.DirectCommands || session.StorageRoot != "/data/media/0" || session.NativeFolder != expected)
            throw new TwrpWorkflowException("Twrp_UnsafePath");
    }

    private async Task OwnDirectDataPayloadAsync(TwrpSession session, bool create, CancellationToken token)
    {
        ValidateDirectDataFolder(session);
        // Ownership metadata is used only for payload handling/cleanup, never
        // as a CLI, root, mount, encryption or space preflight.
        string createFolder = create ? "test ! -e \"$path\"\ntest ! -L \"$path\"\nmkdir -p \"$path\"" : "";
        await ShellAsync(session.Device.Serial, $$"""
            set -eu
            path={{TwrpSafety.Quote(session.NativeFolder)}}
            {{createFolder}}
            test "$(readlink -f "$path")" = "$path"
            test ! -L "$path/.deepdroid-operation"
            if [ -e "$path/.deepdroid-operation" ]; then
                test "$(cat "$path/.deepdroid-operation")" = {{TwrpSafety.Quote(session.Id.ToString("N"))}}
            else
                printf '%s' {{TwrpSafety.Quote(session.Id.ToString("N"))}} > "$path/.deepdroid-operation"
            fi
            """, token).ConfigureAwait(false);
    }

    private async Task<TwrpSession> RecoverDirectDataAsync(TwrpSession session, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        using var operation = Start(session.Device.Serial, token, canCancel: true);
        using var stopCleanup = new StopRegistration(_confirmedStops, operation.OperationId);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, operation.CancellationToken);
        if (session.State == TwrpSessionState.Completed) return session;
        try
        {
            if (!session.NativeCompleted || !session.NativeResultVerified) throw new TwrpWorkflowException("Twrp_DirectResultUnknown");
            session.StopRequested = false;
            if (!session.CleanupCompleted && session.RebootRequested)
            {
                // Re-enter only to finish export/cleanup, never to replay the
                // native backup/restore command after an interrupted export.
                await EnterRecoveryCoreAsync(session.Device.Serial, session.Device.Codename, session.SourceImagePath,
                    new DataEntryProgress(key => ReportData(session, progress, key)), lifetime.Token, rememberIdentity: false).ConfigureAwait(false);
                session.RebootRequested = false;
                await store.SaveAsync(session, lifetime.Token).ConfigureAwait(false);
            }
            if (!session.CleanupCompleted) await PrepareDirectScreenAsync(session, progress, lifetime.Token).ConfigureAwait(false);
            await FinishDirectDataAsync(session, progress, lifetime.Token).ConfigureAwait(false);
            return session;
        }
        catch (Exception exception)
        {
            session.StopRequested = _confirmedStops.ContainsKey(operation.OperationId);
            session.State = session.NativeCompleted ? TwrpSessionState.NativeCompleted : TwrpSessionState.NeedsRecovery;
            session.WorkflowError = exception is TwrpWorkflowException known ? known.MessageKey : "Twrp_DirectResultUnknown";
            if (session.CliBackup && session.NativeCompleted)
            {
                session.ExportState = lifetime.IsCancellationRequested ? TwrpExportState.Canceled : TwrpExportState.Failed;
                session.ExportError = session.WorkflowError;
            }
            logger.LogWarning(exception, "Direct Data session {Operation} retained without replay.", session.Id);
            await store.SaveAsync(session, CancellationToken.None).ConfigureAwait(false);
            return session;
        }
    }
}
