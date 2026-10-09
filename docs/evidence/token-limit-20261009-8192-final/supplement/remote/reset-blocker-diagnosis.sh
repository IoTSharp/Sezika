#!/bin/bash
set -euo pipefail
task=/root/sezika-experiments/20261009-repaired-training-8192
run=$task/.artifacts/stage-gpu-reset-approved
begin=$SECONDS
test "$(hostname)" = digisyn-gpu01
for file in device-users-before.txt device-users-after-stop.txt device-users-after-stop.stderr reset-result.txt restoration.tsv result.tsv; do
 ((SECONDS-begin<20)) || exit 124
 if [[ -f $run/$file ]]; then printf 'file=%s\n' "$file"; timeout 2s cat "$run/$file"; fi
done
for container in da214fdd10e65e4800fd8ac02b66ec21f19ab56541f4c2e7f18e63a6a80d3011 74301ee0b2cf9e39d0901bbc7b4e4e9f641d0668a31d67ae920c35f9d1ad62d5; do
 ((SECONDS-begin<25)) || exit 124
 timeout 4s docker inspect --format 'container={{.Id}} state={{.State.Status}} init_pid={{.State.Pid}} exit={{.State.ExitCode}}' "$container"
done
read -r -a pids < "$run/device-users-after-stop.txt" || true
((${#pids[@]}<=16)) || exit 65
for known_pid in "${pids[@]}"; do
 ((SECONDS-begin<30)) || exit 124
 [[ $known_pid =~ ^[0-9]+$ ]] || exit 65
 timeout 3s ps -o pid,ppid,lstart,args -p "$known_pid"
 if [[ -f /proc/$known_pid/stat ]]; then timeout 2s cat "/proc/$known_pid/stat"; fi
done
for port in 8001 8003; do
 ((SECONDS-begin<35)) || exit 124
 printf 'health_port=%s ' "$port"
 timeout 5s curl --max-time 3 -s -w ' http_status=%{http_code}\n' "http://127.0.0.1:$port/health" || true
done
echo 'read_only=true no_reset_attempted=true'
