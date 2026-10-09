#!/bin/bash
set -euo pipefail
umask 077
task=/root/sezika-experiments/20261009-repaired-training-8192
test "$(hostname)" = digisyn-gpu01
test ! -L "$task" && test "$(timeout 2s cat "$task/task-identity")" = sezika-token-limit-20261009-8192
test "$(timeout 2s cat "$task/thread-identity")" = 01a11f25-335a-7571-a735-7171ace8c3b9
test ! -L "$task/stage.lock"
exec {lockfd}> "$task/stage.lock"
/usr/bin/flock -n "$lockfd" || exit 73
receipt="$task/logs/recovery-final-closure.tsv"
if [[ -e $receipt ]]; then timeout 2s cat "$receipt"; printf 'closure_exists_no_repeat\n'; exit 73; fi
begin=$SECONDS; known=0; live=0; unknown=0; files=0; runs=0
printf 'source\tpid\texpected_start_tick\tstate\n' > "$receipt"
check_identity() {
  local source=$1 pid=$2 expected=$3 raw tail status=absent
  ((SECONDS-begin<20 && known<3072)) || return 124
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
for name in runner-exit7 runner-tiny1 stage-boundary-tiny stage-boundary-tiny2 stage-publication stage-capacity2 stage-boundary-full stage-boundary-real stage-capacity stage-train stage-validate stage-regress; do
  ((runs<12 && SECONDS-begin<20)) || exit 124
  runs=$((runs+1)); run="$task/.artifacts/$name"
  if [[ ! -e $run ]]; then printf 'run_absent %s\n' "$name"; continue; fi
  test -d "$run" && test ! -L "$run"
  for basename in identity.tsv supervisor.tsv stage-shell.tsv; do
    ((files<64 && SECONDS-begin<20)) || exit 124
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
((live==0 && unknown==0)) || exit 1
set +e
timeout 5s nvidia-smi -i 0 --query-gpu=uuid,memory.used,memory.free --format=csv,noheader,nounits > "$task/logs/recovery-gpu-final.csv" 2> "$task/logs/recovery-gpu-final.stderr"
gpu_rc=$?
set -e
printf 'final_gpu_query_exit=%s\n' "$gpu_rc" > "$task/logs/recovery-final-state.txt"
for container in da214fdd10e65e4800fd8ac02b66ec21f19ab56541f4c2e7f18e63a6a80d3011 74301ee0b2cf9e39d0901bbc7b4e4e9f641d0668a31d67ae920c35f9d1ad62d5; do
 ((SECONDS-begin<45)) || exit 124
 timeout 4s docker inspect --format 'container={{.Id}} name={{.Name}} state={{.State.Status}} init_pid={{.State.Pid}} image={{.Config.Image}} restart={{json .HostConfig.RestartPolicy}}' "$container" >> "$task/logs/recovery-services-final.txt"
 timeout 4s docker top "$container" -eo pid,ppid,lstart,args >> "$task/logs/recovery-services-final.txt"
done
for port in 8001 8003; do
 ((SECONDS-begin<55)) || exit 124
 set +e
 timeout 5s curl --max-time 3 -s -o "$task/logs/recovery-health-$port.json" -w 'port=%{url_effective} http_status=%{http_code}\n' "http://127.0.0.1:$port/health" >> "$task/logs/recovery-final-state.txt"
 http_rc=$?
 set -e
 echo "health_port=$port curl_exit=$http_rc" >> "$task/logs/recovery-final-state.txt"
done
timeout 4s journalctl -k --since '2026-10-09 10:49:00 UTC' --no-pager -n 50 > "$task/logs/recovery-kernel-final.txt"
printf '%s  %s\n' C6F3AE942FB90CA842FD49AFF7AE6505EB02A71128264EDC2EF08BC323A48905 "$task/output/run-24576/expanded-training-report.json" 4205457556736AB07799B462D42C377710E765CB14EDB9AAE508C1558C5E9D64 "$task/output/run-24576/expanded-head.asset" 4709E847A135265F71C092BB18C66CF291DE87D90970CD8932ADD6FBC7F4264F "$task/output/run-24576/expanded-training.checkpoint" B8FB9CECC0912F034E0FEA184E8AA78871D11533093DCCF4F5AAD74646007551 "$task/package/model.json" | timeout 3s sha256sum -c --quiet -
test ! -e "$task/recovery-evidence.tar" && test ! -e "$task/recovery-evidence.tar.partial"
cd "$task"
parts=(.artifacts/stage-gpu-reset-approved .artifacts/stage-gpu-reset-approved2 .artifacts/stage-gpu-reset-command .artifacts/stage-gpu-driver-reload logs/recovery-final-closure.tsv logs/recovery-gpu-final.csv logs/recovery-gpu-final.stderr logs/recovery-final-state.txt logs/recovery-services-final.txt logs/recovery-kernel-final.txt)
for port in 8001 8003; do if [[ -f logs/recovery-health-$port.json ]]; then parts+=("logs/recovery-health-$port.json"); fi; done
((SECONDS-begin<65)) || exit 124
total=$(timeout 3s du -sb -- "${parts[@]}" | awk '{sum+=$1} END {printf "%.0f\n",sum}')
[[ $total =~ ^[0-9]+$ ]] && ((total<=8*1024*1024))
timeout --signal=TERM --kill-after=1s 12s tar -cf recovery-evidence.tar.partial -- "${parts[@]}"
size=$(stat -c %s recovery-evidence.tar.partial); ((size<=10*1024*1024))
mv -n -- recovery-evidence.tar.partial recovery-evidence.tar
test ! -e recovery-evidence.tar.partial
timeout 3s sha256sum recovery-evidence.tar > logs/recovery-evidence.sha256
timeout 2s cat logs/recovery-evidence.sha256 logs/recovery-final-state.txt logs/recovery-services-final.txt
echo "recovery_evidence_complete bytes=$size no_new_training=true"