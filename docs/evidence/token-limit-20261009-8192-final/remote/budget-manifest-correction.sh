#!/bin/bash
set -euo pipefail
umask 077
task=/root/sezika-experiments/20261009-repaired-training-8192
test "$(hostname)" = digisyn-gpu01
test "$(timeout 2s cat "$task/task-identity")" = sezika-token-limit-20261009-8192
test ! -e "$task/.artifacts/stage-boundary-tiny" && test ! -e "$task/logs/budget-manifest-correction.tsv"
printf '%s  %s\n' a72500fb383ea876ca827650d0bd1a2a293e8730fa2648997e3d051d5336ebf4 "$task/logs/phase-common-before-budget-record.sh" 86a13954fa2834820e8c5df337d2a7ccaed9a6b44a4e324fb52d5032c067665a "$task/remote/phase-common.sh" | timeout 2s sha256sum -c --quiet -
cp -- "$task/remote/files.sha256" "$task/logs/remote-files-before-crlf-correction.sha256"
sed -i 's/^a72500fb383ea876ca827650d0bd1a2a293e8730fa2648997e3d051d5336ebf4  phase-common.sh/86a13954fa2834820e8c5df337d2a7ccaed9a6b44a4e324fb52d5032c067665a  phase-common.sh/' "$task/remote/files.sha256"
(cd "$task/remote" && timeout 5s sha256sum -c --quiet files.sha256)
printf 'old_sha256\ta72500fb383ea876ca827650d0bd1a2a293e8730fa2648997e3d051d5336ebf4\nnew_sha256\t86a13954fa2834820e8c5df337d2a7ccaed9a6b44a4e324fb52d5032c067665a\nreason\tCRLF inventory sed anchor correction; original failure retained; real8192 budget6GiB\n' > "$task/logs/budget-manifest-correction.tsv"
printf 'budget_manifest_correction_verified_no_gpu_started=true\n'
