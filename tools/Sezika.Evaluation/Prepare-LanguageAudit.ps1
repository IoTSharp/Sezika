param([Parameter(Mandatory)][string]$OutputDirectory, [ValidateRange(1,12)][int]$MaxRecords = 1)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7+ required.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if ([IO.Path]::Exists($destination)) { throw 'OutputDirectory must be new.' }
$utf8 = [Text.UTF8Encoding]::new($false)
$watch = [Diagnostics.Stopwatch]::StartNew()
$paths = @('data/s4-02/en/general/test.jsonl','data/s4-02/en/support/test.jsonl','data/s4-02/zh/general/test.jsonl','data/s4-02/zh/support/test.jsonl')
$rows = [Collections.Generic.List[string]]::new()
$sources = [Collections.Generic.List[object]]::new()
for ($fileIndex = 0; $fileIndex -lt $paths.Count; $fileIndex++) {
    if ($watch.Elapsed.TotalSeconds -ge 10) { throw 'Language audit preparation timeout.' }
    $path = Join-Path $root $paths[$fileIndex]
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines = @(Get-Content -LiteralPath $path -Encoding utf8)
    if ($lines.Count -ne 3) { throw 'Expected three original test fixtures per file.' }
    $sources.Add([ordered]@{ path=$paths[$fileIndex]; sha256=$hash })
    for ($rowIndex = 0; $rowIndex -lt 3 -and $rows.Count -lt $MaxRecords; $rowIndex++) {
        if ($watch.Elapsed.TotalSeconds -ge 10) { throw 'Language audit preparation timeout.' }
        $row = $lines[$rowIndex] | ConvertFrom-Json -AsHashtable
        if ($row.split -cne 'test' -or $row.language -cnotin @('en','zh')) { throw 'Only original language-tagged test fixtures are allowed.' }
        $criteria = if ($row.primitive -eq 'boolean') { [ordered]@{ false=$row.criteria.when_false; true=$row.criteria.when_true } } else { $row.criteria }
        $question = [ordered]@{ type=$row.primitive; instructions=$row.instructions; criteria=$criteria }
        $value = [ordered]@{ id=$row.id; language=$row.language; domain=$row.domain; family=$row.id; source_family='sezika-s4-02-original-test-fixture'; input=[ordered]@{ state=$row.state; questions=[ordered]@{ decision=$question } }; reference=[ordered]@{ target=$row.label } }
        $rows.Add(($value | ConvertTo-Json -Depth 16 -Compress))
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hash) { throw 'Source changed during preparation.' }
    Write-Output "Language fixtures $($fileIndex + 1)/4; selected $($rows.Count)/$MaxRecords"
}
if ($rows.Count -ne $MaxRecords) { throw 'Language fixture count mismatch.' }
[IO.Directory]::CreateDirectory($destination) | Out-Null
$data = Join-Path $destination 'eval.jsonl'
[IO.File]::WriteAllText($data, (($rows -join "`n") + "`n"), $utf8)
$manifest = [ordered]@{ schema_version=1; status='original_fixture_audit_not_independent_quality_acceptance'; license='Apache-2.0'; sources=$sources.ToArray(); records=$rows.Count; dataset_sha256=(Get-FileHash -LiteralPath $data -Algorithm SHA256).Hash.ToLowerInvariant(); transformation='Preserve original test inputs, labels and candidate order; map Boolean when_false/when_true to false/true. No calibration rows, prompt selection or model outputs.' }
[IO.File]::WriteAllText((Join-Path $destination 'manifest.json'), ($manifest | ConvertTo-Json -Depth 16), $utf8)
