#requires -Version 7.0
$ErrorActionPreference='Stop'
$task='D:\source\Sezika\.artifacts\repaired-training-20261009'
$clock=[Diagnostics.Stopwatch]::StartNew()
$self=Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -OperationTimeoutSec 2
$known=[Collections.Generic.List[object]]::new()
$receipts=0
function Check {if($clock.Elapsed.TotalSeconds -ge 40 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'Closure cancelled/deadline'}}
function Add-Expected($Id,$Started,$Argv,$Parent,$Source){
 Check
 if($null -ne $Id -and [int]$Id -gt 0 -and $null -ne $Started){$known.Add(@{pid=[int]$Id;started=([DateTime]$Started).ToUniversalTime();argv=$Argv;parent=$Parent;source=$Source})}
 if($known.Count -gt 500){throw 'Registered identities cap500'}
}
foreach($relative in @('process-logs','audit-length-reference/process-logs','audit-v2-git-logs')){
 Check;$directory=Join-Path $task $relative
 if(-not (Test-Path -LiteralPath $directory)){continue}
 $files=@(Get-ChildItem -LiteralPath $directory -File -Filter '*.result.json'|Select-Object -First 61)
 if($files.Count -gt 60){throw 'Receipt item cap60'}
 foreach($file in $files){
  Check;$value=Get-Content -Raw -LiteralPath $file.FullName|ConvertFrom-Json
  $receipts++
  Add-Expected $value.Pid $value.Started ($value.FilePath+' '+($value.Arguments -join ' ')) $value.ParentPid $file.FullName
  Add-Expected $value.Launcher.Pid $value.Launcher.Started $value.Launcher.CommandLine $value.Launcher.ParentPid ($file.FullName+'#launcher')
  foreach($item in @($value.Processes)|Select-Object -First 65){Check;Add-Expected $item.Pid $item.Started $item.CommandLine $item.ParentPid ($file.FullName+'#observed')}
 }
}
foreach($name in @('audit-data-validation.json','audit-git-bytes.json','audit-git-bytes-probe.json','audit-execution-transfer-review.json','audit-budget-helpers-review.json','audit-length-block.json','short-process-followup.json')){
 Check;$path=Join-Path $task $name
 if(-not (Test-Path -LiteralPath $path)){continue}
 $value=Get-Content -Raw -LiteralPath $path|ConvertFrom-Json
 foreach($key in @('reader','observer')){Check;$item=$value.$key;if($item){Add-Expected $item.ProcessId $item.CreationDate $item.CommandLine $item.ParentProcessId ($path+'#'+$key)}}
 if($value.git_child){$item=$value.git_child;Add-Expected $item.pid $item.started ($item.arguments -join ' ') $item.parent_pid ($path+'#git_child')}
}
$ids=@($known.pid|Sort-Object -Unique)
if($ids.Count -gt 180){throw 'Unique PIDs cap180'}
$filter=($ids|ForEach-Object {'ProcessId='+$_}) -join ' OR '
Check
$actual=if($ids.Count -gt 0){@(Get-CimInstance Win32_Process -Filter $filter -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 5)}else{@()}
$byPid=@{};foreach($item in $actual){Check;$byPid[[int]$item.ProcessId]=$item}
$rows=@(foreach($item in $known){
 Check;$process=$byPid[$item.pid]
 $state=if($item.pid -eq $PID){'current_closure_reader_excluded'}elseif(-not $process){'absent'}elseif([Math]::Abs(($process.CreationDate.ToUniversalTime()-$item.started).Ticks) -gt 10){'reused'}else{'matching_start_live'}
 @{pid=$item.pid;started=$item.started;source=$item.source;state=$state;recorded_argv_present=(-not [string]::IsNullOrWhiteSpace($item.argv))}
})
$live=@($rows|Where-Object state -eq 'matching_start_live')
$out=Join-Path $task 'local-process-closure.json'
if(Test-Path -LiteralPath $out){throw 'Preserve existing closure'}
[IO.File]::WriteAllText($out,(@{utc=[DateTime]::UtcNow.ToString('o');receipts=$receipts;records=$known.Count;unique_pids=$ids.Count;matching_live=$live.Count;unknown_current=0;scope='exact current-task registered PID/start identities only; no signals';rows=$rows;reader=$self|Select-Object ProcessId,CreationDate,CommandLine,ParentProcessId;limitations=@('Original guardian unknown/short OS argv gaps retained; spawn argv is not an observed OS argv.','Some short read-only root tool commands were not contemporaneously captured with OS PID/start; tool exits/argv are retained but complete historical process coverage is not asserted.','Closure reader remains in scope until its actual tool exit and subsequent independent check.')}|ConvertTo-Json -Depth 7),[Text.UTF8Encoding]::new($false))
Write-Output "receipts=$receipts records=$($known.Count) pids=$($ids.Count) matching_live=$($live.Count) closure_reader=$PID"
if($live.Count -gt 0){throw 'Exact registered task process remains live; preserve and inspect'}
