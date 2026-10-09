$ErrorActionPreference='Stop'
$task=$PSScriptRoot;$clock=[Diagnostics.Stopwatch]::StartNew()
function Check {if($clock.Elapsed.TotalSeconds -ge 120 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'patch prepare cancelled/deadline'}}
$patch=Join-Path $task 'publication-patch';[IO.Directory]::CreateDirectory($patch)|Out-Null
$all=[Collections.Generic.List[string]]::new()
$groups=@(@{name='bundle';before='publish-linux';after='publish-linux-v2'},@{name='capacity';before='capacity-linux';after='capacity-linux-v2'},@{name='regression';before='regression-linux';after='regression-linux-v2'})
foreach($g in $groups){
 Check;[IO.Directory]::CreateDirectory((Join-Path $patch $g.name))|Out-Null
 $files=@(Get-ChildItem -LiteralPath (Join-Path $task $g.after) -File|Select-Object -First 201)
 if($files.Count -gt 200){throw 'delta file cap'}
 $count=0
 foreach($f in $files){
  Check;$prior=Join-Path $task ($g.before+'/'+$f.Name);$sha=(Get-FileHash -LiteralPath $f.FullName).Hash.ToLowerInvariant()
  if(-not [IO.File]::Exists($prior) -or (Get-FileHash -LiteralPath $prior).Hash.ToLowerInvariant() -cne $sha){
   if($f.Name -cnotmatch '^[A-Za-z0-9_.-]+$' -or $f.Length -gt 4MB -or $count -ge 16){throw 'unexpected delta scope'}
   [IO.File]::Copy($f.FullName,(Join-Path $patch ($g.name+'/'+$f.Name)),$false);$all.Add($sha+'  '+$g.name+'/'+$f.Name);$count++
  }
 }
 Write-Output ($g.name+' changed_files='+$count)
}
foreach($name in @('Sezika.Tests','Sezika.Tests.dll','Sezika.Tests.pdb','Sezika.Tests.deps.json','Sezika.Tests.runtimeconfig.json','Sezika.IndependentModelTool.dll','Sezika.IndependentModelTool.pdb')){
 Check;$path=Join-Path $task ('tests-linux/'+$name);[IO.File]::Copy($path,(Join-Path $patch ('bundle/'+$name)),$false);$all.Add((Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()+'  bundle/'+$name)
}
$p=Join-Path $task 'remote/phase-common.sh';[IO.File]::Copy($p,(Join-Path $task 'phase-common-before-publication-fix.sh'),$false)
$s=[IO.File]::ReadAllText($p).Replace('${phase-} == capacity ||','${phase-} == publication || ${phase-} == capacity2 || ${phase-} == capacity ||')
$s=$s.Replace('$phase != capacity ]];','$phase != capacity && $phase != capacity2 && $phase != publication ]];').Replace('stage-capacity/result.tsv','stage-capacity2/result.tsv').Replace('$phase == capacity ]];','$phase == capacity || $phase == capacity2 ]];')
$s=$s.Replace('capacity)'+"`n",'publication)'+"`n"+'  executable="$task/bundle/Sezika.Tests"; args=(--linux-publication); runner_seconds=30; outer_seconds=42'+"`n"+'  ;;'+"`n"+'capacity|capacity2)'+"`n").Replace('"$task/output/capacity"','"$task/output/$phase"')
[IO.File]::WriteAllText($p,$s,[Text.UTF8Encoding]::new($false));[IO.Directory]::CreateDirectory((Join-Path $patch 'remote'))|Out-Null;[IO.File]::Copy($p,(Join-Path $patch 'remote/phase-common.sh'),$false);$all.Add((Get-FileHash -LiteralPath $p).Hash.ToLowerInvariant()+'  remote/phase-common.sh')
[IO.File]::WriteAllText((Join-Path $patch 'files.sha256'),(($all -join "`n")+"`n"),[Text.UTF8Encoding]::new($false))
try{& (Join-Path $task 'Invoke-BoundedProcess.ps1') -FilePath 'C:\Windows\System32\tar.exe' -ArgumentList @('-cf',(Join-Path $task 'publication-patch.tar'),'-C',$patch,'.') -TimeoutSeconds 25 -LogName 'publication-patch-pack' -ProcessQueryTimeoutSeconds 2 -CancelFile (Join-Path $task 'cancel.request')|Out-Null}catch{Write-Output $_.Exception.Message}
$r=Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -Filter '*publication-patch-pack.result.json' -File|Select-Object -First 1|Get-Content -Raw|ConvertFrom-Json;if($null -eq $r.ExitCode -or $r.ExitCode -ne 0){throw 'patch pack failed'}
$sha=(Get-FileHash -LiteralPath (Join-Path $task 'publication-patch.tar')).Hash.ToLowerInvariant()
$template=@'
#!/bin/bash
set -euo pipefail
umask 077
task=/root/sezika-experiments/20261009-repaired-training-8192
test "$(hostname)" = digisyn-gpu01
test "$(timeout 2s cat "$task/task-identity")" = sezika-token-limit-20261009-8192
test ! -e "$task/.artifacts/stage-train" && test ! -e "$task/logs/publication-patch"
printf '%s  %s\n' ARCHIVEHASH "$task/publication-patch.tar" | timeout 3s sha256sum -c --quiet -
(( $(stat -c %s "$task/publication-patch.tar") <= 16777216 ))
timeout 2s tar -tf "$task/publication-patch.tar" > "$task/logs/publication-patch-inventory.txt"
count=0; begin=$SECONDS
while IFS= read -r name; do
 ((count<80 && SECONDS-begin<5)) || exit 124
 [[ $name == ./ || $name == ./files.sha256 || $name == ./bundle/ || $name == ./capacity/ || $name == ./regression/ || $name == ./remote/ || $name =~ ^\./(bundle|capacity|regression|remote)/[A-Za-z0-9_.-]+$ ]] || exit 65
 count=$((count+1))
done < "$task/logs/publication-patch-inventory.txt"
mkdir "$task/logs/publication-patch" "$task/logs/publication-before-fix"
timeout 2s tar -xf "$task/publication-patch.tar" -C "$task/logs/publication-patch"
(cd "$task/logs/publication-patch" && timeout 3s sha256sum -c --quiet files.sha256)
begin=$SECONDS; count=0
for group in bundle capacity regression remote; do
 ((SECONDS-begin<20)) || exit 124
 (cd "$task/$group" && timeout 5s sha256sum -c --quiet files.sha256)
 mkdir "$task/logs/publication-before-fix/$group"
 cp -- "$task/$group/files.sha256" "$task/logs/publication-before-fix/$group/files.sha256"
done
while read -r hash relative; do
 ((count<64 && SECONDS-begin<20)) || exit 124
 [[ $hash =~ ^[0-9a-f]{64}$ && $relative =~ ^(bundle|capacity|regression|remote)/[A-Za-z0-9_.-]+$ ]] || exit 65
 group=${relative%%/*}; name=${relative#*/}
 if [[ -f $task/$relative ]]; then cp -- "$task/$relative" "$task/logs/publication-before-fix/$relative"; fi
 cp -- "$task/logs/publication-patch/$relative" "$task/$relative"
 timeout 2s awk -v n="$name" '{v=$2; sub(/\r$/,"",v); if(v!=n)print}' "$task/$group/files.sha256" > "$task/$group/files.sha256.new"
 printf '%s  %s\n' "$hash" "$name" >> "$task/$group/files.sha256.new"
 mv -- "$task/$group/files.sha256.new" "$task/$group/files.sha256"
 count=$((count+1))
done < "$task/logs/publication-patch/files.sha256"
for group in bundle capacity regression remote; do
 ((SECONDS-begin<25)) || exit 124
 (cd "$task/$group" && timeout 5s sha256sum -c --quiet files.sha256)
done
chmod 700 "$task/bundle/Sezika.Tests"
timeout 2s bash -n "$task/remote/phase-common.sh"
printf 'publication_fix_files\t%s\nformal_training_started\tfalse\noriginal_failure_preserved\ttrue\n' "$count" > "$task/logs/publication-fix.tsv"
printf 'publication_fix_verified_no_formal_training_yet=true\n'
'@
$template=$template.Replace('ARCHIVEHASH',$sha);[IO.File]::WriteAllText((Join-Path $task 'remote/publication-fix.sh'),$template+"`n",[Text.UTF8Encoding]::new($false))
foreach($phase in @('publication','capacity2')){Check;[IO.File]::WriteAllText((Join-Path $task ('remote/'+$phase+'.sh')),('#!/bin/bash'+"`n"+'phase='+$phase+"`n"+'source /root/sezika-experiments/20261009-repaired-training-8192/remote/phase-common.sh'+"`n"),[Text.UTF8Encoding]::new($false))}
try{& (Join-Path $task 'Invoke-BoundedProcess.ps1') -FilePath 'C:\Windows\System32\OpenSSH\scp.exe' -ArgumentList @('-P','2222','-o','BatchMode=yes','-o','ConnectTimeout=8','-o','StrictHostKeyChecking=yes',(Join-Path $task 'publication-patch.tar'),'root@211.149.170.42:/root/sezika-experiments/20261009-repaired-training-8192/') -TimeoutSeconds 45 -LogName 'publication-patch-upload' -ProcessQueryTimeoutSeconds 2 -CancelFile (Join-Path $task 'cancel.request')|Out-Null}catch{Write-Output $_.Exception.Message}
$r=Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -Filter '*publication-patch-upload.result.json' -File|Select-Object -First 1|Get-Content -Raw|ConvertFrom-Json;if($null -eq $r.ExitCode -or $r.ExitCode -ne 0){throw 'patch upload failed'}
& (Join-Path $task 'Run-Remote.ps1') -Phase publication-fix -Seconds 70
& (Join-Path $task 'Run-Remote.ps1') -Phase publication -Seconds 65
& (Join-Path $task 'Run-Remote.ps1') -Phase capacity2 -Seconds 115
