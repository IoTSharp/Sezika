param(
    [Parameter(Mandatory)][ValidateSet('smoke','matrix')][string]$Phase,
    [switch]$WindowGranted,
    [string[]]$BlockingIdentityPaths = @('D:\source\Sezika\.artifacts\processes\20260925-162727-006-s3-aot-linux-simd-final.identity.json')
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or newer is required.' }
if (!$WindowGranted) { throw 'The coordinating agent must release the inference window before this script runs.' }
if ($BlockingIdentityPaths.Count -gt 16) { throw 'At most 16 known blocker identities are accepted.' }
$root = 'D:\source\Sezika'
$work = Join-Path $root '.artifacts/s5-profile-20260926'
$dll = Join-Path $root '.artifacts/build/s5-profile-20260926/bin/Sezika.Benchmarks/release/Sezika.Benchmarks.dll'
$checkWatch = [Diagnostics.Stopwatch]::StartNew()
$observations = [Collections.Generic.List[object]]::new()
foreach ($identityPath in $BlockingIdentityPaths) {
    if ($checkWatch.Elapsed.TotalSeconds -ge 30) { throw 'Resource identity check timed out.' }
    $identity = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $([int]$identity.Pid)"
    $same = $false
    if ($null -ne $process) {
        $startDifference = [Math]::Abs(($process.CreationDate.ToUniversalTime() - ([DateTimeOffset]$identity.Started).UtcDateTime).TotalMilliseconds)
        $same = $startDifference -lt 1 -and $process.CommandLine -ceq $identity.CommandLine
    }
    $observations.Add([ordered]@{ identity_path=$identityPath; pid=$identity.Pid; started=$identity.Started; same_process_active=$same })
    if ($same) { throw "Known inference task PID $($identity.Pid) is still active; no performance process started." }
    $resultPath = $identityPath.Replace('.identity.json', '.result.json')
    if (!(Test-Path -LiteralPath $resultPath)) { throw "Known task has no completed runner record: $resultPath" }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($result.Status -notin @('Succeeded','Failed','TimedOut')) { throw 'Known task has no terminal runner status.' }
}
$linuxRecordPath = Join-Path $root '.artifacts/s3-aot-20260926/linux-simd-final-linux-process.json'
$linuxRecord = Get-Content -LiteralPath $linuxRecordPath -Raw | ConvertFrom-Json
if (!$linuxRecord.completed) { throw 'The known Linux inference supervisor has not completed.' }

$output = Join-Path $work "cuda-$Phase.json"
$idleOutput = Join-Path $work "cuda-$Phase-window.json"
if ((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath $idleOutput)) { throw 'Existing run evidence must not be overwritten.' }
$idle = [ordered]@{ checked_utc=[DateTime]::UtcNow.ToString('O'); coordinator_window_granted=$true;
    known_windows_tasks=$observations; linux_supervisor=$linuxRecordPath; linux_completed=$linuxRecord.completed;
    scope='Checks known task identities and coordinator release; does not claim machine-wide absence of unrelated processes.' }
[IO.File]::WriteAllText($idleOutput, ($idle | ConvertTo-Json -Depth 10) + "`n", [Text.UTF8Encoding]::new($false))

$matrix = $Phase -eq 'matrix'
if ($matrix) {
    $smoke = Get-Content -LiteralPath (Join-Path $work 'cuda-smoke.json') -Raw | ConvertFrom-Json
    if ($smoke.status -ne 'passed' -or $smoke.profile.status -ne 'complete' -or !$smoke.model_objects_collected -or
        $smoke.cuda_after_unload.owned_bytes -ne 0 -or $smoke.cuda_after_unload.release_failure_count -ne 0) {
        throw 'The smoke report and returned resources must pass before the matrix.'
    }
}
$arguments = @($dll, '--mode','profile','--backend','cuda','--model',(Join-Path $root '.artifacts/models/laya-mmbert'),
    '--lengths',$(if ($matrix) {'short,medium,long'} else {'short'}),
    '--questions',$(if ($matrix) {'1,8,32'} else {'1'}),
    '--samples',$(if ($matrix) {'3'} else {'1'}),
    '--warmup',$(if ($matrix) {'1'} else {'0'}),'--cycles','1',
    '--timeout-seconds',$(if ($matrix) {'1700'} else {'150'}),
    '--cpu','13th Gen Intel(R) Core(TM) i9-13900HX','--environment','windows-local', '--output',$output)
& (Join-Path $root 'tools/Invoke-BoundedProcess.ps1') -FilePath 'C:\Program Files\dotnet\dotnet.exe' `
    -ArgumentList $arguments -TimeoutSeconds $(if ($matrix) {1750} else {180}) -LogName "s5-profile-cuda-$Phase" -WorkingDirectory $root
$report = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
$expectedCases = if ($matrix) {9} else {1}
$expectedSamples = if ($matrix) {3} else {1}
$checkWatch.Restart()
if ($report.status -ne 'passed' -or $report.backend -ne 'cuda' -or $report.profile.status -ne 'complete' -or
    $report.profile.planned_cases -ne $expectedCases -or $report.profile.cases.Count -ne $expectedCases -or
    $report.profile.successful_cases -ne $expectedCases -or $report.profile.request_coverage -ne 1 -or
    $report.profile.question_coverage -ne 1 -or !$report.model_objects_collected -or
    $report.request_token_budget -ne 32768 -or $report.request_deadline_seconds -ne 300 -or $report.code_artifacts.Count -ne 4) {
    throw 'Performance report did not pass identity, coverage, request budget or collection gates.'
}
$alignment = $report.alignment
if ($null -eq $alignment -or $alignment.encoder_max_absolute_error -gt $alignment.encoder_tolerance -or
    $alignment.logits_max_absolute_error -gt $alignment.logits_tolerance -or
    $alignment.probability_max_absolute_error -gt $alignment.probability_tolerance) { throw 'Numerical smoke gate failed.' }
$unload = $report.cuda_after_unload
if ($null -eq $unload -or $unload.owned_bytes -ne 0 -or $unload.owned_allocation_count -ne 0 -or
    $unload.loaded_module_count -ne 0 -or $unload.release_failure_count -ne 0) { throw 'CUDA resources did not return to zero.' }
$expectedTokens = @{ 'short-1'=36; 'short-8'=302; 'short-32'=1206; 'medium-1'=116; 'medium-8'=942;
    'medium-32'=3766; 'long-1'=516; 'long-8'=4142; 'long-32'=16566 }
foreach ($row in $report.profile.cases) {
    if ($checkWatch.Elapsed.TotalSeconds -ge 30) { throw 'Report validation timed out.' }
    if ($row.status -ne 'measured' -or $row.end_to_end_milliseconds.Count -ne $expectedSamples -or
        $row.actual_sequences.Count -ne $row.questions -or $row.rendering_verification -ne 'exact_token_marker_type_match' -or
        $row.proposed_truncation -or $row.rendered_total_tokens -ne $expectedTokens[$row.id] -or
        $row.response.usage.token_count -ne $expectedTokens[$row.id] -or $row.resource_observation_errors.Count -ne 0 -or
        $row.cuda_telemetry_status -ne 'measured_independent_instrumented_request') { throw "Invalid measured row $($row.id)." }
    for ($index=0; $index -lt $row.questions; $index++) {
        if ($checkWatch.Elapsed.TotalSeconds -ge 30) { throw 'Report sequence validation timed out.' }
        $actual = $row.actual_sequences[$index]
        $expected = $row.rendered_sequences[$index]
        if ($actual.tokens_sha256 -ne $expected.tokens_sha256 -or $actual.token_count -ne $expected.token_count -or
            $actual.type_id -ne $expected.type_id -or ($actual.markers -join ',') -ne ($expected.markers -join ',')) {
            throw "Actual token/marker/type differs for $($row.id)."
        }
    }
}
Write-Output "Validated $expectedCases real CUDA profile rows, numerical smoke and complete resource return: $output"
