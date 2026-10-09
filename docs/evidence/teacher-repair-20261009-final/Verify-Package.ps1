#requires -Version 7.0
param([switch]$Probe)
$ErrorActionPreference='Stop';$root=$PSScriptRoot;$clock=[Diagnostics.Stopwatch]::StartNew()
function Check {if($clock.Elapsed.TotalSeconds -gt 120 -or (Test-Path "$root/cancel.request")){throw 'Verification120s/cancel'}}
$plan=Get-Content "$root/plan.json" -Raw|ConvertFrom-Json
$source=@(Get-Content $plan.source -TotalCount 257|ForEach-Object {Check;$_|ConvertFrom-Json -Depth 50})
if($source.Count -ne 256 -or (Get-FileHash $plan.source).Hash -cne $plan.source_sha256){throw 'Parent identity'}
if($Probe){$row=$source[0];if(-not $row.record_id -or -not $row.request.state.text){throw 'Tiny source'};Write-Output 'Verification tiny source passed';exit 0}
$data="$root/data-v3";$m=Get-Content "$data/experiment-manifest.json" -Raw|ConvertFrom-Json
$rows=@(Get-Content "$data/experiment-records.jsonl" -TotalCount 257|ForEach-Object {Check;$_|ConvertFrom-Json -Depth 50})
$ledger=Get-Content "$data/derivation-ledger.json" -Raw|ConvertFrom-Json -Depth 60
$catalog=Get-Content "$data/source-catalog.json" -Raw|ConvertFrom-Json -Depth 30
$provenance=Get-Content "$data/input-provenance.json" -Raw|ConvertFrom-Json -Depth 20
if($rows.Count -ne $m.total -or $ledger.records.Count -ne 256 -or $catalog.records.Count -ne $m.total -or $provenance.records.Count -ne $m.total){throw 'Denominator mismatch'}
$original=@{};foreach($row in $source){Check;$original.Add($row.record_id,$row)}
$ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$families=@{}
foreach($row in $rows){
 Check;if(-not $ids.Add($row.record_id)){throw 'Duplicate row'};$old=$original[$row.record_id]
 if($row.request.state.text -cne $old.request.state.text -or $row.split -cne $old.split -or $row.family_id -cne $old.family_id -or $row.language -cne $old.language){throw 'Immutable source changed'}
 if($families.ContainsKey($row.family_id) -and $families[$row.family_id] -cne $row.split){throw 'Family split leak'};$families[$row.family_id]=$row.split
 $q=$row.request.questions[0];$candidateIds=if($q.type -ceq 'choice'){@($q.candidates.id)}else{@('false','true')}
 if($row.target_index -lt 0 -or $row.target_index -ge $candidateIds.Count -or $row.teacher_binding.selected_candidate_id -cne $candidateIds[$row.target_index]){throw 'Target mapping'}
}
foreach($asset in @($m.records,$m.provenance,$m.derivation)+@($m.sources)){Check;$file=Join-Path $data ([IO.Path]::GetFileName($asset.path));if((Get-FileHash $file).Hash -cne $asset.sha256 -or (Get-Item $file).Length -ne $asset.bytes){throw 'Asset hash/bytes mismatch'}}
$train=@($rows|Where-Object split -CEQ train).Count;$dev=@($rows|Where-Object split -CEQ development).Count
$unchanged=@($ledger.records|Where-Object action -CEQ retained_unchanged).Count;$repaired=@($ledger.records|Where-Object action -CEQ repaired_and_reaudited).Count;$deleted=@($ledger.records|Where-Object action -CEQ deleted).Count
if($unchanged -ne 178 -or $repaired+$deleted -ne 78 -or $train -ne $m.train -or $dev -ne $m.development -or $train+$dev -ne $rows.Count){throw 'Split/action totals mismatch'}
$requests=Get-Content "$data/teacher-requests.json" -Raw|ConvertFrom-Json -Depth 10
$responses=Get-Content "$data/teacher-responses.json" -Raw|ConvertFrom-Json -Depth 10
if($requests.batches.Count -gt 47 -or $responses.batches.Count -ne $requests.batches.Count){throw 'Teacher binding cap'}
foreach($item in $requests.batches){Check;if([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Convert]::FromBase64String($item.bytes_base64))) -cne $item.sha256){throw 'Wire request hash'}}
foreach($item in $responses.batches){Check;if([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Convert]::FromBase64String($item.bytes_base64))) -cne $item.sha256){throw 'Wire response hash'}}
$report=@{utc=[DateTimeOffset]::UtcNow.ToString('o');status='passed_static_package_checks';retained=$rows.Count;train=$train;development=$dev;unchanged=$unchanged;repaired=$repaired;deleted=$deleted;catalog_rows=$catalog.records.Count;bound_review_batches=$requests.batches.Count;immutable_state_split_family_language=$true;formal_human_review=$false;strict1024='pending_native_renderer';native_v3_contract='pending';gpu_training_started=$false}
if(Test-Path "$root/package-verification.json"){throw 'Preserve previous verification'}
[IO.File]::WriteAllText("$root/package-verification.json",($report|ConvertTo-Json -Depth 10));$report|ConvertTo-Json -Depth 6
