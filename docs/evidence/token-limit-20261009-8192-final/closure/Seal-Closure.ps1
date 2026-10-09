#requires -Version 7.0
param([ValidateRange(1,2048)][int]$MaxItems=2048,[switch]$Preview)
$ErrorActionPreference='Stop'
$task=[IO.Path]::GetFullPath($PSScriptRoot)
$base='D:\source\Sezika\docs\evidence\token-limit-20261009-8192-final'
$destination=Join-Path $base 'closure'
$clock=[Diagnostics.Stopwatch]::StartNew()
function Check {if($clock.Elapsed.TotalSeconds -ge 90 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'closure seal deadline/cancellation'}}
if(Test-Path -LiteralPath $destination){throw 'existing closure evidence retained'}
$old=@{}
foreach($name in @('inventory.json','supplement/inventory.json')){
 Check
 $inv=Get-Content -Raw -LiteralPath (Join-Path $base $name)|ConvertFrom-Json
 if($inv.entries.Count -ne $inv.files -or $inv.files -gt 1024){throw 'inventory count cap/mismatch'}
 foreach($entry in $inv.entries){Check;$old[$entry.path]=$entry.sha256}
}
$candidates=[Collections.Generic.List[object]]::new()
foreach($directory in @($task,(Join-Path $task 'remote'),(Join-Path $task 'process-logs'))){
 Check
 $files=@(Get-ChildItem -LiteralPath $directory -File -Force|Select-Object -First 1025)
 if($files.Count -gt 1024){throw 'directory file cap1024'}
 foreach($file in $files){
  Check
  $relative=[IO.Path]::GetRelativePath($task,$file.FullName).Replace('\','/')
  if($relative -match '\.tar$' -or $file.Name -ceq 'archive-index-pathspec.nul'){continue}
  if($relative.StartsWith('process-logs/')){
   $stem=[regex]::Replace($file.Name,'\.(result\.json|identity\.json|stdout\.log|stderr\.log)$','')
   if($stem -ceq $file.Name -or -not [IO.File]::Exists((Join-Path $directory ($stem+'.result.json')))){continue}
  }
  $candidates.Add($file)
 }
}
if($candidates.Count -gt 2048){throw 'candidate file cap2048'}
$entries=[Collections.Generic.List[object]]::new()
$count=[Math]::Min($MaxItems,$candidates.Count)
for($i=0;$i -lt $count -and $i -lt 2048;$i++){
 Check
 $file=$candidates[$i];$file.Refresh()
 if($file.Length -gt 4MB -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'file cap4MB/reparse point'}
 $relative=[IO.Path]::GetRelativePath($task,$file.FullName).Replace('\','/')
 $hash=(Get-FileHash -LiteralPath $file.FullName).Hash
 if($old.ContainsKey($relative) -and $old[$relative] -ceq $hash){continue}
 if(-not $Preview){
  $target=[IO.Path]::GetFullPath((Join-Path $destination $relative))
  if(-not $target.StartsWith($destination+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'closure destination escaped'}
  [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))|Out-Null
  [IO.File]::Copy($file.FullName,$target,$false)
  if((Get-FileHash -LiteralPath $target).Hash -cne $hash -or (Get-Item -LiteralPath $target).Length -ne $file.Length){throw ('closure copy differs: '+$relative)}
 }
 $entries.Add(@{path=$relative;bytes=$file.Length;sha256=$hash;source=$file.FullName;already_in_prior_inventories=$old.ContainsKey($relative)})
 if($i%150 -eq 0){Write-Output "closure_seal=$i/$($candidates.Count) new=$($entries.Count)"}
}
if($Preview){Write-Output "preview_checked=$count no_copy=true";exit 0}
if($count -ne $candidates.Count){throw 'full sealing required'}
[IO.Directory]::CreateDirectory($destination)|Out-Null
$inventory=@{utc=[DateTime]::UtcNow.ToString('o');files=$entries.Count;bytes=($entries|Measure-Object bytes -Sum).Sum;entries=$entries;scope='new/changed completed local receipts, final failed SSH observation and direct TCP probe, scripts and stage review; previous evidence unchanged';original_inventory_sha256=(Get-FileHash -LiteralPath (Join-Path $base 'inventory.json')).Hash;supplement_inventory_sha256=(Get-FileHash -LiteralPath (Join-Path $base 'supplement/inventory.json')).Hash;limitation='this sealing invocation plus later exact index/commit and final process closure receipts remain in local task root; no false circular claim of committing own receipt'}
[IO.File]::WriteAllText((Join-Path $destination 'inventory.json'),($inventory|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $task 'closure-archive-summary.json'),(@{utc=$inventory.utc;files=$inventory.files;bytes=$inventory.bytes;inventory_sha256=(Get-FileHash -LiteralPath (Join-Path $destination 'inventory.json')).Hash}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Output "closure_files=$($entries.Count) bytes=$($inventory.bytes)"
