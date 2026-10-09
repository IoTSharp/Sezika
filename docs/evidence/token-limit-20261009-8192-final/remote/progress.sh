#!/bin/bash
set -euo pipefail
task=/root/sezika-experiments/20261009-repaired-training-8192
test "$(hostname)" = digisyn-gpu01
test "$(timeout 2s cat "$task/task-identity")" = sezika-token-limit-20261009-8192
begin=$SECONDS; count=0
for phase in boundary-tiny boundary-tiny2 boundary-full boundary-real publication capacity capacity2 train validate regress; do
  ((count<10 && SECONDS-begin<20)) || exit 124
  count=$((count+1)); run="$task/.artifacts/stage-$phase"
  [[ -d $run && ! -L $run ]] || continue
  printf 'stage=%s\n' "$phase"
  if [[ -f $run/result.tsv ]]; then timeout 2s awk -F '\t' '$1=="status" || $1=="payload_exit" || $1=="verified_live_count" || $1=="unknown_count" {print}' "$run/result.tsv"; fi
  if [[ -f $run/payload.stdout.log ]]; then timeout 2s tail -n 4 "$run/payload.stdout.log"; fi
  if [[ -f $run/payload.stderr.log ]]; then timeout 2s tail -n 4 "$run/payload.stderr.log"; fi
done
timeout 3s nvidia-smi -i 0 --query-gpu=uuid,memory.free --format=csv,noheader,nounits
