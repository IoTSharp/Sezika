#requires -Version 7.0
[CmdletBinding()]
param([switch]$FixtureOnly)
$ErrorActionPreference='Stop'
$task='D:\source\Sezika\.artifacts\teacher-expansion-20261008'
$clock=[Diagnostics.Stopwatch]::StartNew()
function Check { if($clock.Elapsed.TotalSeconds -ge 240 -or [IO.File]::Exists("$task/cancel.request")){throw 'Package cancelled/deadline'} }
function Save([string]$Path,$Value){Check; if([IO.File]::Exists($Path)){throw 'Never overwrite a package file'};[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 70 -Compress),[Text.UTF8Encoding]::new($false))}
function Asset([string]$Name){Check;$p=Join-Path $output $Name;return [ordered]@{path=$p;bytes=(Get-Item -LiteralPath $p).Length;sha256=(Get-FileHash -LiteralPath $p).Hash}}
$output=Join-Path $task $(if($FixtureOnly){'input-fixture'}else{'data'})
if(Test-Path -LiteralPath $output){throw 'Fresh package directory required'}
[void][IO.Directory]::CreateDirectory($output)
$native=Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 5
Save (Join-Path $output 'package-writer.identity.json') ($native|Select-Object ProcessId,CreationDate,CommandLine,ParentProcessId)
$rows=[Collections.Generic.List[object]]::new();$catalog=[Collections.Generic.List[object]]::new()
$requests=[Collections.Generic.List[object]]::new();$responses=[Collections.Generic.List[object]]::new();$labels=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
if($FixtureOnly){
  $template=Get-Content -Raw -LiteralPath 'D:\source\Sezika\.artifacts\expansion-engineering-20261008\v2-structural-template.json'|ConvertFrom-Json -AsHashtable
  Save (Join-Path $output 'teacher-requests.json') @{batches=@()};Save (Join-Path $output 'teacher-responses.json') @{batches=@()}
  $reqHash=(Asset 'teacher-requests.json').sha256;$respHash=(Asset 'teacher-responses.json').sha256
  for($i=0;$i -lt 256;$i++){
    Check;$id='fixture-'+$i;$split=if($i -lt 192){'train'}else{'development'}
    $request=($template.record_jsonl_row.request|ConvertTo-Json -Depth 30 -Compress)|ConvertFrom-Json -AsHashtable
    $request.questions[0].id=$id;$binding=[ordered]@{source_canonical_id=$id;response_id='fixture-only';request_sha256=$reqHash;response_sha256=$respHash;selected_candidate_id='delivery';target_index=1}
    $rows.Add([ordered]@{record_id=$id;family_id=$id;language='zh';split=$split;target_index=1;request=$request;teacher_binding=$binding})
    $catalog.Add([ordered]@{record_id=$id;source_canonical_id=$id;family_id=$id;language='zh';split=$split;source_kind='synthetic_contract_fixture'})
  }
  Save (Join-Path $output 'source-catalog.json') @{records=@($catalog)}
}else{
  . (Join-Path $task 'Test-LabelAuditPayload.ps1')
  $prepared=@(Get-Content -LiteralPath "$task/prepared-records.jsonl" -TotalCount 257|ForEach-Object {Check;$_|ConvertFrom-Json -AsHashtable})
  if($prepared.Count -ne 256){throw 'Prepared denominator differs'}
  $batchNames=@('tiny2','recovery01','recovery02','retry03','retry04','final05v2','final06v2')+@(7..32|ForEach-Object {'batch'+$_.ToString('00')})
  $blockers=[Collections.Generic.List[object]]::new()
  for($b=0;$b -lt 33;$b++){
    Check;$dir=Join-Path $task $batchNames[$b];$result=Get-Content -Raw -LiteralPath "$dir/result.json"|ConvertFrom-Json
    if($result.status -cne 'completed_and_validated' -or $result.http_status -ne 200 -or @($result.cleanup_errors).Count){throw 'Actual batch result is not complete'}
    $requestBytes=[IO.File]::ReadAllBytes("$dir/request.json");$responseBytes=[IO.File]::ReadAllBytes("$dir/response.json")
    $requestHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($requestBytes));$responseHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($responseBytes))
    $response=[Text.UTF8Encoding]::new($false,$true).GetString($responseBytes)|ConvertFrom-Json -Depth 80
    $texts=@($response.output|Where-Object type -CEQ message|ForEach-Object {$_.content}|Where-Object type -CEQ output_text)
    if($texts.Count -ne 1){throw 'Output text denominator'}
    $selected=@($prepared[$result.start_index..($result.start_index+$result.record_count-1)]|ForEach-Object {$_|ConvertTo-Json -Depth 60 -Compress|ConvertFrom-Json})
    $verified=@(Test-LabelAuditPayload -Json $texts[0].text -SelectedRows $selected)
    foreach($review in $verified){
      Check;if($review.answer_status -cne 'determinate' -or $review.clarity_status -cne 'clear' -or $review.question_revision_needed){$blockers.Add($review)}
      if(-not $labels.TryAdd($review.record_id,[ordered]@{review=$review;response_id=$response.id;request_sha256=$requestHash;response_sha256=$responseHash})){throw 'Repeated teacher row'}
    }
    $requests.Add(@{sha256=$requestHash;bytes_base64=[Convert]::ToBase64String($requestBytes)})
    $responses.Add(@{response_id=$response.id;request_sha256=$requestHash;sha256=$responseHash;bytes_base64=[Convert]::ToBase64String($responseBytes)})
    Write-Output "Archived actual batch $($b+1)/33"
  }
  if($blockers.Count){Save (Join-Path $output 'not-eligible-reviews.json') @{records=@($blockers);training_started=$false};throw 'Unclear/non-determinate/revision-needed rows retained; do not force labels or train'}
  if($labels.Count -ne 256){throw 'All256 actual answers required'}
  Save (Join-Path $output 'teacher-requests.json') @{batches=@($requests)};Save (Join-Path $output 'teacher-responses.json') @{batches=@($responses)}
  for($i=0;$i -lt 256;$i++){
    Check;$row=$prepared[$i];$label=$labels[$row.record_id];$review=$label.review
    $binding=[ordered]@{source_canonical_id=$row.source_canonical_id;response_id=$label.response_id;request_sha256=$label.request_sha256;response_sha256=$label.response_sha256;selected_candidate_id=$review.suggested_correct_id;target_index=$review.suggested_correct_index}
    $rows.Add([ordered]@{record_id=$row.record_id;family_id=$row.family_id;language=$row.language;split=$row.split;target_index=$review.suggested_correct_index;request=$row.request;teacher_binding=$binding})
  }
  [IO.File]::Copy("$task/source-catalog.json","$output/source-catalog.json",$false)
}
$lines=[Collections.Generic.List[string]]::new();$provenance=[Collections.Generic.List[object]]::new()
for($i=0;$i -lt 256;$i++){
  Check;$row=$rows[$i];$q=$row.request.questions[0];$ids=if($q.type -ceq 'choice'){@($q.candidates.id)}else{@('false','true')}
  $lines.Add(($row|ConvertTo-Json -Depth 60 -Compress))
  $provenance.Add([ordered]@{record_id=$row.record_id;family_id=$row.family_id;split=$row.split;target_index=$row.target_index;candidate_ids=$ids;selected_candidate_id=$row.teacher_binding.selected_candidate_id;teacher_binding=$row.teacher_binding})
}
[IO.File]::WriteAllLines("$output/experiment-records.jsonl",$lines,[Text.UTF8Encoding]::new($false))
Save (Join-Path $output 'input-provenance.json') @{records=@($provenance)}
$sources=@(foreach($pair in @(@('source_catalog','source-catalog.json'),@('teacher_requests','teacher-requests.json'),@('teacher_responses','teacher-responses.json'))){$a=Asset $pair[1];$a.kind=$pair[0];$a})
$teacherApi=if($FixtureOnly){$null}else{@{provider='sonnet_vip';model='gpt-6.1-sol';effort='max';identity='gateway-response-declaration-only';training_permission='user-authorized-experiment-only';license='unknown'}}
$frozenUtc=if($FixtureOnly){[DateTime]::UtcNow.ToString('o')}else{
  $selection=Get-Content -Raw -LiteralPath "$task/model-selection.json"|ConvertFrom-Json -DateKind String
  $freeze=Get-Content -Raw -LiteralPath "$task/source-split-freeze.json"|ConvertFrom-Json -DateKind String
  if((Get-FileHash -LiteralPath "$task/prepared-records.jsonl").Hash -cne $selection.source_sha256 -or $freeze.source_sha256 -cne $selection.source_sha256 -or $freeze.catalog_sha256 -cne $selection.catalog_sha256 -or -not $freeze.metadata_added_without_changing_request_split_target -or [DateTimeOffset]::Parse($freeze.source_split_frozen_utc) -gt [DateTimeOffset]::Parse($selection.api_phase_started_utc)){throw 'Actual pre-API split freeze identity/time differs'}
  $freeze.source_split_frozen_utc
}
$manifest=[ordered]@{schema_version=2;contract='sezika/teacher-labelled-experiment-data/v2';dataset_id='general-zh-en-teacher-expansion-256-v1';purpose='teacher_labelled_experiment';protocol='decision-v1';student_model_id='sezika/mmbert-base-independent-s4';formal=$false;human_review='pending';teacher_output_training_permitted=$false;user_authorized_experiment=$true;teacher_answer_exposed=$true;eligible_as_sealed_test=$false;total=256;train=192;development=64;records=(Asset 'experiment-records.jsonl');sources=$sources;teacher=$null;teacher_api=$teacherApi;fixture_only=[bool]$FixtureOnly;split_rule='source-family-frozen-192-64-v1';split_rule_frozen_utc=$frozenUtc;provenance=(Asset 'input-provenance.json')}
Save (Join-Path $output 'experiment-manifest.json') $manifest
Save (Join-Path $output 'package-summary.json') @{fixture_only=[bool]$FixtureOnly;records=256;train=192;development=64;actual_api_batches=$requests.Count;manifest_sha256=(Asset 'experiment-manifest.json').sha256;elapsed_seconds=$clock.Elapsed.TotalSeconds}
Write-Output "Package prepared: fixture=$FixtureOnly; manifest=$((Asset 'experiment-manifest.json').sha256)"
