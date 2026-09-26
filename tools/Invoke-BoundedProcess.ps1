param(
    [Parameter(Mandatory)][string]$FilePath,
    [string[]]$ArgumentList = @(),
    [ValidateRange(1,1800)][int]$TimeoutSeconds = 180,
    [string]$LogName = 'process',
    [string]$WorkingDirectory,
    [string]$LogDirectory,
    [hashtable]$Environment = @{}
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or newer is required.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$logs = if ($LogDirectory) { [IO.Path]::GetFullPath($LogDirectory) } else { Join-Path $root '.artifacts/processes' }
[IO.Directory]::CreateDirectory($logs) | Out-Null
$label = ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')) + '-' + ($LogName -replace '[^a-zA-Z0-9_-]', '_')
$stdoutPath = Join-Path $logs "$label.stdout.log"
$stderrPath = Join-Path $logs "$label.stderr.log"
$resultPath = Join-Path $logs "$label.result.json"
$identityPath = Join-Path $logs "$label.identity.json"
$start = [Diagnostics.ProcessStartInfo]::new($FilePath)
$start.WorkingDirectory = if ($WorkingDirectory) { [IO.Path]::GetFullPath($WorkingDirectory) } else { $root }
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER'] = '1'
$start.Environment['MSBUILDDISABLENODEREUSE'] = '1'
$start.Environment['UseSharedCompilation'] = 'false'
if ($ArgumentList.Count -gt 4096 -or $Environment.Count -gt 256) { throw 'Argument/environment item limit exceeded.' }
$setupWatch = [Diagnostics.Stopwatch]::StartNew()
foreach ($arg in $ArgumentList) {
    if ($setupWatch.Elapsed.TotalSeconds -ge 5) { throw 'Argument setup timeout.' }
    $start.ArgumentList.Add($arg)
}
foreach ($key in $Environment.Keys) {
    if ($setupWatch.Elapsed.TotalSeconds -ge 5) { throw 'Environment setup timeout.' }
    if ($null -eq $Environment[$key]) { [void]$start.Environment.Remove([string]$key) }
    else { $start.Environment[[string]$key] = [string]$Environment[$key] }
}

# Windows descendants retain their recorded parent IDs even if the parent exits.
# Remember identities while the tree is alive; never terminate by executable name.
$tracked = @{}
$cleanupErrors = [Collections.Generic.List[string]]::new()
function Test-SameIdentity($Expected, $Actual) {
    return $null -ne $Actual -and $Expected.Pid -eq $Actual.ProcessId -and
        $Expected.CreationDate -eq $Actual.CreationDate -and
        $Expected.ParentPid -eq $Actual.ParentProcessId -and
        $Expected.CommandLine -ceq $Actual.CommandLine
}
function Add-Identity($NativeProcess, [int]$Depth) {
    $tracked[[int]$NativeProcess.ProcessId] = [pscustomobject]@{
        Pid = [int]$NativeProcess.ProcessId
        CreationDate = $NativeProcess.CreationDate
        Started = $NativeProcess.CreationDate.ToUniversalTime()
        CommandLine = $NativeProcess.CommandLine
        ParentPid = [int]$NativeProcess.ParentProcessId
        Depth = $Depth
        TerminatedByRunner = $false
    }
}
function Update-Descendants {
    $scanWatch = [Diagnostics.Stopwatch]::StartNew()
    $snapshot = @(Get-CimInstance Win32_Process -Property ProcessId,ParentProcessId,CreationDate,CommandLine -OperationTimeoutSec 2 | Select-Object -First 16385)
    if ($snapshot.Count -gt 16384) { throw 'Process snapshot item limit exceeded.' }
    $byPid = @{}
    $byParent = @{}
    foreach ($item in $snapshot) {
        if ($scanWatch.Elapsed.TotalSeconds -ge 5) { throw 'Process snapshot timeout.' }
        $byPid[[int]$item.ProcessId] = $item
        $parentKey = [int]$item.ParentProcessId
        if (-not $byParent.ContainsKey($parentKey)) { $byParent[$parentKey] = [Collections.Generic.List[object]]::new() }
        $byParent[$parentKey].Add($item)
    }
    $queue = [Collections.Generic.Queue[object]]::new()
    foreach ($known in @($tracked.Values)) {
        if ($scanWatch.Elapsed.TotalSeconds -ge 5) { throw 'Descendant discovery timeout.' }
        $queue.Enqueue($known)
    }
    # Both iteration and elapsed-time limits are independent of the queue contents.
    for ($index = 0; $index -lt 2048 -and $queue.Count -gt 0; $index++) {
        if ($scanWatch.Elapsed.TotalSeconds -ge 5) { throw 'Descendant discovery timeout.' }
        $parent = $queue.Dequeue()
        $currentParent = $byPid[$parent.Pid]
        if ($null -ne $currentParent -and -not (Test-SameIdentity $parent $currentParent)) { continue }
        foreach ($child in $byParent[$parent.Pid]) {
            if ($scanWatch.Elapsed.TotalSeconds -ge 5) { throw 'Descendant discovery timeout.' }
            if ($null -eq $child -or $child.CreationDate -lt $parent.CreationDate -or $tracked.ContainsKey([int]$child.ProcessId)) { continue }
            if ($tracked.Count -ge 2048) { throw 'Tracked descendant item limit exceeded.' }
            Add-Identity $child ($parent.Depth + 1)
            $queue.Enqueue($tracked[[int]$child.ProcessId])
        }
    }
    if ($queue.Count -gt 0) { throw 'Descendant discovery iteration limit exceeded.' }
    # Return the same snapshot for cleanup so historical, exited PIDs never
    # trigger individual CIM queries.
    return $byPid
}

$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
$watch = [Diagnostics.Stopwatch]::StartNew()
$started = $false
$rootStarted = $null
$taskPid = $null
$exitCode = $null
$failure = $null
$status = 'Cancelled'
$outStream = $null
$errStream = $null
$stdout = $null
$stderr = $null
$copyCancellation = [Threading.CancellationTokenSource]::new()
$launcherIdentity = Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -OperationTimeoutSec 2
$launcher = [pscustomobject]@{
    Pid=$PID; Started=$launcherIdentity.CreationDate; CommandLine=$launcherIdentity.CommandLine
    ParentPid=$launcherIdentity.ParentProcessId
}
try {
    # Copy directly into logs so partial output survives timeout or cancellation.
    $outStream = [IO.FileStream]::new($stdoutPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read, 1)
    $errStream = [IO.FileStream]::new($stderrPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read, 1)
    if (-not $process.Start()) { throw 'Process failed to start.' }
    $started = $true
    $taskPid = $process.Id
    $rootStarted = $process.StartTime.ToUniversalTime()
    $stdout = $process.StandardOutput.BaseStream.CopyToAsync($outStream, $copyCancellation.Token)
    $stderr = $process.StandardError.BaseStream.CopyToAsync($errStream, $copyCancellation.Token)
    # A short-lived child can exit while CIM is resolving its PID. Preserve the
    # retained Process handle's start/exit facts; never treat a live unobserved root as safe.
    $identity = $null
    try { $identity = Get-CimInstance Win32_Process -Filter "ProcessId=$taskPid" -OperationTimeoutSec 2 }
    catch { if (-not $process.HasExited) { throw } }
    if ($null -ne $identity) { Add-Identity $identity 0 }
    elseif (-not $process.HasExited) { throw 'Could not record the running root process identity.' }
    [IO.File]::WriteAllText($identityPath, ([pscustomobject]@{
        Pid=$taskPid; Started=$rootStarted; CommandLine=$identity.CommandLine
        IdentityObservation=if ($null -ne $identity) { 'cim_snapshot' } else { 'exited_before_cim_snapshot_see_recorded_argv' }
        ParentPid=$PID; ObservedParentPid=$identity.ParentProcessId
        ParentIdentitySource='Process.Start caller'; Launcher=$launcher
        TimeoutSeconds=$TimeoutSeconds; FilePath=$FilePath
        Arguments=$ArgumentList; WorkingDirectory=$start.WorkingDirectory
    } | ConvertTo-Json -Depth 4))
    Write-Output "Started PID $($process.Id), timeout ${TimeoutSeconds}s: $FilePath $($ArgumentList -join ' ')"
    $null = Update-Descendants
    $nextSnapshotSeconds = $watch.Elapsed.TotalSeconds + 3
    # At most 2*TimeoutSeconds+1 iterations, additionally bounded by elapsed time.
    # Root exit/deadline checks remain every 500 ms; full process snapshots run
    # at most once per three seconds, plus the initial and cleanup snapshots.
    for ($attempt=0; $attempt -le (2 * $TimeoutSeconds); $attempt++) {
        if ($process.HasExited) { break }
        if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
            $status = 'TimedOut'
            throw "Process timeout (${TimeoutSeconds}s)."
        }
        if ($watch.Elapsed.TotalSeconds -ge $nextSnapshotSeconds) {
            $null = Update-Descendants
            $nextSnapshotSeconds = $watch.Elapsed.TotalSeconds + 3
        }
        if (($attempt % 60) -eq 59) { Write-Output "PID $taskPid running: $([int]$watch.Elapsed.TotalSeconds)s" }
        Start-Sleep -Milliseconds 500
    }
    if (-not $process.HasExited) { throw 'Process iteration limit exceeded.' }
    $exitCode = $process.ExitCode
    if ($exitCode -ne 0) { throw "Process failed with exit code $exitCode." }
    $status = 'Succeeded'
} catch {
    $failure = $_.Exception.Message
    if ($status -ne 'TimedOut') { $status = 'Failed' }
} finally {
    $cleanupWatch = [Diagnostics.Stopwatch]::StartNew()
    # Six passes / eight seconds maximum. Check PID, creation time, command line,
    # and recorded parent chain before every individual termination.
    for ($pass = 0; $pass -lt 6 -and $cleanupWatch.Elapsed.TotalSeconds -lt 8; $pass++) {
        $currentProcesses = @{}
        try { if ($tracked.Count -gt 0) { $currentProcesses = Update-Descendants } }
        catch { $cleanupErrors.Add($_.Exception.Message) }
        $remaining = 0
        foreach ($known in @($tracked.Values | Sort-Object Depth -Descending)) {
            # Skip exited or reused PIDs using this pass's single snapshot.
            if (-not (Test-SameIdentity $known $currentProcesses[$known.Pid])) { continue }
            if ($cleanupWatch.Elapsed.TotalSeconds -ge 8) { $cleanupErrors.Add('Cleanup time limit reached.'); break }
            try {
                # Only a still-live, matching task process incurs this final
                # identity refresh immediately before opening its handle.
                $current = Get-CimInstance Win32_Process -Filter "ProcessId=$($known.Pid)" -OperationTimeoutSec 2
                if (-not (Test-SameIdentity $known $current)) { continue }
                $remaining++
                $childProcess = [Diagnostics.Process]::GetProcessById($known.Pid)
                try {
                    # CIM timestamps have microsecond precision; process handles
                    # preserve 100 ns ticks, so compare within one microsecond.
                    if ([Math]::Abs(($childProcess.StartTime.ToUniversalTime() - $known.Started).Ticks) -gt 10) { continue }
                    $childProcess.Kill()
                    $known.TerminatedByRunner = $true
                } finally { $childProcess.Dispose() }
            } catch {
                $cleanupErrors.Add("PID $($known.Pid): $($_.Exception.Message)")
            }
        }
        if ($remaining -eq 0) { break }
        Start-Sleep -Milliseconds 200
    }
    if ($remaining -gt 0 -and ($pass -ge 6 -or $cleanupWatch.Elapsed.TotalSeconds -ge 8)) {
        $cleanupErrors.Add('Cleanup ended before all recorded processes were confirmed exited.')
    }
    # The handle belongs to the process started above, even if CIM failed before
    # its identity could be recorded. Only this root handle is used as fallback.
    if ($started -and -not $process.HasExited) {
        try {
            if ($process.StartTime.ToUniversalTime() -eq $rootStarted) { $process.Kill(); [void]$process.WaitForExit(1000) }
        } catch { $cleanupErrors.Add("Root process cleanup: $($_.Exception.Message)") }
    }
    try {
        if ($null -ne $stdout -and $null -ne $stderr) {
            $copies = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdout, $stderr))
            if (-not $copies.Wait(2000)) { $copyCancellation.Cancel(); $cleanupErrors.Add('Output drain exceeded two seconds; logs may be partial.') }
        }
    } catch { $cleanupErrors.Add("Output capture: $($_.Exception.Message)") }
    $copyCancellation.Cancel()
    if ($null -ne $outStream) { $outStream.Dispose() }
    if ($null -ne $errStream) { $errStream.Dispose() }
    if ($cleanupErrors.Count -gt 0 -and $status -eq 'Succeeded') { $status = 'CleanupFailed'; $failure = 'Process cleanup or output capture failed.' }
    $result = [pscustomobject]@{
        Status=$status; Pid=$taskPid; ExitCode=$exitCode; Error=$failure
        ElapsedSeconds=[Math]::Round($watch.Elapsed.TotalSeconds,3); TimeoutSeconds=$TimeoutSeconds
        FilePath=$FilePath; Arguments=$ArgumentList; WorkingDirectory=$start.WorkingDirectory
        Started=$rootStarted; ParentPid=$PID; Launcher=$launcher
        StdoutPath=$stdoutPath; StderrPath=$stderrPath; CleanupErrors=@($cleanupErrors.ToArray())
        Processes=@($tracked.Values | Sort-Object Depth,Pid)
        Limitation='WSL/Linux processes require a Linux timeout wrapper; Windows process tracking cannot verify or terminate Linux descendants. Windows snapshots run at start/end and at most once per three seconds; short-lived intermediate processes between snapshots can escape tracking.'
    }
    [IO.File]::WriteAllText($resultPath, ($result | ConvertTo-Json -Depth 6))
    $copyCancellation.Dispose()
    $process.Dispose()
}
if ([IO.File]::Exists($stdoutPath)) { Write-Output ([IO.File]::ReadAllText($stdoutPath)) }
if ([IO.File]::Exists($stderrPath)) { Write-Output ([IO.File]::ReadAllText($stderrPath)) }
Write-Output "Completed PID $taskPid, status $status, exit $exitCode, elapsed $([Math]::Round($watch.Elapsed.TotalSeconds,3))s; result: $resultPath"
if ($failure) { throw $failure }
