#requires -Version 7.0
param([switch]$Probe)
$ErrorActionPreference='Stop'
$auditRoot='D:\source\Sezika\docs\evidence\teacher-repair-20261009-final'
$auditOutputRoot='D:\source\Sezika\.artifacts\repaired-training-20261009'
$auditClock=[Diagnostics.Stopwatch]::StartNew()
function Check-Audit {
    if($auditClock.Elapsed.TotalSeconds -ge 90 -or (Test-Path -LiteralPath (Join-Path $auditOutputRoot 'audit-cancel.request'))){throw 'Audit90s/cancel'}
}
function Assert-Audit([bool]$condition,[string]$description){Check-Audit;if(-not $condition){throw $description}}
function Same-Json($left,$right){
    return [System.Text.Json.Nodes.JsonNode]::DeepEquals([System.Text.Json.Nodes.JsonNode]::Parse(($left|ConvertTo-Json -Depth 60 -Compress)),[System.Text.Json.Nodes.JsonNode]::Parse(($right|ConvertTo-Json -Depth 60 -Compress)))
}
function Read-Json([string]$name){Check-Audit;return Get-Content -LiteralPath (Join-Path $auditRoot $name) -Raw|ConvertFrom-Json -Depth 100 -DateKind String}
$auditIdentity=Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 5
$auditInventory=Read-Json 'inventory.json'
Assert-Audit (@($auditInventory.files).Count -eq 332) 'Inventory332'
if($Probe){
    $auditFile=$auditInventory.files[0]
    $auditFullPath=[IO.Path]::GetFullPath((Join-Path $auditRoot $auditFile.path))
    Assert-Audit ($auditFullPath.StartsWith($auditRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Probe path boundary'
    Assert-Audit ((Get-Item -LiteralPath $auditFullPath).Length -eq $auditFile.bytes) 'Probe bytes'
    Assert-Audit ((Get-FileHash -LiteralPath $auditFullPath -Algorithm SHA256).Hash -ceq $auditFile.sha256) 'Probe hash'
    [ordered]@{utc=[DateTimeOffset]::UtcNow.ToString('o');probe='passed';items=1;seconds=$auditClock.Elapsed.TotalSeconds;reader=$auditIdentity|Select-Object ProcessId,CreationDate,CommandLine,ParentProcessId}|ConvertTo-Json -Depth 5
    exit 0
}
$auditTotalBytes=0L
foreach($auditFile in $auditInventory.files){
    Check-Audit
    $auditFullPath=[IO.Path]::GetFullPath((Join-Path $auditRoot $auditFile.path))
    Assert-Audit ($auditFullPath.StartsWith($auditRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Inventory path boundary'
    Assert-Audit ((Get-Item -LiteralPath $auditFullPath).Length -eq $auditFile.bytes) ('Inventory bytes '+$auditFile.path)
    Assert-Audit ((Get-FileHash -LiteralPath $auditFullPath -Algorithm SHA256).Hash -ceq $auditFile.sha256) ('Inventory hash '+$auditFile.path)
    $auditTotalBytes+=$auditFile.bytes
}
Assert-Audit ($auditTotalBytes -eq 8281934 -and $auditTotalBytes -eq $auditInventory.total_bytes) 'Inventory total bytes'
Write-Output 'Audit inventory332 byte/hash checks passed'
$auditManifest=Read-Json 'data-v3\experiment-manifest.json'
$auditLedger=Read-Json 'data-v3\derivation-ledger.json'
$auditCatalog=Read-Json 'data-v3\source-catalog.json'
$auditProvenance=Read-Json 'data-v3\input-provenance.json'
$auditRequests=Read-Json 'data-v3\teacher-requests.json'
$auditResponses=Read-Json 'data-v3\teacher-responses.json'
$auditPlan=Read-Json 'plan.json'
Assert-Audit ((Get-FileHash -LiteralPath (Join-Path $auditRoot 'data-v3\experiment-manifest.json')).Hash -ceq 'E8E74CE0BBF1A72DA5FCDF4DCAD9D302513DC5DC42CB548BFD21D2569621AEB6') 'Handoff manifest hash'
Assert-Audit ((Get-FileHash -LiteralPath (Join-Path $auditRoot 'data-v3\derivation-ledger.json')).Hash -ceq '55F3226946831C4B9CBD7F6EE10D1194764860C8343A9386CAA40C678CE24EC0') 'Handoff derivation hash'
$auditAssets=@($auditManifest.records,$auditManifest.provenance,$auditManifest.derivation)+@($auditManifest.sources)
Assert-Audit ($auditAssets.Count -eq 6) 'Manifest asset6'
foreach($auditAsset in $auditAssets){
    Check-Audit;$auditName=[IO.Path]::GetFileName($auditAsset.path)
    $auditPermanent=Join-Path (Join-Path $auditRoot 'data-v3') $auditName
    Assert-Audit ((Get-Item -LiteralPath $auditPermanent).Length -eq $auditAsset.bytes -and (Get-FileHash -LiteralPath $auditPermanent).Hash -ceq $auditAsset.sha256) ('Manifest asset '+$auditName)
    Assert-Audit ((Get-Item -LiteralPath $auditAsset.path).Length -eq $auditAsset.bytes -and (Get-FileHash -LiteralPath $auditAsset.path).Hash -ceq $auditAsset.sha256) ('Working mirror '+$auditName)
}
Assert-Audit ((Get-FileHash -LiteralPath $auditPlan.source).Hash -ceq $auditPlan.source_sha256 -and $auditLedger.source_sha256 -ceq $auditPlan.source_sha256) 'Original source identity'
$auditOriginal=@(Get-Content -LiteralPath $auditPlan.source -TotalCount 257|ForEach-Object {Check-Audit;$_|ConvertFrom-Json -Depth 60})
$auditRows=@(Get-Content -LiteralPath (Join-Path $auditRoot 'data-v3\experiment-records.jsonl') -TotalCount 257|ForEach-Object {Check-Audit;$_|ConvertFrom-Json -Depth 60})
Assert-Audit ($auditOriginal.Count -eq 256 -and $auditLedger.records.Count -eq 256 -and $auditRows.Count -eq 215 -and $auditCatalog.records.Count -eq 215 -and $auditProvenance.records.Count -eq 215) 'Actual denominator'
$auditOriginalById=@{};$auditLedgerById=@{};$auditRowsById=@{};$auditCatalogById=@{};$auditProvenanceById=@{};$auditFamily=@{}
foreach($auditRow in $auditOriginal){Check-Audit;Assert-Audit (-not $auditOriginalById.ContainsKey($auditRow.record_id)) 'Original unique ID';$auditOriginalById.Add($auditRow.record_id,$auditRow)}
foreach($auditRow in $auditLedger.records){
    Check-Audit;Assert-Audit (-not $auditLedgerById.ContainsKey($auditRow.record_id)) 'Ledger unique ID';$auditLedgerById.Add($auditRow.record_id,$auditRow)
    $auditOld=$auditOriginalById[$auditRow.record_id]
    Assert-Audit ($null -ne $auditOld -and $auditRow.source_canonical_id -ceq $auditOld.source_canonical_id -and $auditRow.family_id -ceq $auditOld.family_id -and $auditRow.language -ceq $auditOld.language -and $auditRow.split -ceq $auditOld.split -and $auditRow.source_gold_index -eq $auditOld.target_index) ('Ledger original invariants '+$auditRow.record_id)
    Assert-Audit (Same-Json $auditRow.original_request $auditOld.request) ('Ledger original request '+$auditRow.record_id)
    Assert-Audit ($auditRow.human_review -ceq 'pending' -and -not $auditRow.formal) 'Ledger formal state'
}
foreach($auditRow in $auditCatalog.records){Check-Audit;Assert-Audit (-not $auditCatalogById.ContainsKey($auditRow.record_id)) 'Catalog unique ID';$auditCatalogById.Add($auditRow.record_id,$auditRow)}
foreach($auditRow in $auditProvenance.records){Check-Audit;Assert-Audit (-not $auditProvenanceById.ContainsKey($auditRow.record_id)) 'Provenance unique ID';$auditProvenanceById.Add($auditRow.record_id,$auditRow)}
$auditTrain=0;$auditDev=0;$auditChoice=0;$auditBoolean=0;$auditAction=@{retained_unchanged=0;repaired_and_reaudited=0;deleted=0};$auditDeletedSplit=@{train=0;development=0}
foreach($auditRow in $auditLedger.records){Check-Audit;Assert-Audit ($auditAction.ContainsKey($auditRow.action)) 'Action enum';$auditAction[$auditRow.action]++;if($auditRow.action -ceq 'deleted'){$auditDeletedSplit[$auditRow.split]++}}
foreach($auditRow in $auditRows){
    Check-Audit;Assert-Audit (-not $auditRowsById.ContainsKey($auditRow.record_id)) 'Retained unique ID';$auditRowsById.Add($auditRow.record_id,$auditRow)
    $auditOld=$auditOriginalById[$auditRow.record_id];$auditDerivation=$auditLedgerById[$auditRow.record_id];$auditSource=$auditCatalogById[$auditRow.record_id];$auditProv=$auditProvenanceById[$auditRow.record_id]
    Assert-Audit ($null -ne $auditOld -and $auditDerivation.action -cne 'deleted') 'Retained identity'
    Assert-Audit ($auditRow.request.state.text -ceq $auditOld.request.state.text -and $auditRow.family_id -ceq $auditOld.family_id -and $auditRow.language -ceq $auditOld.language -and $auditRow.split -ceq $auditOld.split -and $auditRow.request.model_id -ceq $auditOld.request.model_id -and $auditRow.request.schema_version -eq $auditOld.request.schema_version) ('Retained source invariants '+$auditRow.record_id)
    Assert-Audit ($auditSource.source_canonical_id -ceq $auditOld.source_canonical_id -and (Same-Json $auditSource.source $auditOld.source)) ('Catalog original source '+$auditRow.record_id)
    Assert-Audit ($auditProv.target_index -eq $auditRow.target_index -and $auditProv.split -ceq $auditRow.split -and $auditProv.family_id -ceq $auditRow.family_id -and (Same-Json $auditProv.teacher_binding $auditRow.teacher_binding)) 'Provenance binding'
    if($auditFamily.ContainsKey($auditRow.family_id)){Assert-Audit ($auditFamily[$auditRow.family_id] -ceq $auditRow.split) 'Family split leak'}else{$auditFamily.Add($auditRow.family_id,$auditRow.split)}
    if($auditDerivation.action -ceq 'retained_unchanged'){Assert-Audit (Same-Json $auditRow.request $auditOld.request) 'Unchanged request'}else{Assert-Audit (Same-Json $auditRow.request $auditDerivation.revised_request) 'Revised request';Assert-Audit ($auditRow.teacher_binding.request_sha256 -ceq $auditDerivation.reaudit_request_sha256 -and $auditRow.teacher_binding.response_sha256 -ceq $auditDerivation.reaudit_response_sha256) 'Reaudit binding'}
    $auditQuestion=$auditRow.request.questions[0];Assert-Audit ($auditRow.request.questions.Count -eq 1) 'One question'
    $auditIds=if($auditQuestion.type -ceq 'choice'){@($auditQuestion.candidates.id);$auditChoice++}elseif($auditQuestion.type -ceq 'boolean'){@('false','true');$auditBoolean++}else{throw 'Unexpected Score supervision'}
    Assert-Audit ($auditRow.target_index -ge 0 -and $auditRow.target_index -lt $auditIds.Count -and $auditRow.teacher_binding.selected_candidate_id -ceq $auditIds[$auditRow.target_index] -and $auditRow.teacher_binding.target_index -eq $auditRow.target_index -and (Same-Json $auditProv.candidate_ids $auditIds)) 'Candidate target mapping'
    if($auditRow.split -ceq 'train'){$auditTrain++}elseif($auditRow.split -ceq 'development'){$auditDev++}else{throw 'Unexpected split'}
}
Assert-Audit ($auditTrain -eq 162 -and $auditDev -eq 53 -and $auditChoice -eq 140 -and $auditBoolean -eq 75 -and $auditAction.retained_unchanged -eq 178 -and $auditAction.repaired_and_reaudited -eq 37 -and $auditAction.deleted -eq 41 -and $auditDeletedSplit.train -eq 30 -and $auditDeletedSplit.development -eq 11) 'Actual groups/actions'
Write-Output 'Audit256 derivation and215 retained invariants passed'
Assert-Audit ($auditRequests.batches.Count -eq 40 -and $auditResponses.batches.Count -eq 40) 'Wire batches40'
$auditRequestHashes=@{};$auditResponseHashes=@{}
foreach($auditItem in $auditRequests.batches){Check-Audit;Assert-Audit ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Convert]::FromBase64String($auditItem.bytes_base64))) -ceq $auditItem.sha256) 'Wire request hash';Assert-Audit (-not $auditRequestHashes.ContainsKey($auditItem.sha256)) 'Unique wire request';$auditRequestHashes.Add($auditItem.sha256,$true)}
foreach($auditItem in $auditResponses.batches){Check-Audit;Assert-Audit ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Convert]::FromBase64String($auditItem.bytes_base64))) -ceq $auditItem.sha256 -and $auditRequestHashes.ContainsKey($auditItem.request_sha256)) 'Wire response hash/request';Assert-Audit (-not $auditResponseHashes.ContainsKey($auditItem.sha256)) 'Unique wire response';$auditResponseHashes.Add($auditItem.sha256,$auditItem)}
foreach($auditRow in $auditRows){Check-Audit;Assert-Audit ($auditRequestHashes.ContainsKey($auditRow.teacher_binding.request_sha256) -and $auditResponseHashes.ContainsKey($auditRow.teacher_binding.response_sha256)) 'Retained wire refs';$auditResponse=$auditResponseHashes[$auditRow.teacher_binding.response_sha256];Assert-Audit ($auditResponse.response_id -ceq $auditRow.teacher_binding.response_id -and $auditResponse.request_sha256 -ceq $auditRow.teacher_binding.request_sha256) 'Retained response binding'}
$auditSourceFileCount=0
foreach($auditProperty in $auditCatalog.source_files.PSObject.Properties){Check-Audit;$auditSourceFileCount++;Assert-Audit ($auditSourceFileCount -le 8) 'Source file cap8';$auditAsset=$auditProperty.Value;Assert-Audit ((Get-Item -LiteralPath $auditAsset.path).Length -eq $auditAsset.bytes -and (Get-FileHash -LiteralPath $auditAsset.path).Hash -ceq $auditAsset.sha256) 'Source file bytes/hash'}
$auditHttp=Read-Json 'http-ledger.json';Assert-Audit ($auditHttp.http_requests_sent -eq 21 -and $auditHttp.http200 -eq 21 -and $auditHttp.validated_batches -eq 21 -and $auditHttp.transport_attempts -eq 21) 'HTTP ledger21'
$auditClosure=Read-Json 'process-closure.json';Assert-Audit ($auditClosure.receipts -eq 22 -and $auditClosure.identity_records -eq 112 -and $auditClosure.unique_pids -eq 93 -and $auditClosure.matching_live -eq 0 -and $auditClosure.unknown -eq 0 -and $auditClosure.historical_records_without_observed_argv -eq 18) 'Historical closure totals'
$auditKnown=@($auditClosure.records)
$auditFollowup=Read-Json 'closure-followup.json'
$auditKnown+= [pscustomobject]@{Pid=$auditFollowup.reader.ProcessId;Started=$auditFollowup.reader.CreationDate;CommandLine=$auditFollowup.reader.CommandLine;ParentPid=$auditFollowup.reader.ParentProcessId;source='packager-followup'}
$auditGitClosure=Get-Content -LiteralPath 'D:\source\Sezika\.artifacts\teacher-repair-20261009\git-closure.json' -Raw|ConvertFrom-Json -Depth 20 -DateKind String
Assert-Audit ($auditGitClosure.checks.Count -eq 5) 'Git closure5'
foreach($auditItem in $auditGitClosure.checks){Check-Audit;$auditKnown+=[pscustomobject]@{Pid=$auditItem.pid;Started=$auditItem.started;CommandLine=$null;ParentPid=$null;source='git-closure'}}
Assert-Audit ($auditKnown.Count -eq 118) 'Current identity cap118'
$auditPids=@($auditKnown.Pid|Sort-Object -Unique);Assert-Audit ($auditPids.Count -le 99) 'Unique PID cap99';$auditFound=@{};$auditQueryCount=0
for($auditOffset=0;$auditOffset -lt $auditPids.Count -and $auditOffset -lt 99;$auditOffset+=32){
    Check-Audit;$auditQueryCount++;Assert-Audit ($auditQueryCount -le 4) 'PID query cap4'
    $auditLast=[Math]::Min($auditOffset+31,$auditPids.Count-1);$auditPart=@($auditPids[$auditOffset..$auditLast])
    $auditFilter=(@($auditPart|ForEach-Object {'ProcessId='+$_}) -join ' OR ')
    $auditCurrent=@(Get-CimInstance Win32_Process -Filter $auditFilter -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 5)
    Assert-Audit ($auditCurrent.Count -le $auditPart.Count) 'PID narrow query rows'
    foreach($auditPid in $auditPart){Check-Audit;$auditFound[[int]$auditPid]=$null}
    foreach($auditProc in $auditCurrent){Check-Audit;$auditFound[[int]$auditProc.ProcessId]=$auditProc}
    Write-Output ('Audit exact registered PIDs '+[Math]::Min($auditOffset+32,$auditPids.Count)+'/'+$auditPids.Count)
}
$auditStates=[Collections.Generic.List[object]]::new();$auditMatching=0;$auditUnknown=0
foreach($auditItem in $auditKnown){
    Check-Audit;$auditActual=$auditFound[[int]$auditItem.Pid];$auditState='absent'
    if($null -ne $auditActual){
        if([Math]::Abs(($auditActual.CreationDate.ToUniversalTime()-[DateTimeOffset]::Parse($auditItem.Started).UtcDateTime).Ticks) -gt 10){$auditState='pid_reused'}
        elseif([string]::IsNullOrWhiteSpace([string]$auditItem.CommandLine) -or $auditActual.CommandLine -cne $auditItem.CommandLine -or ($null -ne $auditItem.ParentPid -and $auditActual.ParentProcessId -ne $auditItem.ParentPid)){$auditState='matching_start_incomplete';$auditUnknown++}
        else{$auditState='matching_owned_live';$auditMatching++}
    }
    $auditStates.Add([pscustomobject]@{pid=$auditItem.Pid;started=$auditItem.Started;source=$auditItem.source;state=$auditState})
}
Assert-Audit ($auditMatching -eq 0 -and $auditUnknown -eq 0) 'Current registered processes not closed'
$auditReport=[ordered]@{
    utc=[DateTimeOffset]::UtcNow.ToString('o');status='passed_independent_static_checks';seconds=$auditClock.Elapsed.TotalSeconds
    permanent_inventory_files=332;permanent_total_with_inventory=333;permanent_inventory_bytes=$auditTotalBytes
    manifest_sha256='E8E74CE0BBF1A72DA5FCDF4DCAD9D302513DC5DC42CB548BFD21D2569621AEB6';derivation_sha256='55F3226946831C4B9CBD7F6EE10D1194764860C8343A9386CAA40C678CE24EC0'
    original=256;retained=215;train=$auditTrain;development=$auditDev;choice=$auditChoice;boolean=$auditBoolean;unchanged=178;repaired=37;deleted=41;deleted_split=$auditDeletedSplit
    actual_catalog_rows=$auditCatalog.records.Count;actual_provenance_rows=$auditProvenance.records.Count;catalog_inherited_record_count=$auditCatalog.record_count;catalog_inherited_split_counts=$auditCatalog.split_counts
    catalog_metadata_caveat='Catalog aggregate fields remain parent256/192/64/160/96 snapshot values. Native v3 must derive actual denominators from manifest and retained rows, validate them, and explicitly distinguish parent aggregates.'
    original_state_family_split_language_gold_source_preserved=$true;retained_target_mapping_and_wire_binding=$true;wire_request_response_pairs=40;source_files=$auditSourceFileCount;http200=21
    closure_historical_receipts=22;closure_historical_identity_records=112;closure_historical_unique_pids=93;historical_missing_argv=18
    current_registered_records=$auditKnown.Count;current_registered_unique_pids=$auditPids.Count;matching_live=$auditMatching;unknown=$auditUnknown;current_states=@($auditStates)
    reader=$auditIdentity|Select-Object ProcessId,CreationDate,CommandLine,ParentProcessId
    limits='Read-only PS7;90seconds;332inventory/256original/215retained/40wire-pairs/8source-files/118identities/99PIDs/4queries;cancel audit-cancel.request;no child process launched'
    limitations=@('No native renderer/tokenizer strict1024 measurement in this audit.','No GPU training, teacher calls, or model quality review.','Current absence does not fill18missing historical argv or prove unobserved short-lived descendants.','Git object byte comparison pending separate read-only audit.');formal_human_review='pending';model_quality_passed=$false;training_executed=$false
}
$auditOutput=Join-Path $auditOutputRoot 'audit-data-validation.json'
Assert-Audit (-not (Test-Path -LiteralPath $auditOutput)) 'Preserve prior audit output'
[IO.File]::WriteAllText($auditOutput,($auditReport|ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
Write-Output ('Audit passed evidence='+$auditOutput)
