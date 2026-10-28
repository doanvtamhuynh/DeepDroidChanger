using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    private async Task PrepareDirectScreenAsync(TwrpSession session, IProgress<TwrpProgress>? progress, CancellationToken token)
    {
        if (session.Device.Codename != "sargo") return;
        ReportData(session, progress, "Twrp_KeepingScreenAwake");
        try
        {
            // A backlight write alone cannot enable a blanked DRM CRTC. Wake
            // through the actual power-key device before sending any TWRP CLI.
            await ShellAsync(session.Device.Serial, """
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
            if (session.RecoveryScreenTimeout is null)
            {
                string value = await ReadTwrpVariableAsync(session.Device.Serial, "tw_screen_timeout_secs", token).ConfigureAwait(false);
                if (!int.TryParse(value, out int seconds) || seconds < 0)
                    throw new TwrpWorkflowException("Twrp_ScreenAwakeFailed");
                session.RecoveryScreenTimeout = seconds;
                await store.SaveAsync(session, token).ConfigureAwait(false);
            }
            var result = await ShellAsync(session.Device.Serial, "twrp set tw_screen_timeout_secs 0\ntwrp get tw_screen_timeout_secs", token).ConfigureAwait(false);
            if (GetField(ParseFields(result.StandardOutput), "tw_screen_timeout_secs") != "0")
                throw new TwrpWorkflowException("Twrp_ScreenAwakeFailed");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { throw new TwrpWorkflowException("Twrp_ScreenAwakeFailed"); }
    }

    private async Task RestoreDirectScreenAsync(TwrpSession session, CancellationToken token)
    {
        if (session.RecoveryScreenTimeout is not int seconds) return;
        await EntryCommandAsync(ct => adb.RunAdbShellAsync(session.Device.Serial, "twrp set tw_screen_timeout_secs " + seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), ct), CommandTimeout, token).ConfigureAwait(false);
        session.RecoveryScreenTimeout = null;
        await store.SaveAsync(session, token).ConfigureAwait(false);
    }
}
