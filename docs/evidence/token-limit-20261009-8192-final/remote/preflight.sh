#!/bin/bash
set -euo pipefail
umask 077
begin=$SECONDS
task=/root/sezika-experiments/20261009-repaired-training-8192
test "$(hostname)" = digisyn-gpu01
timeout 5s nvidia-smi -i 0 --query-gpu=uuid,name,memory.used,memory.free,driver_version --format=csv,noheader,nounits
timeout 5s nvidia-smi --query-compute-apps=pid,process_name,used_gpu_memory --format=csv,noheader,nounits
timeout 3s awk '$1=="MemAvailable:" {print}' /proc/meminfo
timeout 3s df -B1 --output=avail /root/sezika-experiments
timeout 3s ps -eo pid,ppid,lstart,args | timeout 2s awk 'NR==1 || /Sezika.ExperimentTrainer|RegressionCheck|teacher-expansion|repaired-training|real-head-v1/ {print; count++; if(count>48)exit 2}'
test ! -L "$task"
if [[ -e $task ]]; then
  timeout 2s cat "$task/task-identity" "$task/thread-identity"
  for name in train validate regress; do
    ((SECONDS-begin<25)) || exit 124
    run="$task/.artifacts/stage-$name"
    if [[ -f $run/result.tsv ]]; then timeout 2s cat "$run/result.tsv"; fi
  done
else
  printf 'new_task_root_absent=true\n'
fi
test "$(timeout 2s cat /root/sezika-experiments/20261008-real-head-v1/task-identity)" = sezika-real-gpu-head-experiment-20261008-v1
test -d /root/sezika-experiments/20261008-real-head-v1/package
printf 'read_only_preflight_complete=true\n'
