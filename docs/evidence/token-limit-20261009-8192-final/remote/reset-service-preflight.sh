#!/bin/bash
# Read-only recovery planning. No stop/start/reset/reboot or CUDA work.
set -euo pipefail
start=$SECONDS
deadline=$((start+40))
check() { [[ $SECONDS -lt $deadline ]] || { echo preflight_deadline >&2; exit 124; }; }
check
date -u '+snapshot_utc=%Y-%m-%dT%H:%M:%SZ'
timeout 4s hostname
timeout 4s ps -o pid,ppid,lstart,args -p 3055,3068,2990,2982,2938,2937
for known_pid in 3055 3068; do
 check
 if [[ ! -d /proc/$known_pid ]]; then echo "known_pid=$known_pid absent"; continue; fi
 printf 'known_pid=%s starttick=' "$known_pid"
 timeout 3s awk '{print $22}' "/proc/$known_pid/stat"
 timeout 3s sed -n '1,8p' "/proc/$known_pid/cgroup"
 container_id=$(timeout 3s grep -oE '[0-9a-f]{64}' "/proc/$known_pid/cgroup" | head -n 1 || true)
 if [[ $container_id =~ ^[0-9a-f]{64}$ ]]; then
  timeout 5s docker inspect --format 'container={{.Id}} name={{.Name}} image={{.Config.Image}} state={{.State.Status}} init_pid={{.State.Pid}} restart={{json .HostConfig.RestartPolicy}} gpu={{json .HostConfig.DeviceRequests}} cmd={{json .Config.Cmd}}' "$container_id"
 else
  echo "known_pid=$known_pid container_identity_unresolved"
 fi
done
check
echo 'read_only_no_reset_no_signals_no_gpu_work=true'
echo "elapsed_seconds=$((SECONDS-start))"
