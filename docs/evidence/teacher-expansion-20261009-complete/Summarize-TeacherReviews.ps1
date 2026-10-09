#requires -Version 7.0
param([switch]$Final,[switch]$Final16)
$ErrorActionPreference='Stop';$root=$PSScriptRoot;$clock=[Diagnostics.Stopwatch]::StartNew()
$names=@('tiny2','recovery01','recovery02','retry03','retry04','retry05','retry06')+@(7..32|ForEach-Object {'batch'+$_.ToString('00')})
if($Final16){$names=@('tiny2','recovery01','recovery02','retry03','retry04','final05v2','final06v2')+@(7..32|ForEach-Object {'batch'+$_.ToString('00')})}
$all=[Collections.Generic.List[object]]::new();$batches=[Collections.Generic.List[object]]::new();$ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
for($i=0;$i -lt 33;$i++) {
  if($clock.Elapsed.TotalSeconds -gt 45){throw 'Summary45s bound'}
  $name=$names[$i];$dir=Join-Path $root $name
  if(-not(Test-Path -LiteralPath "$dir/result.json")){$batches.Add(@{batch=$name;status='not_completed'});continue}
  $result=Get-Content -Raw -LiteralPath "$dir/result.json"|ConvertFrom-Json
  $batches.Add(@{batch=$name;status=$result.status;http_status=$result.http_status;count=$result.record_count})
  if($result.status -cne 'completed_and_validated'){continue}
  $reviews=@(Get-Content -Raw -LiteralPath "$dir/validated-reviews.json"|ConvertFrom-Json)
  if($reviews.Count -ne $result.record_count){throw 'Review denominator mismatch'}
  foreach($review in $reviews){if(-not $ids.Add($review.record_id)){throw 'Repeated successful teacher row'};$all.Add($review)}
}
$blockers=@($all|Where-Object {$_.answer_status -cne 'determinate' -or $_.clarity_status -cne 'clear' -or $_.question_revision_needed})
$summary=[ordered]@{utc=[DateTimeOffset]::UtcNow.ToString('o');frozen_source_records=256;reviewed=$all.Count;not_reviewed=256-$all.Count;eligible=$all.Count-$blockers.Count;quality_blockers=$blockers.Count;changed_label=@($all|Where-Object change_original_label).Count;training_ready=($all.Count -eq 256 -and $blockers.Count -eq 0);human_review='pending';formal=$false;training_executed=$false;batches=@($batches);blockers=$blockers}
$path=Join-Path $root $(if($Final16){'final16-review-summary.json'}elseif($Final){'teacher-review-final-summary.json'}else{'teacher-review-progress.json'})
if(($Final -or $Final16) -and (Test-Path -LiteralPath $path)){throw 'Final evidence already exists'}
[IO.File]::WriteAllText($path,($summary|ConvertTo-Json -Depth 40),[Text.UTF8Encoding]::new($false))
[pscustomobject]$summary|Select-Object utc,reviewed,not_reviewed,eligible,quality_blockers,changed_label,training_ready|ConvertTo-Json
