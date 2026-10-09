#!/bin/bash
# Explicit user approval: 可以重置gpu. Only two previously identified containers.
set -euo pipefail
task=/root/sezika-experiments/20261009-repaired-training-8192
run=$task/.artifacts/stage-gpu-reset-approved
gpu=GPU-e786e313-bc92-a63d-a833-f4e48cbafb66
containers=(da214fdd10e65e4800fd8ac02b66ec21f19ab56541f4c2e7f18e63a6a80d3011 74301ee0b2cf9e39d0901bbc7b4e4e9f641d0668a31d67ae920c35f9d1ad62d5)
started=$SECONDS
stop_attempted=0
restore_done=0
check() { ((SECONDS-started<290)) && test ! -e "$task/cancel.request" && test ! -e "$run/cancel.request"; }
restore() {
 local restore_started=$SECONDS rc
 if ((stop_attempted==0 || restore_done==1)); then return 0; fi
 for container in "${containers[@]}"; do
  ((SECONDS-restore_started<35)) || return 124
  set +e
  timeout --signal=TERM --kill-after=2s 12s docker start "$container" >> "$run/restoration.tsv" 2>&1
  rc=$?
  set -e
  printf 'restore_container=%s exit=%s\n' "$container" "$rc" | tee -a "$run/restoration.tsv"
  ((rc==0)) || return "$rc"
 done
 restore_done=1
}
finish() {
 local original=$? restore_rc=0
 trap - EXIT TERM INT
 restore || restore_rc=$?
 printf 'payload_original_exit=%s restore_exit=%s restore_done=%s\n' "$original" "$restore_rc" "$restore_done" | tee -a "$run/restoration.tsv"
 if ((original==0 && restore_rc!=0)); then exit "$restore_rc"; fi
 exit "$original"
}
trap finish EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
check
test "$(hostname)" = digisyn-gpu01
timeout 5s nvidia-smi -L | tee "$run/gpu-identity-before.txt"
test "$(timeout 5s nvidia-smi -L | sed -n '1p')" = 'GPU 0: NVIDIA GeForce RTX 4090 (UUID: GPU-e786e313-bc92-a63d-a833-f4e48cbafb66)'
timeout 5s nvidia-smi --help > "$run/nvidia-smi-help.txt"
grep -q -- '--gpu-reset' "$run/nvidia-smi-help.txt"
for index in 0 1; do
 check
 container=${containers[$index]}
 expected_init=2990; expected_pid=3055; expected_tick=1542; expected_parent=2990; expected_shim=2938
 if ((index==1)); then expected_init=2982; expected_pid=3068; expected_tick=1586; expected_parent=2982; expected_shim=2937; fi
 test "$(timeout 4s docker inspect --format '{{.Id}} {{.State.Status}} {{.State.Pid}}' "$container")" = "$container running $expected_init"
 raw=$(cat "/proc/$expected_pid/stat"); tail=${raw##*) }; read -r -a fields <<< "$tail"
 test "${fields[19]}" = "$expected_tick" && test "${fields[1]}" = "$expected_parent"
 raw=$(cat "/proc/$expected_init/stat"); tail=${raw##*) }; read -r -a fields <<< "$tail"; test "${fields[1]}" = "$expected_shim"
 actual_argv=$(timeout 3s sha256sum "/proc/$expected_pid/cmdline"); actual_argv=${actual_argv%% *}
 if ((index==0)); then
  expected_argv=$(printf '%s\0' /app/llama-server --model /models/Hy-MT2-30B-A3B-Q8_0.gguf --alias hy-mt2-30b-a3b-q8-0 --host 127.0.0.1 --port 8003 --ctx-size 4096 --n-predict 4096 --n-gpu-layers all --parallel 1 --batch-size 512 --ubatch-size 256 --flash-attn on --jinja --temperature 0.7 --top-k -1 --top-p 1.0 --repeat-penalty 1.0 | sha256sum)
 else
  expected_argv=$(printf '%s\0' /usr/bin/python /usr/local/bin/uvicorn app:app --host 127.0.0.1 --port 8001 --workers 1 --no-access-log | sha256sum)
 fi
 test "$actual_argv" = "${expected_argv%% *}"
 timeout 3s ps -o pid,ppid,lstart,args -p "$expected_pid,$expected_init,$expected_shim" | tee -a "$run/services-before.txt"
 timeout 4s docker inspect --format 'container={{.Id}} name={{.Name}} image={{.Config.Image}} state={{.State.Status}} init_pid={{.State.Pid}} restart={{json .HostConfig.RestartPolicy}} cmd={{json .Config.Cmd}}' "$container" | tee -a "$run/services-before.txt"
done
check
set +e
timeout 4s fuser /dev/nvidia0 > "$run/device-users-before.txt" 2> "$run/device-users-before.stderr"
fuser_rc=$?
set -e
((fuser_rc==0 || fuser_rc==1)) || exit "$fuser_rc"
read -r -a users < "$run/device-users-before.txt" || true
((${#users[@]}<=32)) || exit 65
for device_pid in "${users[@]}"; do
 check
 [[ $device_pid == 3055 || $device_pid == 3068 ]] || { echo "unapproved_gpu_client=$device_pid" >&2; exit 73; }
done
for container in "${containers[@]}"; do
 check
 stop_attempted=1
 printf 'stop_container=%s utc=%s\n' "$container" "$(date -u +%FT%TZ)"
 timeout --signal=TERM --kill-after=2s 30s docker stop --time 15 "$container"
done
check
set +e
timeout 4s fuser /dev/nvidia0 > "$run/device-users-after-stop.txt" 2> "$run/device-users-after-stop.stderr"
fuser_rc=$?
set -e
((fuser_rc==1)) || { echo "device_still_used_or_unreadable=$fuser_rc" >&2; exit 73; }
printf 'reset_gpu=%s utc=%s\n' "$gpu" "$(date -u +%FT%TZ)"
set +e
timeout --signal=TERM --kill-after=2s 60s nvidia-smi --gpu-reset -i "$gpu" > "$run/reset.stdout.log" 2> "$run/reset.stderr.log"
reset_rc=$?
set -e
printf 'reset_exit=%s\n' "$reset_rc" | tee "$run/reset-result.txt"
cat "$run/reset.stdout.log" "$run/reset.stderr.log"
((reset_rc==0)) || exit "$reset_rc"
check
timeout 5s nvidia-smi -i "$gpu" --query-gpu=uuid,memory.free --format=csv,noheader,nounits | tee "$run/gpu-after-reset.csv"
restore
health_started=$SECONDS
for attempt in {1..16}; do
 check
 ((SECONDS-health_started<100)) || exit 124
 translation_code=$(timeout 5s curl --max-time 3 -s -o "$run/translation-health.json" -w '%{http_code}' http://127.0.0.1:8003/health || true)
 asr_code=$(timeout 5s curl --max-time 3 -s -o "$run/asr-health.json" -w '%{http_code}' http://127.0.0.1:8001/health || true)
 printf 'health_attempt=%s translation=%s asr=%s\n' "$attempt" "$translation_code" "$asr_code" | tee -a "$run/health.tsv"
 if [[ $translation_code == 200 && $asr_code == 200 ]]; then break; fi
 ((attempt<16)) || exit 69
 sleep 3
done
check
for container in "${containers[@]}"; do
 timeout 4s docker inspect --format 'container={{.Id}} state={{.State.Status}} init_pid={{.State.Pid}}' "$container" | tee -a "$run/services-after.txt"
done
timeout 5s nvidia-smi -i "$gpu" --query-gpu=uuid,memory.free --format=csv,noheader,nounits | tee "$run/gpu-after-restore.csv"
timeout 5s nvidia-smi --query-compute-apps=pid,process_name,used_gpu_memory --format=csv,noheader,nounits | tee "$run/gpu-processes-after.csv"
echo "gpu_reset_and_original_services_restored=true elapsed_seconds=$((SECONDS-started))"
