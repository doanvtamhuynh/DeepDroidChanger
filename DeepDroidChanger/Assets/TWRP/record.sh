# The image builder prepends the immutable operation configuration.
# Capture nandroid status BEFORE GUIAction::cmd starts its own operation.
phase=${1:-idle}
if [ "$phase" = restore-complete ]; then
    # ORS executes this continuation only when its preceding restore returned
    # success. It is not the always-zero status returned by GUIAction::twcmd.
    printf '%s' "$OP" > "$WORK/native-restore-complete.tmp" || exit 1
    mv "$WORK/native-restore-complete.tmp" "$WORK/native-restore-complete" || exit 1
    sync
    exit 0
fi
status=${2:-1}
cancel=${3:-0}
case "$phase" in probe|query|partition|setup|restorelist|run) ;; *) exit 0 ;; esac
case "$status" in 0|1) ;; *) status=1 ;; esac
case "$cancel" in 1) status=1 ;; esac
mkdir -p "$WORK" || exit 1
encrypted=${4:-unknown}; decrypted=${5:-unknown}; data_size=${6:-unknown}
present=${7:-unknown}; size=${8:-unknown}; mount_point=${9:-unknown}; fs=${10:-unknown}
printf 'encrypted=%s\ndecrypted=%s\ndataSize=%s\npresent=%s\nsize=%s\nmount=%s\nfs=%s\n' \
    "${encrypted#e}" "${decrypted#d}" "${data_size#b}" "${present#p}" "${size#s}" "${mount_point#m}" "${fs#f}" > "$WORK/values-$phase.tmp" || exit 1
mv "$WORK/values-$phase.tmp" "$WORK/values-$phase" || exit 1
printf '%s|%s|%s\n' "$OP" "$phase" "$status" > "$WORK/bridge-$phase.tmp" || exit 1
mv "$WORK/bridge-$phase.tmp" "$WORK/bridge-$phase" || exit 1
