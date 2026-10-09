#!/bin/bash
set -euo pipefail
task=/root/sezika-experiments/20261009-repaired-training-8192
begin=$SECONDS
check() { ((SECONDS-begin<45)) && test ! -e "$task/cancel.request"; }
check
printf 'read_only_postreboot_diagnosis=true\n'
date -u +%FT%TZ
timeout 2s cat /proc/sys/kernel/random/boot_id
timeout 2s uname -r
timeout 2s cat /proc/driver/nvidia/version
timeout 3s cat /proc/driver/nvidia/gpus/0000:65:00.0/information
check
for attribute in vendor device enable power_state; do
 check
 path=/sys/bus/pci/devices/0000:65:00.0/$attribute
 if [[ -f $path ]]; then printf 'pci_%s=' "$attribute"; timeout 2s cat "$path"; fi
done
if command -v lspci >/dev/null; then timeout 3s lspci -s 0000:65:00.0 -nn -k; fi
check
set +e
timeout 8s nvidia-smi -i GPU-e786e313-bc92-a63d-a833-f4e48cbafb66 --query-gpu=uuid,name,memory.used,memory.free --format=csv,noheader,nounits
gpu_exit=$?
set -e
printf 'postreboot_gpu_query_exit=%s\n' "$gpu_exit"
check
timeout 4s journalctl -k -b --no-pager --utc --grep='NVRM|Xid|nvidia|65:00' -n 180 --output=short-iso
check
printf '%s  %s\n' \
 C6F3AE942FB90CA842FD49AFF7AE6505EB02A71128264EDC2EF08BC323A48905 "$task/output/run-24576/expanded-training-report.json" \
 4205457556736AB07799B462D42C377710E765CB14EDB9AAE508C1558C5E9D64 "$task/output/run-24576/expanded-head.asset" \
 4709E847A135265F71C092BB18C66CF291DE87D90970CD8932ADD6FBC7F4264F "$task/output/run-24576/expanded-training.checkpoint" \
 E8E74CE0BBF1A72DA5FCDF4DCAD9D302513DC5DC42CB548BFD21D2569621AEB6 "$task/data/experiment-manifest.json" \
 55F3226946831C4B9CBD7F6EE10D1194764860C8343A9386CAA40C678CE24EC0 "$task/data/derivation-ledger.json" \
 B8FB9CECC0912F034E0FEA184E8AA78871D11533093DCCF4F5AAD74646007551 "$task/package/model.json" | timeout 4s sha256sum -c -
printf 'saved_artifact_hashes_verified=true\n'
for container in da214fdd10e65e4800fd8ac02b66ec21f19ab56541f4c2e7f18e63a6a80d3011 74301ee0b2cf9e39d0901bbc7b4e4e9f641d0668a31d67ae920c35f9d1ad62d5; do
 check
 timeout 3s docker inspect --format 'container={{.Id}} state={{.State.Status}} pid={{.State.Pid}} started={{.State.StartedAt}} image={{.Image}} command={{json .Config.Cmd}}' "$container"
done
printf 'diagnosis_completed=true elapsed_seconds=%s gpu_recovered=false\n' "$((SECONDS-begin))"
