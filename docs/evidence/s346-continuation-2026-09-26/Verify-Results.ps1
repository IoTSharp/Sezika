param([switch]$RequireFinal, [Parameter(Mandatory)][string]$OutputPath,
    [ValidateRange(1,3)][int]$MaxDatasets=1, [ValidateRange(1,1000)][int]$MaxProcessResults=1)
$ErrorActionPreference='Stop'
$watch=[Diagnostics.Stopwatch]::StartNew()
$taskRoot=[IO.Path]::GetFullPath($PSScriptRoot)
function Read-Report([string]$path){
    if($watch.Elapsed.TotalSeconds -ge 60){throw 'Verification deadline exceeded.'}
    if((Get-Item -LiteralPath $path).Length -gt 8388608){throw 'Report exceeds 8 MiB.'}
    Get-Content -LiteralPath $path -Raw -Encoding utf8 | ConvertFrom-Json
}
$datasets=[Collections.Generic.List[object]]::new()
foreach($spec in @(@{Name='paws-final';Total=250},@{Name='nimble-final';Total=324},@{Name='language-final';Total=12})[0..($MaxDatasets-1)]){
    $path=Join-Path $taskRoot "$($spec.Name)/run.json"
    if(-not (Test-Path -LiteralPath $path)){if($RequireFinal){throw "Missing $path"};continue}
    $run=Read-Report $path
    if($run.status -ne 'selected_batches_complete' -or $run.comparisons.Count -gt 8){throw 'Final workflow did not complete without numerical differences.'}
    $rows=0;$issues=0;$nearTies=0;$maxLogit=0.0;$maxProbability=0.0
    foreach($entry in $run.comparisons){
        if((Get-FileHash -LiteralPath $entry.path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $entry.sha256){throw 'Comparison hash differs.'}
        $comparison=Read-Report $entry.path
        if(-not $comparison.full_manifest_passed){throw 'Comparison failed.'}
        $rows+=$comparison.compared_count;$issues+=$comparison.issues.Count;$nearTies+=$comparison.near_tie_cases
        $maxLogit=[Math]::Max($maxLogit,$comparison.max_logit_error)
        $maxProbability=[Math]::Max($maxProbability,$comparison.max_probability_error)
    }
    if($rows -ne $spec.Total -or $issues -ne 0){throw 'Final dataset coverage differs.'}
    $datasets.Add([ordered]@{name=$spec.Name;compared=$rows;issues=$issues;near_ties=$nearTies;max_logit_error=$maxLogit;max_probability_error=$maxProbability})
}
$trace=Read-Report (Join-Path $taskRoot 'trace-complete-summary.json')
if($trace.passed_backend_cells -ne 54 -or $trace.incomplete_runs -ne 1 -or $trace.full_s306_gate_passed){throw 'Trace summary boundary changed.'}
$resultDirs=@('processes','paws-full/processes','nimble-full/processes','nimble-remaining/processes','language-full/processes','language-smoke/processes','language-final-smoke/processes','paws-final/processes','nimble-final/processes','language-final/processes','aggregate-duplicate-logs','aggregate-wrong-hash-logs')
$runs=[Collections.Generic.List[object]]::new()
$live=@(Get-CimInstance Win32_Process -OperationTimeoutSec 3)
foreach($relative in $resultDirs){
    $dir=Join-Path $taskRoot $relative
    if(-not(Test-Path -LiteralPath $dir)){continue}
    $files=@(Get-ChildItem -LiteralPath $dir -Filter '*.result.json' -File | Select-Object -First 401)
    if($files.Count -gt 400){throw 'Too many runner results.'}
    foreach($file in $files){
        if($runs.Count -ge $MaxProcessResults){break}
        $run=Read-Report $file.FullName
        if($run.CleanupErrors.Count -ne 0){throw "Cleanup errors in $($file.Name)"}
        if($run.Processes.Count -gt 256){throw 'Too many descendants in one result.'}
        foreach($process in @($run.Processes | Select-Object -First 257)){
            if(@($live | Where-Object { $_.ProcessId -eq $process.Pid -and [Math]::Abs(($_.CreationDate.ToUniversalTime()-([datetimeoffset]$process.Started).UtcDateTime).TotalSeconds) -lt 0.01 }).Count){throw "Task-owned process still live: $($process.Pid)"}
        }
        $runs.Add([ordered]@{path=[IO.Path]::GetRelativePath($taskRoot,$file.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant();status=$run.Status;exit_code=$run.ExitCode;pid=$run.Pid;started=$run.Started;elapsed_seconds=$run.ElapsedSeconds;timeout_seconds=$run.TimeoutSeconds;cleanup_errors=$run.CleanupErrors.Count})
    }
}
$result=[ordered]@{schema_version='sezika.continuation-validation.v1';datasets=$datasets.ToArray();trace_passed_cells=$trace.passed_backend_cells;trace_incomplete_runs=$trace.incomplete_runs;owned_processes_live=0;process_results=$runs.Count;failed_results=@($runs | Where-Object status -ne 'Succeeded').Count;executions=$runs.ToArray();scope='Failed and timeout executions remain individually listed; read narrative for expected negatives and repaired failures. No publication/AOT claim.'}
$output=[IO.Path]::GetFullPath($OutputPath)
if([IO.Path]::Exists($output)){throw 'Output must be new.'}
[IO.File]::WriteAllText($output,($result | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
Write-Output "Verified $($datasets.Count) final datasets and $($runs.Count) completed process results."
