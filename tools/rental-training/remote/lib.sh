#!/bin/bash
set -euo pipefail
umask 077
task=${1:?task root required}
[[ $task =~ ^/[A-Za-z0-9_./+-]+/sezika-rental-20261009-01a12099$ && $task != *..* && ! -L $task ]] || exit 64
[[ $(timeout 2s cat "$task/task-identity") == sezika-rental-20261009-01a12099 ]] || exit 65
guard_begin=$SECONDS
guard() { ((SECONDS-guard_begin<${guard_seconds:-60})) && [[ ! -e $task/cancel.request ]]; }
verify_receipt() {
  local receipt=$1 start=$SECONDS key value
  [[ -f $receipt && ! -L $receipt ]] || return 73
  for item in 'status completed' 'payload_exit 0' 'final_check_complete 1' 'cleanup_incomplete 0' 'verified_live_count 0' 'unknown_count 0' 'unchecked_count 0' 'cleanup_budget_exhausted 0'; do
    ((SECONDS-start<5)) || return 124
    read -r key value <<< "$item"
    [[ $(timeout 1s awk -F '\t' -v k="$key" '$1==k {print $2}' "$receipt") == "$value" ]] || return 73
  done
}
record_shell() {
  local output=$1 start=$SECONDS pid=$$ raw tail arg argv
  local -a fields
  printf 'observed_utc\tpid\tstart_tick\tppid\tpgid\tsid\targv_shell_escaped\n' > "$output"
  for ((depth=0; depth<32 && SECONDS-start<4; depth++)); do
    [[ $pid =~ ^[0-9]+$ && $pid -gt 0 ]] || break
    IFS= read -r raw < "/proc/$pid/stat" || return 65
    tail=${raw##*) }; read -r -a fields <<< "$tail"; ((${#fields[@]}>=20)) || return 65
    argv=''; exec {cmdfd}< "/proc/$pid/cmdline"
    for ((argument=0; argument<128 && SECONDS-start<4; argument++)); do
      arg=''; IFS= read -r -d '' arg <&$cmdfd || break
      printf -v arg '%q' "$arg"; argv+="$arg "
    done
    exec {cmdfd}<&-
    ((argument<128)) || return 65
    printf '%(%FT%TZ)T\t%s\t%s\t%s\t%s\t%s\t%s\n' -1 "$pid" "${fields[19]}" "${fields[1]}" "${fields[2]}" "${fields[3]}" "$argv" >> "$output"
    [[ ${fields[1]} != "$pid" ]] || return 65; pid=${fields[1]}
  done
  ((depth<32 && SECONDS-start<4))
}
resources() {
  local output=$1 row apps uuid free available
  gpu_uuid=$(timeout 2s cat "$task/gpu-uuid")
  [[ $gpu_uuid =~ ^GPU-[A-Fa-f0-9-]+$ ]] || return 65
  row=$(timeout 6s nvidia-smi -i "$gpu_uuid" --query-gpu=uuid,memory.free --format=csv,noheader,nounits)
  IFS=',' read -r uuid free <<< "$row"; free=${free//[[:space:]]/}
  [[ $uuid == "$gpu_uuid" && $free =~ ^[0-9]+$ ]] && ((free>=5120)) || return 75
  apps=$(timeout 5s nvidia-smi -i "$gpu_uuid" --query-compute-apps=pid,process_name,used_gpu_memory --format=csv,noheader,nounits)
  [[ -z $apps ]] || { printf 'selected_gpu_has_other_compute_users\n%s\n' "$apps" >&2; return 75; }
  available=$(timeout 2s awk '$1=="MemAvailable:" {print $2}' /proc/meminfo)
  [[ $available =~ ^[0-9]+$ ]] && ((available>=4194304)) || return 75
  printf 'observed_utc\t%(%FT%TZ)T\ngpu_uuid\t%s\nfree_mib\t%s\nhost_available_kib\t%s\nowned_gpu_limit_bytes\t3221225472\nhost_rss_limit_bytes\t4294967296\n' -1 "$uuid" "$free" "$available" > "$output"
  export CUDA_VISIBLE_DEVICES="$gpu_uuid"
}
