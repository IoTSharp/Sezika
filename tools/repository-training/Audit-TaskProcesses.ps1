param([Parameter(Mandatory)][string[]]$LogDirectories,[Parameter(Mandatory)][string]$Output)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required.'}
if($LogDirectories.Count -gt 12 -or (Test-Path -LiteralPath $Output)){throw 'Audit directory count12/fresh output required.'}
$watch=[Diagnostics.Stopwatch]::StartNew();$records=[Collections.Generic.List[object]]::new();$historicalIncomplete=0;$receipts=0
function Guard{if($watch.Elapsed.TotalSeconds -ge 90){throw 'Process audit deadline90s.'}}
foreach($directory in $LogDirectories){
    Guard;$files=@(Get-ChildItem -LiteralPath $directory -File -Filter '*.result.json'|Select-Object -First 257);if($files.Count -gt 256){throw 'Receipt count bound256 per directory.'}
    foreach($file in $files){
        Guard;$receipt=Get-Content -LiteralPath $file.FullName -Raw|ConvertFrom-Json;$receipts++
        foreach($process in @($receipt.Processes)){Guard;if($records.Count -ge 2048){throw 'Recorded identity bound2048.'};$records.Add($process)}
        if(@($receipt.Processes).Count -eq 0 -and $receipt.Pid){$historicalIncomplete++;$records.Add([pscustomobject]@{Pid=$receipt.Pid;Started=$receipt.Started;ParentPid=$receipt.ParentPid;CommandLine=$null})}
    }
}
$pids=@($records|ForEach-Object {[int]$_.Pid}|Sort-Object -Unique);if($pids.Count -gt 1024){throw 'PID bound1024.'}
$actual=@{};$queries=0
for($offset=0;$offset -lt $pids.Count;$offset+=32){
    Guard;$queries++;if($queries -gt 32){throw 'CIM query count32.'};$batch=@($pids|Select-Object -Skip $offset -First 32);$filter=($batch|ForEach-Object {'ProcessId='+$_}) -join ' OR '
    foreach($process in @(Get-CimInstance Win32_Process -Filter $filter -Property ProcessId,ParentProcessId,CreationDate,CommandLine -OperationTimeoutSec 5|Select-Object -First 33)){Guard;$actual[[int]$process.ProcessId]=$process}
}
$live=[Collections.Generic.List[object]]::new();$unknown=[Collections.Generic.List[object]]::new();$absent=0;$reused=0
foreach($record in $records){
    Guard;$process=$actual[[int]$record.Pid];if($null -eq $process){$absent++;continue}
    $expected=([DateTime]$record.Started).ToUniversalTime();$started=$process.CreationDate.ToUniversalTime()
    if([Math]::Abs(($expected-$started).Ticks) -gt 10000){$reused++;continue}
    if(-not $record.CommandLine){$unknown.Add($record);continue}
    if($record.ParentPid -eq $process.ParentProcessId -and $record.CommandLine -ceq $process.CommandLine){$live.Add($record)}else{$unknown.Add($record)}
}
$result=[ordered]@{status=if($live.Count -eq 0 -and $unknown.Count -eq 0){'PASS_current_recorded_task_identities_absent_or_reused'}else{'FAIL_current_live_or_unknown'};receipts=$receipts;identity_records=$records.Count;unique_pids=$pids.Count;matching_live=$live.Count;unknown=$unknown.Count;absent_records=$absent;reused_records=$reused;historically_unobserved_root_receipts=$historicalIncomplete;historical_incomplete_receipts_preserved=$true;queries=$queries;elapsed_seconds=$watch.Elapsed.TotalSeconds;live=$live.ToArray();unknown_identities=$unknown.ToArray();termination_performed=$false}
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Output),($result|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false));[pscustomobject]$result
if($live.Count -or $unknown.Count){throw 'Audit is incomplete; no process was terminated.'}
