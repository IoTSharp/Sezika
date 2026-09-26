param([switch]$Worker)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or newer is required.' }
$repository = 'D:\source\Sezika'
$taskDirectory = Join-Path $repository '.artifacts/s5-profile-20260926'
$proofDirectory = Join-Path $taskDirectory 'aot-gates-final'
$runner = Join-Path $repository 'tools/Invoke-BoundedProcess.ps1'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$benchmarkDll = Join-Path $repository '.artifacts/build/s5-profile-final2-20260926/bin/Sezika.Benchmarks/release/Sezika.Benchmarks.dll'
$normalConfig = [IO.Path]::ChangeExtension($benchmarkDll, '.runtimeconfig.json')

if (!$Worker) {
    if (!(Test-Path -LiteralPath $benchmarkDll) -or !(Test-Path -LiteralPath $normalConfig)) {
        throw 'The separate final build must exist before executing these checks.'
    }
    if (Test-Path -LiteralPath $proofDirectory) { throw 'Existing check evidence must not be overwritten.' }
    [IO.Directory]::CreateDirectory($proofDirectory) | Out-Null
    $powerShell = [Environment]::ProcessPath
    & $runner -FilePath $powerShell -ArgumentList @('-NoProfile','-File',$PSCommandPath,'-Worker') `
        -WorkingDirectory $repository -TimeoutSeconds 90 -LogName 's5-profile-aot-gates-workflow' `
        -LogDirectory (Join-Path $proofDirectory 'workflow-process')
    return
}

$watch = [Diagnostics.Stopwatch]::StartNew()
function Assert-WithinDeadline {
    if ($watch.Elapsed.TotalSeconds -ge 85) { throw 'AOT gate workflow exceeded its cooperative deadline.' }
}
function Write-NewUtf8([string]$Path, [string]$Text) {
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Text)
        $stream.Write($bytes)
    }
    finally { $stream.Dispose() }
}
function Get-IdentityHash([string]$Path) {
    Assert-WithinDeadline
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$summaryPath = Join-Path $proofDirectory 'checks.json'
$summary = [ordered]@{
    schema_version=1; status='running'; started_utc=[DateTime]::UtcNow.ToString('O');
    scope='Two real managed-host rejection checks; no model loading or inference. Separate final build identity.';
    max_negative_invocations=2; per_process_timeout_seconds=30; outer_workflow_timeout_seconds=90;
    worker_pid=$PID; benchmark_dll=$benchmarkDll; benchmark_sha256=$null;
    normal_runtimeconfig=$normalConfig; normal_runtimeconfig_sha256=$null;
    disabled_runtimeconfig=$null; disabled_runtimeconfig_sha256=$null;
    checks=[Collections.Generic.List[object]]::new(); error=$null; elapsed_seconds=$null
}
$summaryStream = [IO.FileStream]::new($summaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    Assert-WithinDeadline
    $summary.benchmark_sha256 = Get-IdentityHash $benchmarkDll
    $summary.normal_runtimeconfig_sha256 = Get-IdentityHash $normalConfig
    if ((Get-Item -LiteralPath $normalConfig).Length -gt 65536) { throw 'Runtime config exceeds 64 KiB.' }
    $config = Get-Content -LiteralPath $normalConfig -Raw | ConvertFrom-Json -AsHashtable
    if (!$config.runtimeOptions.Contains('configProperties')) { $config.runtimeOptions.configProperties = @{} }
    $config.runtimeOptions.configProperties['System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported'] = $false
    $config.runtimeOptions.configProperties['System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeCompiled'] = $false
    $disabledConfig = Join-Path $proofDirectory 'dynamic-disabled.runtimeconfig.json'
    Write-NewUtf8 $disabledConfig (($config | ConvertTo-Json -Depth 16) + "`n")
    $summary.disabled_runtimeconfig = $disabledConfig
    $summary.disabled_runtimeconfig_sha256 = Get-IdentityHash $disabledConfig
    $missingModel = Join-Path $proofDirectory 'model-does-not-exist'
    if (Test-Path -LiteralPath $missingModel) { throw 'Negative-check model path must not exist.' }

    $modes = @('ordinary-managed','dynamic-disabled-managed')
    for ($caseIndex=0; $caseIndex -lt 2; $caseIndex++) {
        Assert-WithinDeadline
        if ($watch.Elapsed.TotalSeconds -gt 45) { throw 'Insufficient workflow time for another bounded negative check.' }
        $mode = $modes[$caseIndex]
        $output = Join-Path $proofDirectory "$mode.json"
        $logDirectory = Join-Path $proofDirectory "$mode-process"
        if ((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath $logDirectory)) {
            throw 'Existing per-check evidence must not be overwritten.'
        }
        $benchmarkArguments = @('--require-aot','--mode','profile','--backend','simd','--model',$missingModel,
            '--lengths','short','--questions','1','--samples','1','--warmup','0','--cycles','1',
            '--timeout-seconds','10','--output',$output)
        $arguments = if ($caseIndex -eq 0) { @($benchmarkDll) + $benchmarkArguments }
            else { @('exec','--runtimeconfig',$disabledConfig,$benchmarkDll) + $benchmarkArguments }
        $check = [ordered]@{mode=$mode;status='running';report=$output;arguments=$arguments;runner_exception=$null}
        $summary.checks.Add($check)
        Write-Output "AOT negative check $($caseIndex + 1)/2: $mode"
        try {
            & $runner -FilePath $dotnet -ArgumentList $arguments -WorkingDirectory $repository `
                -TimeoutSeconds 30 -LogName "s5-profile-aot-$mode" -LogDirectory $logDirectory
        }
        catch { $check.runner_exception = $_.Exception.Message }
        Assert-WithinDeadline
        $results = @(Get-ChildItem -LiteralPath $logDirectory -Filter '*.result.json' -File)
        if ($results.Count -ne 1) { throw 'Expected exactly one runner result for the selected invocation.' }
        $resultPath = $results[0].FullName
        $identityPath = $resultPath.Replace('.result.json','.identity.json')
        $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        $identity = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
        if ($result.ExitCode -ne 2 -or $result.Status -ne 'Failed' -or @($result.CleanupErrors).Count -ne 0 -or
            $check.runner_exception -ne 'Process failed with exit code 2.') {
            throw "Unexpected runner outcome for $mode; rejection must be exit 2 with successful cleanup."
        }
        if ($identity.Pid -ne $result.Pid -or $identity.Pid -le 0 -or
            $identity.ParentPid -ne $PID -or $result.ParentPid -ne $PID -or $identity.Launcher.Pid -ne $PID -or
            [string]::IsNullOrWhiteSpace($identity.Launcher.CommandLine) -or $identity.Launcher.ParentPid -le 0 -or
            $identity.FilePath -cne $dotnet -or $result.FilePath -cne $dotnet -or
            $identity.WorkingDirectory -cne $repository -or $result.WorkingDirectory -cne $repository -or
            ($identity.Arguments | ConvertTo-Json -Compress) -cne ($arguments | ConvertTo-Json -Compress) -or
            ($result.Arguments | ConvertTo-Json -Compress) -cne ($arguments | ConvertTo-Json -Compress) -or
            [Math]::Abs((([DateTimeOffset]$identity.Started).UtcDateTime - ([DateTimeOffset]$result.Started).UtcDateTime).TotalMilliseconds) -ge 1) {
            throw 'Runner PID, start, command arguments, working directory or parent identity does not match.'
        }
        $live = Get-CimInstance Win32_Process -Filter "ProcessId = $([int]$identity.Pid)" -OperationTimeoutSec 2
        if ($null -ne $live -and [Math]::Abs(($live.CreationDate.ToUniversalTime() - ([DateTimeOffset]$identity.Started).UtcDateTime).TotalMilliseconds) -lt 1) {
            throw 'The exact negative-check process is still active.'
        }
        if ((Get-Item -LiteralPath $output).Length -gt 1048576) { throw 'Negative-check report exceeds 1 MiB.' }
        $report = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        if ($report.status -ne 'failed' -or $report.native_aot -ne $false -or $report.managed_host_detected -ne $true -or
            $report.error_code -ne 'InvalidOperationException' -or $report.error -notlike '--require-aot requires a Native AOT executable*' -or
            @($report.loads).Count -ne 0 -or $null -ne $report.manifest_sha256 -or
            $report.profile.planned_cases -ne 1 -or $report.profile.successful_cases -ne 0 -or $report.profile.status -ne 'incomplete') {
            throw 'The managed process was not rejected by the AOT gate before model identity/loading.'
        }
        if ($caseIndex -eq 1 -and ($report.is_dynamic_code_supported -ne $false -or $report.is_dynamic_code_compiled -ne $false)) {
            throw 'Dynamic-code switches did not take effect; this is not a valid disabled-switch negative check.'
        }
        $check.status = 'passed'
        $check.exit_code = $result.ExitCode
        $check.native_aot = $report.native_aot
        $check.managed_host_detected = $report.managed_host_detected
        $check.is_dynamic_code_supported = $report.is_dynamic_code_supported
        $check.is_dynamic_code_compiled = $report.is_dynamic_code_compiled
        $check.load_count = @($report.loads).Count
        $check.root_pid = $identity.Pid
        $check.root_started = $identity.Started
        $check.parent_pid = $identity.ParentPid
        $check.root_command_line_captured = ![string]::IsNullOrWhiteSpace($identity.CommandLine)
        $check.report_sha256 = Get-IdentityHash $output
        $check.result_path = $resultPath
        $check.result_sha256 = Get-IdentityHash $resultPath
        $check.identity_path = $identityPath
        $check.identity_sha256 = Get-IdentityHash $identityPath
        $check.stdout_path = $result.StdoutPath
        $check.stdout_sha256 = Get-IdentityHash $result.StdoutPath
        $check.stderr_path = $result.StderrPath
        $check.stderr_sha256 = Get-IdentityHash $result.StderrPath
        Write-Output "PASS ${mode}: managed host rejected before model loading, exit 2."
    }
    if ((Get-IdentityHash $benchmarkDll) -cne $summary.benchmark_sha256 -or
        (Get-IdentityHash $normalConfig) -cne $summary.normal_runtimeconfig_sha256) {
        throw 'The final build identity changed during the checks.'
    }
    $summary.status = 'passed'
}
catch { $summary.status = 'failed'; $summary.error = $_.Exception.Message; throw }
finally {
    $summary.elapsed_seconds = [Math]::Round($watch.Elapsed.TotalSeconds,3)
    $json = ($summary | ConvertTo-Json -Depth 16) + "`n"
    try { $summaryStream.Write([Text.UTF8Encoding]::new($false).GetBytes($json)) }
    finally { $summaryStream.Dispose() }
}
