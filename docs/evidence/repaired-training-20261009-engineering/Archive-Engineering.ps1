#requires -Version 7.0
param([string]$ActiveLogName='permanent-engineering-archive-fixed')
$ErrorActionPreference='Stop'
$task='D:\source\Sezika\.artifacts\repaired-training-20261009'
$destination='D:\source\Sezika\docs\evidence\repaired-training-20261009-engineering'
$clock=[Diagnostics.Stopwatch]::StartNew()
$files=[Collections.Generic.List[object]]::new()
function Check {if($clock.Elapsed.TotalSeconds -ge 70 -or $files.Count -gt 320 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'Archive cancelled/time/file bound'}}
if(Test-Path -LiteralPath $destination){throw 'Fresh permanent archive required'}
function Add([string]$Path,[string]$Relative){Check;$item=Get-Item -LiteralPath $Path;if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $item.Length -gt 5MB){throw 'Archive file scope/size'};$files.Add(@{path=$item.FullName;relative=$Relative;bytes=$item.Length;sha256=(Get-FileHash -LiteralPath $item.FullName).Hash})}
$top=@(Get-ChildItem -LiteralPath $task -File|Where-Object {$_.Extension -cin @('.json','.ps1','.txt')}|Select-Object -First 61)
if($top.Count -gt 60){throw 'Top evidence cap60'}
foreach($item in $top){Check;Add $item.FullName $item.Name}
foreach($relative in @('process-logs','audit-v2-git-logs','compiler-log-evidence','remote','audit-length-reference/process-logs')){
 Check;$directory=Join-Path $task $relative;if(-not (Test-Path -LiteralPath $directory)){continue}
 $entries=@(Get-ChildItem -LiteralPath $directory -File|Select-Object -First 161)
 if($entries.Count -gt 160){throw 'Evidence subdirectory cap160'}
 foreach($item in $entries){Check;if($relative -eq 'process-logs' -and $item.Name.Contains('-'+$ActiveLogName+'.')){continue};Add $item.FullName ($relative+'/'+$item.Name)}
}
foreach($relative in @('capacity-check/Program.cs','capacity-check/CapacityCheck.csproj','regression-check/Program.cs','regression-check/RegressionCheck.csproj','audit-length-reference/Program.cs','audit-length-reference/AuditLengthReference.csproj')){Check;$path=Join-Path $task $relative;if(Test-Path -LiteralPath $path){Add $path $relative}}
[void][IO.Directory]::CreateDirectory($destination)
$total=0L
foreach($item in $files){
 Check;$total+=$item.bytes;if($total -gt 20MB){throw 'Permanent evidence byte cap20MiB'}
 $target=[IO.Path]::GetFullPath((Join-Path $destination $item.relative))
 if(-not $target.StartsWith($destination+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Archive destination escaped root'}
 [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target));[IO.File]::Copy($item.path,$target,$false)
 if((Get-FileHash -LiteralPath $target).Hash -cne $item.sha256){throw 'Permanent archive hash differs'}
}
[IO.File]::WriteAllText((Join-Path $destination 'inventory.json'),(@{utc=[DateTime]::UtcNow.ToString('o');thread='01a11f25-335a-7571-a735-7171ace8c3b9';files=$files.Count;bytes=$total;scope='new engineering/check/length/build/process evidence; immutable teacher215 original archive remains separate; no model/encoder/runtime binaries copied';records=@($files.ToArray())}|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
Write-Output "permanent_engineering_archive_files=$($files.Count) bytes=$total"
