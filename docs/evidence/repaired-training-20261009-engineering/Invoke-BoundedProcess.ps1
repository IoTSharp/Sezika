param(
    [Parameter(Mandatory)][string]$FilePath,
    [string[]]$ArgumentList = @(),
    [ValidateRange(1,1800)][int]$TimeoutSeconds = 180,
    [string]$LogName = 'process',
    [string]$WorkingDirectory,
    [string]$LogDirectory,
    [hashtable]$Environment = @{},
    [ValidateRange(2,10)][int]$ProcessQueryTimeoutSeconds = 5,
    [string]$CancelFile = 'D:\source\Sezika\.artifacts\repaired-training-20261009\cancel.request'
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or newer is required.' }
$root = 'D:\source\Sezika'
$logs = if ($LogDirectory) { [IO.Path]::GetFullPath($LogDirectory) } else { Join-Path $PSScriptRoot 'process-logs' }
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
$observationWarnings = [Collections.Generic.List[string]]::new()
$lastObservation = $null
$observerProgress = [Diagnostics.Stopwatch]::StartNew()
function Test-SameIdentity($Expected, $Actual) {
    return $null -ne $Actual -and $Expected.Pid -eq $Actual.ProcessId -and
        $Expected.CreationDate -eq $Actual.CreationDate -and
        $Expected.ParentPid -eq $Actual.ParentProcessId -and
        $Expected.CommandLine -ceq $Actual.CommandLine
}
function Add-Identity($NativeProcess, [int]$Depth) {
    if ($Depth -gt 32 -or $tracked.Count -ge 64) { throw 'Task identity depth32/items64 bound exceeded.' }
    if ($null -eq $NativeProcess.CreationDate -or [string]::IsNullOrWhiteSpace($NativeProcess.CommandLine)) { throw 'Task creation/complete argv is not observed.' }
    $chain = @()
    if ($Depth -gt 0) {
        $parent = $tracked[[int]$NativeProcess.ParentProcessId]
        if ($null -eq $parent -or $parent.Depth + 1 -ne $Depth -or $NativeProcess.CreationDate -lt $parent.CreationDate) { throw 'Child parent-chain ownership differs.' }
        $chain = @($parent.ParentChain) + @([ordered]@{Pid=$parent.Pid;CreationDate=$parent.CreationDate;CommandLine=$parent.CommandLine;ParentPid=$parent.ParentPid})
    }
    $tracked[[int]$NativeProcess.ProcessId] = [pscustomobject]@{
        Pid = [int]$NativeProcess.ProcessId
        CreationDate = $NativeProcess.CreationDate
        Started = $NativeProcess.CreationDate.ToUniversalTime()
        CommandLine = $NativeProcess.CommandLine
        ParentPid = [int]$NativeProcess.ParentProcessId
        Depth = $Depth
        ParentChain = $chain
        TerminatedByRunner = $false
    }
}
function Update-Descendants([double]$DeadlineSeconds, [Diagnostics.Stopwatch]$DeadlineWatch) {
    $scanWatch = [Diagnostics.Stopwatch]::StartNew()
    $byPid = @{}
    $complete = $false; $reason = ''; $queried = [Collections.Generic.HashSet[int]]::new()
    for ($query = 0; $query -lt 10; $query++) {
        if ([object]::ReferenceEquals($DeadlineWatch,$watch) -and $CancelFile -and [IO.File]::Exists($CancelFile)) { $reason='task cancellation observed'; break }
        $remaining = [Math]::Min(20-$scanWatch.Elapsed.TotalSeconds,$DeadlineSeconds-$DeadlineWatch.Elapsed.TotalSeconds)
        if ($remaining -lt 1) { $reason='observer deadline reached'; break }
        $pending = @($tracked.Values | Where-Object {-not $queried.Contains([int]$_.Pid)} | Select-Object -First 65)
        if ($pending.Count -eq 0) { $complete=$true; break }
        if ($pending.Count -gt 64) { $reason='observer task item cap'; break }
        $terms = [Collections.Generic.List[string]]::new()
        foreach ($known in $pending) { $terms.Add("ProcessId=$($known.Pid)"); $terms.Add("ParentProcessId=$($known.Pid)") }
        # WQL only names already registered task IDs and their direct children.
        # Never remove -Filter or fall back to an all-machine enumeration.
        $timeout = [Math]::Max(1,[Math]::Min($ProcessQueryTimeoutSeconds,[Math]::Floor($remaining)))
        try { $snapshot=@(Get-CimInstance Win32_Process -Filter ($terms -join ' OR ') -Property ProcessId,ParentProcessId,CreationDate,CommandLine -OperationTimeoutSec $timeout | Select-Object -First 65) }
        catch { $reason=$_.Exception.Message; break }
        if ($scanWatch.Elapsed.TotalSeconds -ge 20 -or $DeadlineWatch.Elapsed.TotalSeconds -ge $DeadlineSeconds) { $reason='observer returned beyond bounded deadline'; break }
        if ($snapshot.Count -gt 64) { $reason='observer result cap64 exceeded'; break }
        foreach ($item in $snapshot) { $byPid[[int]$item.ProcessId]=$item }
        foreach ($known in $pending) {
            [void]$queried.Add([int]$known.Pid)
            $actualParent=$byPid[[int]$known.Pid]
            # New descendants require a currently verified parent; historical
            # registered children remain tracked after parent exit or PID reuse.
            if (-not (Test-SameIdentity $known $actualParent)) {
                if (@($snapshot | Where-Object {$_.ParentProcessId -eq $known.Pid -and -not $tracked.ContainsKey([int]$_.ProcessId)}).Count -gt 0) { $reason='unverified child of absent/reused registered parent'; break }
                continue
            }
            foreach ($child in $snapshot) {
                if ($scanWatch.Elapsed.TotalSeconds -ge 20 -or $DeadlineWatch.Elapsed.TotalSeconds -ge $DeadlineSeconds) { $reason='observer discovery time bound'; break }
                if ($child.ParentProcessId -ne $known.Pid -or $tracked.ContainsKey([int]$child.ProcessId)) { continue }
                try { Add-Identity $child ($known.Depth+1) } catch { $reason=$_.Exception.Message; break }
            }
            if ($reason) { break }
        }
        if ($reason) { break }
        if ($queried.Count -eq $tracked.Count) { $complete=$true; break }
    }
    if (-not $complete -and -not $reason) { $reason='observer query cap10 reached' }
    return [pscustomobject]@{ByPid=$byPid;Complete=$complete;Reason=$reason;Queries=[Math]::Min(10,$query+1);ElapsedSeconds=$scanWatch.Elapsed.TotalSeconds}
}
function Observe-Task([double]$DeadlineSeconds,[Diagnostics.Stopwatch]$DeadlineWatch) {
    if ($tracked.Count -eq 0 -and $started -and -not $process.HasExited) {
        $remaining=$DeadlineSeconds-$DeadlineWatch.Elapsed.TotalSeconds
        if ($remaining -ge 1) {
            try {
                $native=Get-CimInstance Win32_Process -Filter "ProcessId=$taskPid" -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec ([Math]::Max(1,[Math]::Min($ProcessQueryTimeoutSeconds,[Math]::Floor($remaining))))
                if ($native -and $native.ParentProcessId -eq $PID -and [Math]::Abs(($native.CreationDate.ToUniversalTime()-$rootStarted).Ticks) -le 10) { Add-Identity $native 0 }
            } catch { $observationWarnings.Add('root registration deferred: '+$_.Exception.Message) }
        }
    }
    $result=Update-Descendants $DeadlineSeconds $DeadlineWatch
    if (-not $result.Complete) {
        $observationWarnings.Add('observer incomplete: '+$result.Reason)
        if ($observerProgress.Elapsed.TotalSeconds -ge 15) { Write-Output "Task observer deferred; payload deadline unchanged: $($result.Reason)"; $observerProgress.Restart() }
    }
    return $result
}

$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
$watch = [Diagnostics.Stopwatch]::StartNew()
$executionDeadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$started = $false
$rootStarted = $null
$taskPid = $null
$exitCode = $null
$failure = $null
$status = 'Starting'
$outStream = $null
$errStream = $null
$stdout = $null
$stderr = $null
$copyCancellation = [Threading.CancellationTokenSource]::new()
$launcherIdentity = Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -OperationTimeoutSec $ProcessQueryTimeoutSeconds
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
    try { $identity = Get-CimInstance Win32_Process -Filter "ProcessId=$taskPid" -OperationTimeoutSec $ProcessQueryTimeoutSeconds }
    catch { $observationWarnings.Add('initial root observer deferred: '+$_.Exception.Message) }
    if ($null -ne $identity -and $identity.ParentProcessId -eq $PID -and [Math]::Abs(($identity.CreationDate.ToUniversalTime()-$rootStarted).Ticks) -le 10) {
        try { Add-Identity $identity 0 } catch { $observationWarnings.Add('initial root identity deferred: '+$_.Exception.Message) }
    }
    elseif (-not $process.HasExited) { $observationWarnings.Add('Running root identity registration pending; no early observer kill.') }
    [IO.File]::WriteAllText($identityPath, ([pscustomobject]@{
        Pid=$taskPid; Started=$rootStarted; CommandLine=$identity.CommandLine
        IdentityObservation=if ($null -ne $identity) { 'cim_snapshot' } else { 'exited_before_cim_snapshot_see_recorded_argv' }
        ParentPid=$PID; ObservedParentPid=$identity.ParentProcessId
        ParentIdentitySource='Process.Start caller'; Launcher=$launcher
        TimeoutSeconds=$TimeoutSeconds; ProcessQueryTimeoutSeconds=$ProcessQueryTimeoutSeconds; FilePath=$FilePath
        Arguments=$ArgumentList; WorkingDirectory=$start.WorkingDirectory
    } | ConvertTo-Json -Depth 4))
    Write-Output "Started PID $($process.Id), timeout ${TimeoutSeconds}s: $FilePath $($ArgumentList -join ' ')"
    $lastObservation = Observe-Task $TimeoutSeconds $watch
    $nextSnapshotSeconds = $watch.Elapsed.TotalSeconds + 3
    # At most 2*TimeoutSeconds+1 iterations, additionally bounded by elapsed time.
    # Root exit/deadline checks remain every 500 ms; task-only filtered observations
    # run at most once per three seconds. Observer failure never shortens the deadline.
    for ($attempt=0; $attempt -le (2 * $TimeoutSeconds); $attempt++) {
        if ($process.HasExited) { break }
        if ($CancelFile -and [IO.File]::Exists($CancelFile)) { $status='Cancelled'; throw 'Task cancellation requested.' }
        if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
            $status = 'TimedOut'
            throw "Process timeout (${TimeoutSeconds}s)."
        }
        if ($watch.Elapsed.TotalSeconds -ge $nextSnapshotSeconds) {
            $lastObservation = Observe-Task $TimeoutSeconds $watch
            $nextSnapshotSeconds = $watch.Elapsed.TotalSeconds + 3
        }
        if (($attempt % 60) -eq 59) { Write-Output "PID $taskPid running: $([int]$watch.Elapsed.TotalSeconds)s" }
        Start-Sleep -Milliseconds 500
    }
    if (-not $process.HasExited) { throw 'Process iteration limit exceeded.' }
    if ($process.ExitTime.ToUniversalTime() -gt $executionDeadlineUtc) { $status='TimedOut'; throw 'Payload exited after the real execution deadline.' }
    $exitCode = $process.ExitCode
    if ($exitCode -ne 0) { throw "Process failed with exit code $exitCode." }
    $status = 'Succeeded'
} catch {
    $failure = $_.Exception.Message
    if ($status -notin @('TimedOut','Cancelled')) { $status = 'Failed' }
} finally {
    $cleanupWatch = [Diagnostics.Stopwatch]::StartNew()
    # Three passes, one shared30s cleanup budget including final observation and output drain.
    $cleanupSeconds = 30; $remaining=0; $finalComplete=$false; $unknownCount=1; $verifiedLiveCount=0
    for ($pass = 0; $pass -lt 3 -and $cleanupWatch.Elapsed.TotalSeconds -lt 23; $pass++) {
        $currentProcesses = @{}
        try { $lastObservation=Observe-Task 23 $cleanupWatch; $currentProcesses=$lastObservation.ByPid }
        catch { $observationWarnings.Add('cleanup observer deferred: '+$_.Exception.Message); $lastObservation=$null }
        $remaining = 0
        foreach ($known in @($tracked.Values | Sort-Object Depth -Descending)) {
            # Skip exited or reused PIDs using this pass's single snapshot.
            if (-not (Test-SameIdentity $known $currentProcesses[$known.Pid])) { continue }
            if ($cleanupWatch.Elapsed.TotalSeconds -ge 23) { break }
            try {
                # Only a still-live, matching task process incurs this final
                # identity refresh immediately before opening its handle.
                $querySeconds=[Math]::Min($ProcessQueryTimeoutSeconds,[Math]::Floor(23-$cleanupWatch.Elapsed.TotalSeconds))
                if ($querySeconds -lt 1) { break }
                $current = Get-CimInstance Win32_Process -Filter "ProcessId=$($known.Pid)" -OperationTimeoutSec $querySeconds
                if (-not (Test-SameIdentity $known $current)) { continue }
                if ($known.Depth -gt 0) {
                    if ($known.ParentChain.Count -ne $known.Depth) { throw 'Recorded parent-chain depth differs.' }
                    foreach ($ancestor in $known.ParentChain) {
                        $recorded=$tracked[[int]$ancestor.Pid]
                        if ($null -eq $recorded -or $recorded.CreationDate -ne $ancestor.CreationDate -or $recorded.ParentPid -ne $ancestor.ParentPid -or $recorded.CommandLine -cne $ancestor.CommandLine) { throw 'Recorded parent-chain identity differs.' }
                    }
                } elseif ($known.ParentPid -ne $PID) { throw 'Root ownership parent differs.' }
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
        if ($remaining -eq 0 -and $lastObservation -and $lastObservation.Complete) { break }
        Start-Sleep -Milliseconds 200
    }
    try {
        $final=Observe-Task 28 $cleanupWatch
        $verifiedLiveCount=0; $unknownCount=if($final.Complete){0}else{1}
        foreach($known in @($tracked.Values)){if(Test-SameIdentity $known $final.ByPid[$known.Pid]){$verifiedLiveCount++}}
        if($started -and $tracked.Count -eq 0){$unknownCount++}
        $finalComplete=$final.Complete -and $unknownCount -eq 0 -and $verifiedLiveCount -eq 0
    } catch { $unknownCount=1; $observationWarnings.Add('final observer: '+$_.Exception.Message) }
    if(-not $finalComplete){$cleanupErrors.Add("Final task inventory unverified: live=$verifiedLiveCount unknown=$unknownCount")} 
    try {
        if ($null -ne $stdout -and $null -ne $stderr) {
            $copies = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@($stdout, $stderr))
            $drainMilliseconds=[Math]::Max(0,[Math]::Min(2000,[Math]::Floor((30-$cleanupWatch.Elapsed.TotalSeconds)*1000)))
            if (-not $copies.Wait($drainMilliseconds)) { $copyCancellation.Cancel(); $cleanupErrors.Add('Bounded output drain expired; logs may be partial.') }
        }
    } catch { $cleanupErrors.Add("Output capture: $($_.Exception.Message)") }
    $copyCancellation.Cancel()
    if ($null -ne $outStream) { $outStream.Dispose() }
    if ($null -ne $errStream) { $errStream.Dispose() }
    $cleanupBudgetExhausted=$cleanupWatch.Elapsed.TotalSeconds -ge 30
    if($cleanupBudgetExhausted){$finalComplete=$false;$unknownCount=[Math]::Max(1,$unknownCount);$cleanupErrors.Add('Shared30s cleanup budget exhausted; final closure not asserted.')}
    if ($cleanupErrors.Count -gt 0 -and $status -eq 'Succeeded') { $status = 'CleanupFailed'; $failure = 'Process cleanup or output capture failed.' }
    $result = [pscustomobject]@{
        Status=$status; Pid=$taskPid; ExitCode=$exitCode; Error=$failure
        ElapsedSeconds=[Math]::Round($watch.Elapsed.TotalSeconds,3); TimeoutSeconds=$TimeoutSeconds; ProcessQueryTimeoutSeconds=$ProcessQueryTimeoutSeconds
        FilePath=$FilePath; Arguments=$ArgumentList; WorkingDirectory=$start.WorkingDirectory
        Started=$rootStarted; ParentPid=$PID; Launcher=$launcher
        StdoutPath=$stdoutPath; StderrPath=$stderrPath; CleanupErrors=@($cleanupErrors.ToArray())
        FinalCheckComplete=$finalComplete; VerifiedLiveCount=$verifiedLiveCount; UnknownCount=$unknownCount
        CleanupSeconds=30; CleanupElapsedSeconds=$cleanupWatch.Elapsed.TotalSeconds; CleanupBudgetExhausted=$cleanupBudgetExhausted; ObservationWarnings=@($observationWarnings.ToArray())
        ProcessInventoryScope='registered_task_pid_or_parentpid_only'; MaximumTrackedItems=64; MaximumDepth=32; MaximumQueriesPerPass=10; MaximumPassSeconds=20
        Processes=@($tracked.Values | Sort-Object Depth,Pid)
        Limitation='WSL/Linux processes need their independent Linux runner. CIM limits are cooperative; blocking provider calls can overrun. Short intermediates can exit before observation, and new children of an already-exited parent cannot be safely registered. Incomplete observations are retained, never treated as an empty inventory.'
    }
    [IO.File]::WriteAllText($resultPath, ($result | ConvertTo-Json -Depth 6))
    $copyCancellation.Dispose()
    $process.Dispose()
}
if ([IO.File]::Exists($stdoutPath)) { Write-Output ([IO.File]::ReadAllText($stdoutPath)) }
if ([IO.File]::Exists($stderrPath)) { Write-Output ([IO.File]::ReadAllText($stderrPath)) }
Write-Output "Completed PID $taskPid, status $status, exit $exitCode, elapsed $([Math]::Round($watch.Elapsed.TotalSeconds,3))s; result: $resultPath"
if ($failure) { throw $failure }
