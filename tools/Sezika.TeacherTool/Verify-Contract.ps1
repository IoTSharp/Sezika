param(
    [string]$ToolDll = (Join-Path $PSScriptRoot 'bin/Release/net10.0/Sezika.TeacherTool.dll'),
    [ValidateRange(1,32)][int]$MaximumCases = 32,
    [ValidateRange(10,300)][int]$TimeoutSeconds = 240
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7+ required.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$ToolDll = [IO.Path]::GetFullPath($ToolDll)
if (-not [IO.File]::Exists($ToolDll)) { throw "Build the tool first: $ToolDll" }
$dotnet = (Get-Command dotnet -CommandType Application).Source
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $root ('.artifacts/teacher-contract-' + [Guid]::NewGuid().ToString('N'))))
$inputs = Join-Path $artifactRoot 'temporary-inputs'
[IO.Directory]::CreateDirectory($inputs) | Out-Null
$watch = [Diagnostics.Stopwatch]::StartNew()
$hash = 'a' * 64
try {
$baseRecord = [ordered]@{
    record_id='r1'; review_status='pending'; split='train'; source='synthetic-contract-test'; family_id='f1'
    request_sha256=$hash; prompt_sha256=$hash; model_id='synthetic-unverified'; model_revision='fixture'
}
function New-Record([string]$Id, [string]$Status = 'pending', [string]$Split = 'train', [string]$Family = 'f1') {
    $row = [ordered]@{}
    foreach ($key in $baseRecord.Keys) {
        if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw 'Record fixture setup timeout.' }
        $row[$key] = $baseRecord[$key]
    }
    $row.record_id=$Id; $row.review_status=$Status; $row.split=$Split; $row.family_id=$Family
    if ($Status -in @('accepted','rejected')) { $row.response_sha256=$hash; $row.human_review_id='synthetic-review-not-real-approval' }
    return ($row | ConvertTo-Json -Compress)
}
function New-Catalog([string]$Status, [string]$Endpoint = 'http://127.0.0.1:8080/') {
    return (@{ candidate=@{ id='qwen35-9b-q4km'; service='IoTSharp/Tomur'; model='qwen35-9b-q4km'; revision='synthetic'; license='unverified-fixture'; status=$Status; endpoint=$Endpoint; terms_evidence='synthetic-not-license-approval' } } | ConvertTo-Json -Depth 4 -Compress)
}
$pending = New-Record 'r1'
$reviewedRejected = New-Record 'r1' 'rejected'
$cases = [Collections.Generic.List[object]]::new()
function Add-Case($Name, $Command, $Text, $Exit, $Expected = @{}, [byte[]]$Bytes = $null, [bool]$ExistingReport = $false) {
    $cases.Add([pscustomobject]@{Name=$Name; Command=$Command; Text=$Text; Exit=$Exit; Expected=$Expected; Bytes=$Bytes; ExistingReport=$ExistingReport})
}
Add-Case 'pending-smoke' 'validate-records' $pending 0 @{total=1; pending=1; invalid=0; schema_version=3}
Add-Case 'all-status-denominators' 'validate-records' ((New-Record 'r1') + "`n" + (New-Record 'r2' 'accepted') + "`n" + (New-Record 'r3' 'rejected') + "`n" + (New-Record 'r4' 'failed') + "`n" + (New-Record 'r5' 'abstained')) 0 @{total=5; pending=1; accepted=1; rejected=1; failed=1; abstained=1; invalid=0}
Add-Case 'duplicate-review-status' 'validate-records' ('{"review_status":"accepted",' + $pending.Substring(1)) 3 @{total=1; invalid=1; pending=0}
Add-Case 'escaped-duplicate' 'validate-records' ('{"review_\u0073tatus":"accepted",' + $pending.Substring(1)) 3 @{invalid=1}
Add-Case 'nested-duplicate' 'validate-records' ('{"unused":{"a":1,"a":2},' + $pending.Substring(1)) 3 @{invalid=1}
Add-Case 'human-rejected-is-valid-format' 'validate-records' $reviewedRejected 0 @{rejected=1; invalid=0}
Add-Case 'duplicate-record-id' 'validate-records' ($pending + "`n" + $pending) 3 @{total=2; pending=1; invalid=1}
Add-Case 'family-cross-split' 'validate-records' ($pending + "`n" + (New-Record 'r2' 'pending' 'development')) 3 @{total=2; pending=1; invalid=1}
Add-Case 'invalid-id-does-not-reserve' 'validate-records' (($pending.Replace('"split":"train"','"split":"sealed_test"')) + "`n" + $pending) 3 @{total=2; pending=1; invalid=1}
Add-Case 'utf8-byte-bound' 'validate-records' ('{"padding":"' + ('中' * 22000) + '",' + $pending.Substring(1)) 3 @{invalid=1; pending=0}
Add-Case 'invalid-utf8' 'validate-records' '' 1 @{} ([byte[]]@(0xC3,0x28))
Add-Case 'empty-input' 'validate-records' '' 1
$rows = [Collections.Generic.List[string]]::new()
for ($index=1; $index -le 33; $index++) {
    if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw 'Fixture setup timeout.' }
    $rows.Add((New-Record "r$index"))
}
Add-Case '32-records-allowed' 'validate-records' (($rows | Select-Object -First 32) -join "`r`n") 0 @{total=32; pending=32; invalid=0}
Add-Case '33-records-rejected' 'validate-records' ($rows -join "`n") 1
Add-Case 'bom-records' 'validate-records' ([char]0xFEFF + $pending) 0 @{pending=1}
Add-Case 'sealed-test-forbidden' 'validate-records' ($pending.Replace('"split":"train"','"split":"sealed_test"')) 3 @{invalid=1}
Add-Case 'optional-hash-validated' 'validate-records' ('{"response_sha256":"bad",' + $pending.Substring(1)) 3 @{invalid=1}
Add-Case 'wrong-optional-type' 'validate-records' ('{"response_sha256":1,' + $pending.Substring(1)) 3 @{invalid=1}
Add-Case 'output-never-overwritten' 'validate-records' $pending 1 @{} $null $true
Add-Case 'catalog-unverified-blocked' 'validate-catalog' (New-Catalog 'unverified') 3 @{status='blocked_unverified'; independently_verified=$false}
Add-Case 'catalog-declaration-is-not-evidence' 'validate-catalog' (New-Catalog 'verified') 0 @{status='valid_declared_verified_not_independently_verified'; independently_verified=$false}
Add-Case 'catalog-remote-forbidden' 'validate-catalog' (New-Catalog 'verified' 'https://example.com/') 1
Add-Case 'catalog-credentials-forbidden' 'validate-catalog' (New-Catalog 'verified' 'http://user:password@localhost:8080/') 1
Add-Case 'catalog-duplicate-status' 'validate-catalog' ((New-Catalog 'unverified').Replace('"candidate":{','"candidate":{"status":"verified",')) 1
if ($cases.Count -gt 32) { throw 'Case item bound exceeded.' }
$results = [Collections.Generic.List[object]]::new()
    # Comparison bounds were reviewed; execute MaximumCases=1 before the full matrix.
    for ($index=0; $index -lt [Math]::Min($MaximumCases,$cases.Count); $index++) {
        if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw 'Verification wall-clock budget exceeded.' }
        $case=$cases[$index]
        Write-Output "Contract case $($index+1)/$([Math]::Min($MaximumCases,$cases.Count)): $($case.Name)"
        $inputPath=Join-Path $inputs ($case.Name + '.json')
        $reportPath=Join-Path $artifactRoot ($case.Name + '.report.json')
        $logs=Join-Path $artifactRoot ($case.Name + '-process')
        if ($null -ne $case.Bytes) { [IO.File]::WriteAllBytes($inputPath,$case.Bytes) }
        else { [IO.File]::WriteAllText($inputPath,$case.Text,[Text.UTF8Encoding]::new($false)) }
        if ($case.ExistingReport) { [IO.File]::WriteAllText($reportPath,'do-not-overwrite') }
        $option=if ($case.Command -eq 'validate-catalog') {'--catalog'} else {'--records'}
        $runFailure=$null
        try {
            & (Join-Path $root 'tools/Invoke-BoundedProcess.ps1') -FilePath $dotnet -ArgumentList @($ToolDll,$case.Command,$option,$inputPath,'--report',$reportPath,'--timeout-ms','3000') -TimeoutSeconds 10 -LogName $case.Name -LogDirectory $logs -WorkingDirectory $root | Out-Host
        } catch { $runFailure=$_.Exception.Message }
        $processReports=@(Get-ChildItem -LiteralPath $logs -Filter '*.result.json' -File | Select-Object -First 2)
        if ($processReports.Count -ne 1) { throw "Expected one process result for $($case.Name)." }
        $processResult=Get-Content -LiteralPath $processReports[0].FullName -Raw | ConvertFrom-Json
        if ($processResult.ExitCode -ne $case.Exit -or $processResult.CleanupErrors.Count -ne 0) { throw "Unexpected process result for $($case.Name): exit=$($processResult.ExitCode), cleanup=$($processResult.CleanupErrors). $runFailure" }
        if ($case.ExistingReport) {
            if ([IO.File]::ReadAllText($reportPath) -cne 'do-not-overwrite') { throw 'Existing output was overwritten.' }
        } elseif ($case.Expected.Count -gt 0) {
            $report=Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
            foreach ($key in $case.Expected.Keys) {
                if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw 'Report assertion timeout.' }
                if ($report.$key -cne $case.Expected[$key]) { throw "Case $($case.Name): $key mismatch." }
            }
            $expectedHash=(Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash
            if ($report.source_sha256 -cne $expectedHash) { throw 'Report hash does not bind the parsed input snapshot.' }
            if ($case.Command -eq 'validate-records') {
                if ($report.teacher_labels_permitted_for_training -ne $false -or $report.total -ne ($report.accepted+$report.rejected+$report.pending+$report.failed+$report.abstained+$report.invalid)) { throw 'Training permission or denominator invariant failed.' }
            }
        } elseif ([IO.File]::Exists($reportPath)) { throw "Fatal validation unexpectedly wrote a report: $($case.Name)." }
        $results.Add([pscustomobject]@{case=$case.Name; exit_code=$processResult.ExitCode; passed=$true; process_result=$processReports[0].FullName})
    }
    [IO.File]::WriteAllText((Join-Path $artifactRoot 'summary.json'),(@{schema_version=1;synthetic_contract_only=$true; tool_dll=$ToolDll; tool_sha256=(Get-FileHash -LiteralPath $ToolDll).Hash; passed=$results.Count; elapsed_seconds=$watch.Elapsed.TotalSeconds; results=$results.ToArray()} | ConvertTo-Json -Depth 5))
    Write-Output "Passed $($results.Count) synthetic contract cases; artifacts: $artifactRoot"
} finally {
    $resolvedInputs=[IO.Path]::GetFullPath($inputs)
    if ($resolvedInputs -ne (Join-Path $artifactRoot 'temporary-inputs') -or -not $resolvedInputs.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup ownership check failed.' }
    # Only this invocation's GUID-named fixture directory is temporary; audit logs/reports remain reviewable.
    if ([IO.Directory]::Exists($resolvedInputs)) { Remove-Item -LiteralPath $resolvedInputs -Recurse -Force }
}
