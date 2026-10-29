using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    public async Task<TwrpOperation> FactoryResetAsync(string serial, string expectedMarker,
        IProgress<TwrpProgress>? progress, CancellationToken cancellationToken,
        DeviceActionSource source = DeviceActionSource.SingleDevice)
    {
        TwrpSafety.ValidateSerial(serial);
        cancellationToken.ThrowIfCancellationRequested();
        // The configured type selects recovery entry only; no live identity check.
        if (!IsDataDevice(expectedMarker)) throw new TwrpWorkflowException("FactoryReset_UnsupportedFailed");
        using var ownership = coordinator.TryStart(serial, DeviceActionKind.FactoryReset, canCancel: true,
            cancellationToken, source: source) ?? throw new TwrpWorkflowException("Twrp_DeviceBusy");
        using var stopCleanup = new StopRegistration(_confirmedStops, ownership.OperationId);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ownership.CancellationToken);
        CancellationToken token = lifetime.Token;
        var operation = new TwrpOperation { Kind = TwrpOperationKind.FactoryReset, Device = DataDevice(serial, expectedMarker) };
        cache.Create(operation);
        LogOperation(operation, $"Factory Reset started: operation={operation.Id}; serial={serial}; source={source}; configured type={expectedMarker}.");
        try
        {
            operation.State = TwrpOperationState.EnteringRecovery;
            await EnterRecoveryCoreAsync(serial, expectedMarker, null,
                new DataEntryProgress(key => ReportData(operation, progress, key)), token).ConfigureAwait(false);
            await RunFactoryResetFormatAsync(operation, progress, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await RebootFactoryResetAsync(operation, progress, token).ConfigureAwait(false);
            operation.State = TwrpOperationState.Cleaning;
        }
        catch (Exception exception)
        {
            await HandleFactoryResetFailureAsync(operation, exception, ownership.CancellationReason, progress, token).ConfigureAwait(false);
        }
        finally
        {
            CleanFactoryResetCache(operation, progress);
        }
        if (operation.State == TwrpOperationState.Completed)
        {
            logger.LogInformation("Factory Reset {Operation} on {Serial}: format command exited successfully, reboot system sent, PC cache cleaned.",
                operation.Id, serial);
            // Completion must not recreate operation.log after successful cache cleanup.
            NotifyTwrpProgress(operation, progress, new(operation.Id, operation.State, "FactoryReset_ResultSuccess"));
        }
        return operation;
    }

    private async Task RunFactoryResetFormatAsync(TwrpOperation operation, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        operation.State = TwrpOperationState.NativeFormatRunning;
        ReportData(operation, progress, "FactoryReset_Formatting");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(RecoveryTimeout);
        try
        {
            limit.Token.ThrowIfCancellationRequested();
            LogOperation(operation, "Sending native command once: twrp format data");
            operation.NativeStarted = true;
            var result = await adb.RunAdbShellStreamingAsync(operation.Device.Serial, "twrp format data",
                update => LogProcessUpdate(operation, update), limit.Token).ConfigureAwait(false);
            operation.NativeCompleted = true;
            LogCommandResult(operation, "twrp format data", result);
            limit.Token.ThrowIfCancellationRequested();
            if (result.ExitCode != 0) throw new TwrpWorkflowException("FactoryReset_FormatFailed");
            operation.State = TwrpOperationState.NativeCompleted;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            LogOperation(operation, "Format command timeout after " + RecoveryTimeout + ". Native TWRP may continue.");
            throw new TwrpWorkflowException("FactoryReset_FormatTimeoutFailed");
        }
    }

    private async Task RebootFactoryResetAsync(TwrpOperation operation, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        operation.State = TwrpOperationState.Rebooting;
        ReportData(operation, progress, "FactoryReset_Rebooting");
        token.ThrowIfCancellationRequested();
        LogOperation(operation, "Sending reboot system once; Android startup will not be checked.");
        await AdbAsync(operation, "reboot system", token).ConfigureAwait(false);
    }

    private async Task HandleFactoryResetFailureAsync(TwrpOperation operation, Exception exception,
        DeviceActionCancellationReason cancellationReason, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        TwrpOperationState stage = operation.State;
        operation.StopRequested = token.IsCancellationRequested;
        operation.WorkflowError = GetFactoryResetError(operation, exception);
        operation.State = operation.StopRequested ? TwrpOperationState.Stopped : TwrpOperationState.Failed;
        LogOperation(operation, $"Outcome={operation.State}; stage={stage}; error={operation.WorkflowError}; cancellation={cancellationReason}; format sent={operation.NativeStarted}; command exited={operation.NativeCompleted}.");
        LogOperation(operation, stage == TwrpOperationState.Rebooting
            ? "Reboot may already have been accepted. No automatic command retry."
            : "No reboot system was sent. No automatic format retry.");
        if (operation.StopRequested)
        {
            logger.LogInformation("Factory Reset {Operation} on {Serial} canceled at {Stage}: {Reason}.",
                operation.Id, operation.Device.Serial, stage, cancellationReason);
        }
        else
        {
            LogOperation(operation, exception.ToString());
            logger.LogWarning(exception, "Factory Reset {Operation} on {Serial} failed at {Stage}; logs at {Directory}.",
                operation.Id, operation.Device.Serial, stage, operation.CacheDirectory);
        }
        await CaptureDataLogsAsync(operation).ConfigureAwait(false);
        ReportData(operation, progress, operation.WorkflowError);
    }

    private static string GetFactoryResetError(TwrpOperation operation, Exception exception)
    {
        if (operation.StopRequested)
        {
            if (operation.State == TwrpOperationState.Rebooting) return "FactoryReset_RebootCanceled";
            if (!operation.NativeStarted) return "FactoryReset_EntryCanceled";
            if (operation.NativeCompleted) return "FactoryReset_FormatCompletedCanceled";
            return "FactoryReset_Canceled";
        }
        if (operation.State == TwrpOperationState.Rebooting) return "FactoryReset_RebootFailed";
        if (exception is TwrpWorkflowException known) return known.MessageKey;
        return operation.NativeStarted ? "FactoryReset_UnknownFailed" : "FactoryReset_PreflightFailed";
    }

    private void CleanFactoryResetCache(TwrpOperation operation, IProgress<TwrpProgress>? progress)
    {
        bool success = operation.State == TwrpOperationState.Cleaning;
        if (success) ReportData(operation, progress, "FactoryReset_Cleaning");
        else LogOperation(operation, "Cleaning PC temporary files; retaining logs at " + operation.CacheDirectory);
        try
        {
            cache.Clean(operation, success);
            if (success) operation.State = TwrpOperationState.Completed;
            else LogOperation(operation, "PC cleanup completed; only logs retained.");
        }
        catch (Exception exception)
        {
            LogOperation(operation, "PC cleanup FAILED; temporary files may remain: " + exception);
            logger.LogWarning(exception, "Cannot clean Factory Reset cache for {Operation} on {Serial}.", operation.Id, operation.Device.Serial);
            if (success)
            {
                operation.State = TwrpOperationState.Failed;
                operation.WorkflowError = "FactoryReset_CleanupFailed";
                ReportData(operation, progress, operation.WorkflowError);
            }
        }
    }
}
