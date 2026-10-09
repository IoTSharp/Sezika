#!/bin/bash
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh" "${1:?}"
phase=${2:?}; [[ $phase == contract || $phase == tiny || $phase == train || $phase == validate || $phase == regress ]] || exit 64
[[ -f $task/logs/install-complete.tsv ]] || exit 65
exec {lockfd}> "$task/stage.lock"; flock -n "$lockfd" || exit 73
run="$task/.artifacts/stage-$phase"
if [[ -e $run ]]; then
  verify_receipt "$run/result.tsv" || { [[ ! -f $run/result.tsv ]] || cat "$run/result.tsv"; printf 'stage_exists_failed_or_incomplete_no_repeat=%s\n' "$phase" >&2; exit 73; }
  printf 'stage_completed_skip=%s\n' "$phase"; exit 0
fi
case "$phase" in
  tiny) verify_receipt "$task/.artifacts/stage-contract/result.tsv";;
  train) verify_receipt "$task/.artifacts/stage-tiny/result.tsv";;
  validate) verify_receipt "$task/.artifacts/stage-train/result.tsv";;
  regress) verify_receipt "$task/.artifacts/stage-validate/result.tsv";;
esac
(cd "$task" && timeout 60s sha256sum -c --quiet files.sha256)
guard_seconds=1650
mkdir "$run"
record_shell "$run/stage-shell.tsv"
if [[ $phase != contract ]]; then resources "$run/resources-before.tsv"; fi
manifest=e8e74ce0bbf1a72da5fcdf4dcad9d302513dc5dc42cb548bfd21d2569621aeb6
executable="$task/bundle/Sezika.ExperimentTrainer"
case "$phase" in
  contract) args=(check-expanded-input --data "$task/data" --manifest-sha256 "$manifest" --fixture-only false --max-checks 1); seconds=65;;
  tiny) args=(check-gpu --device 0 --max-checks 1 --timeout-seconds 60); seconds=75;;
  train)
    [[ ! -e $task/output/run-24576 ]] || exit 73
    args=(train-expanded --data "$task/data" --manifest-sha256 "$manifest" --package "$task/package" --output "$task/output/run-24576" --device 0 --steps 24576 --seed 17 --learning-rate 0.001 --max-tokens 8192 --training-seconds 300 --timeout-seconds 1500); seconds=1520;;
  validate)
    [[ ! -e $task/output/expanded-validation.json ]] || exit 73
    report=$(timeout 3s sha256sum "$task/output/run-24576/expanded-training-report.json"); report=${report%% *}
    args=(validate-expanded --data "$task/data" --manifest-sha256 "$manifest" --package "$task/package" --output "$task/output/run-24576" --report-sha256 "$report" --validation-output "$task/output/expanded-validation.json" --device 0 --timeout-seconds 1500); seconds=1520;;
  regress)
    [[ ! -e $task/output/original32-regression.json ]] || exit 73
    report=$(timeout 3s sha256sum "$task/output/run-24576/expanded-training-report.json"); report=${report%% *}
    executable="$task/regression/RegressionCheck"
    args=(regress --records "$task/regression_input/experiment-records.jsonl" --audit "$task/regression_input/audit-report.json" --training-report "$task/output/run-24576/expanded-training-report.json" --report-sha256 "$report" --head "$task/output/run-24576/expanded-head.asset" --package "$task/package" --output "$task/output/original32-regression.json" --device 0 --timeout-seconds 300); seconds=315;;
esac
export DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1
export TMPDIR="$run/tmp"; mkdir "$TMPDIR"
printf 'payload_argv'; printf ' %q' "$executable" "${args[@]}"; printf '\n'
printf 'cancel_file=%s/cancel.request\n' "$run"
set +e
timeout --signal=TERM --kill-after=5s "$((seconds+20))s" bash "$task/remote/Invoke-BoundedLinux.sh" "$run" "$seconds" 8 0 file none -- "$executable" "${args[@]}"
rc=$?
set -e
printf '%s\n' "$rc" > "$run/outer-exit.txt"
[[ ! -f $run/result.tsv ]] || timeout 2s cat "$run/result.tsv"
timeout 2s tail -n 16 "$run/payload.stdout.log" "$run/payload.stderr.log" || true
((rc==0)) || exit "$rc"
verify_receipt "$run/result.tsv"
if [[ $phase != contract ]]; then resources "$run/resources-after.tsv"; fi
printf 'stage_completed=%s\n' "$phase"
