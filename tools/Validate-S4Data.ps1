[CmdletBinding()]
param(
    [string]$Root = (Join-Path $PSScriptRoot '..'),
    [string]$ManifestPath = 'datasets/s4-05/example-manifest.json',
    [string]$ReportPath,
    [switch]$RequireAdmission,
    [ValidateRange(1, 120)][int]$TimeoutSeconds = 60,
    [ValidateRange(1, 1000)][int]$MaxRecordsPerFile = 1000
)

# S4-05 is an admission diagnostic. It never approves a licence, labels a
# sealed test, or reports model quality. The fixture is intentionally blocked;
# -RequireAdmission is retained for callers that already pass the switch.
$ErrorActionPreference = 'Stop'
$rootPath = [IO.Path]::GetFullPath($Root)
$manifestPath = [IO.Path]::GetFullPath((Join-Path $rootPath $ManifestPath))
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$maxFileBytes = 4MB
$maxTextChars = 8192
$maxSources = 32
$maxIssues = 10000
$maxPairs = [long]$MaxRecordsPerFile * ($MaxRecordsPerFile - 1) / 2
$nearDuplicateThreshold = 0.82
$strictUtf8 = [Text.UTF8Encoding]::new($false, $true)

function Assert-Budget([string]$Progress) {
    if ([DateTime]::UtcNow -gt $deadline) { throw "S4-05 validation exceeded $TimeoutSeconds seconds ($Progress)." }
}

function Add-Diagnostic([System.Collections.Generic.List[object]]$Issues, [string]$Code,
    [string]$Subject, [string]$Detail, [string]$RelatedId = $null) {
    if ($Issues.Count -ge $maxIssues) { throw 'Diagnostic issue limit exceeded.' }
    $Issues.Add([pscustomobject]@{ code = $Code; subject = $Subject; related_id = $RelatedId; detail = $Detail })
}

function Get-JsonBytes([string]$Path) {
    Assert-Budget "read $Path"
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing artifact: $Path" }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse-point artifact is not allowed: $Path" }
    if ($item.Length -lt 1 -or $item.Length -gt $maxFileBytes) { throw "Artifact size must be 1..$maxFileBytes bytes: $Path" }
    return [IO.File]::ReadAllBytes($item.FullName)
}

function Get-Sha256([byte[]]$Bytes) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function Read-StrictJson([byte[]]$Bytes, [string]$Description) {
    Assert-Budget "parse $Description"
    try {
        $options = [Text.Json.JsonDocumentOptions]::new()
        $options.AllowDuplicateProperties = $false
        $options.MaxDepth = 32
        # Passing a byte[] plus options is ambiguous through PowerShell's
        # static overload binder; strict UTF-8 decoding followed by the string
        # overload preserves the same duplicate-property/depth checks.
        $document = [Text.Json.JsonDocument]::Parse([string]$strictUtf8.GetString($Bytes), $options)
        try { ConvertFrom-Json -InputObject $document.RootElement.GetRawText() -Depth 64 }
        finally { $document.Dispose() }
    } catch [Text.Json.JsonException] { throw "Invalid JSON in $Description`: $($_.Exception.Message)" }
}

function Assert-AllowedProperties($Object, [string[]]$Allowed, [string]$Description) {
    $actual = @($Object.PSObject.Properties.Name)
    foreach ($name in $actual) {
        if ($name -notin $Allowed) { throw "Unknown property '$name' in $Description." }
    }
}

function Assert-RelativeArtifact([string]$Base, $Artifact, [string]$Description) {
    if ($null -eq $Artifact -or [string]::IsNullOrWhiteSpace([string]$Artifact.path) -or
        [IO.Path]::IsPathRooted([string]$Artifact.path) -or
        [string]$Artifact.sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw "Invalid $Description artifact reference." }
    $path = [IO.Path]::GetFullPath((Join-Path $Base ([string]$Artifact.path)))
    $relative = [IO.Path]::GetRelativePath($Base, $path)
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or $relative.StartsWith("..$([IO.Path]::DirectorySeparatorChar)")) {
        throw "$Description escapes its manifest directory."
    }
    $cursor = Get-Item -LiteralPath $path -Force
    while ($null -ne $cursor) {
        if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse-point path is not allowed: $path" }
        if ($cursor.FullName.TrimEnd('\') -eq $Base.TrimEnd('\')) { break }
        $parent = $cursor.Directory
        if ($null -eq $parent) { throw "Artifact path has no manifest root: $path" }
        $cursor = $parent
    }
    [pscustomobject]@{ path = $path; declared_hash = ([string]$Artifact.sha256).ToLowerInvariant() }
}

function Verify-Artifact([string]$Base, $Artifact, [string]$Description) {
    $resolved = Assert-RelativeArtifact $Base $Artifact $Description
    $bytes = Get-JsonBytes $resolved.path
    $actual = Get-Sha256 $bytes
    if ($actual -ne $resolved.declared_hash) { throw "SHA-256 mismatch for $Description ($($Artifact.path))." }
    [pscustomobject]@{ bytes = $bytes; path = $resolved.path; sha256 = $actual }
}

function Normalize-Text([string]$Text) {
    Assert-Budget 'normalize text'
    if ($Text.Length -gt $maxTextChars) { throw "Record text exceeds $maxTextChars UTF-16 units." }
    $normalized = $Text.Normalize([Text.NormalizationForm]::FormKC).ToUpperInvariant()
    $builder = [Text.StringBuilder]::new()
    foreach ($rune in $normalized.EnumerateRunes()) {
        Assert-Budget 'normalize text'
        $category = [Text.Rune]::GetUnicodeCategory($rune)
        if ([Text.Rune]::IsLetterOrDigit($rune) -or $category -in @([Globalization.UnicodeCategory]::NonSpacingMark, [Globalization.UnicodeCategory]::SpacingCombiningMark)) {
            [void]$builder.Append($rune.ToString())
        }
    }
    if ($builder.Length -eq 0) { throw 'Record text must contain a letter or digit.' }
    $builder.ToString()
}

function Get-Shingles([string]$Text) {
    $runes = @($Text.EnumerateRunes())
    $set = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if ($runes.Count -lt 3) { [void]$set.Add($Text); return $set }
    for ($i = 0; $i + 2 -lt $runes.Count; $i++) {
        Assert-Budget 'build trigrams'
        [void]$set.Add($runes[$i].ToString() + $runes[$i + 1].ToString() + $runes[$i + 2].ToString())
    }
    $set
}

function Get-Jaccard($Left, $Right) {
    $intersection = 0
    foreach ($shingle in $Left) { Assert-Budget 'compare trigrams'; if ($Right.Contains($shingle)) { $intersection++ } }
    $union = $Left.Count + $Right.Count - $intersection
    if ($union -eq 0) { return 1.0 }
    [double]$intersection / $union
}

function Read-Records([string]$Path) {
    $bytes = Get-JsonBytes $Path
    $text = $strictUtf8.GetString($bytes)
    $rows = [Collections.Generic.List[object]]::new()
    $lines = $text -split "`n"
    if ($lines.Count -gt ($MaxRecordsPerFile + 1)) { throw "Records file exceeds $MaxRecordsPerFile physical lines." }
    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
        Assert-Budget "read record $($lineIndex + 1)/$($lines.Count)"
        $line = $lines[$lineIndex].TrimEnd("`r")
        if ([string]::IsNullOrWhiteSpace($line)) {
            if ($lineIndex -lt ($lines.Count - 1)) { throw "Blank JSONL line at $($lineIndex + 1)." }
            continue
        }
        if ($rows.Count -ge $MaxRecordsPerFile) { throw "Records file exceeds $MaxRecordsPerFile records." }
        $row = Read-StrictJson ($strictUtf8.GetBytes($line)) "records line $($lineIndex + 1)"
        Assert-AllowedProperties $row @('id','source_id','family','entities','language','domain','split','text','derivation','parent_id','human_review','reviewer','reviewed_utc','review_note','exposure') "records line $($lineIndex + 1)"
        $rows.Add($row)
    }
    if ($rows.Count -eq 0) { throw 'Records file is empty.' }
    [pscustomobject]@{ bytes = $bytes; rows = $rows }
}

function Test-S4Manifest([string]$Path) {
    $manifestBytes = Get-JsonBytes $Path
    $manifest = Read-StrictJson $manifestBytes 'manifest'
    Assert-AllowedProperties $manifest @('schema_version','dataset_id','stage','intended_use','records','sources','test_seal') 'manifest'
    if ($manifest.schema_version -ne 1 -or $manifest.stage -notin @('FixtureOnly','Candidate') -or
        $manifest.intended_use -notin @('Research','Commercial','OpenSourceRedistribution')) { throw 'Invalid S4-05 manifest schema, stage, or intended use.' }
    if ([string]::IsNullOrWhiteSpace([string]$manifest.dataset_id) -or @($manifest.sources).Count -lt 1 -or @($manifest.sources).Count -gt $maxSources) { throw 'Invalid S4-05 dataset id or source count.' }
    $base = Split-Path -Parent $Path
    $recordArtifact = Verify-Artifact $base $manifest.records 'records'
    $recordData = Read-Records $recordArtifact.path
    if ($recordArtifact.sha256 -ne (Get-Sha256 $recordData.bytes)) { throw 'Records hash changed while reading.' }
    $sourceById = @{}
    $issues = [Collections.Generic.List[object]]::new()
    if ($manifest.stage -eq 'FixtureOnly') { Add-Diagnostic $issues 'fixture_only' $manifest.dataset_id 'Fixture data cannot be admitted as a real dataset.' }
    foreach ($source in @($manifest.sources)) {
        Assert-Budget 'sources'
        Assert-AllowedProperties $source @('id','upstream_id','upstream_split','revision','origin','raw','license_expression','license_review','license_reviewer','license_reviewed_utc','license_evidence','allowed_uses','allowed_splits','exposure') "source $($source.id)"
        if ([string]::IsNullOrWhiteSpace([string]$source.id) -or $sourceById.ContainsKey([string]$source.id)) { throw "Duplicate/empty source id: $($source.id)" }
        $sourceById[[string]$source.id] = $source
        if ($source.origin -notin @('Original','ThirdParty','PawsViewedTest250','NimbleViewedEval324') -or $source.exposure -notin @('Unseen','DevelopmentViewed','EvaluationViewed') -or
            $source.license_review -notin @('Pending','Approved','Rejected') -or @($source.allowed_uses).Count -gt 3 -or @($source.allowed_splits).Count -gt 5) { throw "Invalid source policy: $($source.id)" }
        [void](Verify-Artifact $base $source.raw "source $($source.id) raw")
        if ($null -ne $source.license_evidence) { [void](Verify-Artifact $base $source.license_evidence "source $($source.id) license evidence") }
        if ($source.license_review -ne 'Approved') { Add-Diagnostic $issues 'license_not_approved' $source.id 'License review is pending or rejected.' }
        if ($source.license_review -eq 'Approved' -and ([string]::IsNullOrWhiteSpace([string]$source.license_reviewer) -or $null -eq $source.license_reviewed_utc -or [DateTimeOffset]$source.license_reviewed_utc -gt [DateTimeOffset]::UtcNow -or $null -eq $source.license_evidence)) { Add-Diagnostic $issues 'license_evidence_missing' $source.id 'Approved license requires reviewer, past UTC time, and hash-bound evidence.' }
        if ($manifest.intended_use -notin @($source.allowed_uses)) { Add-Diagnostic $issues 'use_not_allowed' $source.id 'Manifest intended use is absent from the source allowlist.' }
        $auditOnly = $source.exposure -eq 'EvaluationViewed' -or $source.origin -in @('PawsViewedTest250','NimbleViewedEval324') -or [string]$source.raw.sha256 -in @('c295258fdd73452f196b2673bdbee58cf01fa3f24400c2f1c0f26fd1126bc6f3','8e9e48b8de5206593912ae01ddc95bd77e40ad2ecf4c9292c1711290eca0d896')
        if ($auditOnly -and @($source.allowed_splits) -ne @('Audit')) { Add-Diagnostic $issues 'viewed_source_policy' $source.id 'Viewed evaluation sources may allow only Audit.' }
    }
    $rows = [Collections.Generic.List[object]]::new(); $byId = @{}; $counts = @{}; $fingerprints = @{}; $shingles = @{}
    foreach ($row in $recordData.rows) {
        Assert-Budget "validate record $($rows.Count + 1)/$($recordData.rows.Count)"
        foreach ($field in @('id','source_id','family','language','domain','split','text','derivation','human_review','exposure')) { if ([string]::IsNullOrWhiteSpace([string]$row.$field)) { throw "Missing $field on record." } }
        if ($byId.ContainsKey([string]$row.id)) { throw "Duplicate record id: $($row.id)" }
        $byId[[string]$row.id] = $row; $rows.Add($row)
        if ($row.split -notin @('Training','Development','Calibration','SealedTest','Audit') -or $row.derivation -notin @('Original','Translation','Counterfactual','Paraphrase') -or $row.human_review -notin @('Pending','Approved','Rejected') -or $row.exposure -notin @('Unseen','DevelopmentViewed','EvaluationViewed')) { throw "Invalid split/derivation/review/exposure on $($row.id)." }
        if (@($row.entities).Count -lt 1 -or @($row.entities).Count -gt 32 -or @($row.entities | Where-Object { [string]::IsNullOrWhiteSpace([string]$_) -or ([string]$_).Trim() -ne [string]$_ -or ([string]$_).Length -gt 128 }).Count -gt 0) { throw "Invalid entities on $($row.id)." }
        if (($row.derivation -eq 'Original' -and $null -ne $row.parent_id) -or ($row.derivation -ne 'Original' -and [string]::IsNullOrWhiteSpace([string]$row.parent_id))) { throw "Derivation/parent mismatch on $($row.id)." }
        if (-not $sourceById.ContainsKey([string]$row.source_id)) { throw "Unknown source on $($row.id)." }
        $source = $sourceById[[string]$row.source_id]
        if ($row.split -notin @($source.allowed_splits)) { Add-Diagnostic $issues 'split_not_allowed' $row.id 'Record split is absent from source allowlist.' }
        if ($row.human_review -ne 'Approved' -or [string]::IsNullOrWhiteSpace([string]$row.reviewer) -or $null -eq $row.reviewed_utc -or [DateTimeOffset]$row.reviewed_utc -gt [DateTimeOffset]::UtcNow -or [string]::IsNullOrWhiteSpace([string]$row.review_note)) { Add-Diagnostic $issues 'human_review_required' $row.id 'Human provenance/label review requires reviewer, past UTC time, and note.' }
        $auditOnly = $source.exposure -eq 'EvaluationViewed' -or $source.origin -in @('PawsViewedTest250','NimbleViewedEval324') -or $row.exposure -eq 'EvaluationViewed'
        if ($auditOnly -and $row.split -ne 'Audit') { Add-Diagnostic $issues 'viewed_evaluation_reused' $row.id 'Viewed evaluation content and derivatives are audit-only.' }
        if (($source.exposure -ne 'Unseen' -or $row.exposure -ne 'Unseen') -and $row.split -eq 'SealedTest') { Add-Diagnostic $issues 'sealed_test_exposed' $row.id 'Viewed content cannot become a fresh sealed test.' }
        $counts[$row.split] = 1 + [int]$counts[$row.split]
        $normalized = Normalize-Text ([string]$row.text)
        $fingerprints[$row.id] = Get-Sha256 ($strictUtf8.GetBytes($normalized)); $shingles[$row.id] = Get-Shingles $normalized
    }
    foreach ($row in $rows) {
        if ($row.derivation -eq 'Original') { continue }
        $current = $row; $seen = @{}; $depth = 0
        while ($null -ne $current.parent_id -and $depth -lt $rows.Count) {
            Assert-Budget 'derivation lineage'; $depth++
            if ($seen.ContainsKey([string]$current.id)) { Add-Diagnostic $issues 'parent_cycle' $row.id 'Derivation ancestry contains a cycle.' $current.id; break }
            $seen[[string]$current.id] = $true
            if (-not $byId.ContainsKey([string]$current.parent_id)) { Add-Diagnostic $issues 'parent_missing' $row.id 'Every derivation ancestor must be present in this inventory.' ([string]$current.parent_id); break }
            $parent = $byId[[string]$current.parent_id]
            if ($row.family -ne $parent.family -or $row.split -ne $parent.split) { Add-Diagnostic $issues 'derivation_isolation' $row.id 'Translations, paraphrases, and counterfactuals inherit family and split.' $parent.id }
            $current = $parent
        }
        if ($depth -ge $rows.Count) { Add-Diagnostic $issues 'parent_cycle' $row.id 'Derivation ancestry exceeded the bounded record count.' }
    }
    $rowArray = @($rows); $compared = 0L
    for ($left = 0; $left -lt $rowArray.Count; $left++) {
        for ($right = $left + 1; $right -lt $rowArray.Count; $right++) {
            Assert-Budget "compare pair $($compared + 1)"; $compared++
            if ($compared -gt $maxPairs) { throw 'Record pair limit exceeded.' }
            $a = $rowArray[$left]; $b = $rowArray[$right]
            if ($a.split -eq $b.split) { continue }
            if ($a.family -eq $b.family) { Add-Diagnostic $issues 'family_crosses_split' $a.id 'Family members must remain in one split.' $b.id }
            if (@($a.entities | Where-Object { $_ -in @($b.entities) }).Count -gt 0) { Add-Diagnostic $issues 'entity_crosses_split' $a.id 'A declared entity occurs in different splits.' $b.id }
            if ($fingerprints[$a.id] -eq $fingerprints[$b.id]) { Add-Diagnostic $issues 'fingerprint_crosses_split' $a.id 'Normalized text occurs in different splits.' $b.id }
            elseif ((Get-Jaccard $shingles[$a.id] $shingles[$b.id]) -ge $nearDuplicateThreshold) { Add-Diagnostic $issues 'near_duplicate_crosses_split' $a.id 'Character trigram Jaccard exceeds frozen 0.82 threshold.' $b.id }
        }
    }
    foreach ($required in @('Development','Calibration','SealedTest')) { if ([int]$counts[$required] -le 0) { Add-Diagnostic $issues 'required_split_missing' $manifest.dataset_id "Required split is empty: $required." } }
    $seal = $manifest.test_seal
    Assert-AllowedProperties $seal @('status','custodian','sealed_utc','evidence') 'test_seal'
    if ($seal.status -ne 'Sealed' -or [string]::IsNullOrWhiteSpace([string]$seal.custodian) -or $null -eq $seal.sealed_utc -or [DateTimeOffset]$seal.sealed_utc -gt [DateTimeOffset]::UtcNow -or $null -eq $seal.evidence) { Add-Diagnostic $issues 'test_not_sealed' $manifest.dataset_id 'Seal requires custodian, past UTC time, and hash-bound evidence.' }
    if ($null -ne $seal.evidence) {
        $evidence = Verify-Artifact $base $seal.evidence 'seal evidence'; $sealObject = Read-StrictJson $evidence.bytes 'seal evidence'
        Assert-AllowedProperties $sealObject @('dataset_id','records_sha256','custodian','sealed_utc') 'seal evidence'
        if ($sealObject.dataset_id -ne $manifest.dataset_id -or [string]$sealObject.records_sha256 -ne [string]$manifest.records.sha256 -or $sealObject.custodian -ne $seal.custodian -or [DateTimeOffset]$sealObject.sealed_utc -ne [DateTimeOffset]$seal.sealed_utc) { Add-Diagnostic $issues 'seal_binding_mismatch' $manifest.dataset_id 'Seal evidence must bind dataset, records hash, custodian, and freeze time.' }
    }
    $status = if ($issues.Count -eq 0) { 'checks_passed_pending_human_acceptance' } else { 'blocked' }
    [pscustomobject]@{ dataset_id = $manifest.dataset_id; manifest_sha256 = Get-Sha256 $manifestBytes; records_sha256 = Get-Sha256 $recordData.bytes; status = $status; admission_checks_passed = ($issues.Count -eq 0); requires_human_acceptance = $true; represents_model_quality = $false; record_count = $rows.Count; compared_pairs = $compared; splits = @($counts.GetEnumerator() | ForEach-Object { [pscustomobject]@{ split = $_.Key; count = [int]$_.Value } }); issues = @($issues); checked_utc = [DateTimeOffset]::UtcNow }
}

function Test-IndependentSmoke([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return [pscustomobject]@{ status = 'missing'; records = 0; records_sha256 = $null; issues = @('independent-v1-original manifest is absent') } }
    $bytes = Get-JsonBytes $Path; $manifest = Read-StrictJson $bytes 'independent smoke manifest'
    Assert-AllowedProperties $manifest @('dataset_id','records_file','license_expression','source','label_provenance','allowed_uses','allowed_splits','split_counts','human_acceptance','status','represents_model_quality','records_sha256') 'independent smoke manifest'
    $base = Split-Path -Parent $Path; $recordPath = [IO.Path]::GetFullPath((Join-Path $base ([string]$manifest.records_file)))
    $recordBytes = Get-JsonBytes $recordPath; $actual = Get-Sha256 $recordBytes
    if ($actual -ne [string]$manifest.records_sha256) { throw 'independent-v1-original records hash mismatch.' }
    $lines = $strictUtf8.GetString($recordBytes) -split "`n"; $seen = @{}; $counts = @{}
    foreach ($line in $lines) {
        Assert-Budget 'independent smoke records'; $line = $line.TrimEnd("`r"); if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $row = Read-StrictJson ($strictUtf8.GetBytes($line)) 'independent smoke record'
        Assert-AllowedProperties $row @('record_id','split','language','type','state','instruction','candidates','levels','statement','when_false','when_true','target_index') 'independent smoke record'
        if ([string]::IsNullOrWhiteSpace([string]$row.record_id) -or $seen.ContainsKey([string]$row.record_id) -or $row.split -notin @('train','development','calibration','sealed_test') -or $row.language -notin @('zh','en') -or $row.type -notin @('choice','score','boolean')) { throw "Invalid independent smoke record: $($row.record_id)" }
        $seen[[string]$row.record_id] = $true; $counts[$row.split] = 1 + [int]$counts[$row.split]
    }
    [pscustomobject]@{ status = [string]$manifest.status; records = $seen.Count; records_sha256 = $actual; split_counts = $counts; represents_model_quality = [bool]$manifest.represents_model_quality; issues = @() }
}

try {
    $result = Test-S4Manifest $manifestPath
    $independentPath = Join-Path $rootPath 'datasets/independent-v1-original/manifest.json'
    $smoke = Test-IndependentSmoke $independentPath
    $output = [pscustomobject]@{ schema_version = 1; policy = 's4-05-admission-v1'; s4_05 = $result; independent_v1_original = $smoke }
    if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
        $reportFull = [IO.Path]::GetFullPath((Join-Path $rootPath $ReportPath))
        if (Test-Path -LiteralPath $reportFull) { throw "Report already exists: $reportFull" }
        $parent = Split-Path -Parent $reportFull; if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw "Report directory does not exist: $parent" }
        [IO.File]::WriteAllText($reportFull, ($output | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
    }
    Write-Output "S4-05 contract: $($result.status); records $($result.record_count); pairs $($result.compared_pairs); issues $(@($result.issues).Count); independent smoke $($smoke.status) ($($smoke.records) records)"
    # A blocked candidate is a completed audit with a blocking finding, which
    # has its own stable exit code.  -RequireAdmission remains accepted for CI
    # callers but does not turn a blocked fixture into success.
    if ($result.status -ne 'checks_passed_pending_human_acceptance') { exit 3 }
    exit 0
} catch {
    Write-Error $_
    exit 1
}
