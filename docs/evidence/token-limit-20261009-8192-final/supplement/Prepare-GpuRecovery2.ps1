#requires -Version 7.0
$ErrorActionPreference='Stop'
$task=$PSScriptRoot
$original=[IO.File]::ReadAllText((Join-Path $task 'remote/gpu-reset-payload.sh'))
$payload=$original.Replace('stage-gpu-reset-approved','stage-gpu-reset-approved2').Replace('expected_init=2990; expected_pid=3055; expected_tick=1542; expected_parent=2990; expected_shim=2938','expected_init=3168797; expected_pid=3168859; expected_tick=observed; expected_parent=3168797; expected_shim=3168771').Replace('expected_init=2982; expected_pid=3068; expected_tick=1586; expected_parent=2982; expected_shim=2937','expected_init=3169044; expected_pid=3169105; expected_tick=observed; expected_parent=3169044; expected_shim=3169014').Replace('[[ $device_pid == 3055 || $device_pid == 3068 ]]','[[ $device_pid == 3168859 || $device_pid == 3169105 ]]')
$old='test "${fields[19]}" = "$expected_tick" && test "${fields[1]}" = "$expected_parent"'
$new=@'
if [[ $expected_tick == observed ]]; then expected_tick=${fields[19]}; fi
 printf 'service_identity_pid=%s starttick=%s parent=%s\n' "$expected_pid" "$expected_tick" "$expected_parent" | tee -a "$run/services-before.txt"
 test "${fields[19]}" = "$expected_tick" && test "${fields[1]}" = "$expected_parent"
'@
if(-not $payload.Contains($old)){throw 'identity seam differs'}
$payload=$payload.Replace($old,$new)
$old=@'
set +e
timeout 4s fuser /dev/nvidia0 > "$run/device-users-after-stop.txt" 2> "$run/device-users-after-stop.stderr"
fuser_rc=$?
set -e
((fuser_rc==1)) || { echo "device_still_used_or_unreadable=$fuser_rc" >&2; exit 73; }
'@
$new=@'
device_wait_begin=$SECONDS
for device_check in {1..12}; do
 check
 ((SECONDS-device_wait_begin<30)) || exit 124
 set +e
 timeout 2s fuser /dev/nvidia0 > "$run/device-users-after-stop-$device_check.txt" 2> "$run/device-users-after-stop-$device_check.stderr"
 fuser_rc=$?
 set -e
 echo "device_release_check=$device_check fuser_exit=$fuser_rc"
 if ((fuser_rc==1)); then break; fi
 ((fuser_rc==0 && device_check<12)) || { echo "device_still_used_or_unreadable=$fuser_rc" >&2; exit 73; }
 read -r -a still_pids < "$run/device-users-after-stop-$device_check.txt" || true
 ((${#still_pids[@]}<=16)) || exit 65
 for client_pid in "${still_pids[@]}"; do
  check
  [[ $client_pid =~ ^[0-9]+$ ]] || exit 65
  if [[ -d /proc/$client_pid ]]; then
   timeout 2s ps -o pid,ppid,lstart,args -p "$client_pid" >> "$run/transient-clients.txt" || true
   timeout 2s cat "/proc/$client_pid/stat" "/proc/$client_pid/cgroup" >> "$run/transient-clients.txt" || true
  else echo "pid=$client_pid absent_after_fuser_snapshot" >> "$run/transient-clients.txt"; fi
 done
 sleep 1
done
'@
if(-not $payload.Contains($old)){throw 'device release seam differs'}
$payload=$payload.Replace($old,$new).Replace("`r`n","`n")
$oldBytes=[Text.Encoding]::UTF8.GetBytes($original.Replace("`r`n","`n"))
$bytes=[Text.Encoding]::UTF8.GetBytes($payload)
$old64=[Convert]::ToBase64String($oldBytes);$new64=[Convert]::ToBase64String($bytes)
$oldHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($oldBytes)).ToLowerInvariant()
$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
$script=[IO.File]::ReadAllText((Join-Path $task 'remote/gpu-reset-approved.sh'))
if(-not $script.Contains($old64) -or -not $script.Contains($oldHash)){throw 'frozen first recovery wrapper differs'}
$script=$script.Replace($old64,$new64).Replace($oldHash,$hash).Replace('stage-gpu-reset-approved','stage-gpu-reset-approved2')
[IO.File]::WriteAllText((Join-Path $task 'remote/gpu-reset-payload2.sh'),$payload,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $task 'remote/gpu-reset-approved2.sh'),$script,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $task 'gpu-reset-second-preparation.json'),(@{utc=[DateTime]::UtcNow.ToString('o');first_stage='protected refusal before reset; original services restarted exit0';new_stage='stage-gpu-reset-approved2';payload_sha256=$hash;reset_attempts_so_far=0;device_wait_max_iterations=12;device_wait_seconds=30;authorization='same explicit user GPU reset approval; no new training/reboot or unknown client signals'} | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Output "second_recovery_sha256=$hash"
