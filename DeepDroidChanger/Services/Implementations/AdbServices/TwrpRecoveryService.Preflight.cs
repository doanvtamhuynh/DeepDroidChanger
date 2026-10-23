using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using DeepDroidChanger.Models;
using DeepDroidChanger.Constants;
using Microsoft.Extensions.Logging;

namespace DeepDroidChanger.Services;

public sealed partial class TwrpRecoveryService
{
    public async Task<string> GetDeviceMarkerAsync(string serial, CancellationToken cancellationToken)
    {
        TwrpSafety.ValidateSerial(serial);
        return (await adb.GetPropertyAsync(serial, PropertyConstants.DeepDroidDevice, cancellationToken).ConfigureAwait(false)).Trim();
    }

    public async Task<TwrpPreflight> InspectAsync(string serial, CancellationToken cancellationToken)
    {
        TwrpSafety.ValidateSerial(serial);
        using var ownership = Start(serial, cancellationToken);
        return await InspectCoreAsync(serial, cancellationToken).ConfigureAwait(false);
    }
    private IDeviceActionOperation Start(string serial, CancellationToken token, bool canCancel = false) =>
        coordinator.TryStart(serial, DeviceActionKind.TwrpRecovery, canCancel, token, source: DeviceActionSource.SingleDevice)
        ?? throw new TwrpWorkflowException("Twrp_DeviceBusy");

    private async Task<TwrpPreflight> InspectCoreAsync(string serial, CancellationToken token, string? sourceImagePath = null)
    {
        // This marker is the device identity supplied by DeepDroid. Product,
        // build, hardware and bootloader properties may intentionally be spoofed.
        string device = (await adb.GetPropertyAsync(serial, PropertyConstants.DeepDroidDevice, token).ConfigureAwait(false)).Trim();
        if (device != "sargo") return new(null, ["Twrp_UnsupportedDevice"]);
        try { await images.VerifySourceAsync(token, sourceImagePath).ConfigureAwait(false); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or TwrpWorkflowException) { return new(null, ["Twrp_InvalidImage"]); }
        CommandResult inventory;
        try
        {
            inventory = await root.ExecuteAsRootAsync(serial, async rootToken =>
                await ShellAsync(serial, """
                    set -eu
                    test "$(id -u)" = 0
                    printf 'slot=%s\n' "$(getprop ro.boot.slot_suffix)"
                    for name in boot_a boot_b super system_a system_b vendor_a vendor_b modem_a modem_b modemst1 modemst2 fsg persist metadata userdata; do
                        path=/dev/block/by-name/$name
                        if [ -b "$path" ]; then printf 'block|%s|%s\n' "$name" "$(readlink -f "$path")"; fi
                    done
                    for path in /data/local/tmp /metadata /data/adb; do
                        if [ -d "$path" ] && [ "$(readlink -f "$path")" = "$path" ]; then printf 'stage|%s\n' "$path"; fi
                    done
                    for path in /mnt/media_rw/*; do
                        [ ! -d "$path" ] || printf 'stage|%s\n' "$path"
                    done
                    """, rootToken).ConfigureAwait(false), token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "TWRP root preflight failed for {Serial}.", serial);
            return new(null, ["Twrp_RootRequired"]);
        }
        return await ParseInventoryAsync(serial, device, inventory.StandardOutput, token, sourceImagePath).ConfigureAwait(false);
    }

    private async Task<TwrpPreflight> ParseInventoryAsync(string serial, string deviceMarker, string output, CancellationToken token, string? sourceImagePath)
    {
        var issues = new List<string>();
        string slot;
        try { slot = TwrpSafety.Slot(GetField(ParseFields(output), "slot")); }
        catch (TwrpWorkflowException) { return new(null, ["Twrp_InvalidSlot"]); }
        var blocks = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in output.Split('\n'))
        {
            string[] fields = line.Trim().Split('|');
            if (fields.Length != 3 || fields[0] != "block") continue;
            if (!TwrpSafety.SafeName().IsMatch(fields[1]) || !Regex.IsMatch(fields[2], "^/dev/block/[A-Za-z0-9_./-]+$", RegexOptions.CultureInvariant)
                || fields[2].Contains("..", StringComparison.Ordinal)) throw new TwrpWorkflowException("Twrp_UnsafePath");
            blocks.Add(fields[1], fields[2]);
        }
        if (!blocks.TryGetValue("boot" + slot, out string? boot)) return new(null, ["Twrp_InvalidLayout"]);
        var partitions = new List<TwrpPartition> { new("boot", "/boot", "boot" + slot, 0) };
        if (blocks.ContainsKey("userdata")) partitions.Insert(0, new("data", "/data", "userdata", 0));
        if (blocks.ContainsKey("super")) partitions.Add(new("super", "/super", "super", 0));
        foreach ((string id, string mount, string block) in new[]
        {
            ("efs1", "/ddc_efs1", "modemst1"), ("efs2", "/ddc_efs2", "modemst2"), ("fsg", "/ddc_fsg", "fsg"),
            ("modem", "/ddc_modem", "modem" + slot), ("persist", "/persist_image", "persist")
        })
            if (blocks.ContainsKey(block)) partitions.Add(new(id, mount, block, 0, true));
        var candidates = output.Split('\n').Select(line => line.Trim().Split('|'))
            .Where(fields => fields.Length == 2 && fields[0] == "stage")
            .Select(fields => TwrpSafety.Staging(fields[1])).Distinct(StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) issues.Add("Twrp_NoStaging");
        var device = new TwrpDeviceInfo(serial, deviceMarker, "", "", 0, slot,
            boot, images.GetSourceImageBytes(sourceImagePath), TwrpSafety.HashText(string.Join("\n", blocks.Select(pair => $"{pair.Key}|{pair.Value}"))), "",
            0, partitions, candidates);
        // Discover capabilities in the recovery reached by the real operation. Cached
        // observations may show optional partitions, but never authorize a boot write.
        var previous = (await store.ListAsync(serial, token).ConfigureAwait(false))
            .FirstOrDefault(item => TwrpSafety.Compatible(item.Device, device) && !string.IsNullOrEmpty(item.RecoveryLog));
        var supported = previous is null ? [] : GetField(ParseFields(previous.RecoveryLog), "supported").Split(';', StringSplitOptions.RemoveEmptyEntries);
        return new(device, issues, supported);
    }

}
