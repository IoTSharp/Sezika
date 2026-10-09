#!/bin/bash
set -euo pipefail
task=/root/sezika-experiments/20261009-repaired-training-8192
begin=$SECONDS
test "$(hostname)" = digisyn-gpu01
test ! -L "$task"
test "$(timeout 2s cat "$task/task-identity")" = sezika-token-limit-20261009-8192
test "$(timeout 2s cat "$task/thread-identity")" = 01a11f25-335a-7571-a735-7171ace8c3b9
for name in upload.tar data bundle capacity regression; do
  ((SECONDS-begin<15)) || exit 124
  test ! -e "$task/cancel.request"
  test ! -e "$task/$name"
done
for name in capacity train validate regress; do
  ((SECONDS-begin<15)) || exit 124
  test ! -e "$task/.artifacts/stage-$name"
done
timeout 3s nvidia-smi -i 0 --query-gpu=uuid,memory.free --format=csv,noheader,nounits
timeout 3s nvidia-smi --query-compute-apps=pid,process_name,used_gpu_memory --format=csv,noheader,nounits
timeout 3s cat "$task/logs/bootstrap-create.tsv"
printf 'new_root_bootstrap_only_no_upload_no_gpu_stage=true\n'
