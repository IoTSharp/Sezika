#!/bin/bash
set -euo pipefail
umask 077
task=/root/sezika-experiments/20261009-repaired-training-v3
test "$(hostname)" = digisyn-gpu01
test ! -L "$task" && test "$(timeout 2s cat "$task/task-identity")" = sezika-repaired-training-20261009
test "$(timeout 2s cat "$task/thread-identity")" = 01a11f25-335a-7571-a735-7171ace8c3b9
test ! -L "$task/stage.lock"
exec {lockfd}> "$task/stage.lock"
/usr/bin/flock -n "$lockfd" || exit 73
receipt="$task/logs/independent-final-closure.tsv"
if [[ -e $receipt ]]; then timeout 2s cat "$receipt"; printf 'closure_exists_no_repeat\n'; exit 73; fi
begin=$SECONDS; known=0; live=0; unknown=0; files=0; runs=0
printf 'source\tpid\texpected_start_tick\tstate\n' > "$receipt"
check_identity() {
  local source=$1 pid=$2 expected=$3 raw tail status=absent
  ((SECONDS-begin<20 && known<2048)) || return 124
  [[ $pid =~ ^[0-9]+$ && $expected =~ ^[0-9]+$ ]] || return 65
  known=$((known+1))
  if [[ -d /proc/$pid ]]; then
    if IFS= read -r raw < "/proc/$pid/stat"; then
      tail=${raw##*) }; read -r -a fields <<< "$tail"
      if ((${#fields[@]}<20)); then status=unknown; unknown=$((unknown+1))
      elif [[ ${fields[19]} != "$expected" ]]; then status=reused
      elif [[ ${fields[0]} == Z ]]; then status=zombie
      else status=matching_start_identity_live; live=$((live+1)); fi
    elif [[ -d /proc/$pid ]]; then status=unknown; unknown=$((unknown+1)); fi
  fi
  printf '%s\t%s\t%s\t%s\n' "$source" "$pid" "$expected" "$status" >> "$receipt"
  ((known%32!=0)) || printf 'identity_progress known=%s elapsed_seconds=%s\n' "$known" "$((SECONDS-begin))"
}
# Exact task registrations only. Never signals an identity or scans source/workspaces.
for name in runner-exit7 runner-tiny1 stage-capacity stage-train stage-validate stage-regress; do
  ((runs<6 && SECONDS-begin<20)) || exit 124
  runs=$((runs+1)); run="$task/.artifacts/$name"
  if [[ ! -e $run ]]; then printf 'run_absent %s\n' "$name"; continue; fi
  test -d "$run" && test ! -L "$run"
  for basename in identity.tsv supervisor.tsv stage-shell.tsv; do
    ((files<18 && SECONDS-begin<20)) || exit 124
    file="$run/$basename"; [[ -e $file ]] || continue
    test -f "$file" && test ! -L "$file"; (( $(stat -c %s "$file") <= 1048576 ))
    files=$((files+1)); rows=0
    while IFS=$'\t' read -r pid tick rest; do
      ((rows<256 && SECONDS-begin<20)) || exit 124
      rows=$((rows+1))
      [[ $pid == pid ]] && continue
      check_identity "$name/$basename" "$pid" "$tick"
    done < "$file"
  done
done
printf 'summary\tknown=%s\tlive=%s\tunknown=%s\tfiles=%s\tscope=registered_identities_no_signal\n' "$known" "$live" "$unknown" "$files" >> "$receipt"
timeout 2s cat "$receipt"
timeout 3s nvidia-smi -i 0 --query-gpu=uuid,memory.used,memory.free --format=csv,noheader,nounits > "$task/logs/gpu-final.csv"
timeout 3s nvidia-smi --query-compute-apps=pid,process_name,used_gpu_memory --format=csv,noheader,nounits > "$task/logs/gpu-processes-final.csv"
((live==0 && unknown==0)) || exit 1
# This closure can retain failure evidence. It does not declare successful training.
test ! -e "$task/evidence-bundle.tar" && test ! -e "$task/evidence-bundle.tar.partial"
cd "$task"
total=$(timeout 3s du -sb -- output logs .artifacts | timeout 2s awk '{sum+=$1} END {printf "%.0f\n",sum}')
[[ $total =~ ^[0-9]+$ ]] && ((total<=63*1024*1024))
timeout --signal=TERM --kill-after=1s 15s tar -cf evidence-bundle.tar.partial -- output logs .artifacts task-identity thread-identity remote/files.sha256 data/files.sha256 data/repair-evidence/files.sha256 bundle/files.sha256 capacity/files.sha256 regression/files.sha256 regression_input/files.sha256
size=$(stat -c %s evidence-bundle.tar.partial); ((size<=64*1024*1024))
mv -n -- evidence-bundle.tar.partial evidence-bundle.tar
test ! -e evidence-bundle.tar.partial
timeout 3s sha256sum evidence-bundle.tar > logs/evidence-bundle.sha256
timeout 2s cat logs/evidence-bundle.sha256
printf 'independent_closure_and_evidence_pack_complete bytes=%s\n' "$size"
