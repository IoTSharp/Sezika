$ErrorActionPreference='Stop'
$task=$PSScriptRoot
$r=Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -Filter '*boundary-revision-pack.result.json' -File|Select-Object -First 1|Get-Content -Raw|ConvertFrom-Json
if($r.ExitCode -ne 0){throw 'patch tar failed'}
$archive=(Get-FileHash -LiteralPath (Join-Path $task 'boundary-uuid-patch.tar')).Hash.ToLowerInvariant()
$oldCommon=(Get-FileHash -LiteralPath (Join-Path $task 'phase-common-before-uuid-revision.sh')).Hash.ToLowerInvariant()
$newCommon=(Get-FileHash -LiteralPath (Join-Path $task 'remote/phase-common.sh')).Hash.ToLowerInvariant()
$oldDll=(Get-FileHash -LiteralPath (Join-Path $task 'boundary-linux/BoundaryCheck.dll')).Hash.ToLowerInvariant()
$oldPdb=(Get-FileHash -LiteralPath (Join-Path $task 'boundary-linux/BoundaryCheck.pdb')).Hash.ToLowerInvariant()
$newDll=(Get-FileHash -LiteralPath (Join-Path $task 'boundary-uuid-patch/BoundaryCheck.dll')).Hash.ToLowerInvariant()
$newPdb=(Get-FileHash -LiteralPath (Join-Path $task 'boundary-uuid-patch/BoundaryCheck.pdb')).Hash.ToLowerInvariant()
$template=@'
#!/bin/bash
set -euo pipefail
umask 077
task=/root/sezika-experiments/20261009-repaired-training-8192
test "$(hostname)" = digisyn-gpu01
test "$(timeout 2s cat "$task/task-identity")" = sezika-token-limit-20261009-8192
test ! -e "$task/.artifacts/stage-boundary-tiny2" && test ! -e "$task/logs/boundary-uuid-patch"
printf '%s  %s\n' ARCHIVE "$task/boundary-uuid-patch.tar" OLDCOMMON "$task/remote/phase-common.sh" OLDDLL "$task/boundary/BoundaryCheck.dll" OLDPDB "$task/boundary/BoundaryCheck.pdb" | timeout 3s sha256sum -c --quiet -
(( $(stat -c %s "$task/boundary-uuid-patch.tar") <= 1048576 ))
timeout 2s tar -tf "$task/boundary-uuid-patch.tar" > "$task/logs/boundary-uuid-patch-inventory.txt"
count=0; begin=$SECONDS
while IFS= read -r name; do
 ((count<5 && SECONDS-begin<5)) || exit 124
 [[ $name == ./ || $name == ./BoundaryCheck.dll || $name == ./BoundaryCheck.pdb || $name == ./phase-common.sh || $name == ./files.sha256 ]] || exit 65
 count=$((count+1))
done < "$task/logs/boundary-uuid-patch-inventory.txt"
mkdir "$task/logs/boundary-uuid-patch" "$task/logs/boundary-before-uuid-fix"
timeout 2s tar -xf "$task/boundary-uuid-patch.tar" -C "$task/logs/boundary-uuid-patch"
(cd "$task/logs/boundary-uuid-patch" && timeout 2s sha256sum -c --quiet files.sha256)
cp -- "$task/boundary/BoundaryCheck.dll" "$task/boundary/BoundaryCheck.pdb" "$task/boundary/files.sha256" "$task/logs/boundary-before-uuid-fix/"
cp -- "$task/remote/phase-common.sh" "$task/logs/phase-common-before-uuid-revision.sh"
cp -- "$task/remote/files.sha256" "$task/logs/remote-files-before-uuid-revision.sha256"
cp -- "$task/logs/boundary-uuid-patch/BoundaryCheck.dll" "$task/logs/boundary-uuid-patch/BoundaryCheck.pdb" "$task/boundary/"
cp -- "$task/logs/boundary-uuid-patch/phase-common.sh" "$task/remote/phase-common.sh"
sed -i 's/^OLDDLL  BoundaryCheck.dll/NEWDLL  BoundaryCheck.dll/;s/^OLDPDB  BoundaryCheck.pdb/NEWPDB  BoundaryCheck.pdb/' "$task/boundary/files.sha256"
sed -i 's/^OLDCOMMON  phase-common.sh/NEWCOMMON  phase-common.sh/' "$task/remote/files.sha256"
(cd "$task/boundary" && timeout 5s sha256sum -c --quiet files.sha256)
(cd "$task/remote" && timeout 5s sha256sum -c --quiet files.sha256)
timeout 2s bash -n "$task/remote/phase-common.sh"
printf 'uuid_format_fix_applied_original_failure_preserved_no_encoder_forward_yet=true\n'
'@
$template=$template.Replace('ARCHIVE',$archive).Replace('OLDCOMMON',$oldCommon).Replace('NEWCOMMON',$newCommon).Replace('OLDDLL',$oldDll).Replace('NEWDLL',$newDll).Replace('OLDPDB',$oldPdb).Replace('NEWPDB',$newPdb)
[IO.File]::WriteAllText((Join-Path $task 'remote/boundary-uuid-revision.sh'),$template+"`n",[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $task 'remote/boundary-tiny2.sh'),('#!/bin/bash'+"`n"+'phase=boundary-tiny2'+"`n"+'source /root/sezika-experiments/20261009-repaired-training-8192/remote/phase-common.sh'+"`n"),[Text.UTF8Encoding]::new($false))
try{ & (Join-Path $task 'Invoke-BoundedProcess.ps1') -FilePath 'C:\Windows\System32\OpenSSH\scp.exe' -ArgumentList @('-P','2222','-o','BatchMode=yes','-o','ConnectTimeout=8','-o','StrictHostKeyChecking=yes',(Join-Path $task 'boundary-uuid-patch.tar'),'root@211.149.170.42:/root/sezika-experiments/20261009-repaired-training-8192/') -TimeoutSeconds 40 -LogName 'boundary-revision-upload' -ProcessQueryTimeoutSeconds 2 -CancelFile (Join-Path $task 'cancel.request')|Out-Null }catch{Write-Output $_.Exception.Message}
$r=Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -Filter '*boundary-revision-upload.result.json' -File|Select-Object -First 1|Get-Content -Raw|ConvertFrom-Json
if($null -eq $r.ExitCode -or $r.ExitCode -ne 0){throw 'patch scp failed'}
& (Join-Path $task 'Run-Remote.ps1') -Phase boundary-uuid-revision -Seconds 45
& (Join-Path $task 'Run-Remote.ps1') -Phase boundary-tiny2 -Seconds 170
