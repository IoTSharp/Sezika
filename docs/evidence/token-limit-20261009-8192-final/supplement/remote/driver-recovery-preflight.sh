#!/bin/bash
set -euo pipefail
begin=$SECONDS
test "$(hostname)" = digisyn-gpu01
date -u +%FT%TZ
timeout 2s cat /proc/driver/nvidia/version
timeout 3s awk '/^nvidia/ {print; count++; if(count>=16)exit}' /proc/modules
for module in nvidia nvidia_uvm nvidia_drm nvidia_modeset nvidia_peermem; do
 ((SECONDS-begin<20)) || exit 124
 if [[ -d /sys/module/$module ]]; then printf 'module=%s refcnt=' "$module"; timeout 2s cat "/sys/module/$module/refcnt"; fi
done
timeout 3s modinfo -F filename nvidia
timeout 3s modinfo -F version nvidia
for tool in modprobe rmmod fuser; do command -v "$tool"; done
set +e
timeout 3s systemctl is-active display-manager
echo "display_manager_query_exit=$?"
timeout 4s fuser -v /dev/nvidia0 /dev/nvidiactl /dev/nvidia-uvm
echo "all_nvidia_device_users_query_exit=$?"
set -e
((SECONDS-begin<30)) || exit 124
timeout 3s find /proc/driver/nvidia/gpus -mindepth 1 -maxdepth 1 -type d
timeout 3s journalctl -k --since '2026-10-09 10:40:00 UTC' --no-pager -n 80 | awk '/NVRM|Xid|GPU|nvidia/ {print; n++; if(n>=32)exit}'
for port in 8001 8003; do
 ((SECONDS-begin<45)) || exit 124
 printf 'health_port=%s ' "$port"
 timeout 5s curl --max-time 3 -s -w ' http_status=%{http_code}\n' "http://127.0.0.1:$port/health" || true
done
echo 'read_only=true'
