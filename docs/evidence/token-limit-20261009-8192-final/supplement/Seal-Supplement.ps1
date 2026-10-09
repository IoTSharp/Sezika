#requires -Version 7.0
$ErrorActionPreference='Stop'
$task=[IO.Path]::GetFullPath($PSScriptRoot)
$base='D:\source\Sezika\docs\evidence\token-limit-20261009-8192-final'
$destination=Join-Path $base 'supplement'
$clock=[Diagnostics.Stopwatch]::StartNew()
function Check {if($clock.Elapsed.TotalSeconds -ge 80 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'supplement deadline/cancellation'}}
if(Test-Path -LiteralPath $destination){throw 'existing supplement retained; no overwrite'}
$baseline=Get-Content -Raw -LiteralPath (Join-Path $base 'inventory.json') | ConvertFrom-Json
$old=@{}
foreach($entry in $baseline.entries){Check;$old[$entry.path]=$entry.sha256}
$candidates=[Collections.Generic.List[object]]::new()
foreach($directory in @($task,(Join-Path $task 'remote'),(Join-Path $task 'process-logs'))){
 Check
 $files=@(Get-ChildItem -LiteralPath $directory -File -Force | Select-Object -First 1025)
 if($files.Count -gt 1024){throw 'directory file cap1024'}
 foreach($file in $files){
  Check
  $relative=[IO.Path]::GetRelativePath($task,$file.FullName).Replace('\','/')
  if($relative -match '\.tar$'){continue}
  if($relative.StartsWith('process-logs/')){
   $stem=[regex]::Replace($file.Name,'\.(result\.json|identity\.json|stdout\.log|stderr\.log)$','')
   if($stem -ceq $file.Name -or -not [IO.File]::Exists((Join-Path $directory ($stem+'.result.json')))){continue}
  }
  $candidates.Add($file)
 }
}
$stack=[Collections.Generic.Stack[object]]::new()
$stack.Push(@{path=(Join-Path $task 'recovery-retrieved');depth=0})
for($directoryCount=0;$directoryCount -lt 128 -and $stack.Count -gt 0;$directoryCount++){
 Check
 $entry=$stack.Pop()
 $children=@(Get-ChildItem -LiteralPath $entry.path -Force | Select-Object -First 129)
 if($children.Count -gt 128){throw 'recovery directory cap128'}
 foreach($child in $children){
  Check
  if(($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'recovery reparse point'}
  if($child.PSIsContainer){if($entry.depth -ge 8){throw 'recovery depth cap8'};$stack.Push(@{path=$child.FullName;depth=$entry.depth+1})}
  else{$candidates.Add($child)}
 }
}
if($stack.Count -gt 0 -or $candidates.Count -gt 1024){throw 'supplement total cap1024'}
$entries=[Collections.Generic.List[object]]::new()
[IO.Directory]::CreateDirectory($destination) | Out-Null
for($index=0;$index -lt $candidates.Count -and $index -lt 1024;$index++){
 Check
 $file=$candidates[$index];$file.Refresh()
 if($file.Length -gt 4MB){throw 'supplement per-file cap4MB'}
 $relative=[IO.Path]::GetRelativePath($task,$file.FullName).Replace('\','/')
 $hash=(Get-FileHash -LiteralPath $file.FullName).Hash
 if($old.ContainsKey($relative) -and $old[$relative] -ceq $hash){continue}
 $target=[IO.Path]::GetFullPath((Join-Path $destination $relative))
 if(-not $target.StartsWith($destination+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'supplement escaped destination'}
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
 [IO.File]::Copy($file.FullName,$target,$false)
 if((Get-FileHash -LiteralPath $target).Hash -cne $hash -or (Get-Item -LiteralPath $target).Length -ne $file.Length){throw ('supplement copy differs: '+$relative)}
 $entries.Add(@{path=$relative;bytes=$file.Length;sha256=$hash;source=$file.FullName;already_in_base=$old.ContainsKey($relative)})
 if($index % 100 -eq 0){Write-Output "supplement=$index/$($candidates.Count) copied=$($entries.Count)"}
}
[IO.File]::WriteAllText((Join-Path $destination 'inventory.json'),(@{utc=[DateTime]::UtcNow.ToString('o');files=$entries.Count;bytes=($entries | Measure-Object bytes -Sum).Sum;entries=$entries;scope='new or changed local scripts/receipts/metadata and safe extracted recovery evidence; original554 archive unchanged';limitation='this sealing invocation and subsequent Git/closure receipts are finalized after snapshot; complete local tar retained';original_inventory_sha256=(Get-FileHash -LiteralPath (Join-Path $base 'inventory.json')).Hash} | ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
Write-Output "supplement_files=$($entries.Count) bytes=$(($entries | Measure-Object bytes -Sum).Sum)"
