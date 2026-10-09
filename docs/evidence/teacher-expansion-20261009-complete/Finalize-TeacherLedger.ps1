#requires -Version 7.0
param([switch]$Probe,[switch]$Final16)
$ErrorActionPreference='Stop';$root=$PSScriptRoot;$watch=[Diagnostics.Stopwatch]::StartNew()
function Check {if($watch.Elapsed.TotalSeconds -gt 120 -or (Test-Path -LiteralPath "$root/cancel.request")){throw 'Ledger120s/cancellation'}}
. "$root/Test-LabelAuditPayload.ps1"
$prepared=@(Get-Content -LiteralPath "$root/prepared-records.jsonl" -TotalCount 257|ForEach-Object {$_|ConvertFrom-Json})
if($prepared.Count -ne 256 -or (Get-FileHash "$root/prepared-records.jsonl").Hash -cne '17946E4ED202F45832C4E0E90B62F2CD7AAEDED096896E97268CCDB82DF3C937'){throw 'Frozen source mismatch'}
$names=@('tiny2')+@(1..32|ForEach-Object {'batch'+$_.ToString('00')})+@('batch03v2','batch04v2','recovery01','recovery02','retry03','retry04','retry05','retry06')
if($Final16){$names+=@('final05','final06','final05v2','final06v2')}
if($Probe){$names=@('tiny2')}
$entries=[Collections.Generic.List[object]]::new();$ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$http=0;$success=0;$preHttp=0;$guardianFailures=0
for($i=0;$i -lt $names.Count -and $i -lt 45;$i++){
 Check;$name=$names[$i];$dir=Join-Path $root $name
 $result=if(Test-Path "$dir/result.json"){Get-Content -Raw "$dir/result.json"|ConvertFrom-Json}else{$null}
 $provenance=if(Test-Path "$dir/provenance.json"){Get-Content -Raw "$dir/provenance.json"|ConvertFrom-Json}else{$null}
 $transport=if(Test-Path "$dir/transport-result.json"){Get-Content -Raw "$dir/transport-result.json"|ConvertFrom-Json}else{$null}
 $requestExists=Test-Path "$dir/request.json"
 # Conservatively count persisted requests, including interrupted upstream outcomes.
 if($requestExists){$http++}elseif($result){$preHttp++}
 $state=if($result){$result.status}elseif($requestExists){'interrupted_upstream_unknown'}else{'not_requested'}
 $requestHash=if($requestExists){(Get-FileHash "$dir/request.json").Hash}else{$null}
 $responseHash=if(Test-Path "$dir/response.json"){(Get-FileHash "$dir/response.json").Hash}else{$null}
 if($provenance -and $requestHash -cne $provenance.request_sha256){throw "Request hash differs: $name"}
 if($state -ceq 'completed_and_validated'){
  if($result.http_status -ne 200 -or @($result.cleanup_errors).Count){throw "Bad successful receipt: $name"}
  $response=Get-Content -Raw "$dir/response.json"|ConvertFrom-Json -Depth 80
  if($response.status -cne 'completed' -or $response.model -cne 'gpt-6.1-sol'){throw 'Response identity/status'}
  $texts=@($response.output|Where-Object type -CEQ message|ForEach-Object {$_.content}|Where-Object type -CEQ output_text)
  if($texts.Count -ne 1){throw 'Response text denominator'}
  $selected=@($prepared[$result.start_index..($result.start_index+$result.record_count-1)])
  $reviews=@(Test-LabelAuditPayload -Json $texts[0].text -SelectedRows $selected)
  $saved=Get-Content -Raw "$dir/validated-reviews.json"|ConvertFrom-Json
  if(($saved|ConvertTo-Json -Depth 50 -Compress) -cne ($reviews|ConvertTo-Json -Depth 50 -Compress)){throw 'Saved review differs from raw response'}
  foreach($review in $reviews){Check;if(-not $ids.Add($review.record_id)){throw 'Successful record repeated'}}
  $success++
 }
 $entries.Add([ordered]@{batch=$name;status=$state;request_persisted=$requestExists;count=$result.record_count;http_status=$result.http_status;transport_request_sent=$transport.request_sent;request_sha256=$requestHash;response_sha256=$responseHash})
 Write-Output "Ledger verified $($i+1)/$($names.Count): $name $state"
}
if($Probe){Write-Output 'Tiny ledger probe passed';exit 0}
if(-not(Test-Path "$root/continuous-teacher-result.json")){throw 'Queue must finish first'}
if($Final16 -and -not(Test-Path "$root/final16-v2-queue-result.json")){throw 'Final16 queue must finish first'}
$maximumHttp=if($Final16){40}else{39}
if($http -gt $maximumHttp -or $success -gt 33){throw 'Request budget exceeded'}
$missing=@($prepared|Where-Object {-not $ids.Contains($_.record_id)}|Select-Object record_id,family_id,split,language)
$output=if($Final16){"$root/final16-request-ledger.json"}else{"$root/final-request-ledger.json"};if(Test-Path $output){throw 'Never overwrite ledger'}
[IO.File]::WriteAllText($output,(@{utc=[DateTimeOffset]::UtcNow.ToString('o');maximum_total_http_attempts=$maximumHttp;conservative_http_attempts=$http;recovery_http_attempts=if($Final16){35}else{$http-3};final16_http_attempts=if($Final16){$http-38}else{0};successful_requests=$success;pre_http_failures=$preHttp;reviewed=$ids.Count;not_reviewed=256-$ids.Count;missing_records=$missing;entries=@($entries);count_rule='Persisted request counts conservatively as one attempted HTTP, including interruptions; GET connectivity probe listed separately';connectivity_get_requests=1;upstream_billing_for_failed_or_interrupted_requests='unknown';duplicate_successful_records=0;training_executed=$false}|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
Write-Output "Final: HTTP attempts $http/$maximumHttp; successes $success/33; reviewed $($ids.Count)/256"
