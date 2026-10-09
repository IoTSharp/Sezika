#requires -Version 7.0
[CmdletBinding(DefaultParameterSetName='Api')]
param(
    [Parameter(Mandatory,ParameterSetName='Api')][ValidatePattern('^[a-z][a-z0-9-]{0,31}$')][string]$Batch,
    [Parameter(Mandatory,ParameterSetName='Api')][ValidateRange(0,255)][int]$Start,
    [Parameter(Mandatory,ParameterSetName='Api')][ValidateRange(1,8)][int]$Count,
    [Parameter(ParameterSetName='Api')][ValidateSet('model-selection.json','model-selection-recovery-20261009.json','model-selection-final16-20261009.json')][string]$SelectionFile='model-selection.json',
    [Parameter(ParameterSetName='Api')][ValidateSet('Adapter','Direct')][string]$Transport='Adapter',
    [Parameter(Mandatory,ParameterSetName='Probe')][ValidateSet(0,7)][int]$ProbeExitCode,
    [Parameter(Mandatory,ParameterSetName='Fixture')][ValidateSet(0,7)][int]$FixtureExitCode
)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required.'}
$ownedRoot='D:\source\Sezika\.artifacts\teacher-expansion-20261008'
$ownedPwsh='C:\Program Files\PowerShell\7\pwsh.exe'
if([IO.Path]::GetFullPath($PSScriptRoot) -cne $ownedRoot){throw 'Guardian task root mismatch.'}
if($PSCmdlet.ParameterSetName -ceq 'Fixture'){
    # Fixed, finite fixture: no HTTP, credentials, file writes or child processes.
    Write-Output "PS$($PSVersionTable.PSVersion) owned fixture exit=$FixtureExitCode"
    [Console]::Error.WriteLine("owned fixture stderr exit=$FixtureExitCode")
    Start-Sleep -Seconds 2
    exit $FixtureExitCode
}
if(-not [IO.File]::Exists($ownedPwsh)){throw 'Configured PowerShell 7 executable missing.'}
$ownedIsProbe=$PSCmdlet.ParameterSetName -ceq 'Probe'
if(-not $ownedIsProbe -and ($Start+$Count -gt 256 -or [IO.Directory]::Exists((Join-Path $ownedRoot $Batch)))){throw 'Batch already exists or range invalid; requests are never repeated.'}
$ownedTimeout=if($ownedIsProbe){20}else{280}
$ownedLabel=if($ownedIsProbe){'probe-exit-'+$ProbeExitCode}else{$Batch}
$ownedLogs=Join-Path $ownedRoot 'api-owned-logs'
[IO.Directory]::CreateDirectory($ownedLogs)|Out-Null
$ownedPrefix=Join-Path $ownedLogs ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')+'-'+$ownedLabel)
$ownedIdentityPath=$ownedPrefix+'.identity.json'
$ownedResultPath=$ownedPrefix+'.result.json'
$ownedArguments=if($ownedIsProbe){@('-NoLogo','-NoProfile','-File',$PSCommandPath,'-FixtureExitCode',$ProbeExitCode.ToString())}else{@('-NoLogo','-NoProfile','-File',(Join-Path $ownedRoot 'Invoke-TeacherBatch.ps1'),'-Batch',$Batch,'-Start',$Start.ToString(),'-Count',$Count.ToString(),'-SelectionFile',$SelectionFile,'-Transport',$Transport)}
$ownedTracked=@{}
$ownedErrors=[Collections.Generic.List[object]]::new()
$ownedErrorCount=0
$ownedCleanupErrors=[Collections.Generic.List[string]]::new()
$ownedObservationWatch=[Diagnostics.Stopwatch]::StartNew()
function Add-ObservationError([string]$Stage,[string]$Code){
    $script:ownedErrorCount++
    if($ownedErrors.Count -lt 64){$ownedErrors.Add([ordered]@{stage=$Stage;code=$Code;elapsed_seconds=[Math]::Round($ownedObservationWatch.Elapsed.TotalSeconds,3)})}
}
function Write-OwnedJson([string]$Path,$Value){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))}
function Read-ExactIdentity([int]$ProcessId){
    try{
        $value=@(Get-CimInstance Win32_Process -Filter "ProcessId=$ProcessId" -Property ProcessId,ParentProcessId,CreationDate,CommandLine -OperationTimeoutSec 5)
        if($value.Count -gt 1){Add-ObservationError 'exact_identity' 'UnexpectedItemCount';return [pscustomobject]@{Ok=$false;Value=$null}}
        return [pscustomobject]@{Ok=$true;Value=if($value.Count -eq 1){$value[0]}else{$null}}
    }catch{Add-ObservationError 'exact_identity' $_.Exception.GetType().Name;return [pscustomobject]@{Ok=$false;Value=$null}}
}
function Test-OwnedIdentity($Expected,$Actual){
    return $null -ne $Expected -and $null -ne $Actual -and $Expected.Pid -eq [int]$Actual.ProcessId -and
        $Expected.CreationDate -eq $Actual.CreationDate -and $Expected.ParentPid -eq [int]$Actual.ParentProcessId -and
        -not [string]::IsNullOrWhiteSpace($Expected.CommandLine) -and $Expected.CommandLine -ceq $Actual.CommandLine
}
function Remember-OwnedIdentity($Native,[int]$Depth){
    if($ownedTracked.Count -ge 16 -and -not $ownedTracked.ContainsKey([int]$Native.ProcessId)){Add-ObservationError 'tree' 'TaskTreeItemCap16';return}
    $ownedTracked[[int]$Native.ProcessId]=[pscustomobject]@{Pid=[int]$Native.ProcessId;CreationDate=$Native.CreationDate;Started=$Native.CreationDate.ToUniversalTime();CommandLine=[string]$Native.CommandLine;ParentPid=[int]$Native.ParentProcessId;Depth=$Depth;TerminatedByGuardian=$false;Closure='unobserved';ExitObservedUtc=$null}
}
function Observe-OwnedTree{
    # Narrow WQL only. No complete-machine snapshot is ever requested.
    $scanWatch=[Diagnostics.Stopwatch]::StartNew()
    if($ownedWatch.IsRunning -and -not $ownedProcess.HasExited -and $ownedTimeout-$ownedWatch.Elapsed.TotalSeconds -lt 12){return}
    $rootObservation=Read-ExactIdentity $ownedProcess.Id
    if(-not $rootObservation.Ok){return}
    $rootNative=$rootObservation.Value
    if($null -ne $rootNative){
        if($rootNative.ParentProcessId -ne $PID -or [Math]::Abs(($rootNative.CreationDate.ToUniversalTime()-$ownedStartUtc).Ticks) -gt 10){Add-ObservationError 'root_identity' 'IdentityMismatch';return}
        if($ownedTracked.ContainsKey($ownedProcess.Id) -and -not (Test-OwnedIdentity $ownedTracked[$ownedProcess.Id] $rootNative)){Add-ObservationError 'root_identity' 'RecordedIdentityMismatch';return}
        if(-not $ownedTracked.ContainsKey($ownedProcess.Id)){Remember-OwnedIdentity $rootNative 0}
    }elseif(-not $ownedProcess.HasExited){Add-ObservationError 'root_identity' 'LiveHandleAbsentFromQuery';return}
    $parents=@($ownedTracked.Values|Sort-Object Depth,Pid)
    for($parentIndex=0;$parentIndex -lt [Math]::Min(16,$parents.Count);$parentIndex++){
        if($scanWatch.Elapsed.TotalSeconds -ge 12){Add-ObservationError 'tree' 'ScopedDiscoveryDeadline12s';break}
        $parent=$parents[$parentIndex]
        # This API boundary permits no business children; depth remains bounded.
        if($parent.Depth -ge 3){continue}
        $parentObservation=if($parent.Pid -eq $ownedProcess.Id){$rootObservation}else{Read-ExactIdentity $parent.Pid}
        if(-not $parentObservation.Ok){continue}
        if($null -ne $parentObservation.Value -and -not (Test-OwnedIdentity $parent $parentObservation.Value)){Add-ObservationError 'tree' 'ParentIdentityReused';continue}
        try{$children=@(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($parent.Pid)" -Property ProcessId,ParentProcessId,CreationDate,CommandLine -OperationTimeoutSec 5|Select-Object -First 17)}catch{Add-ObservationError 'parent_children' $_.Exception.GetType().Name;continue}
        if($children.Count -gt 16){Add-ObservationError 'tree' 'FilteredChildrenCap16';continue}
        for($childIndex=0;$childIndex -lt $children.Count;$childIndex++){
            if($scanWatch.Elapsed.TotalSeconds -ge 12){Add-ObservationError 'tree' 'ScopedDiscoveryDeadline12s';break}
            $child=$children[$childIndex]
            if($ownedTracked.ContainsKey([int]$child.ProcessId)){continue}
            if($child.CreationDate -lt $parent.CreationDate -or [string]::IsNullOrWhiteSpace($child.CommandLine)){Add-ObservationError 'tree' 'ChildIdentityIncomplete';continue}
            # An absent historical root must have exited after the child's birth.
            if($null -eq $parentObservation.Value -and ($parent.Pid -ne $ownedProcess.Id -or -not $ownedProcess.HasExited -or $child.CreationDate.ToUniversalTime() -gt $ownedProcess.ExitTime.ToUniversalTime())){Add-ObservationError 'tree' 'HistoricalParentUnproved';continue}
            Remember-OwnedIdentity $child ($parent.Depth+1)
        }
    }
}
function Test-OwnedParentChain($Identity){
    $cursor=$Identity
    $chainWatch=[Diagnostics.Stopwatch]::StartNew()
    for($chainIndex=0;$chainIndex -lt 4 -and $chainWatch.Elapsed.TotalSeconds -lt 25;$chainIndex++){
        if($cursor.Pid -eq $ownedProcess.Id){return $cursor.ParentPid -eq $PID -and [Math]::Abs(($cursor.Started-$ownedStartUtc).Ticks) -le 10}
        if(-not $ownedTracked.ContainsKey($cursor.ParentPid)){return $false}
        $parent=$ownedTracked[$cursor.ParentPid]
        if($cursor.CreationDate -lt $parent.CreationDate){return $false}
        $parentNow=Read-ExactIdentity $parent.Pid
        if(-not $parentNow.Ok -or ($null -ne $parentNow.Value -and -not (Test-OwnedIdentity $parent $parentNow.Value))){return $false}
        if($null -eq $parentNow.Value -and ($parent.Pid -ne $ownedProcess.Id -or -not $ownedProcess.HasExited -or $cursor.Started -gt $ownedProcess.ExitTime.ToUniversalTime())){return $false}
        $cursor=$parent
    }
    return $false
}
function Start-OwnedCapture($Source,[string]$Path){
    $destination=[IO.FileStream]::new($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    $buffer=[byte[]]::new(65536)
    return [pscustomobject]@{Source=$Source;Destination=$destination;Buffer=$buffer;Pending=$Source.ReadAsync($buffer,0,$buffer.Length,$ownedCaptureCancellation.Token);Ended=$false;Limited=$false;Error=$null;BytesCaptured=[long]0;MaximumBytes=[long]4194304;Path=$Path}
}
function Pump-OwnedCapture($Capture){
    if($null -eq $Capture -or $Capture.Ended){return}
    $pumpWatch=[Diagnostics.Stopwatch]::StartNew()
    for($pumpIndex=0;$pumpIndex -lt 32 -and $pumpWatch.Elapsed.TotalMilliseconds -lt 100;$pumpIndex++){
        if(-not $Capture.Pending.IsCompleted){break}
        try{
            $read=$Capture.Pending.GetAwaiter().GetResult()
            if($read -eq 0){$Capture.Ended=$true;break}
            $save=[int][Math]::Min($read,$Capture.MaximumBytes-$Capture.BytesCaptured)
            if($save -gt 0){$Capture.Destination.Write($Capture.Buffer,0,$save);$Capture.BytesCaptured+=$save}
            if($save -lt $read){$Capture.Limited=$true}
            $Capture.Pending=$Capture.Source.ReadAsync($Capture.Buffer,0,$Capture.Buffer.Length,$ownedCaptureCancellation.Token)
        }catch{$Capture.Error=$_.Exception.GetType().Name;$Capture.Ended=$true;break}
    }
}
$ownedLauncherObservation=Read-ExactIdentity $PID
$ownedLauncherNative=$ownedLauncherObservation.Value
$ownedLauncher=[ordered]@{Pid=$PID;Started=[Diagnostics.Process]::GetCurrentProcess().StartTime.ToUniversalTime();CommandLine=[Environment]::CommandLine;ParentPid=if($null -ne $ownedLauncherNative){[int]$ownedLauncherNative.ParentProcessId}else{$null};ParentChain=@();IdentityObserved=$ownedLauncherObservation.Ok -and $null -ne $ownedLauncherNative}
if($null -ne $ownedLauncherNative){
    $ownedLauncher.CommandLine=$ownedLauncherNative.CommandLine
    $ancestorPid=[int]$ownedLauncherNative.ParentProcessId
    for($ancestorIndex=0;$ancestorIndex -lt 2 -and $ownedObservationWatch.Elapsed.TotalSeconds -lt 25;$ancestorIndex++){
        $ancestorObservation=Read-ExactIdentity $ancestorPid
        if(-not $ancestorObservation.Ok -or $null -eq $ancestorObservation.Value){break}
        $ancestor=$ancestorObservation.Value
        $ownedLauncher.ParentChain+=@([ordered]@{Pid=[int]$ancestor.ProcessId;Started=$ancestor.CreationDate.ToUniversalTime();CommandLine=$ancestor.CommandLine;ParentPid=[int]$ancestor.ParentProcessId})
        $ancestorPid=[int]$ancestor.ParentProcessId
    }
}
$ownedStartInfo=[Diagnostics.ProcessStartInfo]::new($ownedPwsh)
$ownedStartInfo.UseShellExecute=$false;$ownedStartInfo.CreateNoWindow=$true
$ownedStartInfo.WorkingDirectory='D:\source\Sezika'
$ownedStartInfo.RedirectStandardOutput=$true;$ownedStartInfo.RedirectStandardError=$true
$argumentWatch=[Diagnostics.Stopwatch]::StartNew()
for($argumentIndex=0;$argumentIndex -lt [Math]::Min(16,$ownedArguments.Count);$argumentIndex++){if($argumentWatch.Elapsed.TotalSeconds -ge 5){throw 'Fixed argv setup5s deadline.'};$ownedStartInfo.ArgumentList.Add($ownedArguments[$argumentIndex])}
if($ownedArguments.Count -gt 16){throw 'Fixed argv cap16.'}
$ownedProcess=[Diagnostics.Process]::new();$ownedProcess.StartInfo=$ownedStartInfo
$ownedCaptureCancellation=[Threading.CancellationTokenSource]::new()
$ownedWatch=[Diagnostics.Stopwatch]::new()
$ownedStarted=$false;$ownedStartUtc=$null;$ownedExitCode=$null;$ownedFailure=$null;$ownedStatus='NotStarted'
$ownedStdout=$null;$ownedStderr=$null;$ownedRootClosure='not_started';$ownedCleanupWatch=$null
try{
    if([IO.File]::Exists((Join-Path $ownedRoot 'cancel.request'))){$ownedStatus='Cancelled';throw 'Cancellation existed before launch.'}
    if(-not $ownedProcess.Start()){throw 'Owned process start failed.'}
    $ownedStarted=$true;$ownedStartUtc=$ownedProcess.StartTime.ToUniversalTime();$ownedWatch.Start()
    $ownedStdout=Start-OwnedCapture $ownedProcess.StandardOutput.BaseStream ($ownedPrefix+'.stdout.log')
    $ownedStderr=Start-OwnedCapture $ownedProcess.StandardError.BaseStream ($ownedPrefix+'.stderr.log')
    Write-OwnedJson $ownedIdentityPath ([ordered]@{Pid=$ownedProcess.Id;Started=$ownedStartUtc;FilePath=$ownedPwsh;Arguments=$ownedArguments;ParentPid=$PID;ParentIdentitySource='Process.Start caller';Launcher=$ownedLauncher;TimeoutSeconds=$ownedTimeout;QueryTimeoutSeconds=5;MaximumTaskProcesses=16;CapturedCommandLine=$null;CredentialArguments=$false})
    Write-Output "Owned API guardian: PID $($ownedProcess.Id), limit ${ownedTimeout}s, label $ownedLabel."
    Observe-OwnedTree
    $nextDiscovery=30.0;$nextProgress=30.0
    # Audit: -lt compares; at most300 iterations AND fixed deadline. No retries.
    for($poll=0;$poll -lt 300;$poll++){
        Pump-OwnedCapture $ownedStdout;Pump-OwnedCapture $ownedStderr
        if($ownedProcess.HasExited){break}
        if([IO.File]::Exists((Join-Path $ownedRoot 'cancel.request'))){$ownedStatus='Cancelled';break}
        if($ownedWatch.Elapsed.TotalSeconds -ge $ownedTimeout){$ownedStatus='TimedOut';break}
        if($ownedWatch.Elapsed.TotalSeconds -ge $nextDiscovery){Observe-OwnedTree;$nextDiscovery=$ownedWatch.Elapsed.TotalSeconds+30}
        if($ownedWatch.Elapsed.TotalSeconds -ge $nextProgress){Write-Output "Owned PID $($ownedProcess.Id) running $([int]$ownedWatch.Elapsed.TotalSeconds)s; observation errors=$ownedErrorCount.";$nextProgress=$ownedWatch.Elapsed.TotalSeconds+30}
        [void]$ownedProcess.WaitForExit(1000)
    }
    if($ownedProcess.HasExited){$ownedExitCode=$ownedProcess.ExitCode;$ownedStatus=if($ownedExitCode -eq 0){'Succeeded'}else{'Failed'};if($ownedExitCode -ne 0){$ownedFailure='ChildExitNonzero'}}
    elseif($ownedStatus -ceq 'NotStarted'){$ownedStatus='IterationLimit';$ownedFailure='PollCap300'}
}catch{
    $ownedFailure=$_.Exception.GetType().Name
    if($ownedStatus -notin @('Cancelled','TimedOut')){$ownedStatus='Failed'}
}finally{
    $ownedCleanupWatch=[Diagnostics.Stopwatch]::StartNew()
    if($ownedStarted){
        # Cancellation allows the child's own HTTP finally a five-second grace.
        if(-not $ownedProcess.HasExited){[void]$ownedProcess.WaitForExit(5000)}
        try{Observe-OwnedTree}catch{Add-ObservationError 'cleanup_discovery' $_.Exception.GetType().Name}
        for($cleanupPass=0;$cleanupPass -lt 2 -and $ownedCleanupWatch.Elapsed.TotalSeconds -lt 35;$cleanupPass++){
            $knownProcesses=@($ownedTracked.Values|Sort-Object Depth -Descending)
            for($cleanupIndex=0;$cleanupIndex -lt [Math]::Min(16,$knownProcesses.Count);$cleanupIndex++){
                if($ownedCleanupWatch.Elapsed.TotalSeconds -ge 35){break}
                $known=$knownProcesses[$cleanupIndex]
                $observation=Read-ExactIdentity $known.Pid
                if(-not $observation.Ok){$known.Closure='query_failed';continue}
                if($null -eq $observation.Value){$known.Closure='absent';continue}
                if(-not (Test-OwnedIdentity $known $observation.Value)){$known.Closure='pid_reused';continue}
                $known.Closure='live_owned'
                if(-not (Test-OwnedParentChain $known)){$known.Closure='parent_chain_unproved';continue}
                # The retained root handle is authoritative, but full identity
                # must ALSO match before signalling. No unproved fallback kill.
                $signalHandle=$null
                try{
                    $signalHandle=if($known.Pid -eq $ownedProcess.Id){$ownedProcess}else{[Diagnostics.Process]::GetProcessById($known.Pid)}
                    if($signalHandle.HasExited){$known.Closure='exited_handle';continue}
                    if([Math]::Abs(($signalHandle.StartTime.ToUniversalTime()-$known.Started).Ticks) -gt 10){$known.Closure='handle_identity_mismatch';continue}
                    $immediate=Read-ExactIdentity $known.Pid
                    if(-not $immediate.Ok -or -not (Test-OwnedIdentity $known $immediate.Value)){$known.Closure='signal_identity_unproved';continue}
                    $signalHandle.Kill();$known.TerminatedByGuardian=$true
                    $known.Closure=if($signalHandle.WaitForExit(1000)){'terminated_and_exited'}else{'termination_pending'}
                }catch{$known.Closure='signal_failed';$ownedCleanupErrors.Add('Signal:'+ $_.Exception.GetType().Name)}finally{if($null -ne $signalHandle -and $known.Pid -ne $ownedProcess.Id){$signalHandle.Dispose()}}
            }
            if($cleanupPass -eq 0){Start-Sleep -Milliseconds 200}
        }
        # Verify root with its original handle; an unsuccessful observation is
        # never represented as absence or as successful cleanup.
        $ownedRootClosure=if($ownedProcess.HasExited){'exited_retained_handle'}else{'still_live_unverified'}
        if($ownedProcess.HasExited){$ownedExitCode=$ownedProcess.ExitCode}
        if($ownedRootClosure -cne 'exited_retained_handle'){$ownedCleanupErrors.Add('Original root process remains live; exact identity could not authorize termination.')}
        for($closureIndex=0;$closureIndex -lt [Math]::Min(16,$ownedTracked.Count) -and $ownedCleanupWatch.Elapsed.TotalSeconds -lt 45;$closureIndex++){
            $closureKnown=@($ownedTracked.Values|Sort-Object Depth,Pid)[$closureIndex]
            $closureObservation=Read-ExactIdentity $closureKnown.Pid
            if(-not $closureObservation.Ok){$closureKnown.Closure='final_query_failed'}
            elseif($null -eq $closureObservation.Value){$closureKnown.Closure='absent'}
            elseif(-not (Test-OwnedIdentity $closureKnown $closureObservation.Value)){$closureKnown.Closure='pid_reused'}
            else{$closureKnown.Closure='live_owned';$ownedCleanupErrors.Add('Owned process still live:'+ $closureKnown.Pid)}
        }
    }
    $drainWatch=[Diagnostics.Stopwatch]::StartNew()
    for($drainIndex=0;$drainIndex -lt 50 -and $drainWatch.Elapsed.TotalSeconds -lt 5;$drainIndex++){
        Pump-OwnedCapture $ownedStdout;Pump-OwnedCapture $ownedStderr
        if(($null -eq $ownedStdout -or $ownedStdout.Ended) -and ($null -eq $ownedStderr -or $ownedStderr.Ended)){break}
        Start-Sleep -Milliseconds 100
    }
    $ownedCaptureCancellation.Cancel()
    $captureCloseWatch=[Diagnostics.Stopwatch]::StartNew()
    foreach($capture in @($ownedStdout,$ownedStderr)){
        if($captureCloseWatch.Elapsed.TotalSeconds -ge 5){$ownedCleanupErrors.Add('CaptureCloseDeadline5s');break}
        if($null -eq $capture){continue}
        if(-not $capture.Ended -or $capture.Limited -or $capture.Error){$ownedCleanupErrors.Add('CaptureIncompleteOrLimited:'+ [IO.Path]::GetFileName($capture.Path))}
        $capture.Destination.Flush();$capture.Destination.Dispose()
    }
    $unverified=@($ownedTracked.Values|Where-Object {$_.Closure -notin @('absent','pid_reused','exited_handle','terminated_and_exited')})
    if($unverified.Count -gt 0){$ownedCleanupErrors.Add('Some task process closures remain unverified.')}
    $rootIdentity=if($ownedStarted -and $ownedTracked.ContainsKey($ownedProcess.Id)){$ownedTracked[$ownedProcess.Id]}else{$null}
    if($ownedStarted -and $null -eq $rootIdentity){$ownedCleanupErrors.Add('Exact root identity was never observed; original handle exit cannot prove descendant closure.')}
    if($ownedCleanupErrors.Count -gt 0 -and $ownedStatus -ceq 'Succeeded'){$ownedStatus='CleanupUnverified'}
    Write-OwnedJson $ownedIdentityPath ([ordered]@{Pid=if($ownedStarted){$ownedProcess.Id}else{$null};Started=$ownedStartUtc;FilePath=$ownedPwsh;Arguments=$ownedArguments;CapturedCommandLine=$rootIdentity.CommandLine;ParentPid=$PID;ParentIdentitySource='Process.Start caller';Launcher=$ownedLauncher;TimeoutSeconds=$ownedTimeout;QueryTimeoutSeconds=5;MaximumTaskProcesses=16;IdentityObservation=if($null -ne $rootIdentity){'exact_cim'}else{'original_handle_and_launch_argv_only'};Processes=@($ownedTracked.Values|Sort-Object Depth,Pid);CredentialArguments=$false})
    Write-OwnedJson $ownedResultPath ([ordered]@{Status=$ownedStatus;Pid=if($ownedStarted){$ownedProcess.Id}else{$null};Started=$ownedStartUtc;ParentPid=$PID;ExitCode=$ownedExitCode;FailureCode=$ownedFailure;RootClosure=$ownedRootClosure;ElapsedSeconds=[Math]::Round($ownedWatch.Elapsed.TotalSeconds,3);TimeoutSeconds=$ownedTimeout;MaximumPollIterations=300;MaximumTaskProcesses=16;QueryTimeoutSeconds=5;ObservationErrorCount=$ownedErrorCount;ObservationErrors=@($ownedErrors.ToArray());CleanupErrors=@($ownedCleanupErrors.ToArray());CleanupElapsedSeconds=[Math]::Round($ownedCleanupWatch.Elapsed.TotalSeconds,3);FilePath=$ownedPwsh;Arguments=$ownedArguments;IdentityPath=$ownedIdentityPath;StdoutPath=$ownedPrefix+'.stdout.log';StderrPath=$ownedPrefix+'.stderr.log';StdoutBytes=if($null -ne $ownedStdout){$ownedStdout.BytesCaptured}else{0};StderrBytes=if($null -ne $ownedStderr){$ownedStderr.BytesCaptured}else{0};MaximumBytesPerLog=4194304;Probe=$ownedIsProbe;Batch=$Batch;ApiRequestIssuedByGuardian=$false;PreservesBatchResults=$true;Processes=@($ownedTracked.Values|Sort-Object Depth,Pid);Limitation='Only this fixed PS7 API worker and its observed Windows descendants; failed exact queries never prove absence. Unproved identities remain reported, never signalled. No complete-machine snapshot. Raw output is retained byte-for-byte up to a 4MiB prefix per stream; a limit makes closure ineligible for success.'})
    $ownedCaptureCancellation.Dispose();$ownedProcess.Dispose()
}
Write-Output "Owned guardian completed: $ownedStatus, child exit $ownedExitCode; result: $ownedResultPath"
if($ownedStatus -ceq 'Succeeded'){exit 0}
if($ownedStatus -ceq 'Failed' -and $null -ne $ownedExitCode -and $ownedExitCode -ne 0){exit $ownedExitCode}
exit 1
