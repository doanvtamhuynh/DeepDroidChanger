using DeepDroidChanger.Models;

namespace DeepDroidChanger.Services;

internal static class TwrpScripts
{
    public static string Remote(TwrpSession session) => TwrpSafety.Staging(session.StagingRoot) + "/deepdroid-twrp/" + session.Id.ToString("N");
    public static string Work(TwrpSession session) => "/tmp/deepdroid-" + session.Id.ToString("N");
    public static string Configuration(TwrpSession session)
    {
        TwrpSafety.ValidateSerial(session.Device.Serial);
        TwrpSafety.ValidateSelection(session.Device, session.Partitions, allowOriginalBoot: session.Kind == TwrpOperationKind.Backup);
        string[] nativeIds = session.Partitions.Where(id => id != "boot" || session.NativeBoot).ToArray();
        string selection = string.Join(";", nativeIds.Select(id => session.Device.Partitions.Single(item => item.Id == id).MountPoint)) + ";";
        var values = new Dictionary<string, string>
        {
            ["OP"] = session.Id.ToString("N"), ["MODE"] = session.Kind == TwrpOperationKind.Restore ? "restore" : "backup",
            ["SLOT"] = TwrpSafety.Slot(session.Device.Slot), ["BOOT"] = TwrpSafety.Boot(session.Device.Slot),
            ["BOOT_DEVICE"] = TwrpSafety.RemoteRoot(session.Device.BootDevice), ["REMOTE"] = Remote(session), ["WORK"] = Work(session),
            ["STORAGE"] = TwrpSafety.Storage(session.StorageRoot), ["BACKUP"] = TwrpSafety.RemoteRoot(session.NativeFolder),
            ["SELECTION"] = selection,
            ["CLI_BACKUP"] = session.CliBackup ? "1" : "0",
            ["CLI_RESTORE"] = session.CliRestore ? "1" : "0",
            ["BACKUP_NAME"] = session.CliBackup ? TwrpSafety.CliBackupName(session.ArchivePath!)
                : session.CliRestore ? TwrpSafety.CliBackupName(session.NativeFolder[(session.NativeFolder.LastIndexOf('/') + 1)..] + ".zip") : "",
            ["COMPRESSION"] = session.Options.Compression ? "1" : "0",
            ["SKIP_DIGEST"] = session.Options.GenerateDigest ? "0" : "1",
            ["SKIP_DIGEST_CHECK"] = session.Options.VerifyDigest ? "0" : "1",
            ["SKIP_SPACE"] = session.Options.SkipSpaceCheck ? "1" : "0",
            ["SELECTED_IDS"] = string.Join(" ", nativeIds),
            ["SAVE_BOOT"] = session.Kind == TwrpOperationKind.Backup && !session.NativeBoot && session.Partitions.Contains("boot") ? "1" : "0",
            ["NATIVE_BOOT"] = session.NativeBoot ? "1" : "0",
            ["CAPABILITIES"] = string.Join(" ", session.Device.Partitions.Where(part => part.Id != "data" && (part.Id != "boot" || session.NativeBoot)).Select(part => part.Id + ":" + part.MountPoint)),
            ["HAS_DATA"] = session.Partitions.Contains("data") ? "1" : "0", ["HAS_SUPER"] = session.Partitions.Contains("super") ? "1" : "0"
        };
        return "#!/system/bin/sh\n" + string.Concat(values.Select(pair => pair.Key + "=" + TwrpSafety.Quote(pair.Value) + "\n"));
    }
    public static string Flash(TwrpSession session) => (Configuration(session) + "\n" + """
set -u
trap 'printf finished > "$REMOTE/flash-finished"; sync' EXIT
test "$(id -u)" = 0 || exit 1
test "$(getprop ro.boot.slot_suffix)" = "$SLOT" || exit 1
test "$(readlink -f "$BOOT")" = "$BOOT_DEVICE" || exit 1
test -f "$REMOTE/original.img" || exit 1
rollback() {
    test "$(getprop ro.boot.slot_suffix)" = "$SLOT" &&
    test "$(readlink -f "$BOOT")" = "$BOOT_DEVICE" &&
    dd if="$REMOTE/original.img" of="$BOOT" bs=4194304 conv=fsync
}
trap 'rollback; exit 1' INT TERM HUP
printf 'FLASH_INTENT\n' > "$REMOTE/flash-state.tmp" && mv "$REMOTE/flash-state.tmp" "$REMOTE/flash-state" || exit 1
sync
if ! dd if="$REMOTE/recovery.img" of="$BOOT" bs=4194304 conv=fsync; then
    rollback && printf 'BOOT_RESTORED\n' > "$REMOTE/flash-state"
    exit 1
fi
printf 'TWRP_FLASHED\n' > "$REMOTE/flash-state"
sync
# If Android never enters recovery, restore the saved boot independently of ADB.
trap '' HUP
sleep 60
rollback && printf 'BOOT_RESTORED\n' > "$REMOTE/flash-state"
sync
""").Replace("\r\n", "\n", StringComparison.Ordinal);

    public static string RescuePowerShell(TwrpSession session)
    {
        TwrpSafety.ValidateSerial(session.Device.Serial);
        string boot = TwrpSafety.Boot(session.Device.Slot), remote = Remote(session);
        return $$"""
$ErrorActionPreference = 'Stop'
# ADB only. The device must be online in Android or recovery.
$image = Join-Path $PSScriptRoot 'original.img'
$isRecovery = ((& adb -s '{{session.Device.Serial}}' shell getprop ro.twrp.boot) | Out-String).Trim() -eq '1'
if ($LASTEXITCODE -ne 0) { throw 'Device is not online through ADB.' }
$rootRequested = $false
try {
    if (!$isRecovery) {
        $rootRequested = $true
        & adb -s '{{session.Device.Serial}}' root
        & adb -s '{{session.Device.Serial}}' wait-for-device
    }
    $identity = ((& adb -s '{{session.Device.Serial}}' shell id -u) | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $identity -ne '0') { throw 'ADB root is required.' }
    $gate = @(
        'set -eu'
        'test "$(getprop ro.boot.slot_suffix)" = "{{session.Device.Slot}}"'
        'test "$(readlink -f {{boot}})" = "{{session.Device.BootDevice}}"'
        'if [ "$(getprop ro.twrp.boot)" = 1 ]; then test "$(getprop init.svc.ddc_guard)" != running; stop recovery; sleep 1; ! pidof recovery; fi'
        'if [ -f "{{remote}}/flash-owned" ] && [ ! -f "{{remote}}/flash-finished" ]; then test "$(cat /proc/sys/kernel/random/boot_id)" != "$(cat {{remote}}/flash.bootid)"; fi'
    ) -join "`n"
    $gate | & adb -s '{{session.Device.Serial}}' shell sh
    if ($LASTEXITCODE -ne 0) { throw 'Boot writer is active or device/slot differs. No write requested.' }
    $remote = if ($isRecovery) { '/tmp/deepdroid-rescue-{{session.Id:N}}' } else { '/data/local/tmp/deepdroid-rescue-{{session.Id:N}}' }
    & adb -s '{{session.Device.Serial}}' shell mkdir -p $remote
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create rescue directory.' }
    & adb -s '{{session.Device.Serial}}' push $image ($remote + '/original.img')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot transfer original boot.' }
    $write = $gate + "`n" + 'dd if="' + $remote + '/original.img" of="{{boot}}" bs=4194304 conv=fsync' + "`nsync`n"
    $write | & adb -s '{{session.Device.Serial}}' shell sh
    if ($LASTEXITCODE -ne 0) { throw 'Boot write failed. No reboot requested.' }
    Write-Host 'Original boot written through ADB. No automatic reboot requested.'
} finally {
    if ($rootRequested) { & adb -s '{{session.Device.Serial}}' unroot }
}
""";
    }
}
