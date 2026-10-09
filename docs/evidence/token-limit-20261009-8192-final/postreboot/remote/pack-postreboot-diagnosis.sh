#!/bin/bash
set -euo pipefail
umask 077
task=/root/sezika-experiments/20261009-repaired-training-8192
run=$task/.artifacts/stage-postreboot-diagnosis
test "$(hostname)" = digisyn-gpu01
test "$(timeout 2s cat /proc/sys/kernel/random/boot_id)" = 31951b0f-232d-40c2-aec0-d7e7dd5215a6
test "$(timeout 2s cat "$task/task-identity")" = sezika-token-limit-20261009-8192
test ! -e "$task/cancel.request" && test ! -L "$run"
test -f "$run/result.tsv" && test ! -L "$run/result.tsv"
test "$(timeout 2s awk -F '\t' '$1=="payload_exit" {print $2}' "$run/result.tsv")" = 0
test "$(timeout 2s awk -F '\t' '$1=="final_check_complete" {print $2}' "$run/result.tsv")" = 1
test ! -e "$task/postreboot-diagnosis-evidence.tar"
timeout 8s tar -cf "$task/postreboot-diagnosis-evidence.tar" -C "$task" .artifacts/stage-postreboot-diagnosis
timeout 2s stat -c 'postreboot_tar_bytes=%s' "$task/postreboot-diagnosis-evidence.tar"
timeout 2s sha256sum "$task/postreboot-diagnosis-evidence.tar"
