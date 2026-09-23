[CmdletBinding()]
param(
    [string]$Root = (Join-Path $PSScriptRoot '..'),
    [ValidateRange(1, 120)][int]$TimeoutSeconds = 30,
    [ValidateRange(1, 10000)][int]$MaxRecordsPerFile = 1000
)

$ErrorActionPreference = 'Stop'
$rootPath = [IO.Path]::GetFullPath($Root)
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$manifestPath = Join-Path $rootPath 'data/s4-02/dataset-manifest.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($manifest.schema_version -ne 1 -or $manifest.status -ne 'fixture_only') { throw 'Unexpected dataset manifest status.' }

function Assert-BeforeDeadline {
    if ([DateTime]::UtcNow -gt $deadline) { throw "S4 data validation exceeded $TimeoutSeconds seconds." }
}

function Get-Sha256([string]$path) {
    Assert-BeforeDeadline
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
}

if ($manifest.prompt_schema.sha256 -ne (Get-Sha256 (Join-Path $rootPath $manifest.prompt_schema.path))) { throw 'Prompt schema hash mismatch.' }
$seenIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$seenEntitiesByGroup = @{}
$seenContent = @{}
$total = 0
foreach ($file in $manifest.files) {
    Assert-BeforeDeadline
    $path = Join-Path $rootPath $file.path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing dataset file: $($file.path)" }
    if ($file.sha256 -ne (Get-Sha256 $path)) { throw "Dataset file hash mismatch: $($file.path)" }
    $lines = Get-Content -LiteralPath $path
    if ($lines.Count -gt $MaxRecordsPerFile) { throw "Dataset file exceeds record bound: $($file.path)" }
    $count = 0
    foreach ($line in $lines) {
        Assert-BeforeDeadline
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $row = $line | ConvertFrom-Json
        if ($row.language -notin @('en', 'zh') -or $row.domain -notin @('general', 'support') -or $row.split -notin @('calibration', 'test')) { throw "Invalid language/domain/split in $($file.path)." }
        if ($row.language -ne $file.language -or $row.domain -ne $file.domain -or $row.split -ne $file.split) { throw "Row metadata does not match manifest entry: $($file.path)" }
        if ($row.primitive -notin @('choice', 'score', 'boolean') -or [string]::IsNullOrWhiteSpace($row.id) -or [string]::IsNullOrWhiteSpace($row.source_entity)) { throw "Invalid row schema in $($file.path)." }
        if (-not $seenIds.Add([string]$row.id)) { throw "Duplicate record id: $($row.id)" }
        $group = "$($row.language)/$($row.domain)/$($row.source_entity)"
        if ($seenEntitiesByGroup.ContainsKey($group) -and $seenEntitiesByGroup[$group] -ne $row.split) { throw "Source entity crosses splits: $group" }
        $seenEntitiesByGroup[$group] = $row.split
        $content = @{ state = $row.state; instructions = $row.instructions; criteria = $row.criteria } | ConvertTo-Json -Compress -Depth 20
        $contentHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($content)))
        if ($seenContent.ContainsKey($contentHash) -and $seenContent[$contentHash] -ne $row.split) { throw "Content crosses splits: $($row.id)" }
        $seenContent[$contentHash] = $row.split
        $count++
        $total++
    }
    if ($count -ne [int]$file.record_count) { throw "Record count mismatch: $($file.path)" }
}
if ($total -ne [int]$manifest.record_count) { throw 'Dataset total record count mismatch.' }
Write-Output "S4 data validation passed: $total records, $($manifest.files.Count) files, fixture_only"
