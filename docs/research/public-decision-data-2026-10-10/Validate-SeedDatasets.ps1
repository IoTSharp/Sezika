param([string]$OutputRoot='C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10',[string]$ResearchRoot=$PSScriptRoot)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required'}
$watch=[Diagnostics.Stopwatch]::StartNew()
$calRoot=Join-Path $OutputRoot 'raw\github\little-g-ai\Sys1Cal-v1\datasets\generated'
$names=@('v0.1.0_tiny_cleanvars_binary.jsonl','v0.1.0_tiny_cleanvars_parallel_primitives.jsonl')
$releases=[Collections.Generic.List[object]]::new();$goldById=@{};$instanceIds=@{};$allLatents=@{};$matchedPairs=0;$consistencyErrors=[Collections.Generic.List[string]]::new()
for($f=0;$f -lt $names.Count;$f++){
 $path=Join-Path $calRoot $names[$f];$lines=@(Get-Content -LiteralPath $path);$latents=@{};$ids=@{};$normalizationErrors=0;$queryErrors=0
 for($i=0;$i -lt $lines.Count;$i++){
  if($watch.Elapsed.TotalSeconds -ge 20){throw 'Seed validation deadline'}
  $row=$lines[$i]|ConvertFrom-Json
  $ids[$row.instance_id]=$true;$latents[$row.latent_instance_id]=$true
  $instanceIds[$row.instance_id]=$true;$allLatents[$row.latent_instance_id]=$true
  $probabilities=@($row.gold_distribution.PSObject.Properties|ForEach-Object {[double]$_.Value})
  $sum=($probabilities|Measure-Object -Sum).Sum
  if([Math]::Abs($sum-1) -gt 1e-10 -or @($probabilities|Where-Object {$_ -lt 0 -or $_ -gt 1}).Count -gt 0){$normalizationErrors++}
  # Official parallel_primitives.py lines 55-57 define the first source
  # outcome as positive. It is not always named A.
  $p=if($f -eq 0){[double]$row.gold_distribution.PSObject.Properties[$row.outcomes[0]].Value}else{[double]$row.gold_distribution.True}
  $pairKey=if($f -eq 0){$row.instance_id}else{$row.generator_metadata.parallel_primitives.source_instance_id}
  if($f -eq 0){$goldById[$pairKey]=$p}
  elseif($goldById.ContainsKey($pairKey)){$matchedPairs++;if([Math]::Abs($goldById[$pairKey]-$p) -gt 1e-10){$consistencyErrors.Add($pairKey)}}
  else{$consistencyErrors.Add('Missing binary counterpart '+$pairKey)}
  if($f -eq 1 -and(@($row.queries.PSObject.Properties.Name|Sort-Object) -join ',') -cne 'choice,noul,score'){$queryErrors++}
 }
 $releases.Add([pscustomobject]@{file=$names[$f];sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant();physical_rows=$lines.Count;unique_instance_ids=$ids.Count;unique_latent_instance_ids=$latents.Count;gold_normalization_errors=$normalizationErrors;parallel_query_errors=$queryErrors})
}
$manifestPath=Join-Path $OutputRoot 'raw\hf\hotchpotch\s1mb-dataset\all-data-manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
$missing=[Collections.Generic.List[string]]::new();$dataFiles=0
$evaluation=@($manifest.evaluation)
for($i=0;$i -lt $evaluation.Count;$i++){
 if($watch.Elapsed.TotalSeconds -ge 20){throw 'S1MB manifest validation deadline'}
 $files=@($evaluation[$i].data_files)
 for($f=0;$f -lt $files.Count;$f++){
  $dataFiles++;$path=Join-Path $OutputRoot ('raw\hf\hotchpotch\s1mb-dataset\'+$files[$f])
  if(-not(Test-Path -LiteralPath $path)){$missing.Add($files[$f])}
 }
}
$report=[pscustomobject]@{
 verified_utc=[DateTime]::UtcNow.ToString('o');
 sys1cal=[pscustomobject]@{releases=$releases;matched_source_instance_pairs=$matchedPairs;cross_primitive_probability_consistency_errors=$consistencyErrors.Count;positive_event_mapping='Official parallel_primitives.py: outcomes[0] -> True; source_instance_id binds paired rows';unique_instance_ids_across_both_releases=$instanceIds.Count;shared_latent_instance_ids=$allLatents.Count;gold_construction_recomputed=$false;model_calibration_validated=$false;use='Separate probability-recovery evaluation; preserve latent families'};
 s1mb=[pscustomobject]@{manifest_sha256=(Get-FileHash -LiteralPath $manifestPath).Hash.ToLowerInvariant();train_subsets=@($manifest.train).Count;evaluation_subsets=$evaluation.Count;cases=($evaluation|Measure-Object cases -Sum).Sum;decisions=($evaluation|Measure-Object decisions -Sum).Sum;declared_data_files=$dataFiles;missing_declared_data_files=$missing.Count;missing=$missing;all_repository_documentation_complete='See source-completeness.json';use='Independent evaluation; no training admission'};
 formal_training_admitted=$false
}
$report|ConvertTo-Json -Depth 9|Set-Content -LiteralPath (Join-Path $ResearchRoot 'seed-dataset-validation.json') -Encoding utf8
$report|ConvertTo-Json -Depth 6 -Compress
