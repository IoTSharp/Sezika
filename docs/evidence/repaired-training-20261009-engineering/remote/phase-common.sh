#!/bin/bash
# Sourced only by the three fixed stage entrypoints. C# enforces owned GPU<=3GiB and RSS<=4GiB.
set -euo pipefail
umask 077
task=/root/sezika-experiments/20261009-repaired-training-v3
old=/root/sezika-experiments/20261008-real-head-v1
[[ ${phase-} == capacity || ${phase-} == train || ${phase-} == validate || ${phase-} == regress ]]
stage_begin=$SECONDS
test "$(hostname)" = digisyn-gpu01
test ! -L "$task" && test ! -L "$old"
test "$(timeout 2s cat "$task/task-identity")" = sezika-repaired-training-20261009
test "$(timeout 2s cat "$task/thread-identity")" = 01a11f25-335a-7571-a735-7171ace8c3b9
test ! -e "$task/cancel.request" && test ! -L "$task/stage.lock"
exec {lockfd}> "$task/stage.lock"
/usr/bin/flock -n "$lockfd" || { printf 'gpu_scheduler_busy\n' >&2; exit 73; }
verify_result() {
  local receipt=$1 check_begin=$SECONDS field key value
  test -f "$receipt" && test ! -L "$receipt"
  for field in 'status completed' 'payload_exit 0' 'final_check_complete 1' 'cleanup_incomplete 0' 'verified_live_count 0' 'unknown_count 0' 'unchecked_count 0' 'cleanup_budget_exhausted 0'; do
    ((SECONDS-check_begin<3)) || return 124
    test ! -e "$task/cancel.request"
    read -r key value <<< "$field"
    test "$(timeout 1s awk -F '\t' -v k="$key" '$1==k {print $2}' "$receipt")" = "$value"
  done
}
verify_result "$task/.artifacts/runner-tiny1/result.tsv"
if [[ $phase != capacity ]]; then verify_result "$task/.artifacts/stage-capacity/result.tsv"; fi
if [[ $phase == validate ]]; then verify_result "$task/.artifacts/stage-train/result.tsv"; fi
if [[ $phase == regress ]]; then verify_result "$task/.artifacts/stage-validate/result.tsv"; fi
run="$task/.artifacts/stage-$phase"
if [[ -e $run || -L $run ]]; then
  [[ -f $run/result.tsv && ! -L $run/result.tsv ]] && timeout 2s cat "$run/result.tsv"
  printf 'stage_exists_no_repeat %s\n' "$phase"; exit 73
fi
mkdir "$run"
stat_identity() { local raw tail; IFS= read -r raw < "/proc/$1/stat" || return 1; tail=${raw##*) }; read -r -a identity_fields <<< "$tail"; ((${#identity_fields[@]}>=20)); }
stat_identity $$
self_tick=${identity_fields[19]}; self_ppid=${identity_fields[1]}; self_pgid=${identity_fields[2]}; self_sid=${identity_fields[3]}
self_argv=''; chain=''; identity_begin=$SECONDS
exec {argvfd}< "/proc/$$/cmdline"
for ((i=0; i<256 && SECONDS-identity_begin<1; i++)); do
  test ! -e "$task/cancel.request"
  arg=''; IFS= read -r -d '' arg <&$argvfd || break
  printf -v arg '%q' "$arg"; self_argv+="$arg "
  ((${#self_argv}<262144)) || exit 65
done
exec {argvfd}<&-
((i<256 && SECONDS-identity_begin<1)) || exit 124
parent=$self_ppid
for ((depth=0; depth<32 && SECONDS-identity_begin<2; depth++)); do
  test ! -e "$task/cancel.request"
  [[ $parent =~ ^[0-9]+$ && $parent -gt 0 ]] || break
  stat_identity "$parent" || exit 65
  chain+="$parent:${identity_fields[19]}:${identity_fields[1]}:${identity_fields[2]}:${identity_fields[3]};"
  next=${identity_fields[1]}; [[ $next != "$parent" ]] || exit 65; parent=$next
done
((depth<32 && SECONDS-identity_begin<2)) || exit 124
printf 'pid\tstart_tick\tstart_utc_seconds_precision\tobserved_utc\tppid\tpgid\tsid\targv_shell_escaped\tparent_chain\n%s\t%s\tsee_start_tick\t%(%FT%TZ)T\t%s\t%s\t%s\t%s\t%s\n' "$$" "$self_tick" -1 "$self_ppid" "$self_pgid" "$self_sid" "$self_argv" "$chain" > "$run/stage-shell.tsv"
printf '%s  %s\n' 32167e8cf7396ac6d1f8f54a8d664dfbfaaef71fccba871433b0fdf73e24f2f1 "$task/remote/Invoke-BoundedLinux.sh" | timeout 2s sha256sum -c --quiet -
for directory in remote bundle capacity data data/repair-evidence regression regression_input; do
  ((SECONDS-stage_begin<30)) || exit 124
  test ! -e "$task/cancel.request"
  (cd "$task/$directory" && timeout 3s sha256sum -c --quiet files.sha256)
done
test "$(timeout 2s cat "$old/task-identity")" = sezika-real-gpu-head-experiment-20261008-v1
test -d "$old/package" && test ! -L "$old/package"
gpu=$(timeout --signal=TERM --kill-after=1s 5s nvidia-smi -i 0 --query-gpu=uuid,memory.free --format=csv,noheader,nounits)
IFS=',' read -r uuid free <<< "$gpu"; free=${free//[[:space:]]/}
test "$uuid" = GPU-e786e313-bc92-a63d-a833-f4e48cbafb66
[[ $free =~ ^[0-9]+$ ]] && ((free>=5120))
available=$(timeout 2s awk '$1=="MemAvailable:" {print $2}' /proc/meminfo)
disk=$(timeout 2s df -B1 --output=avail "$task" | tail -n 1); disk=${disk//[[:space:]]/}
[[ $available =~ ^[0-9]+$ && $disk =~ ^[0-9]+$ ]] && ((available>=4194304 && disk>=2147483648))
printf 'gpu_uuid\t%s\nfree_mib\t%s\nhost_available_kib\t%s\ndisk_available_bytes\t%s\nowned_gpu_limit_bytes\t3221225472\npayload_rss_limit_bytes\t4294967296\n' "$uuid" "$free" "$available" "$disk" > "$run/resources-before.tsv"
timeout 3s nvidia-smi --query-compute-apps=pid,process_name,used_gpu_memory --format=csv,noheader,nounits > "$run/gpu-processes-before.csv"
manifest_line=$(timeout 2s sha256sum "$task/data/experiment-manifest.json"); manifest=${manifest_line%% *}
[[ $manifest =~ ^[0-9a-f]{64}$ ]]
case "$phase" in
capacity)
  test ! -e "$task/output/capacity"; mkdir "$task/output/capacity"
  executable="$task/capacity/CapacityCheck"; args=(gpu "$task/output/capacity"); runner_seconds=77; outer_seconds=89
  ;;
train)
  test ! -e "$task/output/run-24576" && test ! -L "$task/output/run-24576"
  executable="$task/bundle/Sezika.ExperimentTrainer"
  args=(train-expanded --data "$task/data" --manifest-sha256 "$manifest" --package "$old/package" --output "$task/output/run-24576" --device 0 --steps 24576 --seed 17 --learning-rate 0.001 --max-tokens 1024 --training-seconds 300 --timeout-seconds 1500)
  runner_seconds=1520; outer_seconds=1532
  ;;
validate)
  test ! -e "$task/output/expanded-validation.json" && test ! -L "$task/output/expanded-validation.json"
  report_line=$(timeout 2s sha256sum "$task/output/run-24576/expanded-training-report.json"); report=${report_line%% *}; [[ $report =~ ^[0-9a-f]{64}$ ]]
  executable="$task/bundle/Sezika.ExperimentTrainer"
  args=(validate-expanded --data "$task/data" --manifest-sha256 "$manifest" --package "$old/package" --output "$task/output/run-24576" --report-sha256 "$report" --validation-output "$task/output/expanded-validation.json" --device 0 --timeout-seconds 1500)
  printf 'loaded_report_sha256\t%s\n' "$report" > "$run/loaded-report.tsv"
  runner_seconds=1520; outer_seconds=1532
  ;;
regress)
  test ! -e "$task/output/original32-regression.json"
  report_line=$(timeout 2s sha256sum "$task/output/run-24576/expanded-training-report.json"); report=${report_line%% *}; [[ $report =~ ^[0-9a-f]{64}$ ]]
  executable="$task/regression/RegressionCheck"
  args=(regress --records "$task/regression_input/experiment-records.jsonl" --audit "$task/regression_input/audit-report.json" --training-report "$task/output/run-24576/expanded-training-report.json" --report-sha256 "$report" --head "$task/output/run-24576/expanded-head.asset" --package "$old/package" --output "$task/output/original32-regression.json" --device 0 --timeout-seconds 300)
  runner_seconds=315; outer_seconds=327
  ;;
esac
((SECONDS-stage_begin<25)) || exit 124
test ! -e "$task/cancel.request"
printf 'active_stage_cancel_file=%s/cancel.request\n' "$run"
printf 'payload_argv'; printf ' %q' "$executable" "${args[@]}"; printf '\n'
set +e
timeout --signal=TERM --kill-after=3s "${outer_seconds}s" bash "$task/remote/Invoke-BoundedLinux.sh" "$run" "$runner_seconds" 8 0 file none -- "$executable" "${args[@]}"
rc=$?
set -e
printf 'linux_runner_exit=%s\n' "$rc" | tee "$run/outer-exit.txt"
if [[ -f $run/result.tsv ]]; then timeout 2s cat "$run/result.tsv"; fi
timeout 2s tail -n 12 "$run/payload.stdout.log" "$run/payload.stderr.log" || true
((rc==0)) || exit "$rc"
verify_result "$run/result.tsv"
timeout 3s nvidia-smi -i 0 --query-gpu=uuid,memory.free --format=csv,noheader,nounits > "$run/resources-after.csv"
printf 'stage_completed=%s elapsed_seconds=%s\n' "$phase" "$((SECONDS-stage_begin))"
