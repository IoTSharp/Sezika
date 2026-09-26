param(
    [Parameter(Mandatory)][string]$DotnetPath,
    [Parameter(Mandatory)][string]$EvaluationDll,
    [Parameter(Mandatory)][string]$Dataset,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$DatasetSha256,
    [Parameter(Mandatory)][ValidateRange(1,10000)][int]$DatasetTotal,
    [Parameter(Mandatory)][string]$ReferenceCapture,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or newer is required.' }
$watch = [Diagnostics.Stopwatch]::StartNew()
$batchSeconds = 360
$dotnet = [IO.Path]::GetFullPath($DotnetPath)
$evaluation = [IO.Path]::GetFullPath($EvaluationDll)
$datasetPath = [IO.Path]::GetFullPath($Dataset)
$target = [IO.Path]::GetFullPath($OutputDirectory)
if ([IO.Path]::Exists($target)) { throw 'Checks require a new output directory.' }
$referencePath = [IO.Path]::GetFullPath($ReferenceCapture)
if (-not [IO.File]::Exists($dotnet) -or -not [IO.File]::Exists($evaluation) -or -not [IO.File]::Exists($datasetPath)) {
    throw 'Dotnet, Evaluation DLL and dataset must be existing files.'
}
if ((Get-Item -LiteralPath $referencePath).Length -gt 32MB) { throw 'Capture exceeds 32 MiB.' }
$source = [IO.File]::ReadAllText($referencePath)
$original = $source | ConvertFrom-Json -AsHashtable
if ($original.cases.Count -lt 1 -or $original.cases.Count -gt 46 -or $original.cases[0].status -ne 'answered' -or $original.cases[0].primitive -ne 'boolean') {
    throw 'Use a completed capture whose first case is an answered Boolean.'
}
[IO.Directory]::CreateDirectory($target) | Out-Null
$runner = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Invoke-BoundedProcess.ps1'))
$results = [Collections.Generic.List[object]]::new()
$expectedErrors = [ordered]@{
    probabilities = 'Captured probabilities differ from unit-temperature logits.'
    prediction = 'Captured prediction differs from the recorded distribution.'
    incomplete = 'Incomplete or failed capture cannot be scored as a completed selection.'
    selection = 'Capture selection and manifest counts are inconsistent.'
    'selected-ids' = 'Selected case IDs differ from captured cases.'
    'failed-with-numbers' = 'Failed capture carries numerical output or lacks failure semantics.'
    'answered-with-failure' = 'Answered capture must not carry failure semantics.'
    'duplicate-json' = 'Duplicate evaluation JSON property.'
}
$mutations = @($expectedErrors.Keys)

function Assert-BatchBudget {
    if ($watch.Elapsed.TotalSeconds -ge $batchSeconds) { throw 'Capture checks exceeded 360 seconds.' }
}

function Get-InvocationTimeout {
    Assert-BatchBudget
    # Reserve bounded runner cleanup/output-drain time before the batch deadline.
    $remaining = [int][Math]::Floor($batchSeconds - $watch.Elapsed.TotalSeconds) - 15
    if ($remaining -lt 1) { throw 'Capture checks have insufficient time for another bounded process and cleanup.' }
    return [Math]::Min(40, $remaining)
}

function Read-RunEvidence([string]$LogDirectory) {
    Assert-BatchBudget
    $files = [Collections.Generic.List[string]]::new()
    $iterator = [IO.Directory]::EnumerateFiles($LogDirectory).GetEnumerator()
    try {
        # This new, case-owned directory has four runner artifacts; inspect at most four.
        for ($item = 0; $item -lt 4; $item++) {
            Assert-BatchBudget
            if (-not $iterator.MoveNext()) { break }
            $files.Add([IO.Path]::GetFullPath($iterator.Current))
        }
    } finally { $iterator.Dispose() }
    $resultFiles = @($files | Where-Object { $_.EndsWith('.result.json', [StringComparison]::Ordinal) })
    $identityFiles = @($files | Where-Object { $_.EndsWith('.identity.json', [StringComparison]::Ordinal) })
    $stderrFiles = @($files | Where-Object { $_.EndsWith('.stderr.log', [StringComparison]::Ordinal) })
    $stdoutFiles = @($files | Where-Object { $_.EndsWith('.stdout.log', [StringComparison]::Ordinal) })
    if ($files.Count -ne 4 -or $resultFiles.Count -ne 1 -or $identityFiles.Count -ne 1 -or $stderrFiles.Count -ne 1 -or $stdoutFiles.Count -ne 1) {
        throw 'Each case requires one runner result, identity, stdout and stderr artifact.'
    }
    foreach ($path in @($resultFiles[0], $identityFiles[0], $stderrFiles[0])) {
        Assert-BatchBudget
        if ((Get-Item -LiteralPath $path).Length -gt 1MB) { throw 'Case process evidence exceeds 1 MiB.' }
    }
    $result = [IO.File]::ReadAllText($resultFiles[0]) | ConvertFrom-Json
    $identity = [IO.File]::ReadAllText($identityFiles[0]) | ConvertFrom-Json
    $stderr = [IO.File]::ReadAllText($stderrFiles[0]).Trim()
    if ($result.Pid -ne $identity.Pid -or $result.CleanupErrors.Count -ne 0 -or
        [IO.Path]::GetFullPath($result.StderrPath) -cne $stderrFiles[0]) {
        throw 'Case process identity, stderr path or cleanup evidence is inconsistent.'
    }
    return [pscustomobject]@{ ResultPath=$resultFiles[0]; IdentityPath=$identityFiles[0]; StderrPath=$stderrFiles[0]
        Result=$result; Identity=$identity; Stderr=$stderr }
}

# A working positive control is mandatory: unrelated setup failures must never count as rejection tests.
$baselineReport = Join-Path $target 'baseline.report.json'
$baselineLogs = Join-Path $target 'processes/baseline'
$outerSeconds = Get-InvocationTimeout
Write-Output 'Capture check 1/9: unmodified baseline must score successfully'
& $runner -FilePath $dotnet -ArgumentList @(
    $evaluation, '--score-capture', $datasetPath, $DatasetSha256, [string]$DatasetTotal,
    $referencePath, $baselineReport, [string][Math]::Min(30, $outerSeconds)
) -TimeoutSeconds $outerSeconds -LogName 'evaluation-capture-baseline' -LogDirectory $baselineLogs
Assert-BatchBudget
$baseline = Read-RunEvidence $baselineLogs
if ($baseline.Result.Status -ne 'Succeeded' -or $baseline.Result.ExitCode -ne 0 -or -not [IO.File]::Exists($baselineReport)) {
    throw 'Unmodified capture baseline did not successfully produce its report.'
}
if ((Get-Item -LiteralPath $baselineReport).Length -gt 32MB) { throw 'Baseline report exceeds 32 MiB.' }
$baselineValue = [IO.File]::ReadAllText($baselineReport) | ConvertFrom-Json
if ($baselineValue.MeasurementOrigin -cne 'offline_scoring_of_existing_capture_not_new_inference_or_parity_proof' -or
    $baselineValue.Processed -ne $original.cases.Count -or $baselineValue.DatasetTotal -ne $DatasetTotal -or
    -not $baselineValue.DatasetSha256.Equals($DatasetSha256, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Baseline origin, processed count or frozen dataset identity is incorrect.'
}
$results.Add([pscustomobject]@{ Case='baseline'; Passed=$true; Rejected=$false; SyntheticMutation=$false
    ExpectedExitCode=0; ExpectedError=$null; ObservedError=$baseline.Stderr
    ProcessResultPath=$baseline.ResultPath; ProcessIdentityPath=$baseline.IdentityPath; StderrPath=$baseline.StderrPath
    ProcessResult=$baseline.Result; ProcessIdentity=$baseline.Identity })

for ($index = 0; $index -lt $mutations.Count; $index++) {
    Assert-BatchBudget
    $name = $mutations[$index]
    $changed = $source | ConvertFrom-Json -AsHashtable
    switch ($name) {
        'probabilities' { $changed.cases[0].probabilities = if ([Math]::Abs($changed.cases[0].probabilities[0] - 0.5) -gt 0.01) { @(0.5,0.5) } else { @(0.8,0.2) } }
        'prediction' { $changed.cases[0].prediction.boolean = -not $changed.cases[0].prediction.boolean }
        'incomplete' { $changed.measurement_status = 'in_progress' }
        'selection' { $changed.selected_case_count = $changed.cases.Count + 1 }
        'selected-ids' { $changed.selected_case_ids = @($changed.cases.id); $changed.selected_case_ids[0] = 'not-the-captured-id' }
        'failed-with-numbers' { $changed.cases[0].status = 'failed'; $changed.cases[0].failure = @{type='synthetic_test_failure'} }
        'answered-with-failure' { $changed.cases[0].failure = @{type='synthetic_test_failure'} }
        'duplicate-json' { }
    }
    $json = $changed | ConvertTo-Json -Depth 64
    if ($name -eq 'duplicate-json') { $json = $json.Insert($json.IndexOf('{') + 1, '"schema_version":"invalid",') }
    $inputPath = Join-Path $target "$name.capture.json"
    $reportPath = Join-Path $target "$name.report.json"
    [IO.File]::WriteAllText($inputPath, $json, [Text.UTF8Encoding]::new($false))
    Write-Output "Capture check $($index+2)/9: reject $name with its expected reason"
    $caseLogs = Join-Path $target "processes/$name"
    $outerSeconds = Get-InvocationTimeout
    $rejected = $false
    try {
        & $runner -FilePath $dotnet -ArgumentList @(
            $evaluation, '--score-capture', $datasetPath,
            $DatasetSha256, [string]$DatasetTotal, $inputPath, $reportPath, [string][Math]::Min(30, $outerSeconds)
        ) -TimeoutSeconds $outerSeconds -LogName "evaluation-reject-$name" -LogDirectory $caseLogs
    } catch {
        if ($_.Exception.Message -ne 'Process failed with exit code 1.') { throw }
        $rejected = $true
    }
    Assert-BatchBudget
    $evidence = Read-RunEvidence $caseLogs
    $expectedError = 'evaluation-oracle: InvalidDataException: ' + $expectedErrors[$name]
    if (-not $rejected -or $evidence.Result.Status -ne 'Failed' -or $evidence.Result.ExitCode -ne 1 -or
        $evidence.Stderr -cne $expectedError -or [IO.File]::Exists($reportPath)) {
        throw "Capture check did not fail for its intended validation reason: $name; expected=$expectedError; observed=$($evidence.Stderr)"
    }
    $results.Add([pscustomobject]@{ Case=$name; Passed=$true; Rejected=$true; SyntheticMutation=$true
        ExpectedExitCode=1; ExpectedError=$expectedError; ObservedError=$evidence.Stderr
        ProcessResultPath=$evidence.ResultPath; ProcessIdentityPath=$evidence.IdentityPath; StderrPath=$evidence.StderrPath
        ProcessResult=$evidence.Result; ProcessIdentity=$evidence.Identity })
}
Assert-BatchBudget
[IO.File]::WriteAllText((Join-Path $target 'checks.json'), ($results | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
Write-Output "Passed $($results.Count) capture checks (one baseline, eight specific rejections); mutated inputs are testing artifacts, not inference evidence."
