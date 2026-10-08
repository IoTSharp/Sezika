param(
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [ValidateRange(1,8)][int]$MaximumCases=1,
    [ValidateRange(1,8)][int]$StartCase=1,
    [string]$PriorTinyReceipt,
    [string]$PriorTinyReceiptSha256,
    [DateTimeOffset]$SuiteStartedUtc=[DateTimeOffset]::MinValue
)
$ErrorActionPreference='Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 required.' }
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$expected=[IO.Path]::GetFullPath((Join-Path $repo '.artifacts/linux-runner-hardening-20261008'))
$root=[IO.Path]::GetFullPath($ArtifactDirectory)
if (-not $root.StartsWith($expected+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Tests require the exact task artifact subtree.' }
if ($StartCase -ne 1 -and $StartCase -ne 2) { throw 'Only tiny case 1 or continuation from case 2 is supported.' }
if ($MaximumCases -lt $StartCase) { throw 'Invalid case range.' }
function Write-NewArtifact([string]$Path, $Value) {
    $target=[IO.Path]::GetFullPath($Path)
    if (-not $target.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Artifact escaped suite root.' }
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 7))
    $stream=[IO.FileStream]::new($target,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $stream.Write($bytes); $stream.Flush($true) } finally { $stream.Dispose() }
}
$identityPath=Join-Path $root 'suite-identity.json'
$now=[DateTimeOffset]::UtcNow
$currentTick=[Diagnostics.Stopwatch]::GetTimestamp()
$frequency=[Diagnostics.Stopwatch]::Frequency
if ($StartCase -eq 1) {
    if ($MaximumCases -ne 1 -or [IO.Directory]::Exists($root)) { throw 'Tiny requires a new root and exactly case 1.' }
    if ($SuiteStartedUtc -ne [DateTimeOffset]::MinValue -and $SuiteStartedUtc -gt $now) { throw 'Future suite timestamp is forbidden.' }
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $identity=[pscustomobject]@{
        SchemaVersion=1; Root=$root; SuiteStartedUtc=$now.ToString('o')
        StartedTimestamp=$currentTick; Frequency=$frequency; DeadlineTimestamp=$currentTick+180*$frequency
        TestSourceSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
        CaseCount=8; StopStartingAtSeconds=140; TotalSeconds=180
    }
    Write-NewArtifact $identityPath $identity
} else {
    if (-not [IO.Directory]::Exists($root)) { throw 'Continuation requires the original tiny root.' }
    $identityBytes=[IO.File]::ReadAllBytes($identityPath)
    if ($identityBytes.Length -gt 8192) { throw 'Suite identity bound exceeded.' }
    $identity=([Text.Encoding]::UTF8.GetString($identityBytes) | ConvertFrom-Json)
    $originalUtc=[DateTimeOffset]::Parse($identity.SuiteStartedUtc)
    if ($identity.SchemaVersion -ne 1 -or $identity.Root -cne $root -or $identity.Frequency -ne $frequency -or $identity.StartedTimestamp -gt $currentTick -or $originalUtc -gt $now) { throw 'Suite identity or clock mismatch.' }
    if ($SuiteStartedUtc -ne [DateTimeOffset]::MinValue -and $SuiteStartedUtc -ne $originalUtc) { throw 'SuiteStartedUtc must equal the frozen tiny identity.' }
    if ($identity.DeadlineTimestamp -ne ($identity.StartedTimestamp+180*$frequency) -or $identity.TotalSeconds -ne 180 -or $identity.StopStartingAtSeconds -ne 140) { throw 'Suite deadline was changed.' }
    if ((Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash -cne $identity.TestSourceSha256) { throw 'Suite source changed after tiny.' }
    $expectedTiny=[IO.Path]::GetFullPath((Join-Path $root 'normal-child/receipt.json'))
    if (-not $PriorTinyReceipt -or [IO.Path]::GetFullPath($PriorTinyReceipt) -cne $expectedTiny -or $PriorTinyReceiptSha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Continuation requires the exact original tiny receipt and hash.' }
    $prior=[IO.File]::ReadAllBytes($expectedTiny)
    if ($prior.Length -gt 65536 -or [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($prior)) -cne $PriorTinyReceiptSha256.ToUpperInvariant()) { throw 'Prior tiny receipt hash mismatch.' }
    $tiny=([Text.Encoding]::UTF8.GetString($prior) | ConvertFrom-Json)
    $expectedSuiteHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($identityBytes))
    if ($tiny.SuiteIdentityPath -cne $identityPath -or $tiny.SuiteIdentitySha256 -cne $expectedSuiteHash -or $tiny.WindowsStatus -ne 'Succeeded' -or $tiny.WindowsExitCode -ne 0 -or $tiny.WindowsCleanupErrorsCount -ne 0 -or -not $tiny.WindowsInvocationMatches) { throw 'Tiny receipt does not bind this suite or valid Windows completion.' }
    if ($tiny.Run -cne [IO.Path]::GetDirectoryName($expectedTiny) -or $tiny.Linux.status -ne 'completed' -or $tiny.LinuxFinalCheckComplete -ne $true -or $tiny.FixtureOnly) { throw 'Original tiny did not complete.' }
}
$cases=@(
    @{Name='normal-child'; Args=@('/bin/bash','-c','/bin/sleep 0.8 & wait'); Expected='completed'; Complete=$true},
    @{Name='timeout-term-ignore'; Args=@('/bin/bash','-c','trap "" TERM; exec /bin/bash -c ''trap "" TERM; /bin/sleep 10 & wait'''); Expected='timed_out'; Complete=$true},
    @{Name='cancel-file'; Args=@('/bin/sleep','10'); Expected='cancelled_file'; Complete=$true; Cancel=1},
    @{Name='cancel-signal'; Args=@('/bin/sleep','10'); Expected='cancelled_signal'; Complete=$true; Cancel=1; Mode='signal'},
    @{Name='new-session-adopted'; Args=@('/bin/bash','-c','/usr/bin/setsid /bin/sleep 10 & kill -s STOP $$'); Expected='timed_out'; Complete=$true},
    @{Name='expired-cleanup'; Args=@('/bin/true'); Expected='completed'; Complete=$false; Fixture='expired-cleanup'},
    @{Name='truncate-final'; Args=@('/bin/true'); Expected='completed'; Complete=$false; Fixture='truncate-final'},
    @{Name='identity-seams-control'; Args=@('/bin/sleep','10'); Expected='cancelled_file'; Complete=$true; Fixture='identity-seams'; Cancel=1}
)
$results=[Collections.Generic.List[object]]::new()
$runner=Join-Path $repo 'tools/Invoke-BoundedLinuxProcess.ps1'
$suiteFailure=$null
# Human-reviewed: one loop, <=8 cases AND one persisted monotonic 180s total.
# Start no new case after 140s, leave 40s for the active case/finally. Cancellation
# file is checked before each case; the runner owns each case's process finally.
try {
    for ($index=$StartCase-1; $index -lt $MaximumCases -and $index -lt 8; $index++) {
        $elapsed=([Diagnostics.Stopwatch]::GetTimestamp()-$identity.StartedTimestamp)/$frequency
        if ($elapsed -ge 140 -or [IO.File]::Exists((Join-Path $root 'cancel.request'))) { break }
        $case=$cases[$index]
        Write-Output ("CASE "+($index+1)+"/8 "+$case.Name)
        $arguments=@{Name=$case.Name; ArtifactDirectory=$root; TimeoutSeconds=3; CleanupSeconds=8; LinuxArgumentList=$case.Args}
        if ($case.ContainsKey('Cancel')) { $arguments.CancelAfterSeconds=$case.Cancel }
        if ($case.ContainsKey('Mode')) { $arguments.CancellationMode=$case.Mode }
        if ($case.ContainsKey('Fixture')) { $arguments.Fixture=$case.Fixture }
        $output=@(& $runner @arguments)
        $receipt=$output[-1]
        if ($receipt -isnot [pscustomobject] -or $receipt.SchemaVersion -ne 1) { throw 'Structured runner receipt missing.' }
        $expectedExit=switch ($case.Expected) { 'completed' { if ($case.Complete) { 0 } else { 127 } }; 'timed_out' {124}; 'cancelled_file' {125}; 'cancelled_signal' {125}; default {-1} }
        $expectedWindows=if ($expectedExit -eq 0) {'Succeeded'} else {'Failed'}
        $passedWindows=$receipt.WindowsStatus -eq $expectedWindows -and $receipt.WindowsExitCode -eq $expectedExit -and $receipt.WindowsCleanupErrorsCount -eq 0 -and $receipt.WindowsInvocationMatches -eq $true
        $passed=$passedWindows -and $receipt.Linux.status -eq $case.Expected -and $receipt.LinuxFinalCheckComplete -eq $case.Complete
        if ($case.ContainsKey('Fixture') -and $case.Fixture -eq 'expired-cleanup') {
            $passed=$passed -and $receipt.Linux.cleanup_budget_exhausted -eq '1' -and [int]$receipt.Linux.unchecked_count -gt 0
        }
        if ($case.ContainsKey('Fixture') -and $case.Fixture -eq 'truncate-final') {
            $passed=$passed -and [int]$receipt.Linux.unchecked_count -gt 0
        }
        if ($case.ContainsKey('Fixture') -and $case.Fixture -eq 'identity-seams') {
            $passed=$passed -and $receipt.Linux.control_unaffected -eq '1' -and $receipt.Linux.control_owner_closed -eq '1' -and $receipt.Linux.seam_mismatch -eq '1' -and $receipt.Linux.seam_unreadable -eq '2'
        }
        $elapsed=([Diagnostics.Stopwatch]::GetTimestamp()-$identity.StartedTimestamp)/$frequency
        if ($elapsed -gt 180) { $passed=$false }
        $results.Add([pscustomobject]@{Index=$index+1;Name=$case.Name;Passed=$passed;Status=$receipt.Linux.status;Complete=$receipt.LinuxFinalCheckComplete;Run=$receipt.Run;ElapsedSuiteSeconds=$elapsed})
        if (-not $passed) { break }
    }
} catch { $suiteFailure=$_.Exception.Message } finally {
    $elapsed=([Diagnostics.Stopwatch]::GetTimestamp()-$identity.StartedTimestamp)/$frequency
    $failedCount=@($results | Where-Object {-not $_.Passed}).Count
    $summary=[pscustomobject]@{
        StartCase=$StartCase;Requested=$MaximumCases-$StartCase+1;Completed=$results.Count
        SuiteStartedUtc=$identity.SuiteStartedUtc;ElapsedSeconds=$elapsed;Cases=$results.ToArray()
        Failure=$suiteFailure;Passed=($results.Count -eq ($MaximumCases-$StartCase+1) -and $failedCount -eq 0 -and $null -eq $suiteFailure -and $elapsed -le 180)
        Limitations='Controlled predicates do not simulate kernel faults. Raw identity/signal logs and independent PID/token closure still require review; self-declared zero is insufficient. Explicit case graph permits at most supervisor/timer/leader/one child = 4 Linux processes.'
    }
    Write-NewArtifact (Join-Path $root ('summary-'+$StartCase+'-'+$MaximumCases+'.json')) $summary
}
$summary
if (-not $summary.Passed) { throw 'Lifecycle suite incomplete or failed.' }
