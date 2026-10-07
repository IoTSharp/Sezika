param(
    [Parameter(Mandatory)][string]$ApplicationPath,
    [Parameter(Mandatory)][string]$PlanPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$CancelFile,
    [ValidateRange(1,16)][int]$MaxCases = 16
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or newer is required.' }
$watch = [Diagnostics.Stopwatch]::StartNew()
$totalSeconds = 300
$application = [IO.Path]::GetFullPath($ApplicationPath)
$sourcePlanPath = [IO.Path]::GetFullPath($PlanPath)
$target = [IO.Path]::GetFullPath($OutputDirectory)
$cancelPath = if ($CancelFile) { [IO.Path]::GetFullPath($CancelFile) } else { $null }
$runner = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Invoke-BoundedProcess.ps1'))
if (-not [IO.File]::Exists($application) -or -not [IO.File]::Exists($sourcePlanPath) -or -not [IO.File]::Exists($runner)) {
    throw 'Application, plan and bounded runner must be existing files.'
}
if ([IO.Path]::Exists($target)) { throw 'Baseline checks require a new output directory.' }
$executable = $application
$applicationArguments = @()
if ([IO.Path]::GetExtension($application).Equals('.dll', [StringComparison]::OrdinalIgnoreCase)) {
    # Compiler/runtime discovery is limited to PATH; do not scan installation trees.
    $dotnet = Get-Command dotnet -CommandType Application -ErrorAction Stop
    $executable = [IO.Path]::GetFullPath($dotnet.Source)
    $applicationArguments = @($application)
}

function Assert-Budget {
    if ($watch.Elapsed.TotalSeconds -ge $totalSeconds) { throw 'Baseline checks exceeded 300 seconds.' }
    if ($null -ne $cancelPath -and [IO.File]::Exists($cancelPath)) { throw [OperationCanceledException]::new('Baseline checks cancelled by CancelFile.') }
}

function Read-Json([string]$Path) {
    Assert-Budget
    if ((Get-Item -LiteralPath $Path).Length -gt 4MB) { throw 'Baseline check JSON exceeds 4 MiB.' }
    return [IO.File]::ReadAllText($Path) | ConvertFrom-Json -AsHashtable -Depth 64
}

function Write-Json([string]$Path, $Value) {
    Assert-Budget
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 64), [Text.UTF8Encoding]::new($false))
}

function File-Hash([string]$Path) {
    Assert-Budget
    if ((Get-Item -LiteralPath $Path).Length -gt 4MB) { throw 'Baseline check input exceeds 4 MiB.' }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Plan-References($Plan) {
    Assert-Budget
    if ($Plan.datasets.Count -lt 1 -or $Plan.datasets.Count -gt 3) { throw 'These checks require at most the three pinned baseline datasets.' }
    $references = [Collections.Generic.List[object]]::new()
    for ($datasetIndex = 0; $datasetIndex -lt $Plan.datasets.Count; $datasetIndex++) {
        Assert-Budget
        $dataset = $Plan.datasets[$datasetIndex]
        if ($dataset.comparisons.Count -lt 1 -or $dataset.comparisons.Count -gt 16) { throw 'Comparison whitelist exceeds 1..16 entries.' }
        $references.Add($dataset.reference)
        $references.Add($dataset.actual)
        for ($batchIndex = 0; $batchIndex -lt $dataset.comparisons.Count; $batchIndex++) {
            Assert-Budget
            $references.Add($dataset.comparisons[$batchIndex])
        }
    }
    if ($references.Count -gt 22) { throw 'Baseline checks copy at most 22 evidence files and one archive index per case.' }
    return ,$references
}

function Assert-OwnedPath([string]$Path, [string]$Root) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $ownedRoot = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($ownedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Check artifact escapes its case-owned directory.' }
    return $resolved
}

function New-CaseInputs([string]$CaseRoot, [bool]$FixtureOnly) {
    Assert-Budget
    [IO.Directory]::CreateDirectory($CaseRoot) | Out-Null
    $plan = Read-Json $sourcePlanPath
    if ($FixtureOnly) {
        $fixture = @($plan.datasets | Where-Object { $_.evidence_kind -ceq 'original_fixture' })
        if ($fixture.Count -ne 1 -or $fixture[0].dataset_total -ne 12) { throw 'The small positive control requires the pinned original 12-case fixture.' }
        $plan.datasets = @($fixture[0])
    }
    $sourceDirectory = [IO.Path]::GetDirectoryName($sourcePlanPath)
    $sourceIndexPath = [IO.Path]::GetFullPath((Join-Path $sourceDirectory $plan.archive_index.path))
    $sourceArchiveRoot = [IO.Path]::GetDirectoryName($sourceIndexPath)
    $archiveRoot = Join-Path $CaseRoot 'archive'
    $indexPath = Assert-OwnedPath (Join-Path $archiveRoot 'index.json') $CaseRoot
    [IO.Directory]::CreateDirectory($archiveRoot) | Out-Null
    [IO.File]::Copy($sourceIndexPath, $indexPath, $false)
    $index = Read-Json $indexPath
    if ($index.artifacts.Count -lt 1 -or $index.artifacts.Count -gt 1024) { throw 'Pinned archive index exceeds 1..1024 entries.' }
    $references = Plan-References $plan
    $copied = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    for ($fileIndex = 0; $fileIndex -lt $references.Count; $fileIndex++) {
        Assert-Budget
        $reference = $references[$fileIndex]
        $source = [IO.Path]::GetFullPath((Join-Path $sourceDirectory $reference.path))
        $relative = [IO.Path]::GetRelativePath($sourceArchiveRoot, $source)
        $destination = Assert-OwnedPath (Join-Path $archiveRoot $relative) $archiveRoot
        if ((Get-Item -LiteralPath $source).Length -gt 4MB) { throw 'Whitelisted report exceeds 4 MiB.' }
        if (-not $copied.Add($destination)) { throw 'Pinned plan repeats a report path.' }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        [IO.File]::Copy($source, $destination, $false)
        $reference.path = [IO.Path]::GetRelativePath($CaseRoot, $destination).Replace('\', '/')
    }
    $plan.archive_index.path = 'archive/index.json'
    return [pscustomobject]@{ Root=$CaseRoot; Plan=$plan; Index=$index; IndexPath=$indexPath; References=$references }
}

function Save-Mutation($Inputs, $Reference, $Value, [bool]$DuplicateJson = $false) {
    Assert-Budget
    $path = Assert-OwnedPath (Join-Path $Inputs.Root $Reference.path) $Inputs.Root
    Write-Json $path $Value
    if ($DuplicateJson) {
        $json = [IO.File]::ReadAllText($path)
        $json = $json.Insert($json.IndexOf('{') + 1, '"SchemaVersion":2,')
        [IO.File]::WriteAllText($path, $json, [Text.UTF8Encoding]::new($false))
    }
    $Reference.sha256 = File-Hash $path
    $artifact = [IO.Path]::GetRelativePath([IO.Path]::GetDirectoryName($Inputs.IndexPath), $path).Replace('\', '/')
    $matched = 0
    # The source index has 730 rows. This bounded scan only edits the copied index.
    for ($entryIndex = 0; $entryIndex -lt $Inputs.Index.artifacts.Count -and $entryIndex -lt 1024; $entryIndex++) {
        Assert-Budget
        $entry = $Inputs.Index.artifacts[$entryIndex]
        if ($entry.artifact -ceq $artifact) {
            $entry.artifact_sha256 = $Reference.sha256
            if ($entry.transformation -ceq 'byte_preserving_copy') { $entry.source_sha256 = $Reference.sha256 }
            $matched++
        }
    }
    if ($matched -ne 1) { throw 'A mutation must match exactly one copied archive entry.' }
}

function Set-UnansweredMetrics($Metrics) {
    Assert-Budget
    $Metrics.Answered = 0
    $Metrics.Correct = 0
    $Metrics.Coverage = 0
    $Metrics.AccuracyOnAnswered = $null
    $Metrics.AccuracyOverProcessed = 0
    $Metrics.MeanBrier = $null
    $Metrics.MeanNll = $null
    $Metrics.Ece10 = $null
    $Metrics.ScoreMae = $null
    $boolean = $Metrics.Boolean
    $boolean.Answered = 0
    $boolean.Unanswered = $boolean.Processed
    $boolean.Abstained = 0
    $boolean.ReferencePositiveAnswered = 0
    $boolean.ReferenceNegativeAnswered = 0
    $boolean.TruePositive = 0
    $boolean.FalsePositive = 0
    $boolean.TrueNegative = 0
    $boolean.FalseNegative = 0
    $boolean.Coverage = if ($boolean.Processed -eq 0) { $null } else { 0 }
    $boolean.AccuracyOnAnswered = $null
    $boolean.AccuracyOverProcessed = if ($boolean.Processed -eq 0) { $null } else { 0 }
    $boolean.PositiveRecallOnAnswered = $null
    $boolean.NegativeRecallOnAnswered = $null
    $boolean.BalancedAccuracyOnAnswered = $null
    $boolean.AurocOnAnswered = $null
    $boolean.PredictedPositiveRateOnAnswered = $null
}

function Read-RunEvidence([string]$LogDirectory) {
    Assert-Budget
    $files = [Collections.Generic.List[string]]::new()
    $iterator = [IO.Directory]::EnumerateFiles($LogDirectory).GetEnumerator()
    try {
        for ($item = 0; $item -lt 5; $item++) {
            Assert-Budget
            if (-not $iterator.MoveNext()) { break }
            $files.Add([IO.Path]::GetFullPath($iterator.Current))
        }
    } finally { $iterator.Dispose() }
    $resultFiles = @($files | Where-Object { $_.EndsWith('.result.json', [StringComparison]::Ordinal) })
    $identityFiles = @($files | Where-Object { $_.EndsWith('.identity.json', [StringComparison]::Ordinal) })
    $stderrFiles = @($files | Where-Object { $_.EndsWith('.stderr.log', [StringComparison]::Ordinal) })
    if ($files.Count -ne 4 -or $resultFiles.Count -ne 1 -or $identityFiles.Count -ne 1 -or $stderrFiles.Count -ne 1) {
        throw 'Each case requires exactly four runner process artifacts.'
    }
    for ($fileIndex = 0; $fileIndex -lt $files.Count; $fileIndex++) {
        Assert-Budget
        if ((Get-Item -LiteralPath $files[$fileIndex]).Length -gt 1MB) { throw 'Baseline check process evidence exceeds 1 MiB.' }
    }
    $result = Read-Json $resultFiles[0]
    $identity = Read-Json $identityFiles[0]
    $stderr = [IO.File]::ReadAllText($stderrFiles[0]).Trim()
    if ($result.Pid -ne $identity.Pid -or $result.CleanupErrors.Count -ne 0 -or
        [IO.Path]::GetFullPath($result.StderrPath) -cne $stderrFiles[0]) { throw 'Process identity or cleanup evidence is inconsistent.' }
    return [pscustomobject]@{ ResultPath=$resultFiles[0]; IdentityPath=$identityFiles[0]; StderrPath=$stderrFiles[0]; Result=$result; Identity=$identity; Stderr=$stderr }
}

$cases = @(
    'fixture-positive', 'full-positive', 'file-hash', 'duplicate-json', 'missing-comparison', 'mixed-policy',
    'slice-nll', 'wrong-provenance', 'capture-link-hash', 'crossbatch-id', 'all-failed-full',
    'boolean-denominators', 'illegal-numeric', 'slice-drift', 'archive-entry-hash', 'partial-comparison'
)
$errors = @{
    'duplicate-json' = 'Duplicate JSON property: SchemaVersion'
    'missing-comparison' = 'Quality batch counts do not equal the comparison count.'
    'mixed-policy' = 'Unexpected quality schema/rendering/length policy.'
    'slice-nll' = 'Invalid weighted arithmetic: type MeanNll'
    'wrong-provenance' = 'Capture/comparison does not use the fixed baseline identity/policy.'
    'capture-link-hash' = 'Comparison capture hashes differ from quality batches.'
    'crossbatch-id' = 'Case ID is repeated across batches or datasets.'
    'all-failed-full' = 'Quality answer counts differ from the fully answered comparison baseline.'
    'boolean-denominators' = 'Invalid Boolean confusion matrix/denominators.'
    'illegal-numeric' = 'Invalid numeric metric: Ece10.'
    'slice-drift' = 'language denominators differ from their marginal.'
    'archive-entry-hash' = 'Plan hash differs from archived artifact hash.'
    'partial-comparison' = 'Comparison is incomplete or selects a subset.'
}
$results = [Collections.Generic.List[object]]::new()
$completed = $false
$failure = $null
[IO.Directory]::CreateDirectory($target) | Out-Null
try {
    # Exit conditions are explicit -lt comparisons; MaxCases <= 16 and the independent
    # stopwatch <= 300 seconds. MaxCases=1 is the bounded small-input trial before the full run.
    for ($caseIndex = 0; $caseIndex -lt $MaxCases -and $caseIndex -lt $cases.Count; $caseIndex++) {
        Assert-Budget
        $name = $cases[$caseIndex]
        Write-Output "Baseline check $($caseIndex + 1)/${MaxCases}: $name"
        $caseRoot = Assert-OwnedPath (Join-Path $target $name) $target
        $inputs = New-CaseInputs $caseRoot ($name -ceq 'fixture-positive')
        $plan = $inputs.Plan
        $positive = $caseIndex -lt 2
        $actualReference = $plan.datasets[0].actual
        $actualPath = Join-Path $caseRoot $actualReference.path
        $actual = Read-Json $actualPath
        $comparisonReference = $plan.datasets[0].comparisons[0]
        $comparison = Read-Json (Join-Path $caseRoot $comparisonReference.path)
        $expectedError = $null
        switch ($name) {
            'file-hash' {
                [IO.File]::AppendAllText($actualPath, ' ', [Text.UTF8Encoding]::new($false))
                $expectedError = 'Baseline input hash mismatch: ' + [IO.Path]::GetFullPath($actualPath)
            }
            'duplicate-json' { Save-Mutation $inputs $actualReference $actual $true }
            'missing-comparison' { $plan.datasets[0].comparisons = @($plan.datasets[0].comparisons | Select-Object -SkipLast 1) }
            'mixed-policy' { $actual.LengthPolicy = 'strict'; Save-Mutation $inputs $actualReference $actual }
            'slice-nll' { $actual.ByType[0].MeanNll += 0.01; Save-Mutation $inputs $actualReference $actual }
            'wrong-provenance' { $actual.CaptureBatches[0].Provenance.model_id = 'synthetic-test-wrong-model'; Save-Mutation $inputs $actualReference $actual }
            'capture-link-hash' { $comparison.actual_sha256 = '0' * 64; Save-Mutation $inputs $comparisonReference $comparison }
            'crossbatch-id' {
                $secondReference = $plan.datasets[0].comparisons[1]
                $second = Read-Json (Join-Path $caseRoot $secondReference.path)
                $second.selected_case_ids[0] = $comparison.selected_case_ids[0]
                Save-Mutation $inputs $secondReference $second
            }
            'all-failed-full' {
                # Preserve every processed/reference-label denominator while creating a
                # legal all-unanswered aggregate. Fully answered comparisons must reject it.
                $metrics = @($actual) + @($actual.ByType) + @($actual.ByLanguage) + @($actual.ByLanguageAndType.Metrics)
                if ($metrics.Count -ne 4) { throw 'The all-failed test requires the four pinned PAWS metric groups.' }
                for ($metricIndex = 0; $metricIndex -lt $metrics.Count -and $metricIndex -lt 4; $metricIndex++) {
                    Assert-Budget
                    Set-UnansweredMetrics $metrics[$metricIndex]
                }
                $actual.DatasetAnswerCoverage = 0
                $actual.ByFailure = @{ synthetic_test_failed = $actual.Processed }
                Save-Mutation $inputs $actualReference $actual
            }
            'boolean-denominators' { $actual.Boolean.TrueNegative++; Save-Mutation $inputs $actualReference $actual }
            'illegal-numeric' { $actual.Ece10 = 1.1; Save-Mutation $inputs $actualReference $actual }
            'slice-drift' {
                $fixtureReference = $plan.datasets[2].actual
                $fixture = Read-Json (Join-Path $caseRoot $fixtureReference.path)
                $slice = $fixture.ByLanguage[0]
                $slice.Correct--
                $slice.AccuracyOnAnswered = $slice.Correct / $slice.Answered
                $slice.AccuracyOverProcessed = $slice.Correct / $slice.Total
                Save-Mutation $inputs $fixtureReference $fixture
            }
            'archive-entry-hash' {
                $artifact = [IO.Path]::GetRelativePath([IO.Path]::GetDirectoryName($inputs.IndexPath), $actualPath).Replace('\', '/')
                $matched = 0
                for ($entryIndex = 0; $entryIndex -lt $inputs.Index.artifacts.Count -and $entryIndex -lt 1024; $entryIndex++) {
                    Assert-Budget
                    if ($inputs.Index.artifacts[$entryIndex].artifact -ceq $artifact) { $inputs.Index.artifacts[$entryIndex].artifact_sha256 = '0' * 64; $matched++ }
                }
                if ($matched -ne 1) { throw 'Archive hash mutation must match exactly one copied entry.' }
            }
            'partial-comparison' { $comparison.selection_mode = 'explicit_case_ids'; Save-Mutation $inputs $comparisonReference $comparison }
        }
        Write-Json $inputs.IndexPath $inputs.Index
        $plan.archive_index.sha256 = File-Hash $inputs.IndexPath
        $casePlanPath = Join-Path $caseRoot 'plan.json'
        Write-Json $casePlanPath $plan
        $reportPath = Join-Path $caseRoot 'report.json'
        $caseLogs = Join-Path $caseRoot 'processes'
        # Reserve bounded runner cleanup and log drain time before the 300-second deadline.
        $remaining = [int][Math]::Floor($totalSeconds - $watch.Elapsed.TotalSeconds) - 15
        if ($remaining -lt 1) { throw 'Insufficient time for a bounded case and cleanup.' }
        $outerSeconds = [Math]::Min(20, $remaining)
        $innerSeconds = [Math]::Max(1, $outerSeconds - 2)
        $rejected = $false
        try {
            & $runner -FilePath $executable -ArgumentList @($applicationArguments + @('--verify-baseline', $casePlanPath, $reportPath, [string]$innerSeconds)) `
                -TimeoutSeconds $outerSeconds -LogName "baseline-check-$name" -LogDirectory $caseLogs
        } catch {
            if ($_.Exception.Message -ne 'Process failed with exit code 1.') { throw }
            $rejected = $true
        }
        Assert-Budget
        $evidence = Read-RunEvidence $caseLogs
        if ($positive) {
            if ($rejected -or $evidence.Result.Status -cne 'Succeeded' -or $evidence.Result.ExitCode -ne 0 -or -not [IO.File]::Exists($reportPath)) {
                throw 'The unchanged positive control must produce a verified report.'
            }
            $report = Read-Json $reportPath
            if ($report.schema_version -cne 'sezika.audit-baseline-verification.v1' -or $report.status -cne 'verified' -or
                $report.passed -ne $true -or $report.measurement_origin -cne 'archived_evidence_verification_no_inference' -or
                $report.raw_capture_revalidation -cne 'not_performed' -or $report.calibration_established -ne $false -or
                $report.representative_language_quality_established -ne $false -or $report.datasets.Count -ne $plan.datasets.Count) {
                throw 'Positive report falsely changes the archive verification meaning or dataset count.'
            }
            $total = 0
            for ($datasetIndex = 0; $datasetIndex -lt $plan.datasets.Count; $datasetIndex++) {
                Assert-Budget
                $expected = $plan.datasets[$datasetIndex]
                $observed = $report.datasets[$datasetIndex]
                if ($observed.name -cne $expected.name -or $observed.dataset_total -ne $expected.dataset_total -or
                    $observed.actual.total -ne $expected.dataset_total -or $observed.reference.total -ne $expected.dataset_total -or
                    $observed.batches.Count -ne $expected.comparisons.Count -or $observed.dataset_sha256 -cne $expected.dataset_sha256) {
                    throw 'Positive report does not preserve complete frozen dataset denominators.'
                }
                $total += $observed.dataset_total
            }
            if (($name -ceq 'fixture-positive' -and $total -ne 12) -or ($name -ceq 'full-positive' -and $total -ne 586)) {
                throw 'Positive control must verify exactly 12 or 586 archived cases.'
            }
        } else {
            if ($null -eq $expectedError) { $expectedError = $errors[$name] }
            $expectedError = 'baseline verification: InvalidDataException: ' + $expectedError
            if (-not $rejected -or $evidence.Result.Status -cne 'Failed' -or $evidence.Result.ExitCode -ne 1 -or
                $evidence.Stderr -cne $expectedError -or [IO.File]::Exists($reportPath)) {
                throw "Case $name did not reject its intended defect; expected=$expectedError; observed=$($evidence.Stderr)"
            }
        }
        $results.Add([pscustomobject]@{
            Case=$name; Passed=$true; Rejected=(-not $positive); SyntheticMutation=(-not $positive)
            ExpectedExitCode=if ($positive) { 0 } else { 1 }; ExpectedError=$expectedError; ObservedError=$evidence.Stderr
            PlanPath=$casePlanPath; ReportPath=$reportPath; ProcessResultPath=$evidence.ResultPath; ProcessIdentityPath=$evidence.IdentityPath
            StderrPath=$evidence.StderrPath; ProcessResult=$evidence.Result; ProcessIdentity=$evidence.Identity
        })
    }
    $completed = $true
} catch {
    $failure = $_.Exception.Message
    throw
} finally {
    # Copies, reports and logs are reviewable software-test evidence, so retain them.
    # The bounded runner owns and reclaims each child process tree in its finally block;
    # Ctrl+C or an outer runner cancellation follows that same cleanup path. CancelFile is
    # checked between bounded operations; an active child is limited to 20 seconds.
    $summary = [pscustomobject]@{
        SchemaVersion='sezika.audit-baseline-checks.v1'; Status=if ($completed) { 'passed' } else { 'incomplete' }
        ApplicationPath=$application; SourcePlanPath=$sourcePlanPath; RequestedCases=$MaxCases; CompletedCases=$results.Count
        MaxCases=16; TotalTimeoutSeconds=300; ChildTimeoutSeconds=20; ElapsedSeconds=[Math]::Round($watch.Elapsed.TotalSeconds,3)
        Failure=$failure; Cases=@($results.ToArray())
        Meaning='Software validation only. Mutated copies and rebound test hashes are synthetic rejection inputs, not new inference or model-quality measurements.'
        Cleanup='Case process identity/result logs must report no cleanup errors. No source archive, user files, logs or retained test copies are deleted.'
    }
    [IO.File]::WriteAllText((Join-Path $target 'checks.json'), ($summary | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
}
Write-Output "Passed $($results.Count) archived baseline checks; synthetic mutations are software tests only."
