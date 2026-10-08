[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z][a-z0-9_-]{0,63}$')][string]$Label,
    [ValidateSet('Full','Tiny')][string]$Mode = 'Full',
    [string[]]$ReceiptFiles = @(),
    [string[]]$PriorClosureFiles = @(),
    [string[]]$AdditionalIdentityFiles = @(),
    [string]$OutputDirectory = 'D:\source\Sezika\.artifacts\gpu-learnability-20261008',
    [string]$CancelFile = 'D:\source\Sezika\.artifacts\gpu-learnability-closure-prep-20261008\cancel.request',
    [switch]$SkipPausedFiles
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 required.' }
$task = 'D:\source\Sezika\.artifacts\gpu-learnability-20261008'
$prep = 'D:\source\Sezika\.artifacts\gpu-learnability-closure-prep-20261008'
$runnerPrep = 'D:\source\Sezika\.artifacts\gpu-learnability-runner-prep-20261008'
$repo = 'D:\source\Sezika'
$clock = [Diagnostics.Stopwatch]::StartNew()
$errors = [Collections.Generic.List[string]]::new()
$expected = [Collections.Generic.List[object]]::new()
$receiptRows = [Collections.Generic.List[object]]::new()
$comparisonRows = [Collections.Generic.List[object]]::new()
$pausedRows = [Collections.Generic.List[object]]::new()
$registeredSources = [Collections.Generic.List[object]]::new()
$enumeratedDirectories = 0
$observedReceiptCount = 0
$matching = 0; $unknown = 0; $currentCheckerCount = 0; $queryCount = 0
$self = $null; $failure = $null; $cancelled = $false

function Check-Budget([string]$Phase) {
    if ($clock.Elapsed.TotalSeconds -ge 55) { throw "Closure work budget exceeded in $Phase; five seconds reserved for report." }
    if ($CancelFile -and [IO.File]::Exists($CancelFile)) { $script:cancelled = $true; throw "Closure cancellation requested in $Phase." }
}
function Check-ScopedPath([string]$Path, [switch]$Output) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $allowed = if ($Output) { @($task,$prep) } else { @($task,$prep,$runnerPrep) }
    $inside = $false
    foreach ($root in $allowed) {
        Check-Budget 'path guard'
        if ($resolved -ceq $root -or $resolved.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)) { $inside = $true }
    }
    if (-not $inside) { throw "Path outside exact closure task scopes: $resolved" }
    return $resolved
}
function Read-Json([string]$Path) {
    Check-Budget 'JSON read'
    $resolved = Check-ScopedPath $Path
    $info = Get-Item -LiteralPath $resolved
    if (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $info.Length -gt 2MB) { throw "JSON link/size bound: $resolved" }
    $raw = [IO.File]::ReadAllText($resolved)
    $value = ConvertFrom-Json -InputObject $raw -DateKind String
    $registeredSources.Add([ordered]@{ path=$resolved; sha256=(Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash; bytes=$info.Length })
    return $value
}
function Add-Expected($Value, [string]$Source, [string]$Kind, [object[]]$StructuredArguments = @()) {
    Check-Budget 'identity registration'
    if ($null -eq $Value) { return }
    if ($expected.Count -ge 500) { throw 'Known identity record cap 500 exceeded.' }
    $id = if ($null -ne $Value.Pid) { $Value.Pid } else { $Value.ProcessId }
    $started = if ($Value.CreationDate) { $Value.CreationDate } else { $Value.Started }
    $parent = if ($null -ne $Value.ParentPid) { $Value.ParentPid } else { $Value.ParentProcessId }
    if ($null -eq $id -or [int64]$id -le 0 -or [int64]$id -gt [int]::MaxValue) { throw "Invalid registered PID in $Source" }
    if (-not $started -or $null -eq $parent -or [int64]$parent -lt 0 -or [int64]$parent -gt [int]::MaxValue) { throw "Missing registered start/parent identity in $Source" }
    $null=[DateTimeOffset]::Parse($started,[Globalization.CultureInfo]::InvariantCulture)
    # A recorded short-lived descendant can have a null CIM command line.
    # Preserve that gap: only absent/reused PID can close it; a same-start live
    # process still fails below as unknown and must never be signalled.
    if ($Kind -notin @('retained_root_handle','recorded_process_missing_native_argv') -and [string]::IsNullOrWhiteSpace($Value.CommandLine)) { throw "Missing registered native argv in $Source" }
    $expected.Add([pscustomobject]@{
        Pid=[int]$id; Started=$started; CommandLine=$Value.CommandLine; ParentPid=$parent
        Source=$Source; Kind=$Kind; Arguments=$StructuredArguments
        StartedToleranceTicks=if ($Kind -eq 'retained_root_handle') { 10 } else { 0 }
    })
}
function Add-Receipt([string]$Path) {
    Check-Budget 'receipt registration'
    if ($receiptRows.Count -ge 100) { throw 'Result receipt cap 100 exceeded, including independent runner-prep receipts.' }
    $r = Read-Json $Path
    if (-not $r.Pid -or -not $r.Started -or -not $r.FilePath -or -not $r.Status -or $null -eq $r.ParentPid -or $null -eq $r.Launcher) { throw "Incomplete bounded-process result receipt: $Path" }
    if ($r.Launcher.Pid -ne $r.ParentPid) { throw "Result launcher/parent identity mismatch: $Path" }
    $receiptRows.Add([ordered]@{ path=$Path; status=$r.Status; exitCode=$r.ExitCode; cleanupErrors=@($r.CleanupErrors); limitation=$r.Limitation })
    $trackedRoot = $false; $processCount=0
    foreach ($process in @($r.Processes)) {
        Check-Budget 'receipt process array'
        if (++$processCount -gt 500) { throw 'Per-receipt process cap exceeded.' }
        if ($process.Pid -eq $r.Pid) { $trackedRoot = $true }
        $kind = 'recorded_process'
        if ([string]::IsNullOrWhiteSpace($process.CommandLine)) {
            if ($process.Pid -eq $r.Pid -or $process.ParentPid -ne $r.Pid -or $process.Depth -ne 1) { throw "Unproven partial descendant chain in $Path" }
            $rootProof = @($r.Processes | Where-Object { $_.Pid -eq $r.Pid -and $_.ParentPid -eq $r.ParentPid -and -not [string]::IsNullOrWhiteSpace($_.CommandLine) })
            if ($rootProof.Count -ne 1) { throw "Missing complete root proof for partial descendant in $Path" }
            $rootTime = [DateTimeOffset]::Parse($rootProof[0].CreationDate,[Globalization.CultureInfo]::InvariantCulture)
            $childTime = [DateTimeOffset]::Parse($process.CreationDate,[Globalization.CultureInfo]::InvariantCulture)
            if ($childTime -lt $rootTime) { throw "Partial descendant predates root in $Path" }
            $kind = 'recorded_process_missing_native_argv'
        }
        Add-Expected $process $Path $kind
    }
    Add-Expected $r.Launcher $Path 'recorded_launcher'
    if (-not $trackedRoot -and $r.Pid) {
        # Preserve short-lived root handle facts; do not invent a native argv string.
        $identityPath = $Path.Substring(0,$Path.Length-'.result.json'.Length)+'.identity.json'
        $nativeCommand = $null
        if ([IO.File]::Exists($identityPath)) { $identity = Read-Json $identityPath; $nativeCommand = $identity.CommandLine }
        Add-Expected ([pscustomobject]@{Pid=$r.Pid;Started=$r.Started;CommandLine=$nativeCommand;ParentPid=$r.ParentPid}) $Path 'retained_root_handle' @($r.Arguments)
    }
    if (($receiptRows.Count % 10) -eq 0) { Write-Output "Closure registered $($receiptRows.Count) receipts / $($expected.Count) identity records." }
}
function Discover-Receipts([string]$Root, [int]$MaximumDepth) {
    $queue = [Collections.Generic.Queue[object]]::new()
    $queue.Enqueue([pscustomobject]@{Path=$Root;Depth=0})
    $paths = [Collections.Generic.List[string]]::new()
    $scanClock = [Diagnostics.Stopwatch]::StartNew()
    for ($iteration=0; $iteration -lt 128 -and $queue.Count -gt 0; $iteration++) {
        Check-Budget 'receipt directory discovery'
        if ($scanClock.Elapsed.TotalSeconds -ge 15) { throw 'Receipt discovery deadline 15 seconds.' }
        $entry = $queue.Dequeue(); $script:enumeratedDirectories++
        if (($enumeratedDirectories -gt 128)) { throw 'Combined directory discovery cap 128 exceeded.' }
        $info = Get-Item -LiteralPath $entry.Path
        if (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Receipt directory is a link: $($entry.Path)" }
        foreach ($file in [IO.Directory]::EnumerateFiles($entry.Path,'*.result.json',[IO.SearchOption]::TopDirectoryOnly)) {
            Check-Budget 'receipt file discovery'
            if ($scanClock.Elapsed.TotalSeconds -ge 15 -or ++$script:observedReceiptCount -gt 100) { throw 'Receipt discovery item/time cap exceeded.' }
            $paths.Add($file)
        }
        if ($entry.Depth -lt $MaximumDepth) {
            $directoryCount=0
            foreach ($directory in [IO.Directory]::EnumerateDirectories($entry.Path,'*',[IO.SearchOption]::TopDirectoryOnly)) {
                Check-Budget 'child directory discovery'
                if ($scanClock.Elapsed.TotalSeconds -ge 15 -or ++$directoryCount -gt 128 -or $queue.Count -ge 128) { throw 'Child directory discovery item/time cap exceeded.' }
                $queue.Enqueue([pscustomobject]@{Path=$directory;Depth=$entry.Depth+1})
            }
        }
    }
    if ($queue.Count -gt 0) { throw 'Receipt directory iteration cap exceeded.' }
    return $paths.ToArray()
}

$output = Check-ScopedPath $OutputDirectory -Output
[IO.Directory]::CreateDirectory($output) | Out-Null
foreach ($directory in @($task,$prep,$output)) {
    Check-Budget 'output directory guard'
    if (((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Closure output/scope directory must not be a link.' }
}
$outputPath = Join-Path $output ($Label+'.closure.json')
if ([IO.File]::Exists($outputPath)) { throw 'Closure output already exists; choose a new label.' }
if ($ReceiptFiles.Count -gt 100 -or $PriorClosureFiles.Count -gt 10 -or $AdditionalIdentityFiles.Count -gt 5) { throw 'Explicit input item cap exceeded.' }
if ($Mode -eq 'Tiny' -and $ReceiptFiles.Count -ne 1) { throw 'Tiny mode requires exactly one actual result receipt.' }
try {
    $taskIdentity=Read-Json (Join-Path $task 'task-identity.json')
    if ($taskIdentity.task -cne 'gpu-learnability-20261008' -or $taskIdentity.thread -cne '01a11ab6-7ceb-7ac0-9235-58a8eec95729') { throw 'Local experiment task identity mismatch.' }
    $nativeSelf = Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 5
    if (-not $nativeSelf) { throw 'Could not record current closure checker identity.' }
    $self = [ordered]@{Pid=$PID;Started=$nativeSelf.CreationDate.ToUniversalTime().ToString('o');CommandLine=$nativeSelf.CommandLine;ParentPid=$nativeSelf.ParentProcessId;State='checker_current_requires_independent_recheck';RequiresIndependentRecheck=$true}
    $paths = if ($ReceiptFiles.Count -gt 0) { $ReceiptFiles } else { @(Discover-Receipts $task 2) + @(Discover-Receipts (Join-Path $runnerPrep 'tiny-logs') 0) }
    foreach ($path in $paths) { Check-Budget 'receipt collection'; Add-Receipt (Check-ScopedPath $path) }
    if ($Mode -eq 'Full') {
        $shellPath=Join-Path $task 'root-shell-identity.json'
        if ([IO.File]::Exists($shellPath)) { Add-Expected (Read-Json $shellPath) $shellPath 'root_shell_journal' }
        $readersPath=Join-Path $task 'root-readers.jsonl'
        if ([IO.File]::Exists($readersPath)) {
            $readerInfo=Get-Item -LiteralPath $readersPath
            if ($readerInfo.Length -gt 1MB -or ($readerInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reader journal size/link bound.' }
            $lineCount=0
            foreach ($line in [IO.File]::ReadLines($readersPath)) {
                Check-Budget 'reader journal'
                if (++$lineCount -gt 100) { throw 'Reader journal line cap 100 exceeded.' }
                if ([string]::IsNullOrWhiteSpace($line)) { continue }
                Add-Expected (ConvertFrom-Json -InputObject $line -DateKind String) $readersPath 'root_reader_journal'
            }
            $registeredSources.Add([ordered]@{path=$readersPath;sha256=(Get-FileHash -LiteralPath $readersPath).Hash;bytes=$readerInfo.Length})
        }
        foreach ($path in $PriorClosureFiles) {
            Check-Budget 'prior checker records'
            $prior=Read-Json $path
            Add-Expected $prior.Self $path 'prior_closure_checker'
        }
        foreach ($path in $AdditionalIdentityFiles) {
            Check-Budget 'additional identity sources'
            $records = @(Read-Json $path)
            if ($records.Count -gt 50) { throw 'Additional identity source record cap 50 exceeded.' }
            foreach ($record in $records) {
                Check-Budget 'additional identity records'
                Add-Expected $record $path 'additional_registered_reader'
            }
        }
    }
    $ids=@($expected.Pid | Sort-Object -Unique)
    if ($ids.Count -gt 500) { throw 'Unique PID cap 500 exceeded.' }
    $current=@{}
    # At most five queries of 100 exact PIDs, each bounded by five seconds.
    for ($offset=0; $offset -lt $ids.Count -and $queryCount -lt 5; $offset+=100) {
        Check-Budget 'CIM batch'
        $last=[Math]::Min($offset+99,$ids.Count-1)
        $batch=$ids[$offset..$last]
        $filterParts=[Collections.Generic.List[string]]::new()
        foreach ($id in $batch) { Check-Budget 'CIM filter construction'; $filterParts.Add('ProcessId='+$id) }
        $filter=$filterParts -join ' OR '
        $queryCount++
        $snapshot=@(Get-CimInstance Win32_Process -Filter $filter -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 5)
        if ($snapshot.Count -gt 100) { throw 'CIM result exceeds exact query batch cap.' }
        foreach ($native in $snapshot) { Check-Budget 'CIM result registration'; $current[[int]$native.ProcessId]=$native }
    }
    if ($offset -lt $ids.Count) { throw 'CIM batch iteration cap reached.' }
    foreach ($record in $expected) {
        Check-Budget 'identity comparison'
        $native=$current[$record.Pid]; $state='absent'
        if ($native) {
            if (-not $record.Started) { $state='unknown_start'; $unknown++ }
            else {
                $expectedUtc=[DateTimeOffset]::Parse($record.Started,[Globalization.CultureInfo]::InvariantCulture).UtcDateTime
                $startMatches=[Math]::Abs(($expectedUtc-$native.CreationDate.ToUniversalTime()).Ticks) -le $record.StartedToleranceTicks
                if (-not $startMatches) { $state='reused_pid' }
                elseif (-not $record.CommandLine -or $null -eq $record.ParentPid) { $state='unknown_native_argv_or_parent'; $unknown++ }
                elseif ($record.CommandLine -ceq $native.CommandLine -and $record.ParentPid -eq $native.ParentProcessId) {
                    if ($record.Pid -eq $PID) { $state='checker_current_requires_independent_recheck'; $currentCheckerCount++ }
                    else { $state='matching_live'; $matching++ }
                } else { $state='same_start_identity_changed_argv_or_parent'; $unknown++ }
            }
        }
        $comparisonRows.Add([ordered]@{pid=$record.Pid;started=$record.Started;commandLine=$record.CommandLine;nativeArgvObserved=(-not [string]::IsNullOrWhiteSpace($record.CommandLine));parentPid=$record.ParentPid;source=$record.Source;kind=$record.Kind;structuredArguments=@($record.Arguments);startedToleranceTicks=$record.StartedToleranceTicks;state=$state})
    }
    if ($Mode -eq 'Full' -and -not $SkipPausedFiles) {
        $baselinePath=Join-Path $task 'paused-files-baseline.json'
        $baseline=@(Read-Json $baselinePath)
        if ($baseline.Count -gt 20) { throw 'Paused-file baseline cap 20 exceeded.' }
        foreach ($file in $baseline) {
            Check-Budget 'paused file hashes'
            if ($file.path -ceq 'ROADMAP.md') { $pausedRows.Add([ordered]@{path=$file.path;state='excluded_authorized_document_updates';originalSha256=$file.sha256}); continue }
            $path=[IO.Path]::GetFullPath((Join-Path $repo $file.path))
            if (-not $path.StartsWith($repo+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Paused baseline path escapes repository.' }
            $actual=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            $match=$actual -ceq $file.sha256
            $pausedRows.Add([ordered]@{path=$path;originalSha256=$file.sha256;actualSha256=$actual;matches=$match;state=if($match){'unchanged'}else{'changed'}})
            if (-not $match) { $errors.Add("Paused file changed: $path") }
        }
    }
} catch { $failure=$_.Exception.Message; $errors.Add($failure) }
finally {
    $passed=$null -eq $failure -and $matching -eq 0 -and $unknown -eq 0 -and $errors.Count -eq 0
    $report=[ordered]@{
        Label=$Label; Mode=$Mode; Utc=[DateTime]::UtcNow.ToString('o'); ElapsedSeconds=[Math]::Round($clock.Elapsed.TotalSeconds,3)
        MaximumSeconds=60; WorkBudgetSeconds=55; QueryTimeoutSeconds=5; MaximumReceipts=100; MaximumRecords=500
        ReceiptCount=$receiptRows.Count; RecordCount=$expected.Count; UniquePidCount=$ids.Count; CimQueries=$queryCount; EnumeratedDirectories=$enumeratedDirectories
        ProcessClosurePassed=$passed; MatchingLive=$matching; Unknown=$unknown; CurrentCheckerRecords=$currentCheckerCount; Cancelled=$cancelled
        Self=$self; SelfRequiresIndependentRecheck=$true; SignalsSent=$false; Errors=@($errors); Receipts=@($receiptRows); Sources=@($registeredSources)
        KnownIdentityComparisons=@($comparisonRows); PausedFileChecks=@($pausedRows); PausedCheckSkipped=($Mode -eq 'Tiny' -or $SkipPausedFiles)
        Scope=if($Mode -eq 'Tiny'){'one explicit actual receipt only'}elseif($ReceiptFiles.Count -gt 0){'explicit receipt subset plus root journals and requested prior checkers'}else{'current task *.result.json through depth 2; independent runner-prep tiny receipts; root journals; requested prior checkers'}
        Limitations=@('Only registered historical identities are compared; the currently executing checker is explicitly pending an independent new-process check.','Windows PID/start/argv/parent comparison cannot verify Linux descendants or CUDA disposal.','Very short intermediate processes can exit between snapshots; retained root handle facts and structured argv are preserved without inventing a native command line.','An absent PID is established by an exact bounded CIM query; a reused PID is not signalled. Same-start identity changes or missing native argv/parent are unknown and fail.','Receipt status/exit codes are preserved and do not substitute for actual identity comparison.','New receipts created after enumeration require another new-label check; this is a bounded snapshot, not a continuous monitor.','ROADMAP baseline bytes are explicitly excluded because authorized experiment document updates are handled separately.')
    }
    $json=$report | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText($outputPath,$json,[Text.UTF8Encoding]::new($false))
    Write-Output "Closure report $outputPath; historical identities passed=$passed; live=$matching unknown=$unknown; current checker requires independent recheck."
}
if (-not $passed) { throw "Recorded process closure failed; see $outputPath" }
