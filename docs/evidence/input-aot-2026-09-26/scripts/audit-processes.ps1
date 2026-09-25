$ErrorActionPreference = 'Stop'
$taskRoot = 'D:\source\Sezika\.artifacts\s3-aot-20260926'
$watch = [Diagnostics.Stopwatch]::StartNew()
$results = @(Get-ChildItem -LiteralPath 'D:\source\Sezika\.artifacts\processes' -File -Filter '*s3-aot*.result.json')
if ($results.Count -gt 100) { throw 'Result count bound exceeded.' }
$snapshot = @(Get-CimInstance Win32_Process -Property ProcessId,ParentProcessId,CreationDate,CommandLine -OperationTimeoutSec 5 | Select-Object -First 16385)
if ($snapshot.Count -gt 16384) { throw 'Process snapshot bound exceeded.' }
$byPid = @{}
foreach ($item in $snapshot) {
    if ($watch.Elapsed.TotalSeconds -ge 30) { throw 'Audit deadline.' }
    $byPid[[int]$item.ProcessId] = $item
}
$live = [Collections.Generic.List[object]]::new()
$cleanupErrors = [Collections.Generic.List[object]]::new()
$count = 0
foreach ($file in $results) {
    if ($watch.Elapsed.TotalSeconds -ge 30) { throw 'Audit deadline.' }
    $run = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8 | ConvertFrom-Json
    if ($run.CleanupErrors.Count) { $cleanupErrors.Add(@{path=$file.Name;errors=$run.CleanupErrors}) }
    if ($run.Processes.Count -gt 2048) { throw 'Process-tree bound exceeded.' }
    foreach ($known in $run.Processes) {
        if ($watch.Elapsed.TotalSeconds -ge 30) { throw 'Audit deadline.' }
        $count++
        $current = $byPid[[int]$known.Pid]
        if ($current -and $current.CreationDate -eq $known.CreationDate -and
            $current.ParentProcessId -eq $known.ParentPid -and $current.CommandLine -ceq $known.CommandLine) { $live.Add($known) }
    }
}
[ordered]@{captured_utc=[DateTimeOffset]::UtcNow;maximum_results=100;timeout_seconds=30;results=$results.Count;
    recorded_processes_checked=$count;live_matching_processes=$live.ToArray();runner_cleanup_errors=$cleanupErrors.ToArray();
    scope='Read-only audit of recorded Windows task process identities. Linux groups have separate supervisor evidence. No termination by executable name.'} |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskRoot 'windows-cleanup-audit.json') -Encoding utf8NoBOM
if ($live.Count -or $cleanupErrors.Count) { throw 'Recorded process or cleanup issue remains.' }
Write-Output "Verified $count recorded Windows process identities across $($results.Count) runs; none live."
