#requires -Version 7.0
$ErrorActionPreference='Stop'
$task=$PSScriptRoot
$payload=[IO.File]::ReadAllText((Join-Path $task 'remote/gpu-reset-payload.sh')).Replace("`r`n","`n")
if($payload.Length -gt 16000){throw 'recovery payload cap'}
$base64=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payload))
$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($payload))).ToLowerInvariant()
$script=@'
#!/bin/bash
set -euo pipefail
task=/root/sezika-experiments/20261009-repaired-training-8192
test "$(hostname)" = digisyn-gpu01
test ! -L "$task"
test "$(timeout 2s cat "$task/task-identity")" = sezika-token-limit-20261009-8192
test "$(timeout 2s cat "$task/thread-identity")" = 01a11f25-335a-7571-a735-7171ace8c3b9
test ! -e "$task/cancel.request" && test ! -L "$task/stage.lock"
exec {lockfd}> "$task/stage.lock"
flock -n "$lockfd" || exit 73
run=$task/.artifacts/stage-gpu-reset-approved
test ! -e "$run" && test ! -L "$run"
mkdir "$run"
printf '%s\n' 'User explicitly approved GPU reset after identifying temporary Hearon translation/ASR stop and restoration; no host reboot or training.' > "$run/authorization.txt"
printf '%s' '@@BASE64@@' | base64 --decode > "$run/reset-payload.sh"
printf '%s  %s\n' '@@SHA@@' "$run/reset-payload.sh" | timeout 2s sha256sum -c --quiet -
timeout 3s bash -n "$run/reset-payload.sh"
printf '%s  %s\n' 32167e8cf7396ac6d1f8f54a8d664dfbfaaef71fccba871433b0fdf73e24f2f1 "$task/remote/Invoke-BoundedLinux.sh" | timeout 2s sha256sum -c --quiet -
set +e
timeout --signal=TERM --kill-after=3s 372s bash "$task/remote/Invoke-BoundedLinux.sh" "$run" 360 8 0 file none -- /bin/bash "$run/reset-payload.sh"
rc=$?
set -e
printf 'linux_recovery_runner_exit=%s\n' "$rc" | tee "$run/outer-exit.txt"
test -f "$run/result.tsv" && timeout 2s cat "$run/result.tsv"
timeout 3s tail -n 25 "$run/payload.stdout.log" "$run/payload.stderr.log" || true
exit "$rc"
'@
$script=$script.Replace('@@BASE64@@',$base64).Replace('@@SHA@@',$hash)
[IO.File]::WriteAllText((Join-Path $task 'remote/gpu-reset-approved.sh'),$script,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $task 'gpu-reset-user-authorization.json'),(@{utc=[DateTime]::UtcNow.ToString('o');user_message='可以重置gpu ，';context='temporary stop of the two explicitly identified translation/ASR services, UUID reset, restoration, validation of saved head; no new training';payload_sha256=$hash;gpu='GPU-e786e313-bc92-a63d-a833-f4e48cbafb66';remote_root='/root/sezika-experiments/20261009-repaired-training-8192';stage='stage-gpu-reset-approved';work_seconds=360;outer_seconds=372} | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Output "recovery_payload_sha256=$hash"
