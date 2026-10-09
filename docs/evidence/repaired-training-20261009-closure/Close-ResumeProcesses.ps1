#requires -Version 7.0
param(
    [ValidateRange(1,64)][int]$MaximumReceiptsPerDirectory = 64,
    [Parameter(Mandatory)][string]$OutputPath,
    [switch]$Probe
)
$ErrorActionPreference = 'Stop'
$task = 'D:\source\Sezika\.artifacts\repaired-training-20261009'
$destination = [IO.Path]::GetFullPath($OutputPath)
if (-not $destination.StartsWith($task + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must stay within the owned task root.' }
if ([IO.File]::Exists($destination)) { throw 'Preserve previous evidence.' }
$watch = [Diagnostics.Stopwatch]::StartNew()
function Check-Bound {
    if ($watch.Elapsed.TotalSeconds -ge 40 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))) { throw 'Closure cancelled or exceeded 40 seconds.' }
}
$self = Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 2 | Select-Object ProcessId,CreationDate,CommandLine,ParentProcessId
$known = [Collections.Generic.List[object]]::new()
$sources = [Collections.Generic.List[object]]::new()
function Add-Expected($Id, $Started, $Argv, $Parent, $Source) {
    Check-Bound
    if ($null -ne $Id -and [int]$Id -gt 0 -and $null -ne $Started) {
        $known.Add(@{pid=[int]$Id;started=([DateTime]$Started).ToUniversalTime();argv=$Argv;parent=$Parent;source=$Source})
    }
    if ($known.Count -gt 750) { throw 'Registered identity cap750 exceeded.' }
}
# The finite three-directory/64-receipt/65-process bounds are checked before full execution.
foreach ($relative in @('process-logs','audit-length-reference/process-logs','audit-v2-git-logs')) {
    Check-Bound
    $directory = Join-Path $task $relative
    if (-not [IO.Directory]::Exists($directory)) { continue }
    $files = @(Get-ChildItem -LiteralPath $directory -File -Filter '*.result.json' | Select-Object -First ($MaximumReceiptsPerDirectory + 1))
    if (-not $Probe -and $files.Count -gt $MaximumReceiptsPerDirectory) { throw 'Receipt cap exceeded; no truncated full closure.' }
    foreach ($file in ($files | Select-Object -First $MaximumReceiptsPerDirectory)) {
        Check-Bound
        $value = [IO.File]::ReadAllText($file.FullName) | ConvertFrom-Json
        $sources.Add(@{path=$file.FullName;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash;status=$value.Status;actual_exit=$value.ExitCode})
        Add-Expected $value.Pid $value.Started ($value.FilePath+' '+($value.Arguments -join ' ')) $value.ParentPid $file.FullName
        Add-Expected $value.Launcher.Pid $value.Launcher.Started $value.Launcher.CommandLine $value.Launcher.ParentPid ($file.FullName+'#launcher')
        $processes = @($value.Processes | Select-Object -First 65)
        if ($processes.Count -gt 64) { throw 'Receipt process cap64 exceeded.' }
        foreach ($item in $processes) { Check-Bound; Add-Expected $item.Pid $item.Started $item.CommandLine $item.ParentPid ($file.FullName+'#observed') }
    }
}
foreach ($name in @('audit-data-validation.json','audit-git-bytes.json','audit-git-bytes-probe.json','audit-execution-transfer-review.json','audit-budget-helpers-review.json','audit-length-block.json','short-process-followup.json','closure-field-correction.json','resume-precommit-closure.json','resume-closure-probe.json','resume-postcommit-closure.json','resume-doc-review.json','resume-preservation.json')) {
    Check-Bound
    $path = Join-Path $task $name
    if (-not [IO.File]::Exists($path)) { continue }
    $value = [IO.File]::ReadAllText($path) | ConvertFrom-Json
    foreach ($key in @('reader','observer')) {
        Check-Bound
        $item = $value.$key
        if ($item) { Add-Expected $item.ProcessId $item.CreationDate $item.CommandLine $item.ParentProcessId ($path+'#'+$key) }
    }
    if ($value.closure_reader_pid -and $name -eq 'closure-field-correction.json') {
        $original = [IO.File]::ReadAllText((Join-Path $task 'local-process-closure.json')) | ConvertFrom-Json
        $item = $original.reader
        Add-Expected $item.ProcessId $item.CreationDate $item.CommandLine $item.ParentProcessId ($path+'#original-reader')
    }
    if ($value.git_child) { $item=$value.git_child; Add-Expected $item.pid $item.started ($item.arguments -join ' ') $item.parent_pid ($path+'#git_child') }
}
$ids = @($known.pid | Sort-Object -Unique)
if ($ids.Count -gt 280) { throw 'Unique PID cap280 exceeded.' }
$filter = ($ids | ForEach-Object { 'ProcessId=' + $_ }) -join ' OR '
Check-Bound
$actual = if ($ids.Count -gt 0) { @(Get-CimInstance Win32_Process -Filter $filter -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 5 | Select-Object -First 281) } else { @() }
if ($actual.Count -gt 280) { throw 'Unexpected process result cap.' }
$byPid = @{}
foreach ($item in $actual) { Check-Bound; $byPid[[int]$item.ProcessId]=$item }
$rows = @(foreach ($item in $known) {
    Check-Bound
    $process = $byPid[$item.pid]
    $state = if ($item.pid -eq $PID) { 'current_reader_excluded' } elseif (-not $process) { 'absent' } elseif ([Math]::Abs(($process.CreationDate.ToUniversalTime()-$item.started).Ticks) -gt 10) { 'reused' } else { 'matching_start_live' }
    @{pid=$item.pid;started=$item.started;source=$item.source;state=$state;recorded_argv_present=(-not [string]::IsNullOrWhiteSpace($item.argv));live_argv_matches=if($state -eq 'matching_start_live'){$process.CommandLine -ceq $item.argv}else{$null};live_parent_matches=if($state -eq 'matching_start_live'){$process.ParentProcessId -eq $item.parent}else{$null}}
})
$live = @($rows | Where-Object state -eq 'matching_start_live')
Check-Bound
$report = @{utc=[DateTime]::UtcNow.ToString('o');probe=[bool]$Probe;receipts=$sources.Count;records=$known.Count;unique_pids=$ids.Count;matching_start_live=$live.Count;current_reader_excluded=@($rows|Where-Object state -eq 'current_reader_excluded').Count;scope='Only registered task PID/start identities; retained spawn argv is not historical OS observation; no process signals.';reader=$self;receipt_sources=$sources;rows=$rows;bounds=@{seconds=40;directories=3;receipts_per_directory=$MaximumReceiptsPerDirectory;identities=750;unique_pids=280};limitations=@('Short read-only commands lacking contemporaneous OS identities remain outside this ledger.','No claim that exited parents had no never-observed descendants.','Current reader requires actual tool exit and a later independent absence check.')}
[IO.File]::WriteAllText($destination, ($report | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
Write-Output "receipts=$($sources.Count) records=$($known.Count) pids=$($ids.Count) matching_start_live=$($live.Count) reader=$PID"
if ($live.Count -gt 0) { throw 'Registered matching-start process is still live; inspect exact ownership without signalling.' }
