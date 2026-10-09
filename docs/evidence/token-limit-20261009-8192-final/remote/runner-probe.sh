#!/bin/bash
set -euo pipefail
task=/root/sezika-experiments/20261009-repaired-training-8192
test "$(hostname)" = digisyn-gpu01
test ! -L "$task"
test "$(timeout 2s cat "$task/task-identity")" = sezika-token-limit-20261009-8192
test "$(timeout 2s cat "$task/thread-identity")" = 01a11f25-335a-7571-a735-7171ace8c3b9
test ! -e "$task/cancel.request" && test ! -L "$task/stage.lock"
exec {lockfd}> "$task/stage.lock"
/usr/bin/flock -n "$lockfd" || exit 73
printf '%s  %s\n' 32167e8cf7396ac6d1f8f54a8d664dfbfaaef71fccba871433b0fdf73e24f2f1 "$task/remote/Invoke-BoundedLinux.sh" | timeout 2s sha256sum -c --quiet -
begin=$SECONDS; count=0
for probe in exit7 tiny1; do
  ((count<2 && SECONDS-begin<50)) || exit 124
  test ! -e "$task/cancel.request"
  run="$task/.artifacts/runner-$probe"
  if [[ -e $run ]]; then
    [[ -f $run/result.tsv && ! -L $run/result.tsv ]] && timeout 2s cat "$run/result.tsv"
    printf 'probe_exists_no_repeat %s\n' "$probe"; exit 73
  fi
  mkdir "$run"
  if [[ $probe == exit7 ]]; then args=(/bin/bash -c 'exit 7'); expected=7; expected_status=payload_failed; expected_runner=127
  else args=(/bin/true); expected=0; expected_status=completed; expected_runner=0; fi
  printf 'probe_payload_argv'; printf ' %q' "${args[@]}"; printf '\n'
  set +e
  timeout --signal=TERM --kill-after=3s 20s bash "$task/remote/Invoke-BoundedLinux.sh" "$run" 3 8 0 file none -- "${args[@]}"
  rc=$?
  set -e
  printf 'probe=%s runner_exit=%s expected=%s\n' "$probe" "$rc" "$expected_runner"
  test "$rc" = "$expected_runner"
  receipt="$run/result.tsv"; test -f "$receipt" && test ! -L "$receipt"
  fields_begin=$SECONDS
  for field in "status $expected_status" "payload_exit $expected" 'final_check_complete 1' 'cleanup_incomplete 0' 'verified_live_count 0' 'unknown_count 0' 'unchecked_count 0' 'cleanup_budget_exhausted 0'; do
    ((SECONDS-fields_begin<3)) || exit 124
    read -r key value <<< "$field"
    test "$(timeout 1s awk -F '\t' -v k="$key" '$1==k {print $2}' "$receipt")" = "$value"
  done
  timeout 2s cat "$receipt"
  count=$((count+1))
done
printf 'bounded_runner_both_probes_verified\n'
