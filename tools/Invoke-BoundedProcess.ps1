param(
    [Parameter(Mandatory)][string]$FilePath,
    [string[]]$ArgumentList = @(),
    [ValidateRange(1,1800)][int]$TimeoutSeconds = 180,
    [string]$LogName = 'process'
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or newer is required.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$logs = Join-Path $root '.artifacts/processes'
[IO.Directory]::CreateDirectory($logs) | Out-Null
$label = ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')) + '-' + ($LogName -replace '[^a-zA-Z0-9_-]', '_')
$start = [Diagnostics.ProcessStartInfo]::new($FilePath)
$start.WorkingDirectory = $root
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER'] = '1'
foreach ($arg in $ArgumentList) { $start.ArgumentList.Add($arg) }
$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
$watch = [Diagnostics.Stopwatch]::StartNew()
try {
    if (-not $process.Start()) { throw 'Process failed to start.' }
    $identity = Get-CimInstance Win32_Process -Filter "ProcessId=$($process.Id)"
    [pscustomobject]@{ Pid=$process.Id; Started=$process.StartTime.ToUniversalTime(); CommandLine=$identity.CommandLine; ParentPid=$identity.ParentProcessId; TimeoutSeconds=$TimeoutSeconds; FilePath=$FilePath; Arguments=$ArgumentList } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $logs "$label.identity.json")
    Write-Output "Started PID $($process.Id), timeout ${TimeoutSeconds}s: $FilePath $($ArgumentList -join ' ')"
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    # At most TimeoutSeconds+1 iterations, additionally bounded by elapsed time.
    for ($attempt=0; $attempt -le $TimeoutSeconds; $attempt++) {
        if ($process.WaitForExit(1000)) { break }
        if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw "Process timeout (${TimeoutSeconds}s)." }
        if (($attempt % 30) -eq 29) { Write-Output "PID $($process.Id) running: $([int]$watch.Elapsed.TotalSeconds)s" }
    }
    if (-not $process.HasExited) { throw 'Process iteration limit exceeded.' }
    $out = $stdout.GetAwaiter().GetResult()
    $err = $stderr.GetAwaiter().GetResult()
    $out | Set-Content (Join-Path $logs "$label.stdout.log")
    $err | Set-Content (Join-Path $logs "$label.stderr.log")
    Write-Output $out
    if ($err) { Write-Output $err }
    Write-Output "Completed PID $($process.Id), exit $($process.ExitCode), elapsed $([Math]::Round($watch.Elapsed.TotalSeconds,3))s"
    if ($process.ExitCode -ne 0) { throw "Process failed with exit code $($process.ExitCode)." }
} finally {
    if ($process.Id -and -not $process.HasExited) {
        # Process object pins PID/start identity; only this task's descendant tree is terminated.
        $process.Kill($true)
        [void]$process.WaitForExit(10000)
    }
    $process.Dispose()
}
