param([switch]$Smoke)
$ErrorActionPreference = 'Stop'
$root = 'D:\source\Sezika'
$taskRoot = Join-Path $root '.artifacts/s3-aot-20260926'
$destination = if ($Smoke) { Join-Path $taskRoot 'archive-smoke' } else { Join-Path $root 'docs/evidence/input-aot-2026-09-26' }
if (Test-Path -LiteralPath $destination) { throw 'Evidence destination must be new.' }
[IO.Directory]::CreateDirectory($destination) | Out-Null
$watch = [Diagnostics.Stopwatch]::StartNew()
$files = [Collections.Generic.List[object]]::new()
function Bound {
    if ($watch.Elapsed.TotalSeconds -ge 90 -or $files.Count -ge 512) { throw 'Archive exceeds 90 seconds / 512 files.' }
}
function Copy-Evidence([string]$source, [string]$relative) {
    Bound
    $source = [IO.Path]::GetFullPath($source)
    $target = [IO.Path]::GetFullPath((Join-Path $destination $relative))
    if (-not $source.StartsWith((Join-Path $root '.artifacts') + '\',[StringComparison]::OrdinalIgnoreCase) -or
        -not $target.StartsWith($destination + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence path escapes owned roots.' }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
    [IO.File]::Copy($source,$target,$false)
    $originalHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    $actualHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($originalHash -cne $actualHash) { throw 'Evidence copy changed bytes.' }
    $files.Add([ordered]@{path=$relative.Replace('\','/');source=$source;sha256=$actualHash;bytes=(Get-Item -LiteralPath $target).Length})
    if ($files.Count % 32 -eq 0) { Write-Host "Archived $($files.Count)/512 files" }
}
$labels = if ($Smoke) { @('win-cuda') } else { @('win-scalar','win-simd','win-cuda','linux-scalar','linux-simd','linux-cuda') }
$matrix = [Collections.Generic.List[object]]::new()
foreach ($label in $labels) { # <= 6 rows, independent 90-second total deadline.
    Bound
    Write-Host "Verify $label"
    $platform,$backend = $label.Split('-')
    $rid = if ($platform -eq 'win') { 'win-x64' } else { 'linux-x64' }
    $executable = if ($platform -eq 'win') { 'Sezika.OracleCapture.exe' } else { 'Sezika.OracleCapture' }
    $binary = Join-Path $taskRoot "native-final/$rid/$executable"
    $capturePath = Join-Path $taskRoot "$label-final/capture.json"
    $comparisonPath = Join-Path $taskRoot "$label-final-comparison.json"
    $capture = Get-Content -LiteralPath $capturePath -Raw -Encoding utf8 | ConvertFrom-Json
    $comparison = Get-Content -LiteralPath $comparisonPath -Raw -Encoding utf8 | ConvertFrom-Json
    $expectedRows = if ($backend -eq 'scalar') { 3 } else { 46 }
    if ($capture.measurement_status -ne 'completed' -or $capture.cases.Count -ne $expectedRows -or
        $capture.unprocessed_case_ids.Count -ne 0 -or -not $capture.implementation.require_aot -or
        $capture.implementation.is_dynamic_code_supported -or $capture.implementation.is_dynamic_code_compiled -or
        $capture.implementation.managed_host_detected -or $capture.implementation.runtime_identifier -ne $rid -or
        $capture.implementation.backend -ne "${backend}_fp32" -or -not $comparison.passed -or
        $comparison.issues.Count -ne 0 -or $comparison.full_manifest_passed) { throw "Incomplete or invalid matrix evidence: $label" }
    $binaryHash = (Get-FileHash -LiteralPath $binary).Hash.ToLowerInvariant()
    if ($capture.implementation.binary_sha256.process_executable -cne $binaryHash -or
        $comparison.actual_sha256 -cne (Get-FileHash -LiteralPath $capturePath).Hash.ToLowerInvariant()) { throw 'Evidence identity mismatch.' }
    if (($backend -eq 'scalar' -and ($comparison.answered_pairs -ne 3 -or $comparison.failure_pairs -ne 0)) -or
        ($backend -ne 'scalar' -and ($comparison.answered_pairs -ne 38 -or $comparison.failure_pairs -ne 4))) { throw 'Wrong comparison coverage.' }
    Copy-Evidence $capturePath "$label-capture.json"
    Copy-Evidence $comparisonPath "$label-comparison.json"
    Copy-Evidence (Join-Path $taskRoot "$label-final-smoke/capture.json") "$label-smoke-capture.json"
    Copy-Evidence (Join-Path $taskRoot "$label-final-smoke-comparison.json") "$label-smoke-comparison.json"
    if ($backend -ne 'scalar') {
        $fullPath = Join-Path $taskRoot "$label-final-full-comparison.json"
        $full = Get-Content -LiteralPath $fullPath -Raw -Encoding utf8 | ConvertFrom-Json
        $expectedDifferences = @('choice-one-candidate','choice-33-candidates','score-one-level','score-eleven-levels') | Sort-Object
        $observedDifferences = @($full.issues.case_id | Sort-Object -Unique)
        if ($full.passed -or $full.issues.Count -ne 16 -or (($observedDifferences -join ',') -cne ($expectedDifferences -join ','))) {
            throw 'Unexpected full-manifest differences.'
        }
        Copy-Evidence $fullPath "$label-full-comparison.json"
    }
    $lengths = @($capture.cases | Where-Object status -eq 'answered' | ForEach-Object { $_.token_ids.Count })
    $matrix.Add([ordered]@{rid=$rid;backend=$backend;capture_rows=$capture.cases.Count;answered_pairs=$comparison.answered_pairs;
        failure_pairs=$comparison.failure_pairs;selected_passed=$comparison.passed;full_manifest_passed=$comparison.full_manifest_passed;
        max_tokens=($lengths | Measure-Object -Maximum).Maximum;max_logit_error=$comparison.max_logit_error;
        max_probability_error=$comparison.max_probability_error;native_binary=$binary;native_binary_sha256=$binaryHash;
        runtime=$capture.implementation.framework;selected_case_ids=$comparison.selected_case_ids})
}
$validations = [Collections.Generic.List[object]]::new()
if (-not $Smoke) {
    $checks = @(
        @{label='win-final-prompt-tests';exit=0;count=138}, @{label='linux-final-prompt-tests';exit=0;count=138},
        @{label='win-final-publish';exit=0;count=0}, @{label='linux-final-publish';exit=0;count=0},
        @{label='win-final-tests-publish';exit=0;count=0}, @{label='linux-final-tests-publish';exit=0;count=0},
        @{label='managed-final-negative';exit=2;count=0}, @{label='linux-managed-negative';exit=2;count=0})
    foreach ($check in $checks) {
        Bound
        $matches = @(Get-ChildItem -LiteralPath (Join-Path $root '.artifacts/processes') -File -Filter "*s3-aot-$($check.label).result.json")
        if ($matches.Count -ne 1) { throw "Missing or ambiguous validation: $($check.label)" }
        $run = Get-Content -LiteralPath $matches[0].FullName -Raw -Encoding utf8 | ConvertFrom-Json
        if ($run.ExitCode -ne $check.exit -or $run.CleanupErrors.Count -ne 0 -or ($check.exit -eq 0 -and $run.Status -ne 'Succeeded')) { throw 'Validation or cleanup failed.' }
        $stdout = Get-Content -LiteralPath $run.StdoutPath -Raw -Encoding utf8
        if ($check.count -gt 0 -and $stdout -notmatch 'Sezika prompt contract checks passed: 138\b') { throw 'Test count not confirmed.' }
        if ($check.label -like '*publish' -and $stdout -match '(?i)\bwarning (IL|CS)\d+') { throw 'AOT publish emitted a compiler warning.' }
        $validations.Add([ordered]@{label=$check.label;pid=$run.Pid;started=$run.Started;exit_code=$run.ExitCode;
            expected_exit_code=$check.exit;checks=$check.count;elapsed_seconds=$run.ElapsedSeconds;timeout_seconds=$run.TimeoutSeconds;
            result="processes/$($matches[0].Name)"})
    }
    $sourceManifest = Get-Content -LiteralPath (Join-Path $taskRoot 'source-hashes.json') -Raw -Encoding utf8 | ConvertFrom-Json
    if ($sourceManifest.files.Count -gt 256) { throw 'Source inventory bound exceeded.' }
    foreach ($file in $sourceManifest.files) {
        Bound
        if ((Get-FileHash -LiteralPath (Join-Path $root $file.path)).Hash.ToLowerInvariant() -cne $file.sha256) { throw "Source changed after snapshot: $($file.path)" }
    }
    Copy-Evidence (Join-Path $taskRoot 'source-hashes.json') 'source-hashes.json'
    Copy-Evidence (Join-Path $taskRoot 'runtime-dependencies.json') 'runtime-dependencies.json'
    Copy-Evidence (Join-Path $taskRoot 'win-pe-identity.json') 'win-pe-identity.json'
    Copy-Evidence (Join-Path $taskRoot 'native-binary-hashes.json') 'native-binary-hashes.json'
    Copy-Evidence (Join-Path $taskRoot 'managed-should-not-exist/capture.json') 'diagnostic/managed-pilot-capture.json'
    Copy-Evidence (Join-Path $taskRoot 'build-win/bin/Sezika.OracleCapture/release_win-x64/Sezika.OracleCapture.runtimeconfig.json') 'diagnostic/managed-pilot.runtimeconfig.json'
    Copy-Evidence (Join-Path $taskRoot 'linux-process.py') 'scripts/linux-process.py'
    Copy-Evidence (Join-Path $taskRoot 'run-capture.ps1') 'scripts/run-capture.ps1'
    Copy-Evidence (Join-Path $taskRoot 'collect-evidence.ps1') 'scripts/collect-evidence.ps1'
    Copy-Evidence (Join-Path $taskRoot 'audit-processes.ps1') 'scripts/audit-processes.ps1'
    Copy-Evidence (Join-Path $taskRoot 'audit-linux-processes.py') 'scripts/audit-linux-processes.py'
    Copy-Evidence (Join-Path $taskRoot 'windows-cleanup-audit.json') 'windows-cleanup-audit.json'
    Copy-Evidence (Join-Path $taskRoot 'linux-cleanup-audit.json') 'linux-cleanup-audit.json'
    $processFiles = @(Get-ChildItem -LiteralPath (Join-Path $root '.artifacts/processes') -File -Filter '*s3-aot*')
    if ($processFiles.Count -gt 440) { throw 'Process evidence file bound exceeded.' }
    foreach ($file in $processFiles) { Copy-Evidence $file.FullName "processes/$($file.Name)" }
    $linuxFiles = @(Get-ChildItem -LiteralPath $taskRoot -File -Filter '*process.json')
    if ($linuxFiles.Count -gt 20) { throw 'Linux process evidence bound exceeded.' }
    foreach ($file in $linuxFiles) { Copy-Evidence $file.FullName "linux-processes/$($file.Name)" }
}
$summary = [ordered]@{schema_version='sezika.input-aot-regression.v1';captured_utc=[DateTimeOffset]::UtcNow;
    smoke_archive_only=[bool]$Smoke;matrix=$matrix.ToArray();validations=$validations.ToArray();verified_source_files=if ($Smoke) {0} else {$sourceManifest.files.Count};
    timeout_seconds=90;maximum_files=512;files=$files.ToArray();
    scope='Native AOT corrected-input FP32 regression only. Scalar covers three explicit long cases; SIMD/CUDA cover 42/46 supported cases with four API differences retained. No language-quality, calibration, W8A32, bare-metal Linux or performance claim.'}
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $destination 'summary.json') -Encoding utf8NoBOM
Write-Output "Archived $($matrix.Count) matrix rows and $($files.Count) files to $destination"
