#!/bin/bash
set -u
task=/root/sezika-experiments/20261009-repaired-training-8192
test "$(hostname)" = digisyn-gpu01 || exit 65
test "$(timeout 2s cat "$task/task-identity")" = sezika-token-limit-20261009-8192 || exit 65
begin=$SECONDS
timeout 7s nvidia-smi -i 0 --query-gpu=uuid,pci.bus_id,memory.used,memory.free --format=csv,noheader,nounits; printf 'gpu_query_exit=%s\n' "$?"
timeout 4s nvidia-smi --query-compute-apps=pid,process_name,used_gpu_memory --format=csv,noheader,nounits; printf 'gpu_apps_exit=%s\n' "$?"
timeout 3s ps -p 3055,3068,2376890 -o pid=,ppid=,lstart=,args=
timeout 4s journalctl -k --since '2026-10-09 10:05:00 UTC' --until '2026-10-09 10:12:00 UTC' --no-pager -n 120 | timeout 2s awk '/NVRM|Xid|GPU|nvidia/ {print; n++; if(n>=40)exit}'
timeout 2s cat "$task/.artifacts/stage-validate/result.tsv"
timeout 2s tail -n 25 "$task/.artifacts/stage-validate/payload.stderr.log"
if [[ -f $task/output/expanded-validation.json ]]; then timeout 2s sha256sum "$task/output/expanded-validation.json"; else printf 'validation_output_absent=true\n'; fi
if [[ -d $task/.artifacts/stage-regress ]]; then printf 'regression_stage_present=true\n'; else printf 'regression_stage_absent=true\n'; fi
printf 'diagnosis_no_signals_or_gpu_work elapsed_seconds=%s\n' "$((SECONDS-begin))"
