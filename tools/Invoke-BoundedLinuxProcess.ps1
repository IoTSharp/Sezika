param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]{1,48}$')][string]$Name,
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [ValidateRange(1,600)][int]$TimeoutSeconds=3,
    [ValidateRange(1,8)][int]$CleanupSeconds=8,
    [ValidateRange(0,600)][int]$CancelAfterSeconds=0,
    [ValidateSet('file','signal')][string]$CancellationMode='file',
    [ValidateSet('none','expired-cleanup','truncate-final','identity-seams')][string]$Fixture='none',
    [Parameter(Mandatory)][string[]]$LinuxArgumentList
)
$ErrorActionPreference='Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7+ required.' }
if ($CancelAfterSeconds -gt $TimeoutSeconds -or $LinuxArgumentList.Count -lt 1 -or $LinuxArgumentList.Count -gt 256) { throw 'Argument/cancellation bound exceeded.' }
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts=[IO.Path]::GetFullPath((Join-Path $repo '.artifacts'))
$boundary=[IO.Path]::GetFullPath($ArtifactDirectory)
if (-not $boundary.StartsWith($artifacts+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Artifact directory must be below the repository .artifacts.' }
if ($Fixture -ne 'none' -and -not $boundary.StartsWith((Join-Path $artifacts 'linux-runner-hardening-20261008')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Controlled fixtures are task-local only.' }
$run=[IO.Path]::GetFullPath((Join-Path $boundary $Name))
if (-not $run.StartsWith($boundary+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or [IO.Directory]::Exists($run) -or [IO.File]::Exists($run)) { throw 'Run must be CreateNew and below its artifact root.' }
# Known mounted drive conversion only, never shell interpolation or wsl discovery.
function Convert-MountedPath([string]$Path) {
    if ($Path -notmatch '^([A-Za-z]):\\(.+)$') { throw 'Only explicit drive-letter paths are supported.' }
    return '/mnt/'+$Matches[1].ToLowerInvariant()+'/'+($Matches[2] -replace '\\','/')
}
$setup=[Diagnostics.Stopwatch]::StartNew()
[long]$argumentBytes=0
foreach ($arg in $LinuxArgumentList) {
    if ($setup.Elapsed.TotalSeconds -ge 2 -or $arg.Length -gt 16384 -or $arg.Contains([char]0)) { throw 'Argument setup bound exceeded.' }
    $argumentBytes += [Text.Encoding]::UTF8.GetByteCount($arg)
}
if ($argumentBytes -gt 262144) { throw 'Total arguments exceed 256 KiB.' }
[IO.Directory]::CreateDirectory($run) | Out-Null
$linuxArgs=@('-d','Ubuntu','--exec','/bin/bash',(Convert-MountedPath (Join-Path $PSScriptRoot 'Invoke-BoundedLinux.sh')),(Convert-MountedPath $run),[string]$TimeoutSeconds,[string]$CleanupSeconds,[string]$CancelAfterSeconds,$CancellationMode,$Fixture,'--')+$LinuxArgumentList
# Work includes Linux readiness. Cleanup has one deadline and a 200ms evidence
# reserve. This outer cap adds 3s WSL startup + 2s output drain + 2s handoff.
# Invoke-BoundedProcess adds bounded Windows cleanup; it is not a hard kernel-I/O
# guarantee and cannot independently prove Linux descendants have exited.
$outer=$TimeoutSeconds+$CleanupSeconds+7
$outerFailure=$null
try {
    & (Join-Path $PSScriptRoot 'Invoke-BoundedProcess.ps1') -FilePath 'C:\Windows\System32\wsl.exe' -ArgumentList $linuxArgs -TimeoutSeconds $outer -LogName ('linux-'+$Name) -WorkingDirectory $repo -LogDirectory (Join-Path $run 'windows-logs')
} catch { $outerFailure=$_.Exception.Message }
$resultPath=Join-Path $run 'result.tsv'
$fields=@{}
if ([IO.File]::Exists($resultPath)) {
    $lines=[IO.File]::ReadAllLines($resultPath)
    if ($lines.Count -gt 64) { throw 'Linux result item bound exceeded.' }
    $read=[Diagnostics.Stopwatch]::StartNew()
    foreach ($line in $lines) {
        if ($read.Elapsed.TotalSeconds -ge 1) { throw 'Result parsing deadline exceeded.' }
        $pair=$line.Split([char]9,2)
        if ($pair.Count -ne 2 -or $fields.ContainsKey($pair[0])) { throw 'Malformed/duplicate Linux result field.' }
        $fields[$pair[0]]=$pair[1]
    }
}
$linuxComplete=$fields.Count -gt 0 -and $fields.cleanup_incomplete -eq '0' -and $fields.final_check_complete -eq '1' -and $fields.verified_live_count -eq '0' -and $fields.unknown_count -eq '0' -and $fields.unchecked_count -eq '0' -and $fields.cleanup_budget_exhausted -eq '0' -and -not $fields.ContainsKey('serialization_budget_exhausted')
$windowsResultPath=$null; $windowsResultSha256=$null; $windowsStatus=$null; $windowsExitCode=$null; $windowsCleanupCount=-1; $windowsInvocationMatches=$false
$windowsLogs=Join-Path $run 'windows-logs'
$scan=[Diagnostics.Stopwatch]::StartNew(); $found=0
if ([IO.Directory]::Exists($windowsLogs)) {
    foreach ($candidate in [IO.Directory]::EnumerateFiles($windowsLogs,'*.result.json',[IO.SearchOption]::TopDirectoryOnly)) {
        if ($scan.Elapsed.TotalSeconds -ge 1 -or $found -ge 1) { throw 'Windows result enumeration bound exceeded.' }
        $found++
        $windowsResultPath=[IO.Path]::GetFullPath($candidate)
    }
}
if ($found -eq 1) {
    $rawWindows=[IO.File]::ReadAllBytes($windowsResultPath)
    if ($rawWindows.Length -gt 65536) { throw 'Windows result exceeds 64 KiB.' }
    $windowsResultSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($rawWindows))
    $windowsRecord=([Text.Encoding]::UTF8.GetString($rawWindows) | ConvertFrom-Json)
    $windowsStatus=$windowsRecord.Status; $windowsExitCode=$windowsRecord.ExitCode
    $windowsCleanupCount=@($windowsRecord.CleanupErrors).Count
    $actualArguments=@($windowsRecord.Arguments)
    $windowsInvocationMatches=$windowsRecord.FilePath -ceq 'C:\Windows\System32\wsl.exe' -and $actualArguments.Count -eq $linuxArgs.Count
    for ($argumentIndex=0; $argumentIndex -lt $actualArguments.Count -and $argumentIndex -lt 272; $argumentIndex++) {
        if ($scan.Elapsed.TotalSeconds -ge 2) { throw 'Windows invocation comparison deadline exceeded.' }
        if ($actualArguments[$argumentIndex] -cne $linuxArgs[$argumentIndex]) { $windowsInvocationMatches=$false; break }
    }
}
$suiteIdentityPath=Join-Path $boundary 'suite-identity.json'
$suiteIdentitySha256=$null
if ([IO.File]::Exists($suiteIdentityPath)) {
    $suiteBytes=[IO.File]::ReadAllBytes($suiteIdentityPath)
    if ($suiteBytes.Length -gt 8192) { throw 'Suite identity bound exceeded.' }
    $suiteIdentitySha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($suiteBytes))
}

$receipt=[pscustomobject]@{
    SchemaVersion=1; Run=$run; PowerShellVersion=$PSVersionTable.PSVersion.ToString()
    WorkSeconds=$TimeoutSeconds; CleanupSeconds=$CleanupSeconds; OuterWorkCapSeconds=$outer
    OuterFailure=$outerFailure; LinuxFinalCheckComplete=$linuxComplete; Linux=$fields
    WindowsResultPath=$windowsResultPath; WindowsResultSha256=$windowsResultSha256; WindowsStatus=$windowsStatus; WindowsExitCode=$windowsExitCode
    WindowsCleanupErrorsCount=$windowsCleanupCount; WindowsInvocationMatches=$windowsInvocationMatches
    SuiteIdentityPath=$suiteIdentityPath; SuiteIdentitySha256=$suiteIdentitySha256
    FixtureOnly=($Fixture -ne 'none'); Arguments=$linuxArgs
    Limitation='Cooperative ownership snapshots only; blocking kernel I/O and short-lived intermediate processes are not hard-time guarantees. Missing Linux result is unknown, not zero survivors. Windows cleanup is separately bounded and does not verify Linux descendants.'
}
$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($receipt | ConvertTo-Json -Depth 6))
$stream=[IO.FileStream]::new((Join-Path $run 'receipt.json'),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try { $stream.Write($bytes); $stream.Flush($true) } finally { $stream.Dispose() }
$receipt
