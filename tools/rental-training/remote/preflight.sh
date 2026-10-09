#!/bin/bash
set -euo pipefail
umask 077
begin=$SECONDS
base_request='__REMOTE_BASE__'
requested_gpu='__GPU_UUID__'
[[ $base_request == home ]] && base_request="$HOME/sezika-experiments"
[[ $base_request =~ ^/[A-Za-z0-9_./+-]+$ && $base_request != *..* ]] || exit 64
[[ $requested_gpu == auto || $requested_gpu =~ ^GPU-[A-Fa-f0-9-]+$ ]] || exit 64
[[ $(uname -s) == Linux && $(uname -m) == x86_64 ]] || exit 65
[[ ! -L $base_request ]] || exit 65
parent=$base_request
for ((depth=0; depth<16 && SECONDS-begin<15; depth++)); do
  [[ -d $parent ]] && break
  parent=${parent%/*}; [[ -n $parent ]] || parent=/
done
[[ -d $parent && ! -L $parent && -w $parent ]] || exit 65
printf 'observed_utc=%(%FT%TZ)T\n' -1
printf 'hostname=%s\narchitecture=%s\nremote_base=%s\n' "$(hostname)" "$(uname -m)" "$base_request"
printf 'boot_id=%s\n' "$(timeout 2s cat /proc/sys/kernel/random/boot_id)"
timeout 3s uname -a
timeout 3s head -n 8 /etc/os-release
for command in bash timeout flock setsid sha256sum tar awk stat df nvidia-smi ldd; do
  ((SECONDS-begin<20)) || exit 124
  command -v "$command" || exit 69
done
timeout 3s getconf GNU_LIBC_VERSION
available=$(timeout 3s awk '$1=="MemAvailable:" {print $2}' /proc/meminfo)
disk=$(timeout 3s df -B1 --output=avail "$parent" | tail -n 1); disk=${disk//[[:space:]]/}
[[ $available =~ ^[0-9]+$ && $disk =~ ^[0-9]+$ ]] || exit 65
printf 'host_available_kib=%s\ndisk_available_bytes=%s\n' "$available" "$disk"
((available>=4194304 && disk>=6442450944)) || exit 75
gpu_rows=$(timeout --signal=TERM --kill-after=1s 8s nvidia-smi --query-gpu=index,uuid,name,driver_version,memory.free,memory.total --format=csv,noheader,nounits)
apps=$(timeout --signal=TERM --kill-after=1s 5s nvidia-smi --query-compute-apps=gpu_uuid,pid,process_name,used_gpu_memory --format=csv,noheader,nounits)
printf 'gpu_inventory_begin\n%s\ngpu_inventory_end\ncompute_apps_begin\n%s\ncompute_apps_end\n' "$gpu_rows" "$apps"
selected=''; count=0
while IFS=',' read -r index uuid name driver free total; do
  ((count<16 && SECONDS-begin<45)) || exit 124
  count=$((count+1)); uuid=${uuid//[[:space:]]/}; free=${free//[[:space:]]/}
  [[ $uuid =~ ^GPU-[A-Fa-f0-9-]+$ && $free =~ ^[0-9]+$ ]] || exit 65
  if [[ -z $selected && ( $requested_gpu == auto || $requested_gpu == "$uuid" ) ]] && ((free>=5120)) && [[ $apps != *"$uuid"* ]]; then selected=$uuid; fi
done <<< "$gpu_rows"
[[ -n $selected ]] || { printf 'no_idle_gpu_admitted\n' >&2; exit 75; }
printf 'selected_gpu_uuid=%s\npreflight=passed_read_only\nelapsed_seconds=%s\n' "$selected" "$((SECONDS-begin))"
