#!/bin/bash
# Recovery within explicit GPU-reset approval: reload existing driver, no installation.
set -euo pipefail
task=/root/sezika-experiments/20261009-repaired-training-8192
test "$(hostname)" = digisyn-gpu01
test "$(timeout 2s cat "$task/task-identity")" = sezika-token-limit-20261009-8192
test ! -e "$task/cancel.request" && test ! -L "$task/stage.lock"
exec {lockfd}> "$task/stage.lock"
flock -n "$lockfd" || exit 73
run=$task/.artifacts/stage-gpu-driver-reload
test ! -e "$run" && test ! -L "$run"
mkdir "$run"
cat > "$run/payload.sh" <<'PAYLOAD'
#!/bin/bash
set -euo pipefail
task=/root/sezika-experiments/20261009-repaired-training-8192
run=$task/.artifacts/stage-gpu-driver-reload
gpu=GPU-e786e313-bc92-a63d-a833-f4e48cbafb66
containers=(da214fdd10e65e4800fd8ac02b66ec21f19ab56541f4c2e7f18e63a6a80d3011 74301ee0b2cf9e39d0901bbc7b4e4e9f641d0668a31d67ae920c35f9d1ad62d5)
start=$SECONDS; stop_attempted=0; reload_attempted=0; driver_restored=0; services_restored=0
check() { ((SECONDS-start<330)) && test ! -e "$task/cancel.request" && test ! -e "$run/cancel.request"; }
restore_driver() {
 local restore_start=$SECONDS
 if ((reload_attempted==0 || driver_restored==1)); then return 0; fi
 for module in nvidia nvidia_modeset nvidia_drm nvidia_uvm; do
  ((SECONDS-restore_start<65)) || return 124
  timeout --signal=TERM --kill-after=2s 15s /usr/sbin/modprobe "$module" >> "$run/driver-restore.log" 2>&1 || return $?
 done
 driver_restored=1
}
restore_services() {
 local restore_start=$SECONDS
 if ((stop_attempted==0 || services_restored==1)); then return 0; fi
 for container in "${containers[@]}"; do
  ((SECONDS-restore_start<30)) || return 124
  timeout --signal=TERM --kill-after=2s 12s docker start "$container" >> "$run/service-restore.log" 2>&1 || return $?
 done
 services_restored=1
}
finish() {
 local original=$? driver_rc=0 services_rc=0
 trap - EXIT TERM INT
 restore_driver || driver_rc=$?
 restore_services || services_rc=$?
 printf 'original_exit=%s driver_restore_exit=%s services_restore_exit=%s driver_restored=%s services_restored=%s\n' "$original" "$driver_rc" "$services_rc" "$driver_restored" "$services_restored" | tee "$run/restoration-result.txt"
 if ((original==0 && driver_rc!=0)); then exit "$driver_rc"; fi
 if ((original==0 && services_rc!=0)); then exit "$services_rc"; fi
 exit "$original"
}
trap finish EXIT
trap 'exit 143' TERM
trap 'exit 130' INT
date -u +%FT%TZ | tee "$run/started-utc.txt"
check
test "$(timeout 3s modinfo -F version nvidia)" = 580.173.02
test "$(timeout 3s modinfo -F filename nvidia)" = /lib/modules/6.8.0-136-generic/updates/dkms/nvidia.ko.zst
timeout 3s sha256sum /lib/modules/6.8.0-136-generic/updates/dkms/nvidia.ko.zst > "$run/driver-file.sha256"
timeout 3s cat /proc/driver/nvidia/version /proc/driver/nvidia/params > "$run/driver-config-before.txt"
test "$(timeout 3s find /proc/driver/nvidia/gpus -mindepth 1 -maxdepth 1 -type d)" = /proc/driver/nvidia/gpus/0000:65:00.0
grep -q "$gpu" /proc/driver/nvidia/gpus/0000:65:00.0/information
set +e
timeout 3s systemctl is-active display-manager > "$run/display-manager.txt"
display_rc=$?
set -e
((display_rc==3 || display_rc==4)) || exit 73
for container in "${containers[@]}"; do
 check
 state=$(timeout 4s docker inspect --format '{{.Id}} {{.State.Status}}' "$container")
 test "$state" = "$container running"
 timeout 4s docker top "$container" -eo pid,ppid,lstart,args >> "$run/services-before.txt"
 timeout 4s docker inspect --format 'container={{.Id}} name={{.Name}} image={{.Config.Image}} init_pid={{.State.Pid}} cmd={{json .Config.Cmd}}' "$container" >> "$run/services-before.txt"
done
for container in "${containers[@]}"; do
 check
 stop_attempted=1
 timeout --signal=TERM --kill-after=2s 30s docker stop --timeout 15 "$container" | tee -a "$run/service-stop.log"
done
release_start=$SECONDS
for release_check in {1..12}; do
 check
 ((SECONDS-release_start<35)) || exit 124
 set +e
 timeout 2s fuser /dev/nvidia0 /dev/nvidiactl /dev/nvidia-uvm > "$run/device-users-$release_check.txt" 2> "$run/device-users-$release_check.stderr"
 users_rc=$?
 set -e
 echo "device_release_check=$release_check exit=$users_rc"
 if ((users_rc==1)); then break; fi
 ((users_rc==0 && release_check<12)) || exit 73
 sleep 1
done
check
reload_attempted=1
set +e
timeout --signal=TERM --kill-after=2s 30s /usr/sbin/modprobe -r nvidia_uvm nvidia_drm nvidia_modeset nvidia > "$run/unload.stdout.log" 2> "$run/unload.stderr.log"
unload_rc=$?
set -e
printf 'unload_exit=%s\n' "$unload_rc" | tee "$run/unload-result.txt"
cat "$run/unload.stdout.log" "$run/unload.stderr.log"
((unload_rc==0)) || exit "$unload_rc"
restore_driver
check
timeout 3s sha256sum -c "$run/driver-file.sha256"
timeout 3s cat /proc/driver/nvidia/version /proc/driver/nvidia/params > "$run/driver-config-after.txt"
timeout 3s cmp "$run/driver-config-before.txt" "$run/driver-config-after.txt"
set +e
timeout 8s nvidia-smi -i "$gpu" --query-gpu=uuid,memory.free --format=csv,noheader,nounits > "$run/gpu-after-reload.csv" 2> "$run/gpu-after-reload.stderr"
gpu_rc=$?
set -e
printf 'gpu_after_reload_exit=%s\n' "$gpu_rc" | tee "$run/gpu-result.txt"
cat "$run/gpu-after-reload.csv" "$run/gpu-after-reload.stderr"
restore_services
((gpu_rc==0)) || exit "$gpu_rc"
health_start=$SECONDS
for attempt in {1..16}; do
 check
 ((SECONDS-health_start<100)) || exit 124
 translation=$(timeout 5s curl --max-time 3 -s -o "$run/translation-health.json" -w '%{http_code}' http://127.0.0.1:8003/health || true)
 asr=$(timeout 5s curl --max-time 3 -s -o "$run/asr-health.json" -w '%{http_code}' http://127.0.0.1:8001/health || true)
 printf 'health_attempt=%s translation=%s asr=%s\n' "$attempt" "$translation" "$asr" | tee -a "$run/health.tsv"
 if [[ $translation == 200 && $asr == 200 ]]; then break; fi
 ((attempt<16)) || exit 69
 sleep 3
done
timeout 5s nvidia-smi -i "$gpu" --query-gpu=uuid,memory.free --format=csv,noheader,nounits | tee "$run/gpu-after-restoration.csv"
echo "same_driver_reloaded_and_gpu_services_healthy=true elapsed_seconds=$((SECONDS-start))"
PAYLOAD
timeout 2s bash -n "$run/payload.sh"
printf '%s  %s\n' 32167e8cf7396ac6d1f8f54a8d664dfbfaaef71fccba871433b0fdf73e24f2f1 "$task/remote/Invoke-BoundedLinux.sh" | timeout 2s sha256sum -c --quiet -
set +e
timeout --signal=TERM --kill-after=3s 402s bash "$task/remote/Invoke-BoundedLinux.sh" "$run" 390 8 0 file none -- /bin/bash "$run/payload.sh"
rc=$?
set -e
printf 'driver_recovery_runner_exit=%s\n' "$rc" | tee "$run/outer-exit.txt"
timeout 2s cat "$run/result.tsv"
timeout 2s tail -n 25 "$run/payload.stdout.log" "$run/payload.stderr.log"
exit "$rc"
