#!/bin/bash
set -euo pipefail
task=/root/sezika-experiments/20261009-repaired-training-8192
begin=$SECONDS
check() { ((SECONDS-begin<45)); }
test "$(hostname)" = digisyn-gpu01
date -u +%FT%TZ
for container in da214fdd10e65e4800fd8ac02b66ec21f19ab56541f4c2e7f18e63a6a80d3011 74301ee0b2cf9e39d0901bbc7b4e4e9f641d0668a31d67ae920c35f9d1ad62d5; do
 check
 timeout 4s docker inspect --format 'container={{.Id}} state={{.State.Status}} init_pid={{.State.Pid}}' "$container"
 timeout 4s docker top "$container" -eo pid,ppid,lstart,args
done
for attempt in {1..4}; do
 check
 printf 'gpu_users_snapshot=%s\n' "$attempt"
 set +e
 users=$(timeout 4s fuser /dev/nvidia0 2>/dev/null)
 fuser_rc=$?
 set -e
 echo "fuser_exit=$fuser_rc users=$users"
 ((fuser_rc==0 || fuser_rc==1)) || exit "$fuser_rc"
 read -r -a pids <<< "$users"
 ((${#pids[@]}<=16)) || exit 65
 for known_pid in "${pids[@]}"; do
  check
  [[ $known_pid =~ ^[0-9]+$ ]] || exit 65
  if [[ -r /proc/$known_pid/stat ]]; then
   timeout 3s ps -o pid,ppid,lstart,args -p "$known_pid" || true
   timeout 2s cat "/proc/$known_pid/stat" || true
   timeout 2s cat "/proc/$known_pid/cgroup" || true
  else echo "pid=$known_pid absent_after_snapshot"; fi
 done
 ((attempt==4)) || sleep 1
done
echo 'read_only=true'
