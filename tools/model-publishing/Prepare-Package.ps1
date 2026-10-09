#Requires -Version 7.0
<#
Build a local, experimental model publication folder. No network, inference,
teacher calls or child processes are used. Run -ProbeOnly before the full run.
Publication must be performed separately by an authorized account.

OutputDirectory is a new .artifacts/<publication-task>/package-staging folder.
Its package/ child is the only upload directory. Preparation and process
receipts beside package/ are local audit material and must not be uploaded.
The approved publication-notices.json is read beside license-materials/.
Completed or partial packages are preserved; the script never replaces them.
The current model-card template intentionally binds this fixed experiment.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TaskDirectory,
    [string]$ModelDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$LicenseMaterialsDirectory,
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$RepositoryId = 'IoTSharp/Sezika-mmBERT-8192-experimental',
    [ValidateRange(1,120)][int]$TimeoutSeconds = 120,
    [switch]$ProbeOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$timer = [Diagnostics.Stopwatch]::StartNew()
$maximumFiles = 32
$maximumBytes = 2GB
$maximumChunkOperations = 8192
$chunkOperations = 0
$publishedBytes = 0L
$publishedFiles = [Collections.Generic.List[object]]::new()
$lockStream = $null
$packageStarted = $false
$receipt = $null
$encoding = [Text.UTF8Encoding]::new($false)
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '.artifacts'))
$taskRoot = [IO.Path]::GetFullPath($TaskDirectory)
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $ModelDirectory) { $ModelDirectory = Join-Path $taskRoot 'trained-model' }
$modelRoot = [IO.Path]::GetFullPath($ModelDirectory)
if (-not $LicenseMaterialsDirectory) { $LicenseMaterialsDirectory = Join-Path ([IO.Path]::GetDirectoryName($outputRoot)) 'license-materials' }
$materialsRoot = [IO.Path]::GetFullPath($LicenseMaterialsDirectory)
$packageRoot = Join-Path $outputRoot 'package'
$cancelFile = Join-Path $outputRoot 'cancel.request'
$lockPath = Join-Path $outputRoot '.prepare.lock'

function Assert-Inside([string]$Path, [string]$Parent) {
    $prefix = $Parent.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Path is outside its authorized artifact root.' }
}
function Assert-NoArtifactLinks([string]$Path) {
    $cursor = $Path
    for ($depth = 0; $depth -lt 8; $depth++) {
        Assert-Budget
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked artifact paths are not supported.' }
        }
        if ($cursor -eq $repositoryRoot) { return }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    throw 'Artifact ancestor validation exceeded its eight-directory budget.'
}
function Assert-Budget {
    if ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw 'Publication preparation wall-clock budget exhausted.' }
    if ($chunkOperations -ge $maximumChunkOperations) { throw 'Publication preparation chunk-operation budget exhausted.' }
    if (Test-Path -LiteralPath $cancelFile) { throw 'Publication preparation cancelled.' }
}
function Assert-PlainFile([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Expected a regular, non-link file.' }
    if ($item.Length -gt $maximumBytes) { throw 'Single file exceeds publication byte budget.' }
    return $item
}
function Get-BoundedHash([string]$Path) {
    Assert-Budget
    $item = Assert-PlainFile $Path
    $chunkCount = [int][Math]::Max(1, [Math]::Ceiling($item.Length / 8MB))
    if ($chunkCount -gt 256) { throw 'File needs more than 256 read chunks.' }
    $stream = $null
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    $buffer = [byte[]]::new(8MB)
    $readBytes = 0L
    try {
        $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read, 8MB, [IO.FileOptions]::SequentialScan)
        for ($chunk = 0; $chunk -lt $chunkCount; $chunk++) {
            Assert-Budget
            $script:chunkOperations++
            $remaining = $item.Length - $readBytes
            if ($remaining -eq 0) { break }
            $read = $stream.Read($buffer, 0, [int][Math]::Min($buffer.Length, $remaining))
            if ($read -le 0) { throw 'Unexpected end of source file.' }
            $readBytes += $read
            $hash.AppendData($buffer, 0, $read)
        }
        if ($readBytes -ne $item.Length -or $stream.Length -ne $item.Length) { throw 'File length changed during hashing.' }
        return [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
    } finally {
        if ($stream) { $stream.Dispose() }
        $hash.Dispose()
    }
}
function Copy-Verified([string]$Source, [string]$Destination, [string]$ExpectedHash) {
    Assert-Budget
    Assert-Inside ([IO.Path]::GetFullPath($Destination)) $outputRoot
    $item = Assert-PlainFile $Source
    if ($publishedFiles.Count -ge $maximumFiles -or ($publishedBytes + $item.Length) -gt $maximumBytes) { throw 'Publication file/byte budget exceeded.' }
    if (Test-Path -LiteralPath $Destination) { throw 'Existing publication file is preserved.' }
    if ((Get-BoundedHash $Source) -ne $ExpectedHash.ToLowerInvariant()) { throw 'Source SHA256 differs from verified evidence.' }
    $parent = [IO.Path]::GetDirectoryName($Destination)
    if (-not (Test-Path -LiteralPath $parent)) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    $sourceStream = $null
    $destinationStream = $null
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    $buffer = [byte[]]::new(8MB)
    $copied = 0L
    $chunks = [int][Math]::Max(1, [Math]::Ceiling($item.Length / 8MB))
    if ($chunks -gt 256) { throw 'Copy needs more than 256 chunks.' }
    try {
        $sourceStream = [IO.FileStream]::new($Source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read, 8MB, [IO.FileOptions]::SequentialScan)
        $destinationStream = [IO.FileStream]::new($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 8MB)
        for ($chunk = 0; $chunk -lt $chunks; $chunk++) {
            Assert-Budget
            $script:chunkOperations++
            $remaining = $item.Length - $copied
            if ($remaining -eq 0) { break }
            $read = $sourceStream.Read($buffer, 0, [int][Math]::Min($buffer.Length, $remaining))
            if ($read -le 0) { throw 'Unexpected end of file during copy.' }
            $destinationStream.Write($buffer, 0, $read)
            $hash.AppendData($buffer, 0, $read)
            $copied += $read
            if (($chunk % 32) -eq 0) { Write-Progress -Activity 'Copy verified model publication files' -Status ([IO.Path]::GetFileName($Destination)) -PercentComplete ([int](100 * $copied / [Math]::Max(1,$item.Length))) }
        }
        $destinationStream.Flush()
        if ($copied -ne $item.Length -or [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant() -ne $ExpectedHash.ToLowerInvariant()) { throw 'Copied source byte/SHA256 mismatch.' }
    } finally {
        if ($sourceStream) { $sourceStream.Dispose() }
        if ($destinationStream) { $destinationStream.Dispose() }
        $hash.Dispose()
    }
    if ((Get-BoundedHash $Destination) -ne $ExpectedHash.ToLowerInvariant()) { throw 'Destination SHA256 mismatch.' }
    $script:publishedBytes += $copied
    $publishedFiles.Add([ordered]@{path=[IO.Path]::GetRelativePath($packageRoot,$Destination).Replace('\','/');bytes=$copied;sha256=$ExpectedHash.ToLowerInvariant();source_sha256=$ExpectedHash.ToLowerInvariant();kind='copied'})
    Write-Host ('Verified: {0} ({1} bytes)' -f [IO.Path]::GetFileName($Destination),$copied)
}
function Assert-Allowlist([string]$Directory, [string[]]$Names) {
    Assert-Budget
    $items = @(Get-ChildItem -LiteralPath $Directory -Force | Select-Object -First ($Names.Count + 1))
    if ($items.Count -ne $Names.Count) { throw 'Unexpected file count in source directory.' }
    for ($index = 0; $index -lt $items.Count; $index++) {
        Assert-Budget
        if ($items[$index].Name -cnotin $Names -or ($items[$index].Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unknown file or link is not publishable.' }
    }
}
function Write-PublicText([string]$Name, [string]$Text) {
    Assert-Budget
    if ($publishedFiles.Count -ge ($maximumFiles - 1)) { throw 'Publication file budget exceeded.' }
    if ($Text -match '(?i)(hf_[a-z0-9]{20,}|Authorization\s*:\s*Bearer|root\+vm-|token_ids|"command_line"|"task_directory"|[a-z]:\\(?:Users|source)|/root/sezika)') { throw 'Private operational detail was detected in public text.' }
    $path = Join-Path $packageRoot $Name
    Assert-Inside ([IO.Path]::GetFullPath($path)) $packageRoot
    if (Test-Path -LiteralPath $path) { throw 'Existing generated public file is preserved.' }
    $bytes = $encoding.GetByteCount($Text)
    if (($publishedBytes + $bytes) -gt $maximumBytes) { throw 'Publication byte budget exceeded.' }
    [IO.File]::WriteAllText($path,$Text,$encoding)
    $script:publishedBytes += $bytes
    $publishedFiles.Add([ordered]@{path=$Name.Replace('\','/');bytes=$bytes;sha256=(Get-BoundedHash $path);source_sha256=$null;kind='generated'})
}
function Get-AssetHash([string]$Path, $Verification) {
    $assetBindings = @($Verification.assets | Where-Object { [IO.Path]::GetFullPath($_.path) -eq $Path })
    if ($assetBindings.Count -ne 1 -or $assetBindings[0].sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Asset is not uniquely bound by independent verification.' }
    return $assetBindings[0].sha256.ToLowerInvariant()
}

Assert-Inside $taskRoot $artifactsRoot
Assert-Inside $outputRoot $artifactsRoot
Assert-Inside $materialsRoot $artifactsRoot
Assert-NoArtifactLinks $taskRoot
Assert-NoArtifactLinks $outputRoot
Assert-NoArtifactLinks $modelRoot
Assert-NoArtifactLinks $materialsRoot
if ($modelRoot -ne ([IO.Path]::GetFullPath((Join-Path $taskRoot 'trained-model')))) { throw 'ModelDirectory must be the selected task trained-model directory.' }
if ([IO.Path]::GetFileName($outputRoot) -ne 'package-staging') { throw 'OutputDirectory must be a dedicated package-staging folder.' }
if (Test-Path -LiteralPath $outputRoot) {
    $existing = @(Get-ChildItem -LiteralPath $outputRoot -Force | Select-Object -First 3)
    if (@($existing | Where-Object { $_.Name -cne 'probe-result.json' }).Count -gt 0) { throw 'Existing staging contents are preserved; choose a new dedicated task directory.' }
} else { [IO.Directory]::CreateDirectory($outputRoot) | Out-Null }
$process = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $PID)
$parentChain = [Collections.Generic.List[object]]::new()
$nextParentId = $process.ParentProcessId
for ($depth = 0; $depth -lt 8; $depth++) {
    Assert-Budget
    if ($nextParentId -le 0) { break }
    $parent = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $nextParentId)
    if (-not $parent) { break }
    $parentChain.Add([ordered]@{pid=$parent.ProcessId;started=$parent.CreationDate;command_line=$parent.CommandLine;parent_pid=$parent.ParentProcessId})
    if ($nextParentId -eq $parent.ParentProcessId) { break }
    $nextParentId = $parent.ParentProcessId
}
$localProcess = [ordered]@{pid=$PID;started=$process.CreationDate;command_line=$process.CommandLine;parent_pid=$process.ParentProcessId;parent_chain=$parentChain.ToArray();external_processes_started=0}
try {
    $lockStream = [IO.FileStream]::new($lockPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    if ($ProbeOnly) {
        $probeRoot = Join-Path $outputRoot '.probe'
        if (Test-Path -LiteralPath $probeRoot) { throw 'Existing probe is preserved.' }
        [IO.Directory]::CreateDirectory($probeRoot) | Out-Null
        $ownedProbe = @('source-a.txt','source-b.txt','copied-a.txt','copied-b.txt','unexpected.txt')
        try {
            [IO.File]::WriteAllText((Join-Path $probeRoot 'source-a.txt'),'tiny-a',$encoding)
            [IO.File]::WriteAllText((Join-Path $probeRoot 'source-b.txt'),'tiny-b',$encoding)
            Assert-Allowlist $probeRoot @('source-a.txt','source-b.txt')
            for ($index = 0; $index -lt 2; $index++) {
                Assert-Budget
                $suffix = @('a','b')[$index]
                $source = Join-Path $probeRoot ('source-' + $suffix + '.txt')
                Copy-Verified $source (Join-Path $probeRoot ('copied-' + $suffix + '.txt')) (Get-BoundedHash $source)
            }
            [IO.File]::WriteAllText((Join-Path $probeRoot 'unexpected.txt'),'rejected',$encoding)
            $rejected = $false
            try { Assert-Allowlist $probeRoot @('source-a.txt','source-b.txt','copied-a.txt','copied-b.txt') } catch { $rejected = $true }
            if (-not $rejected) { throw 'Unknown-file rejection probe failed.' }
            $receipt = [ordered]@{schema_version=1;status='PASS_two_small_files_hash_copy_path_and_unknown_file_rejection';files=2;unknown_file_rejected=$rejected;elapsed_seconds=$timer.Elapsed.TotalSeconds;process=$localProcess;child_process_cleanup='not_applicable_no_children';maximum_files=$maximumFiles;maximum_bytes=$maximumBytes;maximum_seconds=$TimeoutSeconds;chunk_operations=$chunkOperations}
        } finally {
            for ($index = 0; $index -lt $ownedProbe.Count; $index++) {
                $ownedPath = [IO.Path]::GetFullPath((Join-Path $probeRoot $ownedProbe[$index]))
                Assert-Inside $ownedPath $probeRoot
                if (Test-Path -LiteralPath $ownedPath) { Remove-Item -LiteralPath $ownedPath -Force }
            }
            if (@(Get-ChildItem -LiteralPath $probeRoot -Force | Select-Object -First 1).Count -eq 0) { Remove-Item -LiteralPath $probeRoot }
        }
        [IO.File]::WriteAllText((Join-Path $outputRoot 'probe-result.json'),($receipt | ConvertTo-Json -Depth 8),$encoding)
        $receipt | ConvertTo-Json -Depth 8
        return
    }
    $probeReceiptPath = Join-Path $outputRoot 'probe-result.json'
    if (-not (Test-Path -LiteralPath $probeReceiptPath) -or (Get-Content -LiteralPath $probeReceiptPath -Raw | ConvertFrom-Json).status -ne 'PASS_two_small_files_hash_copy_path_and_unknown_file_rejection') { throw 'Run the two-file -ProbeOnly check first.' }
    Assert-Allowlist $modelRoot @('encoder.safetensors','expanded-head.asset','expanded-training-report.json','expanded-training.checkpoint','files.sha256','model.json','tokenizer')
    Assert-Allowlist (Join-Path $modelRoot 'tokenizer') @('tokenizer.json')
    $verificationPath = Join-Path $taskRoot 'verification.json'
    $verification = Get-Content -LiteralPath $verificationPath -Raw | ConvertFrom-Json
    if (@($verification.assets).Count -gt 32) { throw 'Verification asset lookup exceeds its bounded size.' }
    if ($verification.status -ne 'PASS_experiment_evidence_only_not_formal_acceptance' -or @($verification.checks).Count -ne 10 -or @($verification.checks | Where-Object { $_.status -ne 'PASS' }).Count -ne 0 -or $verification.formal_acceptance) { throw 'Independent experiment verification is incomplete.' }
    $reportPath = Join-Path $modelRoot 'expanded-training-report.json'
    $reportHash = Get-AssetHash $reportPath $verification
    if ((Get-BoundedHash $reportPath) -ne $reportHash) { throw 'Training report is no longer bound to verification.' }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($report.completed_steps -ne 24576 -or $report.seed -ne 17 -or $report.learning_rate -ne 0.001 -or -not $report.encoder_frozen -or $report.train_count -ne 162 -or $report.development_count -ne 53 -or $report.max_tokens -ne 8192 -or $report.encoder_forwards_completed -ne 231) { throw 'Selected model differs from the authorized fixed experiment.' }
    $manifest = Get-Content -LiteralPath (Join-Path $modelRoot 'model.json') -Raw | ConvertFrom-Json
    $noticeIndexPath = Join-Path ([IO.Path]::GetDirectoryName($materialsRoot)) 'publication-notices.json'
    $noticeIndex = Get-Content -LiteralPath $noticeIndexPath -Raw | ConvertFrom-Json
    $noticeFiles = @($noticeIndex.files)
    if (-not $noticeIndex.public_safe -or $noticeIndex.notice -ne 'ASSET-NOTICES.md') { throw 'Notice index must explicitly identify public-safe component notices.' }
    if ($noticeFiles.Count -gt 12 -or $noticeFiles.Count -lt 1) { throw 'Expected 1 to 12 explicitly approved license notice files.' }
    $expectedNoticeBytes = 0L
    for ($index = 0; $index -lt $noticeFiles.Count; $index++) {
        Assert-Budget
        $notice = $noticeFiles[$index]
        if ($notice.file -notmatch '^[A-Za-z0-9_.-]+$' -or $notice.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or $notice.bytes -gt 2MB) { throw 'Invalid approved notice entry.' }
        $expectedNoticeBytes += $notice.bytes
    }
    if ($expectedNoticeBytes -gt 2MB) { throw 'License notices exceed 2 MiB.' }
    [IO.Directory]::CreateDirectory($packageRoot) | Out-Null
    $packageStarted = $true
    $modelFiles = @('encoder.safetensors','tokenizer/tokenizer.json','expanded-head.asset','expanded-training.checkpoint','model.json')
    for ($index = 0; $index -lt $modelFiles.Count; $index++) {
        Assert-Budget
        $relative = $modelFiles[$index]
        $source = [IO.Path]::GetFullPath((Join-Path $modelRoot $relative))
        Assert-Inside $source $modelRoot
        Copy-Verified $source (Join-Path $packageRoot $relative) (Get-AssetHash $source $verification)
    }
    for ($index = 0; $index -lt $noticeFiles.Count; $index++) {
        Assert-Budget
        $notice = $noticeFiles[$index]
        $noticeSource = [IO.Path]::GetFullPath((Join-Path $materialsRoot $notice.file))
        Assert-Inside $noticeSource $materialsRoot
        if ((Assert-PlainFile $noticeSource).Length -ne $notice.bytes) { throw 'License notice byte count mismatch.' }
        $relative = if ($notice.file -eq 'ASSET-NOTICES.md') { 'LICENSE-COMPONENTS.md' } else { 'notices/' + $notice.file }
        Copy-Verified $noticeSource (Join-Path $packageRoot $relative) $notice.sha256
    }
    $publicNoticeEntries = [Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $noticeFiles.Count; $index++) {
        Assert-Budget
        $notice = $noticeFiles[$index]
        $relative = if ($notice.file -eq 'ASSET-NOTICES.md') { 'LICENSE-COMPONENTS.md' } else { 'notices/' + $notice.file }
        $publicNoticeEntries.Add([ordered]@{file=$relative;source_file=$notice.file;bytes=$notice.bytes;sha256=$notice.sha256})
    }
    $publicNoticeIndex = [ordered]@{schema_version=$noticeIndex.schema_version;prepared_date=$noticeIndex.prepared_date;license_scope=$noticeIndex.license_scope;notice='LICENSE-COMPONENTS.md';files=$publicNoticeEntries.ToArray();limitations=$noticeIndex.limitations;source_notice_index_sha256=(Get-BoundedHash $noticeIndexPath)}
    Write-PublicText 'license-provenance.json' ($publicNoticeIndex | ConvertTo-Json -Depth 8)
    $metrics = $verification.metrics
    $observations = $metrics.validation_observations
    if (@($observations.groups).Count -gt 8 -or @($metrics.regression.groups).Count -gt 3) { throw 'Experimental summary grouping budget exceeded.' }
    $trainAgreement = 0; $developmentAgreement = 0; $rotatedAgreement = 0
    for ($index = 0; $index -lt @($observations.groups).Count; $index++) {
        Assert-Budget
        $group = $observations.groups[$index]
        if ($group.split -eq 'train' -and $group.variant -eq 'original') { $trainAgreement += $group.teacher_agreement_count }
        elseif ($group.split -eq 'development' -and $group.variant -eq 'original') { $developmentAgreement += $group.teacher_agreement_count }
        elseif ($group.variant -eq 'fixed-left1') { $rotatedAgreement += $group.teacher_agreement_count }
    }
    if ($trainAgreement -ne 118 -or $developmentAgreement -ne 27 -or $rotatedAgreement -ne 7 -or $observations.selected_id_changes -ne 6 -or $observations.rows -ne 231) { throw 'Selected run differs from the model-card observations.' }
    $summary = [ordered]@{
        schema_version=1;status='experimental_not_formal_acceptance';experiment_date='2026-10-09';training_contract=$report.training_contract
        head_loading=[ordered]@{api='Sezika.IndependentMarkerHeadAssetStore.Load';file='expanded-head.asset';identity=[ordered]@{model_id=$report.identity.model_id;encoder_sha256=$report.identity.encoder_sha256;tokenizer_sha256=$report.identity.tokenizer_sha256;data_manifest_sha256=$report.identity.data_manifest_sha256;hidden_size=$report.identity.hidden_size;protocol=$report.identity.protocol;length_policy=$report.identity.length_policy};expected_feature_sha256=$report.feature_sha256;expected_file_sha256=$report.head_sha256;result_property='Head';integration_status='API signature verified; publication example not independently executed';cli_limitation='The current experiment CLI expects the full original training report; this public aggregate is not a substitute.'}
        training=[ordered]@{updates=$report.completed_steps;learning_rate=$report.learning_rate;seed=$report.seed;encoder_frozen=$report.encoder_frozen;initial_parameters=$report.initial_parameter_source;training_backend=$report.training_backend;feature_backend=$report.feature_backend;runtime_max_tokens=$report.max_tokens;train_records=$report.train_count;development_records=$report.development_count;choice_records=140;boolean_records=75;score_supervision_records=0;forwards=$report.encoder_forwards_completed;elapsed_seconds=$metrics.training_elapsed_seconds;complete_cycles=$metrics.visits.complete_cycles;partial_prefix=$metrics.visits.partial_prefix;sample_visits_min=$metrics.visits.minimum;sample_visits_max=$metrics.visits.maximum}
        independently_loaded_validation=[ordered]@{fresh_process=$true;rows=$observations.rows;original_records=215;rotated_train_choice_records=$observations.rotations;train_teacher_agreement=[ordered]@{matched=$trainAgreement;total=162};development_teacher_agreement=[ordered]@{matched=$developmentAgreement;total=53};rotated_train_choice_teacher_agreement=[ordered]@{matched=$rotatedAgreement;total=16};selected_candidate_id_changes=$observations.selected_id_changes;baseline_train_ce=$report.baseline_train.mean_loss;baseline_development_ce=$report.baseline_development.mean_loss;train_ce=$observations.train_ce;development_ce=$observations.development_ce;rotated_train_choice_ce=$observations.rotation_ce;elapsed_seconds=$metrics.validation_elapsed_seconds;groups=$observations.groups}
        exposed_original32_regression=[ordered]@{rows=$metrics.regression.rows;corrections=$metrics.regression.corrections;score_training_supervision=0;groups=$metrics.regression.groups;elapsed_seconds=$metrics.regression.elapsed_seconds;sealed_test=$false}
        engineering_evidence=[ordered]@{independent_checks_passed=10;independent_checks_total=10;gpu_name=$metrics.device.name;train_peak_owned_gpu_bytes=$metrics.training_resources.snapshot_before_context_dispose.peak_owned_bytes;train_peak_working_set_bytes=$metrics.training_resources.process_peak_working_set_bytes;training_report_sha256=$reportHash;validation_report_sha256=$metrics.validation_sha256;original32_report_sha256=$metrics.regression.sha256;local_verification_sha256=(Get-BoundedHash $verificationPath);data_manifest_sha256=$report.manifest_sha256;derivation_ledger_sha256=$report.derivation_ledger_sha256}
        pending=@('formal_human_quality','calibration','sealed_test','new_head_native_aot','performance_acceptance','third_party_teacher_output_rights','complete_tokenizer_notice_provenance')
        limitations=@('Teacher and audit agreement are not formal accuracy.','Candidate ordering changes selected IDs in 6 of 16 rotations.','Score received no new supervision and is not an accepted scoring capability.','Timings are one experimental run, not a portable benchmark.','Full training and validation reports, questions, teacher responses and row-level sequences are omitted from this publication package.')
    }
    Write-PublicText 'experiment-summary.json' ($summary | ConvertTo-Json -Depth 12)
    $templatePath = Join-Path $PSScriptRoot 'model-card-template.md'
    $card = Get-Content -LiteralPath $templatePath -Raw
    $card = $card.Replace('{{REPOSITORY_ID}}',$RepositoryId).Replace('{{SOURCE_REVISION}}',$manifest.source_revision).Replace('{{TRAINING_REPORT_SHA256}}',$reportHash).Replace('{{HEAD_FEATURE_SHA256}}',$report.feature_sha256).Replace('{{HEAD_FILE_SHA256}}',$report.head_sha256)
    Write-PublicText 'README.md' $card
    $publicManifest = [ordered]@{
        schema_version=1;repository_id=$RepositoryId;status='experimental_not_formal_acceptance';source_repository='https://github.com/IoTSharp/Sezika'
        upstream_encoder=[ordered]@{model_id=$manifest.source_model_id;revision=$manifest.source_revision;source_weights_sha256=$manifest.source_weights_sha256;converted_weights_sha256=$manifest.weights_sha256;tensor_format=$manifest.tensor_format;original_encoder_manifest_sha256=(Get-AssetHash ([IO.Path]::GetFullPath((Join-Path $modelRoot 'model.json'))) $verification);license_field_scope='The unchanged encoder manifest records upstream MIT; this does not grant an omnibus license to the package.'}
        source_evidence_sha256=[ordered]@{training_report=$reportHash;independent_validation=$metrics.validation_sha256;regression=$metrics.regression.sha256;local_verification=(Get-BoundedHash $verificationPath);data_manifest=$report.manifest_sha256;derivation_ledger=$report.derivation_ledger_sha256;approved_license_notice_index=(Get-BoundedHash $noticeIndexPath)}
        licensing='component-specific; see LICENSE-COMPONENTS.md and license-provenance.json'
        files=@($publishedFiles.ToArray());integrity=[ordered]@{all_public_files='SHA256SUMS';manifest_self_hash='SHA256SUMS';sha256sums_self_hash='local preparation receipt, avoiding a recursive checksum claim'}
        omitted_original_files=@('expanded-training-report.json','original-files.sha256','training-data','teacher-responses','row-level-validation','connection-and-process-receipts')
    }
    Write-PublicText 'publication-manifest.json' ($publicManifest | ConvertTo-Json -Depth 12)
    $checksumLines = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $publishedFiles.Count; $index++) {
        Assert-Budget
        $file = $publishedFiles[$index]
        if ((Get-BoundedHash (Join-Path $packageRoot $file.path)) -ne $file.sha256) { throw 'Final publication file SHA256 mismatch.' }
        $checksumLines.Add($file.sha256 + '  ' + $file.path)
    }
    Write-PublicText 'SHA256SUMS' (($checksumLines.ToArray() -join "`n") + "`n")
    $receipt = [ordered]@{schema_version=1;status='PASS_local_publication_package_complete_not_uploaded';package_directory=$packageRoot;repository_id=$RepositoryId;files=$publishedFiles.ToArray();file_count=$publishedFiles.Count;total_bytes=$publishedBytes;sha256sums_sha256=($publishedFiles | Where-Object { $_.path -eq 'SHA256SUMS' }).sha256;publication_manifest_sha256=($publishedFiles | Where-Object { $_.path -eq 'publication-manifest.json' }).sha256;elapsed_seconds=$timer.Elapsed.TotalSeconds;maximum_seconds=$TimeoutSeconds;maximum_files=$maximumFiles;maximum_bytes=$maximumBytes;chunk_operations=$chunkOperations;maximum_chunk_operations=$maximumChunkOperations;process=$localProcess;child_process_cleanup='not_applicable_no_children';source_assets_unchanged=$true;formal_acceptance=$false}
    [IO.File]::WriteAllText((Join-Path $outputRoot 'prepare-result.json'),($receipt | ConvertTo-Json -Depth 12),$encoding)
    ([pscustomobject]$receipt) | Select-Object status,file_count,total_bytes,sha256sums_sha256,publication_manifest_sha256,elapsed_seconds | ConvertTo-Json
} catch {
    if ($packageStarted -and -not (Test-Path -LiteralPath (Join-Path $outputRoot 'prepare-result.json'))) {
        $failure = [ordered]@{schema_version=1;status='FAIL_partial_package_preserved_not_uploadable';error=$_.Exception.Message;elapsed_seconds=$timer.Elapsed.TotalSeconds;maximum_seconds=$TimeoutSeconds;files_written=$publishedFiles.ToArray();process=$localProcess;external_processes_started=0}
        [IO.File]::WriteAllText((Join-Path $outputRoot 'prepare-result.json'),($failure | ConvertTo-Json -Depth 10),$encoding)
    }
    throw
} finally {
    Write-Progress -Activity 'Copy verified model publication files' -Completed
    if ($lockStream) {
        $lockStream.Dispose()
        if ([IO.Path]::GetFullPath($lockPath) -eq (Join-Path $outputRoot '.prepare.lock')) { Remove-Item -LiteralPath $lockPath -Force }
    }
    $timer.Stop()
}
