[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TaskDirectory,
    [Parameter(Mandatory)][string]$ResultsDirectory,
    [Parameter(Mandatory)][string]$ModelDirectory,
    [ValidateRange(30,600)][int]$TimeoutSeconds = 180,
    [switch]$ReaderProbeOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 required.' }

# This verifier starts no external process and writes one task verification JSON.
# All input files stay read-only. ReaderProbeOnly checks one historical row's
# arithmetic/shape and explicitly leaves every actual experiment stage NOT_RUN.
$task = [IO.Path]::GetFullPath($TaskDirectory)
$results = [IO.Path]::GetFullPath($ResultsDirectory)
$model = [IO.Path]::GetFullPath($ModelDirectory)
if (-not [IO.Directory]::Exists($task) -or $task -notmatch '[\\/]rental-training-[A-Za-z0-9-]+$') {
    throw 'Output task must be an existing rental-training task directory.'
}
$clock = [Diagnostics.Stopwatch]::StartNew()
$operationCount = 0
$maximumOperations = 100000
$expectedTask = 'sezika-rental-20261009-01a12099'
$expectedRemote = '/root/sezika-experiments/' + $expectedTask
$expected = @{
    manifest = 'e8e74ce0bbf1a72da5fcdf4dcad9d302513dc5dc42cb548bfd21d2569621aeb6'
    ledger = '55f3226946831c4b9cbd7f6ee10d1194764860c8343a9386caa40c678ce24ec0'
    encoder_manifest = 'b8fb9cecc0912f034e0fea184e8aa78871d11533093dccf4f5aad74646007551'
    encoder = '6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528'
    tokenizer = '197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356'
    gpu = 'C582DC421FED07275A1EDC57F5E314A4'
    original_records = 'a015a8fa182ee62223cafc307c618c515a19e90f843290db9dfb53a56b9a041c'
    original_audit = 'b78a4f42338574aa346a2a6cde3041749bb8c142d0d91c5efc8f116f1eccf463'
}
$checks = [Collections.Generic.List[object]]::new()
$inventory = [Collections.Generic.List[object]]::new()
$stageEvidence = @{}
$metrics = @{}
$hashCache = @{}
$summary = [ordered]@{
    contract = 'sezika/rental-downloaded-experiment-independent-verification/v1'
    started_utc = [DateTime]::UtcNow.ToString('o')
    task_directory = $task; results_directory = $results; model_directory = $model
    status = 'NOT_RUN'; checks = $checks; assets = $inventory
    stage_evidence = $stageEvidence; metrics = $metrics
    formal_acceptance = $false; human_quality = 'pending'; calibration = 'pending'
    sealed_test = 'pending'; license_release = 'pending'; native_aot = 'pending'
    performance = 'pending'; publication = 'pending'
    limitation = 'Read-only verification of downloaded evidence; teacher/audit agreement is not formal accuracy or quality acceptance.'
}
function Guard {
    $script:operationCount++
    if ($script:operationCount -gt $maximumOperations -or $clock.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
        throw 'Verifier item/time bound reached.'
    }
    if ([IO.File]::Exists((Join-Path $task 'cancel.request'))) { throw 'Verifier cancelled.' }
}
function Need([bool]$Condition, [string]$Message) { Guard; if (-not $Condition) { throw $Message } }
function SameHash($Left, $Right) {
    return ($Left -is [string] -and $Right -is [string] -and $Left -cmatch '^[A-Fa-f0-9]{64}$' -and $Right -cmatch '^[A-Fa-f0-9]{64}$' -and $Left.Equals($Right,[StringComparison]::OrdinalIgnoreCase))
}
function SameArray($Left, $Right) {
    if ($null -eq $Left -or $null -eq $Right -or $Left.Count -ne $Right.Count -or $Left.Count -gt 8192) { return $false }
    return (($Left | ConvertTo-Json -Compress -Depth 5) -ceq ($Right | ConvertTo-Json -Compress -Depth 5))
}
function Finite($Value) { return ($null -ne $Value -and [double]::IsFinite([double]$Value)) }
function Close($Left, $Right, [double]$Tolerance = 1e-12) { return ((Finite $Left) -and (Finite $Right) -and [Math]::Abs([double]$Left-[double]$Right) -le $Tolerance) }
function Check([string]$Name, [scriptblock]$Action, [string[]]$RequiredPaths = @()) {
    $missing = @($RequiredPaths | Where-Object { -not [IO.File]::Exists($_) })
    if ($missing.Count -gt 0) {
        $checks.Add([ordered]@{name=$Name;status='NOT_RUN';detail='Required evidence is absent.';missing=$missing}); return
    }
    try {
        Guard; & $Action
        $checks.Add([ordered]@{name=$Name;status='PASS';detail='Verified from pinned downloaded inputs.'})
    } catch {
        $checks.Add([ordered]@{name=$Name;status='FAIL';detail=$_.Exception.Message})
    }
}
function ReadText([string]$Path, [long]$MaximumBytes = 4MB) {
    Guard
    $item = Get-Item -LiteralPath $Path
    Need ($item -is [IO.FileInfo] -and $item.Length -le $MaximumBytes -and -not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) ('Input size/type/reparse point differs: '+$Path)
    return [IO.File]::ReadAllText($item.FullName)
}
function ReadJson([string]$Path) {
    # PS may deserialize ISO times to DateTime; UtcInstant preserves that type.
    return (ReadText $Path | ConvertFrom-Json -AsHashtable -Depth 80)
}
function UtcInstant($Value) {
    if ($Value -is [DateTimeOffset]) { return $Value.ToUniversalTime() }
    if ($Value -is [DateTime]) { return [DateTimeOffset]::new($Value.ToUniversalTime()) }
    Need ($Value -is [string]) 'Timestamp must be DateTime/DateTimeOffset or an explicit offset string.'
    Need ($Value -match '(Z|[+-]\d{2}:\d{2})$') 'Timestamp is missing UTC/offset identity.'
    return [DateTimeOffset]::Parse($Value,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
}
function HashFile([string]$Path, [long]$MaximumBytes = 2GB) {
    Guard; $absolute = [IO.Path]::GetFullPath($Path)
    if ($hashCache.ContainsKey($absolute)) { return $hashCache[$absolute] }
    $item = Get-Item -LiteralPath $absolute
    Need ($item -is [IO.FileInfo] -and $item.Length -le $MaximumBytes -and -not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) ('Hash input size/type differs: '+$absolute)
    $stream = [IO.File]::Open($absolute,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    $buffer = [byte[]]::new(1MB); $finished = $false
    try {
        # 2049 iterations allow the final EOF read of a <=2GiB file.
        for ($chunk=0; $chunk -lt 2049; $chunk++) {
            Guard; $count=$stream.Read($buffer,0,$buffer.Length)
            if ($count -eq 0) { $finished=$true; break }
            $hash.AppendData($buffer,0,$count)
            if ($chunk -gt 0 -and $chunk % 256 -eq 0) { Write-Progress -Activity 'Verify downloaded assets' -Status $item.Name -PercentComplete ([Math]::Min(99,100*$stream.Position/[Math]::Max(1,$item.Length))) }
        }
        Need $finished 'Hash chunk bound exceeded.'
        $value=[Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
        $hashCache[$absolute]=$value; $inventory.Add([ordered]@{path=$absolute;bytes=$item.Length;sha256=$value})
        return $value
    } finally { $hash.Dispose(); $stream.Dispose(); [Array]::Clear($buffer); Write-Progress -Activity 'Verify downloaded assets' -Completed }
}
function IntHash($Values) {
    Need ($Values.Count -ge 1 -and $Values.Count -le 8192) 'Integer vector count differs.'
    [int[]]$ints=$Values; $bytes=[byte[]]::new($ints.Length*4)
    [Buffer]::BlockCopy($ints,0,$bytes,0,$bytes.Length)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}
function ReadTsv([string]$Path) {
    $lines=(ReadText $Path 2MB) -split '\r?\n'; Need ($lines.Count -le 1000) 'TSV row bound differs.'
    $value=@{}
    for ($index=0; $index -lt $lines.Count -and $index -lt 1000; $index++) {
        Guard; if ($lines[$index].Length -eq 0) { continue }; $parts=$lines[$index] -split "`t",2
        Need ($parts.Count -eq 2 -and -not $value.ContainsKey($parts[0])) 'TSV duplicate/invalid field.'
        $value[$parts[0]]=$parts[1]
    }
    return $value
}
function CheckAcceptance($Value) {
    $names=@('formal_training_admission','human_quality','license_release','calibration','sealed_test','native_aot','performance','publication')
    Need ($Value.Count -eq $names.Count) 'Formal acceptance shape differs.'
    foreach ($name in $names) { Guard; Need ($Value.ContainsKey($name) -and $Value[$name] -is [bool] -and $Value[$name] -eq $false) ('Formal acceptance is not explicit false: '+$name) }
}
function CheckResources($Resources, [string]$Name) {
    $snapshot=$Resources.snapshot_before_context_dispose
    Need ($Resources.device_dispose_returned -eq $true -and $Resources.model_dispose_returned -eq $true) ($Name+' disposal did not return.')
    Need ($snapshot.owned_bytes -eq 0 -and $snapshot.owned_allocation_count -eq 0 -and $snapshot.loaded_module_count -eq 0 -and $snapshot.release_failure_count -eq 0) ($Name+' task-owned resource closure differs.')
    Need ($snapshot.peak_owned_bytes -le 3GB -and $Resources.process_peak_working_set_bytes -le 4GB -and $snapshot.free_bytes -ge 5120MB) ($Name+' memory limit/free reserve differs.')
    $metrics[$Name+'_resources']=$Resources
}
function CheckLoss($Loss, [int]$Count, $Parameters) {
    Need ($Loss.backend -ceq 'cuda-frozen-marker-head-cross-entropy-fp32-v1' -and $Loss.example_count -eq $Count -and $Loss.completed_examples -eq $Count -and (Finite $Loss.mean_loss) -and $Loss.mean_loss -ge 0) 'Full GPU CE denominator differs.'
    Need ((SameHash $Loss.parameter_sha256 $Loss.device_parameter_sha256) -and (SameHash $Loss.feature_sha256 $Loss.feature_sha256)) 'CE device/feature hash differs.'
    if ($null -ne $Parameters) { Need (SameHash $Loss.parameter_sha256 $Parameters) 'CE parameter hash differs.' }
}
function BinaryText([IO.BinaryReader]$Reader) {
    Guard; $length=$Reader.ReadInt32(); Need ($length -ge 0 -and $length -le 256) 'Head/checkpoint text bound differs.'
    $bytes=$Reader.ReadBytes($length); Need ($bytes.Length -eq $length) 'Head/checkpoint text truncated.'
    return [Text.UTF8Encoding]::new($false,$true).GetString($bytes)
}
function CheckBinaryAsset([string]$Path, [string]$Magic, $Report) {
    Need ((Get-Item -LiteralPath $Path).Length -le 128KB) 'Binary head/checkpoint size bound differs.'
    $bytes=[IO.File]::ReadAllBytes($Path); Need ($bytes.Length -ge 64) 'Binary asset is truncated.'
    $payload=[byte[]]$bytes[0..($bytes.Length-33)]; $tail=[byte[]]$bytes[($bytes.Length-32)..($bytes.Length-1)]
    Need ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($payload)) -ceq [Convert]::ToHexString($tail)) 'Binary embedded payload checksum differs.'
    $stream=[IO.MemoryStream]::new($payload,$false); $reader=[IO.BinaryReader]::new($stream,[Text.Encoding]::UTF8)
    try {
        Need ([Text.Encoding]::ASCII.GetString($reader.ReadBytes(5)) -ceq $Magic -and $reader.ReadInt32() -eq 1) 'Binary asset magic/version differs.'
        $id=@{model_id=(BinaryText $reader);encoder_sha256=(BinaryText $reader);tokenizer_sha256=(BinaryText $reader);data_manifest_sha256=(BinaryText $reader);hidden_size=$reader.ReadInt32();protocol=(BinaryText $reader);length_policy=(BinaryText $reader)}
        Need ($id.model_id -ceq $Report.identity.model_id -and $id.hidden_size -eq 768 -and $id.protocol -ceq 'decision-v1' -and $id.length_policy -ceq 'strict') 'Binary model/protocol identity differs.'
        foreach ($field in @('encoder_sha256','tokenizer_sha256','data_manifest_sha256')) { Guard; Need (SameHash $id[$field] $Report.identity[$field]) 'Binary identity SHA differs.' }
        Need (SameHash (BinaryText $reader) $Report.feature_sha256) 'Binary feature SHA differs.'
        $seed=$reader.ReadInt32();$rate=$reader.ReadSingle();$steps=$reader.ReadInt32();$loss=$reader.ReadDouble();$count=$reader.ReadInt32()
        Need ($seed -eq 17 -and $steps -eq 24576 -and (Close $rate 0.001 1e-8) -and (Close $loss $Report.last_step_pre_update_selected_example_loss) -and $count -eq 2304 -and $stream.Length-$stream.Position -eq 9216) 'Binary optimizer/shape identity differs.'
        $weights=$reader.ReadBytes(9216); Need ($weights.Length -eq 9216 -and $stream.Position -eq $stream.Length) 'Binary tensor/trailing bytes differ.'
        Need (SameHash ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($weights))) $Report.parameter_sha256) 'Binary float32 parameter SHA differs.'
        $nonzero=0
        for ($index=0; $index -lt 2304; $index++) { Guard; $value=[BitConverter]::ToSingle($weights,$index*4); Need ([float]::IsFinite($value)) 'Binary weight is nonfinite.'; if ($value -ne 0) {$nonzero++}; if ($index -ge 768 -and $index -lt 1536) {Need ($value -eq 0) 'Score row changed despite zero new Score supervision.'} }
        Need ($nonzero -gt 0) 'Saved head parameters did not change from zero.'
    } finally { $reader.Dispose();$stream.Dispose();[Array]::Clear($bytes);[Array]::Clear($payload) }
}
function SeededOrder([int]$Count, [int]$Seed) {
    Need ($Count -ge 2 -and $Count -le 162) 'Seed replay item bound differs.'
    $order=[int[]](0..($Count-1)); [uint64]$state=([uint64]$Seed -bxor [uint64]2654435769)
    if ($state -eq 0) {$state=[uint64]2738958700}
    for ($index=$Count-1; $index -gt 0 -and $index -lt 162; $index--) { Guard; $state=($state -bxor (($state -shl 13) -band [uint64]4294967295)) -band [uint64]4294967295; $state=($state -bxor ($state -shr 17)) -band [uint64]4294967295; $state=($state -bxor (($state -shl 5) -band [uint64]4294967295)) -band [uint64]4294967295; $other=[int]($state % [uint64]($index+1)); $saved=$order[$index];$order[$index]=$order[$other];$order[$other]=$saved }
    return ,$order
}
function CheckSourceRows($Report, [int]$MaximumRows = 215) {
    Need ($MaximumRows -in @(1,215)) 'Source reader accepts one-row probe or full215 only.'
    $source=Join-Path $task 'prepared/data/experiment-records.jsonl'
    Need (SameHash (HashFile $source 4MB) $Report.records_sha256) 'Uploaded actual record SHA differs.'
    Need (SameHash (HashFile (Join-Path $task 'prepared/data/input-provenance.json') 4MB) $Report.input_provenance_sha256) 'Uploaded actual provenance SHA differs.'
    $lines=@((ReadText $source 4MB) -split '\r?\n'|Where-Object {$_.Length -gt 0});Need ($lines.Count -eq 215) 'Pinned source has other than 215 actual records.'
    $original=@($Report.loaded_observations.rows|Where-Object {$_.variant -ceq 'original'});Need ($original.Count -eq 215) 'Report original source binding denominator differs.'
    # Source file order interleaves families/splits; observations group train
    # before development. Bind every unique ID without imposing a new order.
    # SampleVisits separately retains the exact frozen training file order.
    $byId=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    for ($index=0; $index -lt 215; $index++) {Guard; $row=$original[$index];Need ($row.record_id -is [string] -and -not $byId.ContainsKey($row.record_id)) 'Duplicate original report record ID.';$byId.Add($row.record_id,$row)}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($index=0; $index -lt $MaximumRows -and $index -lt 215; $index++) {
        Guard; $sourceRow=$lines[$index]|ConvertFrom-Json -AsHashtable -Depth 40
        Need ($sourceRow.record_id -is [string] -and $seen.Add($sourceRow.record_id) -and $byId.ContainsKey($sourceRow.record_id)) 'Source record ID is duplicate or absent from original observations.'
        $row=$byId[$sourceRow.record_id]
        Need ($sourceRow.record_id -ceq $row.record_id -and $sourceRow.language -ceq $row.language -and $sourceRow.split -ceq $row.split -and $sourceRow.target_index -eq $row.teacher_target_index -and $sourceRow.request.questions.Count -eq 1) 'Original row differs from actual source identity/split/target.'
        $question=$sourceRow.request.questions[0];Need ($question.type -ceq $row.type) 'Source question type differs.'
        if ($question.type -ceq 'choice') {$ids=@($question.candidates|ForEach-Object {$_.id});$texts=@($question.candidates|ForEach-Object {$_.text})}
        else {$ids=@('false','true');$texts=@($question.when_false,$question.when_true)}
        Need (SameArray $ids $row.candidate_ids) 'Actual source candidate IDs differ.'
        for ($candidate=0; $candidate -lt $texts.Count -and $candidate -lt 8; $candidate++) {Guard; $sha=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($texts[$candidate])));Need (SameHash $sha $row.candidate_text_sha256[$candidate]) 'Actual source candidate text SHA differs.'}
    }
    Need ($seen.Count -eq $MaximumRows -and ($MaximumRows -eq 1 -or $seen.Count -eq $byId.Count)) 'Source/original row bijection differs.'
}
function CheckPrediction($Row, [switch]$Regression) {
    $logits=if ($Regression) { $Row.gpu_logits } else { $Row.logits }
    $probabilities=if ($Regression) { $Row.stable_softmax_probabilities } else { $Row.probabilities }
    $target=if ($Regression) { [int]$Row.corrected_audit_target_index } else { [int]$Row.teacher_target_index }
    $nll=if ($Regression) { $Row.corrected_audit_nll } else { $Row.stable_softmax_teacher_nll }
    $count=$Row.candidate_ids.Count
    Need ($count -ge 2 -and $count -le 8 -and $logits.Count -eq $count -and $probabilities.Count -eq $count -and $target -ge 0 -and $target -lt $count) 'Prediction vector/target shape differs.'
    $maximum=[double]::NegativeInfinity; $prediction=-1
    for ($candidate=0; $candidate -lt $count -and $candidate -lt 8; $candidate++) {
        Guard; Need (Finite $logits[$candidate]) 'Nonfinite logit.'
        if ([double]$logits[$candidate] -gt $maximum) { $maximum=[double]$logits[$candidate]; $prediction=$candidate }
    }
    $sum=0.0; $probabilitySum=0.0; $rank=1
    for ($candidate=0; $candidate -lt $count -and $candidate -lt 8; $candidate++) {
        Guard; $sum+=[Math]::Exp([double]$logits[$candidate]-$maximum)
        Need ((Finite $probabilities[$candidate]) -and $probabilities[$candidate] -ge 0 -and $probabilities[$candidate] -le 1) 'Invalid probability.'
        $probabilitySum+=[double]$probabilities[$candidate]
        if ([double]$logits[$candidate] -gt [double]$logits[$target]) { $rank++ }
    }
    for ($candidate=0; $candidate -lt $count -and $candidate -lt 8; $candidate++) { Guard; Need (Close $probabilities[$candidate] ([Math]::Exp([double]$logits[$candidate]-$maximum)/$sum)) 'Stable softmax differs.' }
    Need ((Close $probabilitySum 1.0) -and (Close $nll ($maximum+[Math]::Log($sum)-[double]$logits[$target])) -and $Row.predicted_index -eq $prediction -and $Row.selected_candidate_id -ceq $Row.candidate_ids[$prediction]) 'Stable NLL/first argmax/selected ID differs.'
    if ($Regression) { Need ($Row.corrected_audit_target_id -ceq $Row.candidate_ids[$target] -and $Row.agrees_with_corrected_audit -eq ($prediction -eq $target)) 'Regression agreement/target ID differs.' }
    else { Need ($Row.teacher_target_id -ceq $Row.candidate_ids[$target] -and $Row.agrees_with_teacher -eq ($prediction -eq $target) -and $Row.teacher_target_rank -eq $rank -and (Close $Row.gpu_teacher_ce $nll 2e-5)) 'Teacher agreement/rank/GPU CE differs.' }
}
function CheckObservations($Observations, $Parameters, [string]$Name) {
    $rows=$Observations.rows; Need ($rows.Count -eq 231 -and $Observations.groups.Count -le 18 -and $Observations.position_comparisons.Count -eq 16) ($Name+' observation denominator differs.')
    $indexByKey=@{}; $grouped=@{}; $originalChoice=0; $originalBoolean=0
    $train=[Collections.Generic.List[object]]::new(); $dev=[Collections.Generic.List[object]]::new(); $rotated=[Collections.Generic.List[object]]::new()
    for ($index=0; $index -lt 231; $index++) {
        Guard; $row=$rows[$index]; $key=$row.record_id+':'+$row.variant
        Need (-not $indexByKey.ContainsKey($key) -and $row.language -cin @('zh','en') -and $row.type -cin @('choice','boolean') -and $row.split -cin @('train','development') -and $row.variant -cin @('original','fixed-left1')) 'Duplicate/unsupported observation identity.'
        $indexByKey[$key]=$row
        CheckPrediction $row
        $count=$row.candidate_ids.Count
        Need ($row.original_candidate_ids.Count -eq $count -and $row.candidate_text_sha256.Count -eq $count -and $row.marker_positions.Count -eq $count -and $row.token_ids.Count -ge 2 -and $row.token_ids.Count -le 8192) 'Token/candidate/marker shape differs.'
        Need ($row.original_teacher_target_index -ge 0 -and $row.original_teacher_target_index -lt $count -and $row.teacher_target_id -ceq $row.original_candidate_ids[$row.original_teacher_target_index]) 'Original target identity differs.'
        Need ((SameHash $row.token_sha256 (IntHash $row.token_ids)) -and (SameHash $row.marker_sha256 (IntHash $row.marker_positions))) 'Token/marker int32 little-endian SHA differs.'
        foreach ($hash in @($row.feature_sha256,$row.original_token_sha256,$row.original_marker_sha256,$row.original_feature_sha256)) { Guard; Need (SameHash $hash $hash) 'Feature/original identity SHA syntax differs.' }
        for ($candidate=0; $candidate -lt $count -and $candidate -lt 8; $candidate++) {
            Guard; $expectedIndex=if ($row.variant -ceq 'original') {$candidate} else {($candidate+1)%$count}
            Need ($row.candidate_ids[$candidate] -ceq $row.original_candidate_ids[$expectedIndex] -and (SameHash $row.candidate_text_sha256[$candidate] $row.candidate_text_sha256[$candidate]) -and $row.marker_positions[$candidate] -ge 0 -and $row.marker_positions[$candidate] -lt $row.token_ids.Count -and ($candidate -eq 0 -or $row.marker_positions[$candidate] -gt $row.marker_positions[$candidate-1])) 'Candidate rotation/marker order differs.'
        }
        if ($row.variant -ceq 'fixed-left1') { Need ($row.split -ceq 'train' -and $row.type -ceq 'choice') 'Rotation scope differs.'; $rotated.Add($row) }
        else {
            if ($row.type -ceq 'choice') { $originalChoice++ } else { $originalBoolean++; Need ($count -eq 2) 'Boolean candidate count differs.' }
            if ($row.split -ceq 'train') {$train.Add($row)} else {$dev.Add($row)}
            Need ((SameHash $row.token_sha256 $row.original_token_sha256) -and (SameHash $row.marker_sha256 $row.original_marker_sha256) -and (SameHash $row.feature_sha256 $row.original_feature_sha256)) 'Original row self-binding differs.'
        }
        $groupKey=$row.split+'|'+$row.variant+'|'+$row.language+'|'+$row.type
        if (-not $grouped.ContainsKey($groupKey)) {$grouped[$groupKey]=[Collections.Generic.List[object]]::new()}; $grouped[$groupKey].Add($row)
        if ($index % 32 -eq 0) { Write-Progress -Activity ('Recompute '+$Name) -Status ($index.ToString()+'/231') -PercentComplete (100*$index/231) }
    }
    Write-Progress -Activity ('Recompute '+$Name) -Completed
    Need ($train.Count -eq 162 -and $dev.Count -eq 53 -and $rotated.Count -eq 16 -and $originalChoice -eq 140 -and $originalBoolean -eq 75) '215/162/53/16/Choice140/Boolean75 denominator differs.'
    $lossSets=@(@($Observations.train,$train,162),@($Observations.development,$dev,53),@($Observations.rotated_training_choice,$rotated,16))
    foreach ($set in $lossSets) { Guard; CheckLoss $set[0] $set[2] $Parameters; $mean=($set[1]|Measure-Object -Property gpu_teacher_ce -Average).Average; Need (Close $set[0].mean_loss $mean) 'Complete per-row GPU CE mean differs.' }
    Need ($Observations.groups.Count -eq $grouped.Count) 'Group identity count differs.'
    $seenGroups=@{}
    foreach ($group in $Observations.groups) {
        Guard; $key=$group.split+'|'+$group.variant+'|'+$group.language+'|'+$group.type
        Need ($grouped.ContainsKey($key) -and -not $seenGroups.ContainsKey($key)) 'Duplicate/extra group.'; $seenGroups[$key]=$true
        $items=$grouped[$key]; $agreements=@($items|Where-Object agrees_with_teacher).Count
        Need ($group.total -eq $items.Count -and $group.completed -eq $items.Count -and $group.teacher_agreement_count -eq $agreements -and (Close $group.teacher_agreement_rate ($agreements/[double]$items.Count)) -and (Close $group.mean_gpu_teacher_ce (($items|Measure-Object -Property gpu_teacher_ce -Average).Average)) -and (Close $group.mean_teacher_target_rank (($items|Measure-Object -Property teacher_target_rank -Average).Average))) 'Recomputed group CE/agreement/rank differs.'
    }
    $comparisonById=@{}; $changed=0
    foreach ($comparison in $Observations.position_comparisons) { Guard; Need (-not $comparisonById.ContainsKey($comparison.record_id)) 'Duplicate position comparison.'; $comparisonById[$comparison.record_id]=$comparison }
    foreach ($row in $rotated) {
        Guard; $old=$indexByKey[$row.record_id+':original']; $comparison=$comparisonById[$row.record_id]
        Need ($null -ne $old -and $null -ne $comparison -and (SameArray $row.original_candidate_ids $old.candidate_ids) -and $row.original_teacher_target_index -eq $old.teacher_target_index -and (SameHash $row.original_token_sha256 $old.token_sha256) -and (SameHash $row.original_marker_sha256 $old.marker_sha256) -and (SameHash $row.original_feature_sha256 $old.feature_sha256) -and -not (SameHash $row.token_sha256 $old.token_sha256)) 'Rotated row original token/feature binding differs.'
        for ($candidate=0; $candidate -lt $row.candidate_ids.Count -and $candidate -lt 8; $candidate++) { Guard; Need (SameHash $row.candidate_text_sha256[$candidate] $old.candidate_text_sha256[($candidate+1)%$row.candidate_ids.Count]) 'Rotated candidate text SHA differs.' }
        $isChanged=$old.selected_candidate_id -cne $row.selected_candidate_id
        Need ($comparison.teacher_target_id -ceq $row.teacher_target_id -and $comparison.original_selected_candidate_id -ceq $old.selected_candidate_id -and $comparison.rotated_selected_candidate_id -ceq $row.selected_candidate_id -and $comparison.selected_id_changed -eq $isChanged -and $comparison.original_target_rank -eq $old.teacher_target_rank -and $comparison.rotated_target_rank -eq $row.teacher_target_rank -and $comparison.original_agrees_with_teacher -eq $old.agrees_with_teacher -and $comparison.rotated_agrees_with_teacher -eq $row.agrees_with_teacher) 'Recomputed selected-ID position comparison differs.'
        if ($isChanged) {$changed++}
    }
    $metrics[$Name]=[ordered]@{rows=231;train=162;development=53;rotations=16;selected_id_changes=$changed;train_ce=$Observations.train.mean_loss;development_ce=$Observations.development.mean_loss;rotation_ce=$Observations.rotated_training_choice.mean_loss;groups=$Observations.groups}
}
function CheckRunner([string]$Phase, [string]$Command, [int]$MaximumMilliseconds) {
    $directory=Join-Path $results ('.artifacts/stage-'+$Phase)
    $receipt=ReadTsv (Join-Path $directory 'result.tsv')
    foreach ($pair in @(@('status','completed'),@('payload_exit','0'),@('final_check_complete','1'),@('cleanup_incomplete','0'),@('verified_live_count','0'),@('unknown_count','0'),@('unchecked_count','0'),@('cleanup_budget_exhausted','0'))) { Guard; Need ($receipt[$pair[0]] -ceq $pair[1]) ($Phase+' runner '+$pair[0]+' differs.') }
    Need ([long]$receipt.elapsed_ms -le $MaximumMilliseconds -and $receipt.fixture -ceq 'none') ($Phase+' runner deadline/fixture differs.')
    $identity=ReadText (Join-Path $directory 'identity.tsv') 2MB
    $records=@($identity|ConvertFrom-Csv -Delimiter "`t"); Need ($records.Count -ge 1 -and $records.Count -le 128) 'Runner identity row bound differs.'
    $payload=@($records|Where-Object { $_.argv_shell_escaped.StartsWith($expectedRemote+'/') -and $_.argv_shell_escaped.Contains(' '+$Command+' ') })
    Need ($payload.Count -ge 1 -and $payload.Count -le 16) ($Phase+' actual payload process identity is absent.')
    $identities=[Collections.Generic.List[string]]::new()
    foreach ($record in $payload) { Guard; Need ($record.pid -match '^\d+$' -and $record.start_tick -match '^\d+$' -and $record.parent_chain.Length -gt 0 -and $record.ppid -match '^\d+$') 'Payload PID/start/parent identity differs.'; $identities.Add($record.pid+':'+$record.start_tick) }
    $before=ReadTsv (Join-Path $directory 'resources-before.tsv'); $after=ReadTsv (Join-Path $directory 'resources-after.tsv')
    foreach ($resource in @($before,$after)) { Guard; Need (($resource.gpu_uuid -replace '^GPU-|-' -ceq $expected.gpu.ToLowerInvariant()) -and [long]$resource.free_mib -ge 5120 -and [long]$resource.host_available_kib -ge 4194304 -and [long]$resource.owned_gpu_limit_bytes -eq 3GB -and [long]$resource.host_rss_limit_bytes -eq 4GB) ($Phase+' actual GPU/free/host resource admission differs.') }
    $stageEvidence[$Phase]=[ordered]@{receipt=$receipt;payload_identities=@($identities|Select-Object -Unique);resources_before=$before;resources_after=$after;identity_sha256=(HashFile (Join-Path $directory 'identity.tsv') 2MB)}
}

$reportPath=Join-Path $results 'output/run-24576/expanded-training-report.json'
$validationPath=Join-Path $results 'output/expanded-validation.json'
$regressionPath=Join-Path $results 'output/original32-regression.json'
$training=$null; $validation=$null; $regression=$null; $reportSha=$null
try {
    if ($ReaderProbeOnly) {
        Check 'historical_reader_one_row_shape_and_arithmetic_only' {
            $probe=ReadJson $reportPath
            Need ($probe.training_contract -ceq 'sezika/teacher-labelled-gpu-expanded-experiment-training/v4' -and $probe.loaded_observations.rows.Count -eq 231) 'Historical v4 reader shape differs.'
            CheckPrediction $probe.loaded_observations.rows[0]
            Need (SameHash $probe.loaded_observations.rows[0].token_sha256 (IntHash $probe.loaded_observations.rows[0].token_ids)) 'Historical token int32 SHA reader differs.'
            # First exercise the generated seed loop on two items, then replay
            # the pinned order metadata; neither path executes model inference.
            $tiny=SeededOrder 2 17; Need ($tiny.Count -eq 2 -and $tiny[0] -ne $tiny[1]) 'Tiny bounded order replay differs.'
            $order=SeededOrder 162 17; Need (SameHash (IntHash $order) $probe.sample_visits.order_index_sha256) 'Historical seed/order reader differs.'
            CheckBinaryAsset (Join-Path $ModelDirectory 'expanded-head.asset') 'SZMH1' $probe
            CheckBinaryAsset (Join-Path $ModelDirectory 'expanded-training.checkpoint') 'SZMK1' $probe
            $summary.probe=[ordered]@{source=$reportPath;rows_checked=1;training_executed=$false;gpu_opened=$false;actual_experiment_verified=$false}
        } @($reportPath)
        foreach ($name in @('training','independent_validation','original32_regression','download_deadline')) { $checks.Add([ordered]@{name=$name;status='NOT_RUN';detail='Reader probe is not experiment verification.'}) }
    } else {
        Check 'task_and_package_input_bindings' {
            Need ((ReadText (Join-Path $results 'task-identity') 512).Trim() -ceq $expectedTask) 'Remote experiment identity differs.'
            Need (((ReadText (Join-Path $results 'gpu-uuid') 512).Trim() -replace '^GPU-|-') -ieq $expected.gpu) 'Remote GPU identity differs.'
            $bindings=(ReadText (Join-Path $results 'results-input-bindings.sha256') 8KB) -split '\r?\n'
            $seen=@{}; $allowed=@('task-identity','archive-sha256','gpu-uuid','package-identity.json','files.sha256')
            foreach ($line in $bindings) { Guard; if ($line.Length -eq 0) {continue}; Need ($line -cmatch '^([a-f0-9]{64})  ([A-Za-z0-9_.-]+)$') 'Input SHA list syntax differs.'; $sha=$Matches[1];$name=$Matches[2]; Need ($name -cin $allowed -and -not $seen.ContainsKey($name)) 'Input SHA list identity differs.'; Need (SameHash (HashFile (Join-Path $results $name) 1MB) $sha) 'Input binding SHA differs.'; $seen[$name]=$true }
            Need ($seen.Count -eq 5) 'Five remote input bindings are required.'
            $package=ReadJson (Join-Path $results 'package-identity.json')
            Need ($package.task -ceq $expectedTask -and $package.experiment.total -eq 215 -and $package.experiment.updates -eq 24576 -and $package.native_aot -eq $false) 'Package task/experiment identity differs.'
            Need (SameHash (HashFile (Join-Path $results 'files.sha256') 1MB) $package.files_sha256) 'Package SHA list differs.'
            $prepared=Join-Path $task 'prepared'
            foreach ($pair in @(@('data/experiment-manifest.json',$expected.manifest),@('data/derivation-ledger.json',$expected.ledger))) { Guard; Need (SameHash (HashFile (Join-Path $prepared $pair[0]) 4MB) $pair[1]) 'Local uploaded data/ledger SHA differs.' }
        } @((Join-Path $results 'task-identity'),(Join-Path $results 'results-input-bindings.sha256'),(Join-Path $results 'package-identity.json'))
        Check 'training_v4_fixed_contract_and_asset_binding' {
            $script:training=ReadJson $reportPath; $script:reportSha=HashFile $reportPath 4MB
            $r=$training
            Need ($r.training_contract -ceq 'sezika/teacher-labelled-gpu-expanded-experiment-training/v4' -and $r.status -ceq 'completed_experiment_not_formal_acceptance' -and $r.completed_steps -eq 24576 -and $r.seed -eq 17 -and (Close $r.learning_rate 0.001 1e-8) -and $r.max_tokens -eq 8192 -and $r.encoder_frozen -eq $true -and $r.initial_parameter_source -ceq 'all_zero_three_linear_rows') 'Fixed new training contract differs.'
            Need ($r.step_semantics -ceq '24576_total_committed_updates_seeded_fixed_order_cycles_actual_train_count_no_resume' -and $r.train_count -eq 162 -and $r.development_count -eq 53 -and $r.encoder_forward_limit -eq 384 -and $r.encoder_forwards_started -eq 231 -and $r.encoder_forwards_completed -eq 231 -and $r.training_timeout_seconds -le 300 -and $r.overall_timeout_seconds -le 1500 -and $r.elapsed_seconds -le $r.overall_timeout_seconds -and $r.checkpoint_and_head_roundtrip_matched -eq $true) 'Fixed updates/actual forwards/deadline differs.'
            Need ($r.training_backend -ceq 'cuda-independent-linear-marker-head-sgd-fp32' -and $r.feature_backend -ceq 'cuda-modernbert-fp32-v1' -and $r.actual_data_root -ceq ($expectedRemote+'/data')) 'New remote root/C# GPU backend identity differs.'
            Need ($r.identity.model_id -ceq 'sezika/mmbert-base-independent-s4' -and $r.identity.hidden_size -eq 768 -and $r.identity.protocol -ceq 'decision-v1' -and $r.identity.length_policy -ceq 'strict') 'Encoder feature identity differs.'
            foreach ($pair in @(@($r.manifest_sha256,$expected.manifest),@($r.derivation_ledger_sha256,$expected.ledger),@($r.encoder_manifest_sha256,$expected.encoder_manifest),@($r.identity.encoder_sha256,$expected.encoder),@($r.identity.tokenizer_sha256,$expected.tokenizer),@($r.identity.data_manifest_sha256,$expected.manifest))) { Guard; Need (SameHash $pair[0] $pair[1]) 'Pinned training/model/data SHA differs.' }
            Need ($r.device.uuid -ceq $expected.gpu -and $r.device.name -match 'RTX 5090' -and $r.device.driver_version -gt 0 -and $r.device.device_index -eq 0) 'New rental GPU/device identity differs.'
            Need ($reportSha -cne 'c6f3ae942fb90ca842fd49aff7ae6505eb02a71128264edc2ef08bc323a48905') 'Historical report cannot prove a new training run.'
            Need ($r.data_declaration.total -eq 215 -and $r.data_declaration.train -eq 162 -and $r.data_declaration.development -eq 53 -and $r.data_declaration.schema_version -eq 3 -and $r.data_declaration.formal -eq $false -and $r.data_declaration.human_review -ceq 'pending' -and $r.data_declaration.eligible_as_sealed_test -eq $false) 'Data declaration/formal state differs.'
            foreach ($pair in @(@('expanded-training-report.json',$reportSha),@('expanded-head.asset',$r.head_sha256),@('expanded-training.checkpoint',$r.checkpoint_sha256),@('model.json',$expected.encoder_manifest),@('encoder.safetensors',$expected.encoder),@('tokenizer/tokenizer.json',$expected.tokenizer))) { Guard; Need (SameHash (HashFile (Join-Path $model $pair[0])) $pair[1]) 'Complete downloaded model SHA differs.' }
            foreach ($pair in @(@('expanded-head.asset',$r.head_sha256),@('expanded-training.checkpoint',$r.checkpoint_sha256))) { Guard; Need (SameHash (HashFile (Join-Path $results ('output/run-24576/'+$pair[0])) 8MB) $pair[1]) 'Results vs trained model artifact SHA differs.' }
            CheckAcceptance $r.acceptance; CheckResources $r.resources 'training'
            CheckBinaryAsset (Join-Path $model 'expanded-head.asset') 'SZMH1' $r
            CheckBinaryAsset (Join-Path $model 'expanded-training.checkpoint') 'SZMK1' $r
            CheckLoss $r.baseline_train 162 $null; CheckLoss $r.baseline_development 53 $null
            Need (SameHash $r.baseline_train.parameter_sha256 ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]::new(9216))))) 'Baseline parameter bytes were not all zero.'
            CheckLoss $r.trained_train 162 $r.parameter_sha256; CheckLoss $r.trained_development 53 $r.parameter_sha256
            Need ((SameHash $r.baseline_train.parameter_sha256 $r.baseline_development.parameter_sha256) -and (SameHash $r.feature_sha256 $r.baseline_train.feature_sha256) -and (SameHash $r.feature_sha256 $r.trained_train.feature_sha256)) 'Feature/baseline parameter bindings differ.'
            $metrics.training_report_sha256=$reportSha; $metrics.head_sha256=$r.head_sha256; $metrics.checkpoint_sha256=$r.checkpoint_sha256; $metrics.device=$r.device; $metrics.training_elapsed_seconds=$r.elapsed_seconds
        } @($reportPath,(Join-Path $model 'expanded-training-report.json'))
        Check 'training_complete_rows_CE_groups_and_position_comparisons' {
            Need ($null -ne $training) 'Training report is unavailable.'; CheckObservations $training.loaded_observations $training.parameter_sha256 'training_observations'
            CheckSourceRows $training
            Need ((Close $training.trained_train.mean_loss $training.loaded_observations.train.mean_loss 2e-6) -and (Close $training.trained_development.mean_loss $training.loaded_observations.development.mean_loss 2e-6)) 'Trained vs saved-head full CE differs.'
        } @($reportPath)
        Check 'sample_visits_exact_seeded_order_replay' {
            Need ($null -ne $training) 'Training report is unavailable.'; $v=$training.sample_visits
            $ids=@($training.loaded_observations.rows|Where-Object { $_.split -ceq 'train' -and $_.variant -ceq 'original' }|ForEach-Object {$_.record_id})
            Need ($v.seed -eq 17 -and $v.train_count -eq 162 -and $v.observed_committed_updates -eq 24576 -and $v.complete_order_cycles -eq 151 -and $v.final_cycle_prefix_count -eq 114 -and $v.visits.Count -eq 162 -and (SameArray $v.frozen_file_order $ids)) 'Visit actual denominator/cycles/file order differs.'
            Need ($v.semantics -ceq 'contiguous_actual_commit_callbacks_with_selected_ids_replayed_from_core_xorshift32_fisher_yates_fixed_order_not_direct_kernel_id_observation') 'Commit callback visit semantics differs.'
            $order=SeededOrder 162 17
            $seeded=@($order|ForEach-Object {$ids[$_]}); Need ((SameArray $v.seeded_cycle_order $seeded) -and (SameArray $v.final_cycle_prefix @($seeded|Select-Object -First 114)) -and (SameHash $v.order_index_sha256 (IntHash $order))) 'Exact xorshift32/Fisher-Yates order/prefix SHA differs.'
            $counts=@{}; for ($index=0; $index -lt 162; $index++) { Guard; $counts[$seeded[$index]]=151+[int]($index -lt 114) }
            $total=0; $minimum=153; $maximum=0
            for ($index=0; $index -lt 162; $index++) { Guard; $visit=$v.visits[$index]; Need ($visit.record_id -ceq $ids[$index] -and $visit.committed_updates -eq $counts[$visit.record_id]) 'Per-record committed visit count differs.'; $total+=$visit.committed_updates;$minimum=[Math]::Min($minimum,$visit.committed_updates);$maximum=[Math]::Max($maximum,$visit.committed_updates) }
            Need ($total -eq 24576 -and $minimum -eq 151 -and $maximum -eq 152) 'Total/min/max visits differ.'
            $metrics.visits=[ordered]@{records=162;total=24576;minimum=$minimum;maximum=$maximum;complete_cycles=151;partial_prefix=114}
        } @($reportPath)
        foreach ($phase in @('train','validate','regress')) {
            $phaseCopy=$phase; $command=if ($phase -ceq 'train') {'train-expanded'} elseif ($phase -ceq 'validate') {'validate-expanded'} else {'regress'}
            $milliseconds=if ($phase -ceq 'regress') {315000} else {1520000}
            Check ($phase+'_runner_and_resource_closure') { CheckRunner $phaseCopy $command $milliseconds } @((Join-Path $results ('.artifacts/stage-'+$phase+'/result.tsv')))
        }
        Check 'independent_validation_v4_and_new_process' {
            Need ($null -ne $training -and $null -ne $reportSha) 'Pinned training report is unavailable.'
            $script:validation=ReadJson $validationPath; $v=$validation
            Need ($v.contract -ceq 'sezika/teacher-labelled-gpu-expanded-experiment-validation/v4' -and $v.status -ceq 'completed_experiment_not_formal_acceptance' -and $v.fresh_disk_outputs_matched -eq $true -and $v.max_tokens -eq 8192 -and $v.encoder_forwards_started -eq 231 -and $v.encoder_forwards_completed -eq 231 -and $v.elapsed_seconds -le 1500) 'Fresh independent validation contract/denominator differs.'
            foreach ($pair in @(@($v.source_report_sha256,$reportSha),@($v.manifest_sha256,$expected.manifest),@($v.derivation_ledger_sha256,$expected.ledger),@($v.encoder_manifest_sha256,$expected.encoder_manifest),@($v.records_sha256,$training.records_sha256),@($v.input_provenance_sha256,$training.input_provenance_sha256),@($v.head_sha256,$training.head_sha256),@($v.checkpoint_sha256,$training.checkpoint_sha256),@($v.parameter_sha256,$training.parameter_sha256))) { Guard; Need (SameHash $pair[0] $pair[1]) 'Independent validation source/asset binding differs.' }
            Need ($stageEvidence.ContainsKey('train') -and $stageEvidence.ContainsKey('validate')) 'Independent stage process evidence is unavailable.'
            foreach ($identity in $stageEvidence.validate.payload_identities) { Guard; Need ($identity -cnotin $stageEvidence.train.payload_identities) 'Validation reused the training process identity.' }
            CheckAcceptance $v.acceptance; CheckResources $v.resources 'validation'; CheckObservations $v.observations $training.parameter_sha256 'validation_observations'
            $oldRows=$training.loaded_observations.rows; $newRows=$v.observations.rows
            for ($index=0; $index -lt 231; $index++) {
                Guard; $old=$oldRows[$index];$fresh=$newRows[$index]
                foreach ($field in @('record_id','split','language','type','variant','teacher_target_id','original_teacher_target_index','teacher_target_index','teacher_target_rank','predicted_index','selected_candidate_id','agrees_with_teacher','token_sha256','marker_sha256','feature_sha256','original_token_sha256','original_marker_sha256','original_feature_sha256')) { Guard; Need ($old[$field] -ceq $fresh[$field]) 'Fresh row identity/decision/hash differs.' }
                foreach ($field in @('original_candidate_ids','candidate_ids','candidate_text_sha256','token_ids','marker_positions')) { Guard; Need (SameArray $old[$field] $fresh[$field]) 'Fresh canonical token/text/marker arrays differ.' }
                Need ((Close $old.gpu_teacher_ce $fresh.gpu_teacher_ce 2e-6) -and (Close $old.stable_softmax_teacher_nll $fresh.stable_softmax_teacher_nll 2e-6)) 'Fresh per-row CE differs.'
                for ($candidate=0; $candidate -lt $fresh.logits.Count -and $candidate -lt 8; $candidate++) { Guard; Need ((Close $old.logits[$candidate] $fresh.logits[$candidate] 2e-6) -and (Close $old.probabilities[$candidate] $fresh.probabilities[$candidate] 2e-6)) 'Fresh per-row logits/probabilities differ.' }
            }
            foreach ($field in @('train','development','rotated_training_choice')) { Guard; foreach ($hashName in @('feature_sha256','parameter_sha256','device_parameter_sha256')) { Guard; Need (SameHash $training.loaded_observations[$field][$hashName] $v.observations[$field][$hashName]) 'Fresh full CE feature/parameter SHA differs.' }; Need (Close $training.loaded_observations[$field].mean_loss $v.observations[$field].mean_loss 2e-6) 'Fresh full CE differs.' }
            $metrics.validation_sha256=HashFile $validationPath 4MB; $metrics.validation_elapsed_seconds=$v.elapsed_seconds
        } @($validationPath)
        Check 'original32_corrected_audit_regression' {
            Need ($null -ne $training -and $null -ne $reportSha) 'Pinned training report is unavailable.'
            $script:regression=ReadJson $regressionPath; $r=$regression
            Need ($r.contract -ceq 'sezika/exposed-original32-corrected-label-gpu-regression/v2' -and $r.status -ceq 'completed_exposed_regression_not_quality_acceptance' -and $r.use -ceq 'previously_exposed32_with_ai_audit_sidecar_not_gold_or_sealed' -and $r.total -eq 32 -and $r.completed -eq 32 -and $r.encoder_forwards_started -eq 32 -and $r.encoder_forwards_completed -eq 32 -and $r.corrections -eq 3 -and $r.rows.Count -eq 32 -and $r.groups.Count -eq 3 -and $r.max_tokens -eq 8192 -and $r.elapsed_seconds -le 300) 'Original32 full exposed regression contract differs.'
            Need ($r.device.uuid -ceq $expected.gpu -and $r.device.driver_version -eq $training.device.driver_version -and $r.backend -ceq 'cuda-independent-linear-marker-head' -and $r.score_expanded_training_supervision_count -eq 0) 'Regression device/backend/zero Score supervision differs.'
            foreach ($pair in @(@($r.original_records_sha256,$expected.original_records),@($r.corrected_audit_sha256,$expected.original_audit),@($r.expanded_training_report_sha256,$reportSha),@($r.head_sha256,$training.head_sha256),@($r.expanded_feature_sha256,$training.feature_sha256),@($r.parameter_sha256,$training.parameter_sha256),@($r.encoder_manifest_sha256,$expected.encoder_manifest),@($r.identity.encoder_sha256,$expected.encoder),@($r.identity.tokenizer_sha256,$expected.tokenizer))) { Guard; Need (SameHash $pair[0] $pair[1]) 'Original32 pinned source/model SHA differs.' }
            Need ((SameHash (HashFile (Join-Path $task 'prepared/regression_input/experiment-records.jsonl') 4MB) $expected.original_records) -and (SameHash (HashFile (Join-Path $task 'prepared/regression_input/audit-report.json') 4MB) $expected.original_audit)) 'Actual uploaded original32 source/audit SHA differs.'
            CheckAcceptance $r.acceptance
            $resource=@{snapshot_before_context_dispose=$r.snapshot_after_pipeline_dispose;device_dispose_returned=$r.device_dispose_returned;model_dispose_returned=$r.model_dispose_returned;process_peak_working_set_bytes=$r.process_peak_working_set_bytes;process_working_set_bytes=$r.process_working_set_bytes}; CheckResources $resource 'regression'
            $seen=@{}; $changes=@{}; $byType=@{}
            for ($index=0; $index -lt 32; $index++) {
                Guard; $row=$r.rows[$index]; Need (-not $seen.ContainsKey($row.record_id) -and $row.type -cin @('choice','boolean','score')) 'Regression duplicate/type differs.'; $seen[$row.record_id]=$true
                CheckPrediction $row -Regression
                Need ($row.token_count -ge 2 -and $row.token_count -le 8192 -and $row.marker_positions.Count -eq $row.candidate_ids.Count -and (SameHash $row.token_sha256 $row.token_sha256) -and (SameHash $row.marker_sha256 (IntHash $row.marker_positions))) 'Regression token/marker evidence differs.'
                Need ($row.label_corrected -eq ($row.original_target_index -ne $row.corrected_audit_target_index)) 'Regression correction flag differs.'
                if ($row.label_corrected) {$changes[$row.record_id]=@($row.original_target_index,$row.corrected_audit_target_index)}
                if (-not $byType.ContainsKey($row.type)) {$byType[$row.type]=[Collections.Generic.List[object]]::new()};$byType[$row.type].Add($row)
            }
            Need ($changes.Count -eq 3 -and (SameArray $changes['general-en-score-03'] @(2,3)) -and (SameArray $changes['general-zh-boolean-02'] @(1,0)) -and (SameArray $changes['general-en-boolean-01'] @(1,0))) 'Frozen three audit corrections differ.'
            $seenTypes=@{}
            foreach ($group in $r.groups) { Guard; Need ($byType.ContainsKey($group.type) -and -not $seenTypes.ContainsKey($group.type)) 'Regression groups duplicate/type differs.';$seenTypes[$group.type]=$true;$items=$byType[$group.type];$agree=@($items|Where-Object agrees_with_corrected_audit).Count;$supervision=@($training.loaded_observations.rows|Where-Object {$_.type -ceq $group.type -and $_.split -ceq 'train' -and $_.variant -ceq 'original'}).Count; Need ($group.total -eq $items.Count -and $group.agreement_count -eq $agree -and (Close $group.agreement_rate ($agree/[double]$items.Count)) -and (Close $group.mean_corrected_audit_nll (($items|Measure-Object -Property corrected_audit_nll -Average).Average)) -and $group.expanded_training_supervision_count -eq $supervision) 'Recomputed original32 group agreement/NLL/supervision differs.' }
            $metrics.regression=[ordered]@{rows=32;corrections=3;score_training_supervision=0;groups=$r.groups;elapsed_seconds=$r.elapsed_seconds;sha256=(HashFile $regressionPath 4MB)}
        } @($regressionPath)
        Check 'model_download_UTC_deadlines_and_receipt' {
            $download=ReadJson (Join-Path $task 'model-download.json'); $deadline=ReadJson (Join-Path $task 'deadline.json')
            $when=UtcInstant $download.utc; $target=UtcInstant $deadline.deadline_utc; $rentalEnd=UtcInstant $deadline.rental_end_utc
            Need ($target -eq [DateTimeOffset]::new(2026,10,9,15,22,0,[TimeSpan]::Zero) -and $rentalEnd -eq [DateTimeOffset]::new(2026,10,9,16,22,0,[TimeSpan]::Zero)) 'User target/rental absolute deadline differs.'
            Need ($download.status -ceq 'trained_model_downloaded_and_complete_local_assets_verified' -and [IO.Path]::GetFullPath($download.model_directory) -ceq $model -and $download.remote -ceq $expectedRemote -and $download.formal_acceptance -eq $false -and $download.completed_steps -eq 24576 -and $when -le $target -and $when -lt $rentalEnd) 'Downloaded model receipt/deadline differs.'
            Need (SameHash (HashFile $download.download_archive 16MB) $download.archive_sha256) 'Model archive download SHA differs.'
            $metrics.download=[ordered]@{completed_utc=$when.ToString('o');target_utc=$target.ToString('o');rental_end_utc=$rentalEnd.ToString('o');seconds_before_target=($target-$when).TotalSeconds;archive_sha256=$download.archive_sha256}
        } @((Join-Path $task 'model-download.json'),(Join-Path $task 'deadline.json'))
    }
} catch { $checks.Add([ordered]@{name='verifier_execution';status='FAIL';detail=$_.Exception.Message}) }
finally {
    $failed=@($checks|Where-Object {$_.status -ceq 'FAIL'}).Count
    $notRun=@($checks|Where-Object {$_.status -ceq 'NOT_RUN'}).Count
    $summary.status=if ($failed -gt 0) {'FAIL'} elseif ($notRun -gt 0) {'NOT_RUN'} else {'PASS_experiment_evidence_only_not_formal_acceptance'}
    $summary.finished_utc=[DateTime]::UtcNow.ToString('o');$summary.elapsed_seconds=$clock.Elapsed.TotalSeconds
    $summary.operations=$operationCount;$summary.maximum_operations=$maximumOperations;$summary.timeout_seconds=$TimeoutSeconds
    $summary.external_processes_started=0;$summary.temporary_files_created=0
    $outputName=if ($ReaderProbeOnly) {'verification-reader-probe.json'} else {'verification.json'}
    [IO.File]::WriteAllText((Join-Path $task $outputName),($summary|ConvertTo-Json -Depth 40),[Text.UTF8Encoding]::new($false))
    $summary | ConvertTo-Json -Depth 6
}
if (@($checks|Where-Object {$_.status -ceq 'FAIL'}).Count -gt 0) { exit 1 }
if (@($checks|Where-Object {$_.status -ceq 'NOT_RUN'}).Count -gt 0) { exit 2 }
exit 0
