#requires -Version 7.0
$ErrorActionPreference='Stop';$root=$PSScriptRoot;$old='D:\source\Sezika\.artifacts\teacher-expansion-20261008';$clock=[Diagnostics.Stopwatch]::StartNew()
function Check {if($clock.Elapsed.TotalSeconds -gt 180 -or (Test-Path "$root/cancel.request")){throw 'Package180s/cancel'}}
function Save($Path,$Value){Check;if(Test-Path $Path){throw 'Never overwrite package'};[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 70),[Text.UTF8Encoding]::new($false))}
$plan=Get-Content -Raw "$root/plan.json"|ConvertFrom-Json
if((Get-FileHash $plan.source).Hash -cne $plan.source_sha256 -or (Get-FileHash $plan.repair_source).Hash -cne $plan.repair_source_sha256){throw 'Original source hash changed'}
$source=@(Get-Content $plan.source -TotalCount 257|ForEach-Object {Check;$_|ConvertFrom-Json});if($source.Count -ne 256){throw 'Full source denominator'}
$issues=Get-Content -Raw $plan.repair_source|ConvertFrom-Json;$blocked=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);foreach($r in $issues.records){[void]$blocked.Add($r.record_id)}
$rootExclusions=@{};if(Test-Path "$root/root-semantic-exclusions.json"){foreach($e in (Get-Content -Raw "$root/root-semantic-exclusions.json"|ConvertFrom-Json).records){Check;if(-not $blocked.Contains($e.record_id)){throw 'Root exclusion outside approved problems'};$rootExclusions[$e.record_id]=$e.reason}}
$labels=@{};$repairs=@{};$reviews=@{};$drafts=@{};$repairFailures=@{};$requests=[Collections.Generic.List[object]]::new();$responses=[Collections.Generic.List[object]]::new();$seenRequests=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
. "$root/Test-LabelAuditPayload.ps1"
. "$root/Test-RepairPayload.ps1"
function Read-ValidatedBatch([string]$Directory,[object[]]$Selected,[string]$Kind){
 Check;$receipt=Get-Content -Raw "$Directory/result.json"|ConvertFrom-Json
 if($receipt.status -cne 'completed_and_validated'){throw 'Validated batch required'}
 $wire=[IO.File]::ReadAllBytes("$Directory/request.json");$raw=[IO.File]::ReadAllBytes("$Directory/response.json")
 $reqHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($wire));$resHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($raw))
 $prov=Get-Content -Raw "$Directory/provenance.json"|ConvertFrom-Json;if($prov.request_sha256 -cne $reqHash){throw 'Request hash mismatch'}
 $response=[Text.UTF8Encoding]::new($false,$true).GetString($raw)|ConvertFrom-Json -Depth 80
 if($response.status -cne 'completed' -or $response.model -cne 'gpt-6.1-sol'){throw 'Response status/identity mismatch'}
 $texts=@($response.output|Where-Object type -CEQ message|ForEach-Object {$_.content}|Where-Object type -CEQ output_text);if($texts.Count -ne 1){throw 'One response text required'}
 $verified=if($Kind -ceq 'repair'){@(Test-RepairPayload -Json $texts[0].text -SelectedRows $Selected)}else{@(Test-LabelAuditPayload -Json $texts[0].text -SelectedRows $Selected)}
 if($Kind -cne 'repair' -and $seenRequests.Add($reqHash)){$requests.Add(@{sha256=$reqHash;bytes_base64=[Convert]::ToBase64String($wire)});$responses.Add(@{response_id=$response.id;request_sha256=$reqHash;sha256=$resHash;bytes_base64=[Convert]::ToBase64String($raw)})}
 return @{rows=$verified;request_sha256=$reqHash;response_sha256=$resHash;response_id=$response.id;directory=$Directory}
}
$oldNames=@('tiny2','recovery01','recovery02','retry03','retry04','final05v2','final06v2')+@(7..32|ForEach-Object {'batch'+$_.ToString('00')})
for($i=0;$i -lt 33;$i++){
 Check;$dir=Join-Path $old $oldNames[$i];$result=Get-Content -Raw "$dir/result.json"|ConvertFrom-Json;$rows=@($source[$result.start_index..($result.start_index+$result.record_count-1)])
 $batch=Read-ValidatedBatch $dir $rows 'review';foreach($review in $batch.rows){if($labels.ContainsKey($review.record_id)){throw 'Duplicate prior label'};$labels[$review.record_id]=@{review=$review;binding=$batch}}
}
if($labels.Count -ne 256){throw 'Original256 label denominator'}
$repairSchedule=Get-Content -Raw "$root/repair-schedule.json"|ConvertFrom-Json
foreach($job in $repairSchedule.batches){
 Check;$input=Get-Content -Raw "$root/inputs/$($job.batch).json"|ConvertFrom-Json -Depth 65;$dir=Join-Path $root $job.batch;$result=Get-Content -Raw "$dir/result.json"|ConvertFrom-Json
 if($result.status -cne 'completed_and_validated'){foreach($row in $input.selected){$repairFailures[$row.record_id]=@{batch=$job.batch;reason='No valid repair response; transport/validation failure retained';failure=$result.failure}};continue}
 $batch=Read-ValidatedBatch $dir @($input.selected) 'repair';foreach($proposal in $batch.rows){if($repairs.ContainsKey($proposal.record_id)){throw 'Repeated repair'};$repairs[$proposal.record_id]=@{proposal=$proposal;binding=$batch}}
}
$reviewSchedule=Get-Content -Raw "$root/review-schedule.json"|ConvertFrom-Json
foreach($job in $reviewSchedule.batches){
 Check;$input=Get-Content -Raw "$root/inputs/$($job.batch).json"|ConvertFrom-Json -Depth 65;$dir=Join-Path $root $job.batch;$result=Get-Content -Raw "$dir/result.json"|ConvertFrom-Json
 foreach($draft in $input.selected){if($drafts.ContainsKey($draft.record_id)){throw 'Repeated final-order draft'};$drafts[$draft.record_id]=$draft}
 if($result.status -cne 'completed_and_validated'){continue}
 $batch=Read-ValidatedBatch $dir @($input.selected) 'review';foreach($review in $batch.rows){if($reviews.ContainsKey($review.record_id)){throw 'Repeated reaudit'};$reviews[$review.record_id]=@{review=$review;binding=$batch}}
}
$output=Join-Path $root 'data-v3';if(Test-Path $output){throw 'Fresh output required'};[void][IO.Directory]::CreateDirectory($output)
$rows=[Collections.Generic.List[object]]::new();$ledger=[Collections.Generic.List[object]]::new();$inputProvenance=[Collections.Generic.List[object]]::new();$retainedIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$unchanged=0;$repaired=0;$deleted=0
for($i=0;$i -lt 256;$i++){
 Check;$original=$source[$i];$id=$original.record_id;$row=$original;$label=$labels[$id];$action='retained_unchanged';$reason='Existing clear determinate AI review reused';$repairBinding=$null
 if($blocked.Contains($id)){
  $action='deleted';$reason='No valid repaired and independently reaudited question'
  if($rootExclusions.ContainsKey($id)){$reason=$rootExclusions[$id]}
  elseif($repairFailures.ContainsKey($id)){$reason=$repairFailures[$id].reason}
  elseif($repairs.ContainsKey($id)){
   $proposal=$repairs[$id].proposal;$repairBinding=$repairs[$id].binding;$reason=$proposal.reason
   if($proposal.decision -ceq 'repair'){
    if($reviews.ContainsKey($id)){
     $newReview=$reviews[$id].review
     if($newReview.answer_status -ceq 'determinate' -and $newReview.clarity_status -ceq 'clear' -and -not $newReview.question_revision_needed -and $newReview.suggested_correct_index -eq $drafts[$id].target_index){$action='repaired_and_reaudited';$row=$drafts[$id];$label=$reviews[$id]}else{$reason='Blind reaudit rejected or disagreed: '+$newReview.reason}
    }else{$reason='Repaired proposal has no valid blind reaudit; excluded conservatively'}
   }
  }
 }
 $entry=[ordered]@{record_id=$id;source_canonical_id=$original.source_canonical_id;family_id=$original.family_id;language=$original.language;split=$original.split;source_gold_index=$original.target_index;action=$action;reason=$reason;original_request=$original.request;revised_request=if($action -ceq 'repaired_and_reaudited'){$row.request}else{$null};repair_request_sha256=$repairBinding.request_sha256;repair_response_sha256=$repairBinding.response_sha256;reaudit_request_sha256=if($reviews.ContainsKey($id)){$reviews[$id].binding.request_sha256}else{$null};reaudit_response_sha256=if($reviews.ContainsKey($id)){$reviews[$id].binding.response_sha256}else{$null};human_review='pending';formal=$false}
 $ledger.Add($entry)
 if($action -ceq 'deleted'){$deleted++;continue}
 if($row.request.state.text -cne $original.request.state.text -or $row.family_id -cne $original.family_id -or $row.split -cne $original.split -or $row.language -cne $original.language){throw 'Immutable state/family/split/language changed'}
 $review=$label.review;if($review.answer_status -cne 'determinate' -or $review.clarity_status -cne 'clear' -or $review.question_revision_needed){throw 'Retained row not QA eligible'}
 if($action -ceq 'retained_unchanged'){$unchanged++}else{$repaired++}
 if(-not $retainedIds.Add($id)){throw 'Duplicate retained row'}
 $q=$row.request.questions[0];$candidateIds=if($q.type -ceq 'choice'){@($q.candidates.id)}else{@('false','true')}
 $binding=[ordered]@{source_canonical_id=$original.source_canonical_id;response_id=$label.binding.response_id;request_sha256=$label.binding.request_sha256;response_sha256=$label.binding.response_sha256;selected_candidate_id=$review.suggested_correct_id;target_index=$review.suggested_correct_index}
 $rows.Add([ordered]@{record_id=$id;family_id=$row.family_id;language=$row.language;split=$row.split;target_index=$review.suggested_correct_index;request=$row.request;teacher_binding=$binding})
 $inputProvenance.Add(@{record_id=$id;family_id=$row.family_id;split=$row.split;target_index=$review.suggested_correct_index;candidate_ids=$candidateIds;selected_candidate_id=$review.suggested_correct_id;teacher_binding=$binding})
}
if($unchanged -ne 178 -or $repaired+$deleted -ne 78 -or $rows.Count+$deleted -ne 256){throw 'Final full denominator mismatch'}
$families=@{};foreach($row in $rows){Check;if($families.ContainsKey($row.family_id) -and $families[$row.family_id] -cne $row.split){throw 'Family split leak'};$families[$row.family_id]=$row.split}
$catalog=Get-Content -Raw "$old/source-catalog.json"|ConvertFrom-Json -AsHashtable
$catalog.records=@($catalog.records|Where-Object {$retainedIds.Contains($_.record_id)})
$catalog['derivation']='Question-only AI repairs and explicit deletions; original source files/splits/gold preserved in derivation-ledger';$catalog['parent_source_catalog_sha256']=(Get-FileHash "$old/source-catalog.json").Hash
Save "$output/source-catalog.json" $catalog
[IO.File]::WriteAllLines("$output/experiment-records.jsonl",@($rows|ForEach-Object {Check;$_|ConvertTo-Json -Depth 50 -Compress}),[Text.UTF8Encoding]::new($false))
Save "$output/input-provenance.json" @{records=@($inputProvenance)}
Save "$output/teacher-requests.json" @{batches=@($requests)};Save "$output/teacher-responses.json" @{batches=@($responses)}
Save "$output/derivation-ledger.json" @{contract='sezika/repair-deletion-ledger/v1';source_sha256=$plan.source_sha256;original_total=256;retained=$rows.Count;retained_unchanged=$unchanged;repaired=$repaired;deleted=$deleted;records=@($ledger);formal=$false;human_review='pending'}
function Asset([string]$Name){Check;$path=Join-Path $output $Name;return @{path=$path;bytes=(Get-Item $path).Length;sha256=(Get-FileHash $path).Hash}}
$train=@($rows|Where-Object split -CEQ train).Count;$dev=@($rows|Where-Object split -CEQ development).Count
$sourceAssets=@(foreach($pair in @(@('source_catalog','source-catalog.json'),@('teacher_requests','teacher-requests.json'),@('teacher_responses','teacher-responses.json'))){$asset=Asset $pair[1];$asset.kind=$pair[0];$asset})
$manifest=[ordered]@{schema_version=3;contract='sezika/teacher-labelled-experiment-data/v3';dataset_id='general-zh-en-repaired-20261009-v1';purpose='teacher_labelled_experiment';protocol='decision-v1';student_model_id='sezika/mmbert-base-independent-s4';formal=$false;human_review='pending';teacher_output_training_permitted=$false;user_authorized_experiment=$true;teacher_answer_exposed=$true;eligible_as_sealed_test=$false;total=$rows.Count;train=$train;development=$dev;records=(Asset 'experiment-records.jsonl');sources=$sourceAssets;teacher=$null;teacher_api=@{provider='sonnet_vip';model='gpt-6.1-sol';effort='max';identity='gateway-response-declaration-only';training_permission='user-authorized-experiment-only';license='unknown'};fixture_only=$false;split_rule='inherit-parent-family-split-no-reshuffle-with-explicit-deletions';split_rule_frozen_utc='2026-10-08T11:38:40.6891325Z';provenance=(Asset 'input-provenance.json');derivation=(Asset 'derivation-ledger.json')}
Save "$output/experiment-manifest.json" $manifest
$summary=@{utc=[DateTimeOffset]::UtcNow.ToString('o');source_total=256;problem_total=78;unchanged=$unchanged;repaired=$repaired;deleted=$deleted;retained=$rows.Count;train=$train;development=$dev;formal=$false;human_review='pending';ai_review_conditions_met=$true;native_contract_validation='pending_v3_support_in_next_session';strict1024_lengths='pending_new_version';model_quality_passed=$false;training_executed=$false;manifest_sha256=(Asset 'experiment-manifest.json').sha256;derivation_sha256=(Asset 'derivation-ledger.json').sha256}
Save "$root/final-summary.json" $summary
$md=[Text.StringBuilder]::new();[void]$md.AppendLine('# 修题与删除明细');[void]$md.AppendLine('');[void]$md.AppendLine("原256题：保留原题$unchanged，修订并盲审通过$repaired，删除$deleted；新版本$($rows.Count)题，train$train / development$dev。原始证据不删除；正式人审、模型质量与Native合同/长度验证仍待完成。")
foreach($entry in @($ledger|Where-Object action -CNE retained_unchanged)){Check;[void]$md.AppendLine('');[void]$md.AppendLine("## $($entry.record_id)（$($entry.split)）：$($entry.action)");[void]$md.AppendLine('');[void]$md.AppendLine($entry.reason)}
[IO.File]::WriteAllText("$root/repair-deletion-report.md",$md.ToString(),[Text.UTF8Encoding]::new($false))
$summary|ConvertTo-Json -Depth 5
