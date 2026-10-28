using System.Globalization;
using System.Text.RegularExpressions;
using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    private async Task PrepareDirectScreenAsync(TwrpOperation operation, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        // This is a recovery UI workaround using the configuration snapshot,
        // not another device identity, Data, mount or storage preflight.
        if (operation.RecoveryDevice != "sargo") return;
        ReportData(operation, progress, "Twrp_KeepingScreenAwake");
        try
        {
            // Writing brightness alone cannot re-enable a blanked DRM display.
            // Reuse the recovery's power-key input before sending any TWRP CLI.
            await ShellAsync(operation.Device.Serial, """
                set -eu
                brightness=/sys/class/backlight/panel0-backlight/brightness
                value=$(cat "$brightness")
                case "$value" in ''|*[!0-9]*) exit 1 ;; esac
                if [ "$value" = 0 ]; then
                    power=
                    for node in /dev/input/event*; do
                        if getevent -lp "$node" 2>/dev/null | grep -q 'KEY_POWER'; then power=$node; break; fi
                    done
                    [ -n "$power" ]
                    sendevent "$power" 1 116 1
                    sendevent "$power" 0 0 0
                    sendevent "$power" 1 116 0
                    sendevent "$power" 0 0 0
                fi
                count=0
                while [ "$count" -lt 30 ]; do
                    if [ "$(cat "$brightness")" -gt 0 ] && { [ ! -r /sys/class/drm/card0-DSI-1/enabled ] || [ "$(cat /sys/class/drm/card0-DSI-1/enabled)" = enabled ]; }; then exit 0; fi
                    sleep 0.1
                    count=$((count+1))
                done
                exit 1
                """, token).ConfigureAwait(false);
            await EnsureNativeCommandQueueEmptyAsync(operation, token).ConfigureAwait(false);
            var current = await EntryCommandAsync(ct => adb.RunAdbShellAsync(operation.Device.Serial,
                "twrp get tw_screen_timeout_secs", ct), CommandTimeout, token).ConfigureAwait(false);
            var match = Regex.Match(current.StandardOutput,
                @"(?m)^\s*tw_screen_timeout_secs\s*[:=]\s*([0-9]+)\s*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds))
            {
                LogOperation(operation, "Screen timeout unavailable; screen wake attempted without changing its timeout.");
                return;
            }
            operation.RecoveryScreenTimeout = seconds;
            await EnsureNativeCommandQueueEmptyAsync(operation, token).ConfigureAwait(false);
            await EntryCommandAsync(ct => adb.RunAdbShellAsync(operation.Device.Serial,
                "twrp set tw_screen_timeout_secs 0", ct), CommandTimeout, token).ConfigureAwait(false);
            LogOperation(operation, "Recovery screen awakened; screen timeout temporarily disabled.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (TwrpWorkflowException exception) when (exception.MessageKey == "Twrp_NativeCommandPending") { throw; }
        catch (Exception exception)
        {
            // Screen control is best effort; the device preflight remains get-state only.
            LogOperation(operation, "Recovery screen workaround unavailable: " + exception.Message);
        }
    }

    private async Task RestoreDirectScreenAsync(TwrpOperation operation, CancellationToken token)
    {
        if (operation.RecoveryScreenTimeout is not int seconds || operation.NativeStarted && !operation.NativeCompleted) return;
        await EnsureNativeCommandQueueEmptyAsync(operation, token).ConfigureAwait(false);
        try
        {
            await EntryCommandAsync(ct => adb.RunAdbShellAsync(operation.Device.Serial,
                "twrp set tw_screen_timeout_secs " + seconds.ToString(CultureInfo.InvariantCulture), ct), CommandTimeout, token).ConfigureAwait(false);
            operation.RecoveryScreenTimeout = null;
            LogOperation(operation, "Recovery screen timeout restored.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) { LogOperation(operation, "Unable to restore recovery screen timeout: " + exception.Message); }
    }
}
