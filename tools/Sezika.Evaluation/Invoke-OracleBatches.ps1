param(
    [Parameter(Mandatory)][string]$PlanPath,
    [Parameter(Mandatory)][string]$PythonPath,
    [Parameter(Mandatory)][string]$UpstreamDirectory,
    [Parameter(Mandatory)][string]$ModelDirectory,
    [Parameter(Mandatory)][string]$BuildDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1,256)][int]$MaxBatches = 1,
    [ValidateRange(0,255)][int]$BatchOffset = 0,
    [ValidateRange(60,1740)][int]$TimeoutSeconds = 900,
    [string]$CancelFile,
    [string]$ReferenceIndexPath
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7+ required.' }
$utf8 = [Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $utf8
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$planFile = [IO.Path]::GetFullPath($PlanPath)
$planDirectory = [IO.Path]::GetDirectoryName($planFile)
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if ([IO.Path]::Exists($destination)) { throw 'OutputDirectory must be new.' }
if ((Get-Item -LiteralPath $planFile).Length -gt 1048576) { throw 'Plan exceeds 1 MiB.' }
$planHash = (Get-FileHash -LiteralPath $planFile -Algorithm SHA256).Hash.ToLowerInvariant()
$plan = Get-Content -LiteralPath $planFile -Raw -Encoding utf8 | ConvertFrom-Json
if ($plan.schema_version -ne 'sezika.evaluation-oracle-batches.v1' -or $plan.batches.Count -lt 1 -or $plan.batches.Count -gt 256) { throw 'Invalid batch plan.' }
if ($BatchOffset -ge $plan.batches.Count) { throw 'BatchOffset exceeds plan.' }
$count = [Math]::Min($MaxBatches, $plan.batches.Count - $BatchOffset)
$dotnet = (Get-Command dotnet -CommandType Application).Source
$python = [IO.Path]::GetFullPath($PythonPath)
$source = [IO.Path]::GetFullPath($UpstreamDirectory)
$model = [IO.Path]::GetFullPath($ModelDirectory)
$build = [IO.Path]::GetFullPath($BuildDirectory)
$contract = Join-Path $root 'tests/fixtures/laya-oracle/contract.v1.json'
$runner = Join-Path $root 'tools/Invoke-BoundedProcess.ps1'
$watch = [Diagnostics.Stopwatch]::StartNew()
[IO.Directory]::CreateDirectory($destination) | Out-Null
$logs = Join-Path $destination 'processes'
$referenceIndex = [Collections.Generic.List[object]]::new()
$actualIndex = [Collections.Generic.List[object]]::new()
$comparisons = [Collections.Generic.List[object]]::new()
function Check-Budget {
    if ($CancelFile -and [IO.File]::Exists([IO.Path]::GetFullPath($CancelFile))) { throw 'Batch audit cancelled.' }
    if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds - 35) { throw 'Batch audit deadline reached.' }
}
function Invoke-Stage([string]$Name, [string]$Executable, [string[]]$Arguments, [int]$Maximum, [hashtable]$Environment = @{}, [bool]$AllowComparisonDifference = $false) {
    Check-Budget
    $remaining = [int][Math]::Floor($TimeoutSeconds - $watch.Elapsed.TotalSeconds - 25)
    $limit = [Math]::Min($Maximum, $remaining)
    if ($limit -lt 10) { throw 'Insufficient time for another stage.' }
    try { & $runner -FilePath $Executable -ArgumentList $Arguments -TimeoutSeconds $limit -LogName $Name -LogDirectory $logs -WorkingDirectory $root -Environment $Environment }
    catch {
        if (-not $AllowComparisonDifference) { throw }
        $results = @(Get-ChildItem -LiteralPath $logs -Filter "*-$Name.result.json" -File | Select-Object -First 2)
        if ($results.Count -ne 1) { throw }
        $result = Get-Content -LiteralPath $results[0].FullName -Raw -Encoding utf8 | ConvertFrom-Json
        if ($result.ExitCode -ne 1 -or $result.CleanupErrors.Count -ne 0) { throw }
        Write-Output "$Name recorded a numerical difference; retain the capture and continue the frozen plan."
    }
}
$status = 'running'
$failure = $null
$hasDifferences = $false
$reusableReferences = @{}
if ($ReferenceIndexPath) {
    $referenceIndexFile = [IO.Path]::GetFullPath($ReferenceIndexPath)
    if ((Get-Item -LiteralPath $referenceIndexFile).Length -gt 1048576) { throw 'Reference index exceeds 1 MiB.' }
    $referenceIndexHash = (Get-FileHash -LiteralPath $referenceIndexFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $existing = Get-Content -LiteralPath $referenceIndexFile -Raw -Encoding utf8 | ConvertFrom-Json
    if ($existing.schema_version -cne 'sezika.evaluation-captures.v1' -or $existing.batches.Count -lt 1 -or $existing.batches.Count -gt 256) { throw 'Invalid reference index.' }
    foreach ($entry in $existing.batches) {
        Check-Budget
        if ($reusableReferences.ContainsKey($entry.manifest_sha256)) { throw 'Duplicate reference manifest.' }
        $reusableReferences.Add($entry.manifest_sha256, $entry)
    }
}
try {
    # Fixed plan order; at most 256 batches, no retries. Every child has an independent runner deadline.
    for ($index = 0; $index -lt $count; $index++) {
        Check-Budget
        $batch = $plan.batches[$BatchOffset + $index]
        if ($batch.case_ids.Count -lt 1 -or $batch.case_ids.Count -gt 46) { throw 'Each batch must contain 1..46 rows.' }
        $manifest = [IO.Path]::GetFullPath([string]$batch.manifest, $planDirectory)
        if ((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant() -cne $batch.manifest_sha256) { throw 'Frozen manifest hash mismatch.' }
        $batchDirectory = Join-Path $destination ('batch-{0:D3}' -f ($BatchOffset + $index + 1))
        [IO.Directory]::CreateDirectory($batchDirectory) | Out-Null
        $referenceDirectory = Join-Path $batchDirectory 'reference'
        $actualDirectory = Join-Path $batchDirectory 'actual'
        $reference = Join-Path $referenceDirectory 'capture.json'
        $actual = Join-Path $actualDirectory 'capture.json'
        Write-Output "Oracle batch $($index + 1)/$count, source offset $($batch.offset), rows $($batch.case_ids.Count)"
        $pythonArgs = @('-B',(Join-Path $root 'tools/Sezika.LayaOracle/export_oracle.py'),'--upstream',$source,'--model',$model,'--output',$referenceDirectory,'--cases',$manifest,'--contract',$contract,'--max-cases',[string]$batch.case_ids.Count,'--timeout-seconds','600')
        if ($CancelFile) { $pythonArgs += @('--cancel-file',[IO.Path]::GetFullPath($CancelFile)) }
        if ($ReferenceIndexPath) {
            $entry = $reusableReferences[$batch.manifest_sha256]
            if ($null -eq $entry) { throw 'Selected batch is absent from the frozen reference index.' }
            $reference = [IO.Path]::GetFullPath([string]$entry.capture, [IO.Path]::GetDirectoryName($referenceIndexFile))
            if ((Get-FileHash -LiteralPath $reference -Algorithm SHA256).Hash.ToLowerInvariant() -cne $entry.capture_sha256) { throw 'Reusable reference hash mismatch.' }
            Write-Output "Reusing hash-bound independent reference for batch $($BatchOffset + $index + 1); no reference inference."
        } else {
            Invoke-Stage "reference-$index" $python $pythonArgs 620 @{
                PYTHONUTF8='1'; PYTHONDONTWRITEBYTECODE='1'; HF_HUB_OFFLINE='1'; TRANSFORMERS_OFFLINE='1';
                HF_HUB_DISABLE_TELEMETRY='1'; TOKENIZERS_PARALLELISM='false'; LAYA_CPU_AMP=''
            }
        }
        Invoke-Stage "capture-$index" $dotnet @((Join-Path $build 'Sezika.OracleCapture/release/Sezika.OracleCapture.dll'),'--reference',$reference,'--model',$model,'--cases',$manifest,'--contract',$contract,'--output',$actualDirectory,'--backend','cuda','--length-policy','laya_compatible','--max-cases',[string]$batch.case_ids.Count,'--timeout-seconds','600') 620
        $comparisonPath = Join-Path $batchDirectory 'comparison.json'
        Invoke-Stage "compare-$index" $dotnet @((Join-Path $build 'Sezika.OracleCompare/release/Sezika.OracleCompare.dll'),$reference,$actual,$contract,$manifest,$comparisonPath,'30') 45 @{} $true
        $comparison = Get-Content -LiteralPath $comparisonPath -Raw -Encoding utf8 | ConvertFrom-Json
        if ($comparison.schema_version -cne 'sezika.oracle-comparison.v1' -or $comparison.manifest_count -ne $batch.case_ids.Count) { throw 'Invalid comparison report.' }
        if ((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash.ToLowerInvariant() -cne $batch.manifest_sha256) { throw 'Manifest changed during batch execution.' }
        if ($ReferenceIndexPath -and (Get-FileHash -LiteralPath $reference -Algorithm SHA256).Hash.ToLowerInvariant() -cne $entry.capture_sha256) { throw 'Reusable reference changed during batch execution.' }
        if (-not $comparison.full_manifest_passed) { $hasDifferences = $true }
        $referenceIndex.Add([ordered]@{ capture=$reference; capture_sha256=(Get-FileHash -LiteralPath $reference -Algorithm SHA256).Hash.ToLowerInvariant(); manifest=$manifest; manifest_sha256=$batch.manifest_sha256 })
        $actualIndex.Add([ordered]@{ capture=$actual; capture_sha256=(Get-FileHash -LiteralPath $actual -Algorithm SHA256).Hash.ToLowerInvariant(); manifest=$manifest; manifest_sha256=$batch.manifest_sha256 })
        $comparisons.Add([ordered]@{ path=$comparisonPath; sha256=(Get-FileHash -LiteralPath $comparisonPath -Algorithm SHA256).Hash.ToLowerInvariant(); rows=$batch.case_ids.Count; passed=$comparison.full_manifest_passed })
    }
    if ((Get-FileHash -LiteralPath $planFile -Algorithm SHA256).Hash.ToLowerInvariant() -cne $planHash) { throw 'Plan changed during execution.' }
    if ($ReferenceIndexPath -and (Get-FileHash -LiteralPath $referenceIndexFile -Algorithm SHA256).Hash.ToLowerInvariant() -cne $referenceIndexHash) { throw 'Reference index changed during execution.' }
    $status = if ($hasDifferences) { 'selected_batches_complete_with_differences' } else { 'selected_batches_complete' }
}
catch { $status = 'incomplete'; $failure = $_.Exception.Message; throw }
finally {
    # Completed comparisons, including numerical differences, enter the scoring indexes.
    # Execution failures and unprocessed plan rows remain in run.json.
    [IO.File]::WriteAllText((Join-Path $destination 'references.json'), ([ordered]@{ schema_version='sezika.evaluation-captures.v1'; batches=$referenceIndex.ToArray() } | ConvertTo-Json -Depth 16), $utf8)
    [IO.File]::WriteAllText((Join-Path $destination 'actuals.json'), ([ordered]@{ schema_version='sezika.evaluation-captures.v1'; batches=$actualIndex.ToArray() } | ConvertTo-Json -Depth 16), $utf8)
    [IO.File]::WriteAllText((Join-Path $destination 'run.json'), ([ordered]@{ status=$status; error=$failure; plan=$planFile; plan_sha256=$planHash; dataset_total=$plan.dataset_total; planned_batches=$count; batch_offset=$BatchOffset; completed_batches=$comparisons.Count; comparisons=$comparisons.ToArray(); elapsed_seconds=$watch.Elapsed.TotalSeconds; timeout_seconds=$TimeoutSeconds } | ConvertTo-Json -Depth 16), $utf8)
}
