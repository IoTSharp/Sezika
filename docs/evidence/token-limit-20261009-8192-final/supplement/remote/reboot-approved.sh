#!/bin/bash
set -euo pipefail
task=/root/sezika-experiments/20261009-repaired-training-8192
test "$(hostname)" = digisyn-gpu01
test "$(timeout 2s cat "$task/task-identity")" = sezika-token-limit-20261009-8192
test ! -e "$task/cancel.request" && test ! -L "$task/stage.lock"
exec {lockfd}> "$task/stage.lock"
flock -n "$lockfd" || exit 73
run=$task/.artifacts/stage-reboot-approved
test ! -e "$run" && test ! -L "$run"
mkdir "$run"
printf '%s\n' 'User explicitly permits rebooting this GPU server and checking GPU recovery; no retraining.' > "$run/authorization.txt"
date -u +%FT%TZ > "$run/request-utc.txt"
timeout 2s cat /proc/sys/kernel/random/boot_id > "$run/boot-id-before.txt"
timeout 2s cat /proc/$$/stat > "$run/request-process-stat.txt"
timeout 2s tr '\0' ' ' < /proc/$$/cmdline > "$run/request-process-argv.txt"
for container in da214fdd10e65e4800fd8ac02b66ec21f19ab56541f4c2e7f18e63a6a80d3011 74301ee0b2cf9e39d0901bbc7b4e4e9f641d0668a31d67ae920c35f9d1ad62d5; do
 timeout 4s docker inspect --format 'container={{.Id}} name={{.Name}} state={{.State.Status}} init_pid={{.State.Pid}} restart={{json .HostConfig.RestartPolicy}}' "$container" >> "$run/services-before.txt"
done
printf '%s  %s\n' C6F3AE942FB90CA842FD49AFF7AE6505EB02A71128264EDC2EF08BC323A48905 "$task/output/run-24576/expanded-training-report.json" 4205457556736AB07799B462D42C377710E765CB14EDB9AAE508C1558C5E9D64 "$task/output/run-24576/expanded-head.asset" 4709E847A135265F71C092BB18C66CF291DE87D90970CD8932ADD6FBC7F4264F "$task/output/run-24576/expanded-training.checkpoint" | timeout 3s sha256sum -c --quiet -
timeout 4s sync "$task/recovery-evidence.tar" "$task/output/run-24576/expanded-head.asset"
printf 'reboot_approved_host=digisyn-gpu01 boot_id_before='; cat "$run/boot-id-before.txt"
printf 'reboot_request_utc='; cat "$run/request-utc.txt"
cat "$run/services-before.txt"
set +e
timeout --signal=TERM --kill-after=2s 10s /usr/bin/systemctl reboot
rc=$?
printf 'reboot_request_exit=%s\n' "$rc" | tee "$run/request-exit.txt"
exit "$rc"
