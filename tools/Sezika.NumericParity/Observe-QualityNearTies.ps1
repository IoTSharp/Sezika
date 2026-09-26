param(
    [Parameter(Mandatory)][string]$PawsRunDirectory,
    [string]$NimbleRunDirectory,
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateRange(1,574)][int]$MaxObservedRows = 574
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7+ required.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$planPath = Join-Path $root 'tests/fixtures/laya-oracle/quality-near-tie-plan.v1.json'
$plan = Get-Content -LiteralPath $planPath -Raw -Encoding utf8 | ConvertFrom-Json
if ($plan.planned_rows -ne 574 -or $plan.maximum_margin -ne 0.001 -or $plan.maximum_followup_cases -ne 18 -or $plan.sources_in_order.Count -ne 2) { throw 'Unexpected frozen near-tie plan.' }
$output = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::Exists($output)) { throw 'Output must be new.' }
$watch = [Diagnostics.Stopwatch]::StartNew()
$rows = [Collections.Generic.List[object]]::new()
$sources = [Collections.Generic.List[object]]::new()
$directories = @($PawsRunDirectory, $NimbleRunDirectory)
function Check-Deadline { if ($watch.Elapsed.TotalSeconds -ge 30) { throw 'Near-tie observation exceeded 30 seconds.' } }
for ($sourceIndex = 0; $sourceIndex -lt 2; $sourceIndex++) {
    Check-Deadline
    $spec = $plan.sources_in_order[$sourceIndex]
    if (-not $directories[$sourceIndex] -or $rows.Count -ge $MaxObservedRows) { $sources.Add([ordered]@{name=$spec.name; planned=$spec.records; observed=0; status='unprocessed'}); continue }
    $directory = [IO.Path]::GetFullPath($directories[$sourceIndex])
    $run = Get-Content -LiteralPath (Join-Path $directory 'run.json') -Raw -Encoding utf8 | ConvertFrom-Json
    if ((Get-FileHash -LiteralPath $run.plan -Algorithm SHA256).Hash.ToLowerInvariant() -cne $run.plan_sha256) { throw 'Batch plan hash mismatch.' }
    $batchPlan = Get-Content -LiteralPath $run.plan -Raw -Encoding utf8 | ConvertFrom-Json
    if ($batchPlan.schema_version -cne 'sezika.evaluation-oracle-batches.v1' -or $batchPlan.batches.Count -lt 1 -or $batchPlan.batches.Count -gt 8) { throw 'Expected a bounded batch plan.' }
    if ($batchPlan.dataset_sha256 -cne $spec.sha256 -or $batchPlan.dataset_total -ne $spec.records) { throw 'Source differs from the frozen observation plan.' }
    $expectedIds = @($batchPlan.batches | ForEach-Object { $_.case_ids })
    if ($expectedIds.Count -ne $spec.records) { throw 'Source plan count mismatch.' }
    $indexPath = Join-Path $directory 'references.json'
    $index = Get-Content -LiteralPath $indexPath -Raw -Encoding utf8 | ConvertFrom-Json
    if ($index.schema_version -cne 'sezika.evaluation-captures.v1' -or $index.batches.Count -lt 1 -or $index.batches.Count -gt 8) { throw 'Expected one to eight indexed reference batches per source.' }
    $observed = 0
    for ($batch = 0; $batch -lt $index.batches.Count -and $rows.Count -lt $MaxObservedRows; $batch++) {
        Check-Deadline
        $item = $index.batches[$batch]
        if ((Get-FileHash -LiteralPath $item.manifest -Algorithm SHA256).Hash.ToLowerInvariant() -cne $item.manifest_sha256) { throw 'Reference manifest hash mismatch.' }
        if ((Get-Item -LiteralPath $item.capture).Length -gt 4194304 -or (Get-FileHash -LiteralPath $item.capture -Algorithm SHA256).Hash.ToLowerInvariant() -cne $item.capture_sha256) { throw 'Reference size or hash mismatch.' }
        $capture = Get-Content -LiteralPath $item.capture -Raw -Encoding utf8 | ConvertFrom-Json
        if ($capture.provenance.implementation -cne 'pinned_upstream_laya_agent' -or $capture.provenance.model_revision -cne $plan.model_revision -or $capture.provenance.contract_sha256 -cne $plan.contract_sha256 -or $capture.measurement_status -cne 'complete' -or $capture.cases.Count -gt 46) { throw 'Expected completed independent reference batch.' }
        if ($capture.provenance.model_id -cne $plan.model_id -or $capture.provenance.cases_sha256 -cne $item.manifest_sha256 -or $capture.provenance.upstream_source_revision -cne '4066d5d5fbf08b66c6757ddeedbd797bd7655bc0' -or $capture.provenance.weights_sha256 -cne '9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204' -or $capture.provenance.tokenizer_sha256 -cne '609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f') { throw 'Reference provenance differs from the frozen Laya identity.' }
        for ($caseIndex = 0; $caseIndex -lt $capture.cases.Count -and $rows.Count -lt $MaxObservedRows; $caseIndex++) {
            Check-Deadline
            $case = $capture.cases[$caseIndex]
            if ($observed -ge $spec.records -or $case.id -cne $expectedIds[$observed]) { throw 'Reference source order/count differs.' }
            $margin = $null
            if ($case.status -eq 'answered') {
                $logits = @($case.raw_logits)
                if ($logits.Count -lt 2 -or $logits.Count -gt 64 -or @($logits | Where-Object { -not [double]::IsFinite([double]$_) }).Count -gt 0) { throw 'Invalid reference logits.' }
                $ordered = @($logits | Sort-Object -Descending)
                $margin = [double]$ordered[0] - [double]$ordered[1]
            } elseif ($case.status -ne 'failed') { throw 'Unknown reference case status.' }
            $rows.Add([ordered]@{source=$spec.name; ordinal=$observed; id=$case.id; primitive=$case.primitive; status=$case.status; margin=$margin; near_tie=($null -ne $margin -and $margin -le $plan.maximum_margin); reference=$item.capture; reference_sha256=$item.capture_sha256})
            $observed++
        }
        if ((Get-FileHash -LiteralPath $item.capture -Algorithm SHA256).Hash.ToLowerInvariant() -cne $item.capture_sha256) { throw 'Reference changed during observation.' }
        Write-Output "Near-tie observations $($spec.name): $observed/$($spec.records)"
    }
    $sources.Add([ordered]@{name=$spec.name; planned=$spec.records; observed=$observed; status=if ($observed -eq $spec.records) {'complete'} else {'partial'}; index_sha256=(Get-FileHash -LiteralPath $indexPath -Algorithm SHA256).Hash.ToLowerInvariant()})
}
$hits = @($rows | Where-Object near_tie)
$result = [ordered]@{schema_version='sezika.near-tie-observations.v1'; plan_sha256=(Get-FileHash -LiteralPath $planPath -Algorithm SHA256).Hash.ToLowerInvariant(); planned=574; observed=$rows.Count; unprocessed=(574-$rows.Count); near_ties=$hits.Count; threshold=$plan.maximum_margin; sources=$sources.ToArray(); selected_followup=@($hits | Select-Object -First 18); rows=$rows.ToArray(); scope='Offline observation of existing independent reference logits; no model run, quality selection or calibration.'}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
[IO.File]::WriteAllText($output,($result | ConvertTo-Json -Depth 16),[Text.UTF8Encoding]::new($false))
Write-Output "Observed $($rows.Count)/574; near ties $($hits.Count)."
