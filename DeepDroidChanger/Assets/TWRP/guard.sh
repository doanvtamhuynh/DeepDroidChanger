# The image builder prepends the immutable operation configuration.
# This process is owned by recovery init, never by adb or the TWRP action thread.
set -u
umask 077
mkdir -p "$WORK" || exit 1
exec >> "$WORK/guard.log" 2>&1
trap '' HUP
ERROR=none
BOOT_ERROR=none
RESTORED=0
STARTED=0
DONE=0
DATA_READABLE=0
SUPER_SUPPORTED=0
SUPPORTED=
SKIPPED=
ORIGINAL_READY=0
CLI_PID=
GUARD_PID=$$

stop_requested() { [ "$(cat "$WORK/stop-request" 2>/dev/null)" = "$OP" ]; }
stop_native() {
    # Freeze the engine first so it cannot spawn another writer while its
    # descendants are collected. Never signal adb or the independent guard.
    native_pids=$(pidof recovery 2>/dev/null || :)
    for pid in $native_pids; do kill -STOP "$pid" 2>/dev/null || :; done
    changed=0
    [ -z "$native_pids" ] || changed=1
    while [ "$changed" = 1 ]; do
        changed=0
        for status_file in /proc/[0-9]*/status; do
            [ -r "$status_file" ] || continue
            parent=$(awk '/^PPid:/ {print $2}' "$status_file" 2>/dev/null)
            case "$parent" in ''|*[!0-9]*) continue ;; esac
            case " $native_pids " in *" $parent "*)
                pid=${status_file#/proc/}; pid=${pid%/status}
                case " $native_pids " in *" $pid "*) ;; *)
                    kill -STOP "$pid" 2>/dev/null || :
                    native_pids="$native_pids $pid"; changed=1 ;;
                esac ;;
            esac
        done
    done
    identities=
    for pid in $native_pids; do
        birth=$(awk '{print $22}' "/proc/$pid/stat" 2>/dev/null)
        [ -z "$birth" ] || identities="$identities $pid:$birth"
    done
    stop recovery || :
    for identity in $identities; do
        pid=${identity%:*}; birth=${identity#*:}
        [ "$(awk '{print $22}' "/proc/$pid/stat" 2>/dev/null)" != "$birth" ] || kill -KILL "$pid" 2>/dev/null || :
    done
    count=0
    while [ "$count" -lt 15 ]; do
        live=0
        for pid in $native_pids; do
            [ ! -r "/proc/$pid/status" ] || grep -q '^State:.*Z' "/proc/$pid/status" || live=1
        done
        if [ "$live" = 0 ] && ! pidof recovery >/dev/null 2>&1; then return 0; fi
        sleep 1; count=$((count+1))
    done
    return 1
}

save_result() {
    printf 'op=%s\nstate=%s\nerror=%s\nbootError=%s\nbootRestored=%s\ndataReadable=%s\nsuperSupported=%s\nsupported=%s\nselected=%s\nskipped=%s\n' \
        "$OP" "$1" "$ERROR" "$BOOT_ERROR" "$RESTORED" "$DATA_READABLE" "$SUPER_SUPPORTED" "$SUPPORTED" "$SELECTED_IDS" "$SKIPPED" > "$WORK/result.tmp"
    mv "$WORK/result.tmp" "$WORK/result"
    # /data can have been replaced by native restore. Recreate only this exact
    # operation directory; the original remains in tmpfs throughout.
    if [ "$ORIGINAL_READY" = 1 ] && mkdir -p "$REMOTE" && [ "$(readlink -f "$REMOTE")" = "$REMOTE" ]; then
        cp "$WORK/result" "$REMOTE/result.tmp" && mv "$REMOTE/result.tmp" "$REMOTE/result"
        cp "$WORK/guard.log" "$REMOTE/guard.log" || :
        [ ! -f "$WORK/artifacts" ] || cp "$WORK/artifacts" "$REMOTE/artifacts"
        [ ! -f /tmp/recovery.log ] || cp /tmp/recovery.log "$REMOTE/recovery.log"
    fi
    sync
}
restore_boot() {
    [ "$ORIGINAL_READY" = 1 ] || return 1
    [ "$(getprop ro.boot.slot_suffix)" = "$SLOT" ] || return 1
    [ "$(readlink -f "$BOOT")" = "$BOOT_DEVICE" ] || return 1
    [ -f "$WORK/original.img" ] || return 1
    save_result RESTORE_BOOT_INTENT
    dd if="$WORK/original.img" of="$BOOT" bs=4194304 conv=fsync || return 1
    sync
    RESTORED=1

}
finish() {
    trap - EXIT
    # Stop requests must never interrupt the original-boot writer.
    trap '' INT TERM
    rm -f "$WORK/engine-running"
    [ -z "$CLI_PID" ] || kill -TERM "$CLI_PID" 2>/dev/null || :
    if [ "$STARTED" = 1 ] && [ "$DONE" != 1 ]; then
        # Never reboot while native restore can still write another partition.
        if ! stop_native; then
            BOOT_ERROR=native_still_running
            save_result NEEDS_RECOVERY
            exit 1
        fi
    fi
    if ! restore_boot; then
        BOOT_ERROR=boot_restore_failed
        save_result NEEDS_RECOVERY
        exit 1
    fi
    save_result BOOT_RESTORED
    if [ "$MODE" = restore ] && [ "$STARTED" = 1 ] && [ "$ERROR" != none ]; then
        save_result NEEDS_RECOVERY
        exit 1
    fi
    if [ "$ERROR" = none ]; then save_result COMPLETED; else save_result FAILED; fi
    sync
    # Give the host a chance to collect RAM-only results before reboot. The
    # guard still returns Android independently if the PC is disconnected.
    ack_wait=0
    while [ ! -f "$WORK/host-ack" ] && [ "$ack_wait" -lt 60 ]; do sleep 1; ack_wait=$((ack_wait+1)); done
    reboot system
}
trap finish EXIT
trap 'if stop_requested; then ERROR=user_stopped; else ERROR=interrupted; fi; exit 1' INT TERM
fail() { ERROR=$1; exit 1; }

# Resolve the original boot and current slot before any write.
[ "$(id -u)" = 0 ] || fail root_missing
[ "$(getprop ro.boot.slot_suffix)" = "$SLOT" ] || fail slot_mismatch
[ "$(readlink -f "$BOOT")" = "$BOOT_DEVICE" ] || fail block_mismatch
case "$REMOTE" in /metadata/*)
    if ! grep -q ' /metadata ' /proc/mounts; then mount /dev/block/by-name/metadata /metadata || :; fi ;;
esac
original_wait=0
until [ -f "$WORK/original.img" ]; do
    if [ -f "$WORK/pc-original.img" ]; then
        mv "$WORK/pc-original.img" "$WORK/original.img" || fail original_copy_failed
    elif [ -r "$REMOTE/original.img" ] && [ "$(readlink -f "$REMOTE")" = "$REMOTE" ]; then
        cp "$REMOTE/original.img" "$WORK/device-original.part" && mv "$WORK/device-original.part" "$WORK/original.img" || fail original_copy_failed
    else
        original_wait=$((original_wait+1)); [ "$original_wait" -le 120 ] || fail original_unavailable
        sleep 1
    fi
done
ORIGINAL_READY=1
if [ "$(cat "$REMOTE/stop-request" 2>/dev/null)" = "$OP" ]; then
    printf '%s' "$OP" > "$WORK/stop-request"
fi

cli() {
    stop_requested && fail user_stopped
    phase=$1
    body=$2
    rm -f "$WORK/bridge-$phase"
    {
        printf 'set ddc_bridge_active 1\nset ddc_phase %s\n' "$phase"
        printf '%s\n' "$body"
    } > "$WORK/command.ors"
    # Keep the guard responsive even if the CLI listener blocks. Only this
    # operation's CLI client is a child; the native engine is stopped via init.
    timeout 120 twrp runscript "$WORK/command.ors" > "$WORK/cli-$phase.log" 2>&1 &
    CLI_PID=$!
    while kill -0 "$CLI_PID" 2>/dev/null; do
        stop_requested && fail user_stopped
        sleep 1
    done
    wait "$CLI_PID"
    cli_status=$?
    CLI_PID=
    [ "$cli_status" = 0 ] || return 1
    count=0
    limit=120
    [ "$phase" != run ] || limit=7200
    until [ -f "$WORK/bridge-$phase" ]; do
        stop_requested && fail user_stopped
        count=$((count+1)); [ "$count" -le "$limit" ] || return 1
        sleep 1
    done
    # Let the same TWRP action thread finish its page-return action before
    # sending another CLI command. A missing handshake still fails closed.
    sleep 1
    [ "$(cat "$WORK/bridge-$phase")" = "$OP|$phase|0" ]
}
# CLI commands append to this queue; refuse stale commands instead of replaying them.
if [ "${CLI_BACKUP:-0}" = 1 ] || [ "${CLI_RESTORE:-0}" = 1 ]; then
    [ ! -s /tmp/openrecoveryscript ] || fail stale_ors_queue
fi
cli probe 'set tw_simulate_actions 0
set tw_simulate_fail 0
set tw_cancel_backup 0
set ddc_arg ddc_probe=1
set ddc_function set' || fail bridge_failed
cli query 'set ddc_function set' || fail capability_probe_failed
value() { sed -n "s/^$1=//p" "$WORK/values-$2" | tail -1; }
encrypted=$(value encrypted query)
decrypted=$(value decrypted query)
data_size=$(value dataSize query)
case "$data_size" in ''|*[!0-9]*) data_size=0 ;; esac
if [ "$data_size" -gt 0 ] && { [ "$encrypted" = 0 ] || [ "$decrypted" = 1 ]; } && [ -r /data/system_ce/0 ]; then DATA_READABLE=1; fi
save_result GUARD_READY
# The original is already in RAM and the bridge is proven before the user may
# unlock Data in TWRP. No password is collected or persisted by the PC app.
if [ "$HAS_DATA" = 1 ]; then
    decrypt_wait=0
    while [ "$DATA_READABLE" != 1 ] && [ "$encrypted" = 1 ] && [ "$decrypt_wait" -lt 60 ]; do
        sleep 5
        cli query 'set ddc_function refreshsizes' || fail capability_probe_failed
        encrypted=$(value encrypted query)
        decrypted=$(value decrypted query)
        data_size=$(value dataSize query)
        case "$data_size" in ''|*[!0-9]*) data_size=0 ;; esac
        if [ "$data_size" -gt 0 ] && [ "$decrypted" = 1 ] && [ -r /data/system_ce/0 ]; then DATA_READABLE=1; fi
        decrypt_wait=$((decrypt_wait+1))
    done
fi
[ "$DATA_READABLE" != 1 ] || SUPPORTED='data;'
# Probe the engine's real partition objects, not a guessed DataManager variable.
# Clearing the previous values prevents a missing partition inheriting stale data.
if [ "${CLI_BACKUP:-0}" != 1 ] && [ "${CLI_RESTORE:-0}" != 1 ]; then
for capability in $CAPABILITIES; do
    part_id=${capability%%:*}
    part_mount=${capability#*:}
    cli partition "set tw_partition_is_present 0
set tw_partition_size 0
set tw_partition_mount_point ddc_missing
set ddc_partition $part_mount;
set ddc_arg ddc_partition
set ddc_function getpartitiondetails" || fail capability_probe_failed
    present=$(value present partition)
    size=$(value size partition)
    mount_point=$(value mount partition)
    fs=$(value fs partition)
    case "$size" in ''|*[!0-9]*) size=0 ;; esac
    if [ "$present" = 1 ] && [ "$size" -gt 0 ] && [ "$mount_point" = "$part_mount" ] && [ "$fs" = emmc ]; then
        SUPPORTED="$SUPPORTED$part_id;"
        [ "$part_id" != super ] || SUPER_SUPPORTED=1
    fi
done
fi
[ "$HAS_DATA" != 1 ] || [ "$DATA_READABLE" = 1 ] || fail data_not_decrypted_or_empty
if [ "${CLI_RESTORE:-0}" = 1 ]; then
    [ "$MODE" = restore ] && [ "$SELECTED_IDS" = data ] || fail invalid_cli_selection
    [ -d "$STORAGE" ] && [ "$(readlink -f "$STORAGE")" = "$STORAGE" ] || fail storage_unavailable
    [ -d "$BACKUP" ] && [ "$(readlink -f "$BACKUP")" = "$BACKUP" ] || fail backup_unavailable
    [ ! -L "$BACKUP/.deepdroid-operation" ] && [ "$(cat "$BACKUP/.deepdroid-operation" 2>/dev/null)" = "$OP" ] || fail backup_owner_mismatch
    data_files=0
    for file in "$BACKUP/"*; do
        [ -f "$file" ] && [ ! -L "$file" ] || fail unsafe_artifact
        name=${file##*/}
        case "$name" in
            data.*.win|data.*.win[0-9][0-9][0-9]) [ -s "$file" ] || fail backup_payload_missing; data_files=$((data_files+1)) ;;
            data.*|recovery.log) ;;
            *) fail unsafe_artifact ;;
        esac
    done
    [ "$data_files" -gt 0 ] || fail backup_payload_missing
    cli setup "set tw_storage_path $STORAGE
set tw_enable_adb_backup 0
set tw_encrypt_backup 0
set tw_cancel_backup 0
set ddc_function set" || fail setup_failed
    # Relative-name restore recalculates the backup folder while enumerating
    # mounted storage. Prove its default resolves to this operation's upload,
    # and refuse a same-name backup on another storage before native can write.
    timeout 120 twrp get tw_backups_folder > "$WORK/restore-folder.log" 2>&1 || fail restore_folder_mismatch
    engine_parent=$(sed -n 's/^tw_backups_folder = //p' "$WORK/restore-folder.log" | tail -1)
    [ -n "$engine_parent" ] && [ ! -L "$engine_parent/$BACKUP_NAME/.deepdroid-operation" ] || fail restore_folder_mismatch
    [ "$(cat "$engine_parent/$BACKUP_NAME/.deepdroid-operation" 2>/dev/null)" = "$OP" ] || fail restore_folder_mismatch
    engine_id=${engine_parent##*/}
    for storage in "$STORAGE" /sdcard /storage/emulated/0 /storage/* /mnt/media_rw/* /usb_otg; do
        candidate="$storage/TWRP/BACKUPS/$engine_id/$BACKUP_NAME"
        [ -e "$candidate" ] || continue
        [ ! -L "$candidate/.deepdroid-operation" ] && [ "$(cat "$candidate/.deepdroid-operation" 2>/dev/null)" = "$OP" ] || fail restore_folder_collision
    done
    rm -f "$WORK/native-restore-complete"
    [ ! -s /tmp/openrecoveryscript ] || fail stale_ors_queue
    save_result RECOVERY_RUNNING
    stop_requested && fail user_stopped
    STARTED=1
    touch "$WORK/engine-running"
    # The parent polls Stop while waiting for the CLI. Do not spawn/kill a
    # second shell here: Android mksh can interrupt this guard on that cleanup.
    # First ORS line is exactly `restore <name>`. The continuation records
    # native success for this operation; rollback remains in independent finish().
    restore_arg=$(printf '%s\ncmd /system/bin/sh /system/bin/deepdroid-record.sh restore-complete\n' "$BACKUP_NAME")
    timeout 7200 twrp restore "$restore_arg" > "$WORK/cli-restore.log" 2>&1 &
    CLI_PID=$!
    while kill -0 "$CLI_PID" 2>/dev/null; do
        stop_requested && fail user_stopped
        sleep 1
    done
    wait "$CLI_PID"
    cli_status=$?
    CLI_PID=
    [ "$cli_status" = 0 ] || fail native_operation_failed
    DONE=1
    [ "$(cat "$WORK/native-restore-complete" 2>/dev/null)" = "$OP" ] || fail native_operation_failed
    rm -f "$WORK/engine-running"
    stop_requested && fail user_stopped
    exit 0
fi
# Direct CLI backup: keep recovery/boot owned until the PC confirms the pull.
if [ "${CLI_BACKUP:-0}" = 1 ]; then
    [ "$MODE" = backup ] && [ "$SELECTED_IDS" = data ] || fail invalid_cli_selection
    [ "$DATA_READABLE" = 1 ] || fail data_not_decrypted_or_empty
    [ -d "$STORAGE" ] && [ "$(readlink -f "$STORAGE")" = "$STORAGE" ] || fail storage_unavailable
    [ ! -e "$BACKUP" ] || fail backup_already_exists
    mkdir -p "${BACKUP%/*}" || fail storage_unavailable
    [ "$(readlink -f "${BACKUP%/*}")" = "${BACKUP%/*}" ] || fail storage_unavailable
    cli setup "set tw_storage_path $STORAGE
set tw_backups_folder ${BACKUP%/*}
set tw_enable_adb_backup 0
set tw_encrypt_backup 0
set tw_disable_free_space 0
set tw_cancel_backup 0
set ddc_function set" || fail setup_failed
    save_result RECOVERY_RUNNING
    stop_requested && fail user_stopped
    STARTED=1
    touch "$WORK/engine-running"
    # Stop is handled by the parent CLI wait loop, not a signalled subshell.
    [ ! -s /tmp/openrecoveryscript ] || fail stale_ors_queue
    timeout 7200 twrp backup D "$BACKUP_NAME" > "$WORK/cli-backup.log" 2>&1 &
    CLI_PID=$!
    while kill -0 "$CLI_PID" 2>/dev/null; do
        stop_requested && fail user_stopped
        sleep 1
    done
    wait "$CLI_PID"
    cli_status=$?
    CLI_PID=
    [ "$cli_status" = 0 ] || fail native_operation_failed
    DONE=1
    rm -f "$WORK/engine-running"
    stop_requested && fail user_stopped
    # TWRP copies recovery.log into the new folder at the end of Run_Backup.
    # CLI exit 0 or a directory alone does not prove a completed backup.
    [ -d "$BACKUP" ] && [ "$(readlink -f "$BACKUP")" = "$BACKUP" ] || fail backup_payload_missing
    [ -s "$BACKUP/recovery.log" ] && [ ! -L "$BACKUP/recovery.log" ] || fail backup_payload_missing
    : > "$WORK/artifacts"
    data_files=0
    for file in "$BACKUP/"*; do
        [ ! -L "$file" ] || fail unsafe_artifact
        [ -f "$file" ] || fail unsafe_artifact
        name=${file##*/}
        case "$name" in ''|*[!a-zA-Z0-9_.-]*) fail unsafe_artifact ;; esac
        size=$(stat -c %s "$file") || fail artifact_list_failed
        case "$name" in data.*.win|data.*.win[0-9][0-9][0-9])
            [ "$size" -gt 0 ] || fail backup_payload_missing
            data_files=$((data_files+1)) ;;
        esac
        printf '%s|%s|\n' "$name" "$size" >> "$WORK/artifacts" || fail artifact_list_failed
    done
    [ "$data_files" -gt 0 ] || fail backup_payload_missing
    printf '%s' "$OP" > "$BACKUP/.deepdroid-operation" || fail artifact_list_failed
    save_result BACKUP_READY
    pull_wait=0
    until [ "$(cat "$WORK/payload-pulled" 2>/dev/null)" = "$OP" ]; do
        stop_requested && fail user_stopped
        [ "$(cat "$WORK/pull-failed" 2>/dev/null)" != "$OP" ] || fail payload_pull_failed
        pull_wait=$((pull_wait+1)); [ "$pull_wait" -le 7200 ] || fail payload_pull_timeout
        sleep 1
    done
    exit 0
fi
effective=
SELECTION=
for selected in $SELECTED_IDS; do
    case ";$SUPPORTED" in
        *";$selected;"*)
            effective="$effective $selected"
            if [ "$selected" = data ]; then
                SELECTION="${SELECTION}/data;"
            else
                for capability in $CAPABILITIES; do
                    [ "${capability%%:*}" != "$selected" ] || SELECTION="$SELECTION${capability#*:};"
                done
            fi ;;
        *) SKIPPED="$SKIPPED $selected" ;;
    esac
done
SELECTED_IDS=$effective
if [ -z "$SELECTED_IDS" ]; then
    # A selected original Boot is exported by the PC, never by native nandroid.
    if [ "$MODE" = backup ] && [ "${SAVE_BOOT:-0}" = 1 ]; then : > "$WORK/artifacts"; exit 0; fi
    fail no_partitions_to_process
fi
[ -d "$STORAGE" ] && [ "$(readlink -f "$STORAGE")" = "$STORAGE" ] || fail storage_unavailable
if [ "$MODE" = backup ]; then
    [ ! -e "$BACKUP" ] || fail backup_already_exists
    mkdir -p "${BACKUP%/*}" || fail storage_unavailable
else
    [ -d "$BACKUP" ] && [ "$(readlink -f "$BACKUP")" = "$BACKUP" ] || fail backup_unavailable

fi
if [ "$MODE" = backup ] && [ "${NATIVE_BOOT:-0}" = 1 ]; then
    case ";$SUPPORTED" in *';boot;'*)
        # Recovery and the guard already run in RAM. Native /boot must read the
        # original boot, never the temporary TWRP image written by the host.
        stop_requested && fail user_stopped
        trap '' INT TERM
        restore_boot || fail boot_restore_failed
        trap 'if stop_requested; then ERROR=user_stopped; else ERROR=interrupted; fi; exit 1' INT TERM
        ;;
    esac
fi
cli setup "set tw_storage_path $STORAGE
set tw_backups_folder ${BACKUP%/*}
set tw_backup_name ${BACKUP##*/}
set tw_backup_list $SELECTION
set tw_restore $BACKUP
set tw_enable_adb_backup 0
set tw_encrypt_backup 0
set tw_use_compression ${COMPRESSION:-1}
set tw_skip_digest_generate ${SKIP_DIGEST:-1}
set tw_skip_digest_check ${SKIP_DIGEST_CHECK:-1}
set tw_disable_free_space ${SKIP_SPACE:-0}
set tw_cancel_backup 0" || fail setup_failed
if [ "$MODE" = restore ]; then
    cli restorelist 'set ddc_function readBackup
set ddc_arg ddc_idle' || fail restore_list_failed
fi
save_result RECOVERY_RUNNING
stop_requested && fail user_stopped
STARTED=1
touch "$WORK/engine-running"
# cli() polls the operation's Stop marker even while native nandroid is busy.
if cli run "set tw_restore_selected $SELECTION
set ddc_arg $MODE
set ddc_function nandroid"; then
    DONE=1
else
    # A result file proves the native action returned; a timeout does not.
    [ ! -f "$WORK/bridge-run" ] || DONE=1
    fail native_operation_failed
fi
rm -f "$WORK/engine-running"
stop_requested && fail user_stopped
if [ "$MODE" = backup ]; then
    [ -d "$BACKUP" ] || fail backup_payload_missing
    : > "$WORK/artifacts"
    for file in "$BACKUP/"*; do
        [ -f "$file" ] && [ ! -L "$file" ] || continue
        name=${file##*/}
        case "$name" in ''|*[!a-zA-Z0-9_.-]*) fail unsafe_artifact ;; esac
        printf '%s|%s|\n' "$name" "$(stat -c %s "$file")" >> "$WORK/artifacts" || fail artifact_list_failed
    done
    [ -s "$WORK/artifacts" ] || fail backup_payload_missing
fi
exit 0
