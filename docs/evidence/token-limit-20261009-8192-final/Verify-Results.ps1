$ErrorActionPreference='Stop';$task=$PSScriptRoot;$clock=[Diagnostics.Stopwatch]::StartNew()
function Check {if($clock.Elapsed.TotalSeconds -ge 30 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'verification bound'}}
$expected=@{
 'retrieved/output/run-24576/expanded-training-report.json'='C6F3AE942FB90CA842FD49AFF7AE6505EB02A71128264EDC2EF08BC323A48905'
 'retrieved/output/run-24576/expanded-head.asset'='4205457556736AB07799B462D42C377710E765CB14EDB9AAE508C1558C5E9D64'
 'retrieved/output/run-24576/expanded-training.checkpoint'='4709E847A135265F71C092BB18C66CF291DE87D90970CD8932ADD6FBC7F4264F'
 'retrieved/package/model.json'='B8FB9CECC0912F034E0FEA184E8AA78871D11533093DCCF4F5AAD74646007551'
}
foreach($path in $expected.Keys){Check;if((Get-FileHash -LiteralPath (Join-Path $task $path)).Hash -cne $expected[$path]){throw 'result hash mismatch'}}
$r=Get-Content -Raw -LiteralPath (Join-Path $task 'retrieved/output/run-24576/expanded-training-report.json')|ConvertFrom-Json
$groups=@(foreach($group in @(@{split='train';variant='original';count=162},@{split='development';variant='original';count=53},@{split='train';variant='fixed-left1';count=16})){
 Check;$rows=@($r.loaded_observations.rows|Where-Object{$_.split -ceq $group.split -and $_.variant -ceq $group.variant})
 if($rows.Count -ne $group.count){throw 'actual row denominator differs'}
 $loss=($rows|Measure-Object gpu_teacher_ce -Average).Average
 [pscustomobject]@{split=$group.split;variant=$group.variant;total=$rows.Count;agreement=@($rows|Where-Object agrees_with_teacher).Count;mean_gpu_ce=$loss}
})
if($r.completed_steps -ne 24576 -or $r.max_tokens -ne 8192 -or $r.encoder_forwards_completed -ne 231 -or $r.loaded_observations.rows.Count -ne 231 -or $r.resources.snapshot_before_context_dispose.owned_bytes -ne 0 -or $r.resources.snapshot_before_context_dispose.release_failure_count -ne 0){throw 'training completion contract differs'}
$visits=@($r.sample_visits.per_sample);if($visits.Count -eq 0){$visits=@($r.sample_visits.counts)}
$visitProperty=@($r.sample_visits.PSObject.Properties|Where-Object{$_.Value -is [Array] -and $_.Value.Count -eq 162 -and $_.Value[0].PSObject.Properties.Name -contains 'committed_updates'}|Select-Object -First 1)
if($visitProperty.Count -ne 1){throw 'actual visit map missing'};$visits=@($visitProperty[0].Value)
if(($visits|Measure-Object committed_updates -Sum).Sum -ne 24576 -or ($visits|Measure-Object committed_updates -Minimum).Minimum -ne 151 -or ($visits|Measure-Object committed_updates -Maximum).Maximum -ne 152){throw 'visit counts differ'}
$preserved=Get-Content -Raw -LiteralPath (Join-Path $task 'task-identity.json')|ConvertFrom-Json
$checks=@(foreach($file in $preserved.paused){Check;if($file.path -ceq 'ROADMAP.md'){continue};$actual=(Get-FileHash -LiteralPath (Join-Path 'D:\source\Sezika' $file.path)).Hash;if($actual -cne $file.sha256){throw 'paused source changed'};@{path=$file.path;sha256=$actual;unchanged=$true}})
$immutable='D:\source\Sezika\docs\evidence\teacher-repair-20261009-final\data-v3\experiment-manifest.json'
if((Get-FileHash -LiteralPath $immutable).Hash -cne $preserved.data_manifest_sha256){throw 'immutable215 data changed'}
$summary=@{utc=[DateTime]::UtcNow.ToString('o');status='training_complete_independent_validation_failed_gpu_reset_required';hashes=$expected;training_steps=24576;training_forwards=231;train=162;development=53;max_tokens=8192;groups=$groups;selected_id_changed=@($r.loaded_observations.position_comparisons|Where-Object selected_id_changed).Count;visit_map_count=162;visit_total=24576;visit_minimum=151;visit_maximum=152;full_order_cycles=151;final_cycle_prefix=114;visit_semantics=$r.sample_visits.semantics;training_resources=$r.resources;training_elapsed_seconds=$r.elapsed_seconds;independent_validation_report_present=[IO.File]::Exists((Join-Path $task 'retrieved/output/expanded-validation.json'));original32_stage_present=[IO.Directory]::Exists((Join-Path $task 'retrieved/.artifacts/stage-regress'));acceptance=$r.acceptance;paused=$checks;limitations=@('initial8192 identity reader was carried from2048 bootstrap metadata; not a new OS observation','short-lived guardian unknown histories retained; later absent does not reconstruct argv','GPU reset required; validation last log starts211/215, not proof of211 completed forwards; device release failed','NVML final compute apps N/A; no claim GPU final context cleanup success','CLI progress03 observer timed out; final validation actual exit1 independently retained')}
[IO.File]::WriteAllText((Join-Path $task 'independent-artifact-verification.json'),($summary|ConvertTo-Json -Depth 7),[Text.UTF8Encoding]::new($false))
$groups|ConvertTo-Json -Depth 3
Write-Output 'bytes_rows_visits_paused_data_verified=true'
