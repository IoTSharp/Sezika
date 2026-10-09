#!/bin/bash
set -euo pipefail
task=/root/sezika-experiments/20261009-repaired-training-8192
test "$(hostname)" = digisyn-gpu01
test "$(timeout 2s cat "$task/task-identity")" = sezika-token-limit-20261009-8192
test ! -e "$task/cancel.request"
before=$(timeout 2s cat "$task/.artifacts/stage-reboot-approved/boot-id-before.txt")
after=$(timeout 2s cat /proc/sys/kernel/random/boot_id)
test "$before" != "$after"
printf 'boot_id_before=%s\nboot_id_after=%s\n' "$before" "$after"
date -u +%FT%TZ
set +e
timeout 8s nvidia-smi -i GPU-e786e313-bc92-a63d-a833-f4e48cbafb66 --query-gpu=uuid,name,memory.used,memory.free --format=csv,noheader,nounits
gpu_rc=$?
printf 'gpu_observation_exit=%s\n' "$gpu_rc"
set -e
for container in da214fdd10e65e4800fd8ac02b66ec21f19ab56541f4c2e7f18e63a6a80d3011 74301ee0b2cf9e39d0901bbc7b4e4e9f641d0668a31d67ae920c35f9d1ad62d5; do
 timeout 4s docker inspect --format 'container={{.Id}} name={{.Name}} state={{.State.Status}} init_pid={{.State.Pid}} restart={{json .HostConfig.RestartPolicy}}' "$container"
done
for port in 8001 8003; do
 set +e
 timeout 5s curl --max-time 3 -s -w ' port=%{url_effective} http_status=%{http_code}\n' "http://127.0.0.1:$port/health"
 echo "health_curl_exit=$?"
 set -e
done
exit "$gpu_rc"
