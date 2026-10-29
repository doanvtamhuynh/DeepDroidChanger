using System.IO;
using DeepDroidChanger.Models;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    internal TimeSpan EntryRecoveryTimeout { get; set; } = TimeSpan.FromSeconds(120);

    public async Task EnterRecoveryAsync(string serial, IProgress<string>? progress, CancellationToken cancellationToken,
        DeviceActionSource source = DeviceActionSource.SingleDevice)
    {
        TwrpSafety.ValidateSerial(serial);
        cancellationToken.ThrowIfCancellationRequested();
        using var operation = coordinator.TryStart(serial, DeviceActionKind.EnterTwrp, canCancel: false,
            cancellationToken, source: source) ?? throw new TwrpWorkflowException("Twrp_DeviceBusy");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operation.CancellationToken);
        CancellationToken token = lifetime.Token;
        if (await IsEntryRecoveryAsync(serial, token).ConfigureAwait(false)) return;
        string marker = await GetDeviceMarkerAsync(serial, token).ConfigureAwait(false);
        await EnterRecoveryCoreAsync(serial, marker, null, progress, token).ConfigureAwait(false);
    }

    private async Task EnterRecoveryCoreAsync(string serial, string? expectedMarker, string? sourceImagePath,
        IProgress<string>? progress, CancellationToken token)
    {
        string failureKey = "Twrp_EntryInspectFailed";
        try
        {
            progress?.Report("Twrp_EntryInspect");
            if (await IsEntryRecoveryAsync(serial, token).ConfigureAwait(false)) return;
            string marker = expectedMarker ?? "";
            if (!IsDataDevice(marker)) throw new TwrpWorkflowException("Twrp_EntryUnsupportedDevice");

            failureKey = "Twrp_EntryInvalidImage";
            using FileStream? image = marker == "starlte" ? null : await images.OpenBootSourceAsync(marker, token, sourceImagePath).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            failureKey = "Twrp_EntryRebootFailed";
            progress?.Report(marker == "starlte" ? "Twrp_EntryRebootRecovery" : "Twrp_EntryRebootBootloader");
            await EntryCommandAsync(ct => adb.RunAdbAsync(serial,
                marker == "starlte" ? "reboot recovery" : "reboot bootloader", ct), CommandTimeout, token).ConfigureAwait(false);

            if (image is not null)
            {
                failureKey = "Twrp_EntryBootFailed";
                progress?.Report("Twrp_EntryBoot");
                // Both arguments are validated: serial is a safe token and Name is
                // an existing Windows .img path, which cannot contain a double quote.
                await EntryCommandAsync(ct => adb.RunFastbootAsync($"-s \"{serial}\" boot \"{image.Name}\"", ct),
                    TimeSpan.FromSeconds(120), token).ConfigureAwait(false);
            }

            failureKey = "Twrp_EntryRecoveryWaitFailed";
            progress?.Report("Twrp_EntryWaitRecovery");
            await WaitForEntryAsync(ct => IsEntryRecoveryAsync(serial, ct), EntryRecoveryTimeout, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (TwrpWorkflowException) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Entering TWRP for {Serial} failed at {Stage}.", serial, failureKey);
            throw new TwrpWorkflowException(failureKey, exception);
        }
    }

    private async Task<bool> IsEntryRecoveryAsync(string serial, CancellationToken token)
    {
        var result = await EntryCommandAsync(ct => adb.RunAdbAsync(serial, "get-state", ct),
            TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
        return result.StandardOutput.Trim() == "recovery";
    }

    private static async Task<CommandResult> EntryCommandAsync(Func<CancellationToken, Task<CommandResult>> command,
        TimeSpan timeout, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(timeout);
        limit.Token.ThrowIfCancellationRequested();
        var result = await command(limit.Token).ConfigureAwait(false);
        limit.Token.ThrowIfCancellationRequested();
        if (result.ExitCode != 0)
            throw new IOException($"Device command failed with exit code {result.ExitCode}: {result.StandardOutput.Trim()} {result.StandardError.Trim()}");
        return result;
    }

    private async Task WaitForEntryAsync(Func<CancellationToken, Task<bool>> ready, TimeSpan timeout, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(timeout);
        while (true)
        {
            limit.Token.ThrowIfCancellationRequested();
            try
            {
                if (await ready(limit.Token).ConfigureAwait(false)) return;
            }
            catch (OperationCanceledException) when (limit.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                // ADB is expected to disappear during the mode transition.
                logger.LogDebug(exception, "Waiting for TWRP entry transport.");
            }
            await Task.Delay(PollInterval, limit.Token).ConfigureAwait(false);
        }
    }
}
