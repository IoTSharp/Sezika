#requires -Version 7.0
param([ValidateSet('tiny','inspect','archive')][string]$Mode='inspect',[string]$ActiveLogName='permanent-evidence-archive-v4')
$ErrorActionPreference='Stop'
$task=[IO.Path]::GetFullPath($PSScriptRoot)
$destination='D:\source\Sezika\docs\evidence\token-limit-20261009-8192-final'
$clock=[Diagnostics.Stopwatch]::StartNew()
function Check { if($clock.Elapsed.TotalSeconds -ge 150 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'archive deadline/cancellation'} }
function Enumerate([string]$Root,[int]$MaxFiles=4096){
 $files=[Collections.Generic.List[object]]::new()
 $stack=[Collections.Generic.Stack[object]]::new()
 $stack.Push(@{path=$Root;depth=0})
 for($directoryCount=0;$directoryCount -lt 1024 -and $stack.Count -gt 0;$directoryCount++){
  Check
  $entry=$stack.Pop()
  $children=@(Get-ChildItem -LiteralPath $entry.path -Force | Select-Object -First 513)
  if($children.Count -gt 512){throw 'directory entry cap512'}
  foreach($child in $children){
   Check
   if(($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'reparse point retained; no traversal'}
   if($child.PSIsContainer){
    if($entry.depth -ge 16){throw 'depth cap16'}
    $stack.Push(@{path=$child.FullName;depth=$entry.depth+1})
   }else{
    $files.Add($child)
    if($files.Count -ge $MaxFiles){
     if($Mode -eq 'tiny'){return ,$files.ToArray()}
     throw 'file cap4096'
    }
   }
  }
 }
 if($stack.Count -gt 0){throw 'directory cap1024'}
 return ,$files.ToArray()
}
$files=Enumerate $task $(if($Mode -eq 'tiny'){1}else{4096})
if($Mode -eq 'tiny'){Write-Output "tiny_files=$($files.Count)";exit 0}
$items=@(foreach($f in $files){Check;[pscustomobject]@{path=[IO.Path]::GetRelativePath($task,$f.FullName).Replace('\','/');bytes=$f.Length}})
[IO.File]::WriteAllText((Join-Path $task 'task-file-list.json'),($items | ConvertTo-Json -Depth 3),[Text.UTF8Encoding]::new($false))
Write-Output "file_count=$($files.Count) total_bytes=$(($files | Measure-Object Length -Sum).Sum)"
if($Mode -eq 'inspect'){
 $items | Group-Object {($_.path -split '/')[0]} | Select-Object Name,Count | Format-Table | Out-Host
 exit 0
}
if([IO.Directory]::Exists($destination)){throw 'archive destination already exists; review before reuse'}
[IO.Directory]::CreateDirectory($destination) | Out-Null
$copied=[Collections.Generic.List[object]]::new()
$omitted=[Collections.Generic.List[object]]::new()
for($i=0;$i -lt $files.Count -and $i -lt 4096;$i++){
 Check
 $f=$files[$i]
 $f.Refresh()
 $relative=[IO.Path]::GetRelativePath($task,$f.FullName).Replace('\','/')
 $include=$false
 if($relative -notmatch '/' -and $relative -notmatch '\.tar$'){$include=$true}
 if($relative -match '^(process-logs|remote|retrieved|cpu-boundary|profile|compiler-temp-retained)/'){$include=$true}
 if($relative -match '^(boundary-check|capacity-check|regression-check)/' -and $relative -notmatch '/(bin|obj)/' -and $relative -match '\.(cs|csproj)$'){$include=$true}
 if($relative -match '(^|/)(bin|obj)/' -or $relative -match '\.(dll|pdb|exe|so)(\.[0-9]+)*$'){$include=$false}
 if($relative -match '/(Sezika.Tests|Sezika.ExperimentTrainer|BoundaryCheck|CapacityCheck|RegressionCheck)$'){$include=$false}
 if($relative.StartsWith('process-logs/') -and $relative.Contains('-'+$ActiveLogName+'.')){
  $omitted.Add([pscustomobject]@{path=$relative;bytes=$null;sha256=$null;reason='active own archive log; final SHA and receipt recorded in closure supplement after exit'})
  continue
 }
 if($include){
  $target=[IO.Path]::GetFullPath((Join-Path $destination $relative))
  if(-not $target.StartsWith($destination+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'archive path escaped'}
  $hash=(Get-FileHash -LiteralPath $f.FullName).Hash
  [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
  [IO.File]::Copy($f.FullName,$target,$false)
  if((Get-FileHash -LiteralPath $target).Hash -cne $hash -or ([IO.FileInfo]$target).Length -ne $f.Length){throw "archive byte verification failed: $relative"}
  $copied.Add([pscustomobject]@{path=$relative;bytes=$f.Length;sha256=$hash})
 }else{
  $reason=if($relative -match '\.tar$'){'complete local tar retained; no duplicate tar in Git'}elseif($relative -match '\.(dll|pdb|exe|so)(\.[0-9]+)*$' -or $relative -match '(^|/)(bin|obj)/'){'build binary/cache retained locally'}else{'transfer/build duplicate or task temp retained locally'}
  if($f.Length -gt 200000000){throw 'hash per-file byte cap200MB'}
  $omitted.Add([pscustomobject]@{path=$relative;bytes=$f.Length;sha256=(Get-FileHash -LiteralPath $f.FullName).Hash;reason=$reason})
 }
 if($i % 100 -eq 0){Write-Output "archive item=$i/$($files.Count) copied=$($copied.Count)"}
}
$record=@{utc=[DateTime]::UtcNow.ToString('o');source=$task;destination=$destination;files=$copied.Count;bytes=($copied | Measure-Object bytes -Sum).Sum;entries=$copied;omitted=$omitted;scope='byte-identical evidence/scripts/sources/reports/head/checkpoint; explicit omitted SHA inventory; complete evidence-bundle.tar retained at source';limitation='archive runner output/receipt is recorded after this snapshot and kept in a separate closure supplement'}
[IO.File]::WriteAllText((Join-Path $destination 'inventory.json'),($record | ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $task 'archive-summary.json'),(@{files=$copied.Count;bytes=$record.bytes;omitted=$omitted.Count;destination=$destination;inventory_sha256=(Get-FileHash -LiteralPath (Join-Path $destination 'inventory.json')).Hash} | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Output "archived=$($copied.Count) bytes=$($record.bytes) omitted=$($omitted.Count)"
