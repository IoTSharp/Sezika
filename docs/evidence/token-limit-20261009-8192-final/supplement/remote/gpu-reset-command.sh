#!/bin/bash
# The approved, exact UUID NVIDIA reset command is attempted at most once here.
set -euo pipefail
task=/root/sezika-experiments/20261009-repaired-training-8192
test "$(hostname)" = digisyn-gpu01
test "$(timeout 2s cat "$task/task-identity")" = sezika-token-limit-20261009-8192
test ! -e "$task/cancel.request" && test ! -L "$task/stage.lock"
exec {lockfd}> "$task/stage.lock"
flock -n "$lockfd" || exit 73
run=$task/.artifacts/stage-gpu-reset-command
test ! -e "$run" && test ! -L "$run"
mkdir "$run"
cat > "$run/payload.sh" <<'PAYLOAD'
#!/bin/bash
set -euo pipefail
task=/root/sezika-experiments/20261009-repaired-training-8192
run=$task/.artifacts/stage-gpu-reset-command
gpu=GPU-e786e313-bc92-a63d-a833-f4e48cbafb66
date -u +%FT%TZ | tee "$run/attempt-utc.txt"
for file in /proc/driver/nvidia/gpus/0000:65:00.0/information /sys/bus/pci/devices/0000:65:00.0/vendor /sys/bus/pci/devices/0000:65:00.0/device; do
 if [[ -f $file ]]; then printf 'source=%s\n' "$file"; timeout 2s cat "$file"; fi
done
set +e
timeout 3s fuser /dev/nvidia0 > "$run/device-users.txt" 2> "$run/device-users.stderr"
users_rc=$?
set -e
((users_rc==1)) || { echo "device_users_present_or_unknown=$users_rc"; exit 73; }
test ! -e "$task/cancel.request" && test ! -e "$run/cancel.request"
printf 'authorized_reset_uuid=%s\n' "$gpu"
set +e
timeout --signal=TERM --kill-after=2s 60s nvidia-smi --gpu-reset -i "$gpu" > "$run/reset.stdout.log" 2> "$run/reset.stderr.log"
reset_rc=$?
set -e
printf 'reset_exit=%s\n' "$reset_rc" | tee "$run/reset-result.txt"
cat "$run/reset.stdout.log" "$run/reset.stderr.log"
exit "$reset_rc"
PAYLOAD
timeout 2s bash -n "$run/payload.sh"
printf '%s  %s\n' 32167e8cf7396ac6d1f8f54a8d664dfbfaaef71fccba871433b0fdf73e24f2f1 "$task/remote/Invoke-BoundedLinux.sh" | timeout 2s sha256sum -c --quiet -
set +e
timeout --signal=TERM --kill-after=3s 87s bash "$task/remote/Invoke-BoundedLinux.sh" "$run" 75 8 0 file none -- /bin/bash "$run/payload.sh"
rc=$?
set -e
printf 'linux_reset_command_runner_exit=%s\n' "$rc" | tee "$run/outer-exit.txt"
timeout 2s cat "$run/result.tsv"
timeout 2s cat "$run/payload.stdout.log" "$run/payload.stderr.log"
exit "$rc"
