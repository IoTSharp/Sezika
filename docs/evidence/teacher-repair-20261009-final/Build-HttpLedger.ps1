#requires -Version 7.0
$ErrorActionPreference='Stop';$root=$PSScriptRoot;$watch=[Diagnostics.Stopwatch]::StartNew()
$plan=Get-Content "$root/plan.json" -Raw|ConvertFrom-Json
$start=[DateTimeOffset]::Parse($plan.api_phase_started_utc);$deadline=$start.AddSeconds($plan.maximum_phase_seconds)
$rows=[Collections.Generic.List[object]]::new();$attempts=0;$sent=0;$success=0;$valid=0;$logical=0
foreach($stage in @('repair','review')){
 $schedule=Get-Content "$root/$stage-schedule.json" -Raw|ConvertFrom-Json
 if($schedule.batches.Count -gt 14){throw 'Schedule cap14'}
 foreach($job in $schedule.batches){
  if($watch.Elapsed.TotalSeconds -gt 120 -or (Test-Path "$root/cancel.request")){throw 'Ledger timeout/cancel'}
  $logical++;$dir="$root/$($job.batch)";$r=Get-Content "$dir/result.json" -Raw|ConvertFrom-Json;$t=Get-Content "$dir/transport-result.json" -Raw|ConvertFrom-Json
  if($t.attempts.Count -gt 2){throw 'Attempt cap2'}
  if($r.status -ceq 'completed_and_validated'){$valid++}
  foreach($a in $t.attempts){
   $attempts++;if($a.sent){$sent++};if($a.http_status -eq 200){$success++}
   if([DateTimeOffset]::Parse($a.started_utc) -lt $start -or [DateTimeOffset]::Parse($a.finished_utc) -gt $deadline){throw 'Outside frozen window'}
   $rows.Add(@{stage=$stage;batch=$job.batch;attempt=$a;result_status=$r.status;request_sha256=(Get-FileHash "$dir/request.json").Hash})
  }
 }
}
if($logical -gt $plan.maximum_logical_batches -or $sent -gt $plan.maximum_http_attempts){throw 'Frozen scope exceeded'}
$report=@{utc=[DateTimeOffset]::UtcNow.ToString('o');logical_batches=$logical;transport_attempts=$attempts;http_requests_sent=$sent;http200=$success;validated_batches=$valid;maximum_concurrency=2;phase_started=$start.ToString('o');frozen_deadline=$deadline.ToString('o');old_33_successful_batches_reused_not_resent=$true;failed_upstream_billing='unknown_if_any';attempts=@($rows)}
if(Test-Path "$root/http-ledger.json"){throw 'Preserve ledger'}
[IO.File]::WriteAllText("$root/http-ledger.json",($report|ConvertTo-Json -Depth 15));$report|Select-Object logical_batches,transport_attempts,http_requests_sent,http200,validated_batches|ConvertTo-Json
