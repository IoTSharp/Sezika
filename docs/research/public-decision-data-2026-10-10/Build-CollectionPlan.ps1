param([string]$OutputRoot='C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10',[string]$ResearchRoot=$PSScriptRoot)
$ErrorActionPreference='Stop'
$watch=[Diagnostics.Stopwatch]::StartNew()
$seeds=@(Get-Content -LiteralPath (Join-Path $ResearchRoot 'source-seeds.json') -Raw|ConvertFrom-Json)
$catalog=[Collections.Generic.List[object]]::new()
$assets=[Collections.Generic.List[object]]::new()
$observed=@{}
for ($i=0;$i -lt $seeds.Count;$i++) {
 if ($watch.Elapsed.TotalSeconds -ge 60) {throw 'Plan build timeout'}
 $seed=$seeds[$i]
 $file=Join-Path $OutputRoot ('metadata/hf/'+$seed.id.Replace('/','--')+'.json')
 if (-not (Test-Path -LiteralPath $file)) {
  $catalog.Add([pscustomobject]@{id=$seed.id;platform='Hugging Face';task=$seed.task;priority=$seed.priority;intended_use=$seed.intended_use;verification='metadata_unavailable_401_or_missing';license=$null;revision=$null;source_url='https://huggingface.co/datasets/'+$seed.id;training_admission='pending';redistribution='pending'})
  continue
 }
 $meta=Get-Content -LiteralPath $file -Raw|ConvertFrom-Json
 if ($observed.ContainsKey($meta.id)) {continue}
 $observed[$meta.id]=$true
 $splits=@($meta.cardData.dataset_info|ForEach-Object { $config=$_.config_name; $_.splits|ForEach-Object {[pscustomobject]@{config=$config;split=$_.name;rows=$_.num_examples}} })
 $language=@($meta.cardData.language)
 $catalog.Add([pscustomobject]@{id=$meta.id;requested_id=$seed.id;platform='Hugging Face';task=$seed.task;priority=$seed.priority;intended_use=$seed.intended_use;verification='metadata_frozen';license=$meta.cardData.license;license_tags=@($meta.tags|Where-Object {$_ -like 'license:*'});languages=$language;revision=$meta.sha;files=$meta.siblings.Count;repository_bytes=($meta.siblings|Measure-Object size -Sum).Sum;source_url='https://huggingface.co/datasets/'+$meta.id;metadata_path=$file;metadata_sha256=(Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant();reported_splits=$splits;training_admission='pending_original_split_and_license_review';redistribution='pending_upstream_rights';collection='all_structured_data_and_documentation_at_pinned_revision'})
 # Every selected repository is collected; source aliases and remote code are
 # retained as evidence. Python dataset loaders are never executed.
 $siblings=@($meta.siblings)
 for ($f=0;$f -lt $siblings.Count;$f++) {
  if ($watch.Elapsed.TotalSeconds -ge 60) {throw 'Sibling plan deadline'}
  $s=$siblings[$f]
  $name=[string]$s.rfilename
  if ($name -notmatch '(?i)(\.(jsonl?|parquet|arrow|csv|tsv|txt|gz|zip|zst|bz2|xz|7z|tar|jsonlines|ndjson|tfrecord|md|ya?ml|py|cff)$|(^|/)(LICENSE|NOTICE|COPYING)(\.[^/]+)?$)') {continue}
  # Do not collect response/prediction artifacts as question ground truth.
  if ($name -match '(?i)(^|/)(predictions|results|evaluation_outputs)/') {continue}
  $split=if ($name -match '(?i)(^|[/_.-])train(ing)?([/_.-]|$)') {'source-train'} elseif ($name -match '(?i)(test|eval)') {'source-test-evaluation'} elseif ($name -match '(?i)(dev|valid)') {'source-development'} else {'unclassified-review-required'}
  $assets.Add([pscustomobject]@{name=$meta.id+'/'+$name;kind='hf-source-file';dataset=$meta.id;revision=$meta.sha;split=$split;url='https://huggingface.co/datasets/'+$meta.id+'/resolve/'+$meta.sha+'/'+$name;path='raw/hf/'+$meta.id+'/'+$name;expected_sha256=$s.lfs.sha256;expected_bytes=$s.size})
 }
}
$g=Get-Content -LiteralPath (Join-Path $OutputRoot 'metadata/github/sys1cal-tree.json') -Raw|ConvertFrom-Json
$gs=@($g.tree|Where-Object {$_.path -in @('README.md','LICENSE','hf/Sys1Cal-v1/README.md','datasets/generated/v0.1.0_tiny_cleanvars_binary.jsonl','datasets/generated/v0.1.0_tiny_cleanvars_parallel_primitives.jsonl')})
for ($i=0;$i -lt $gs.Count;$i++) {
 $s=$gs[$i]
 $assets.Add([pscustomobject]@{name='Sys1Cal/'+$s.path;kind='github-source-file';dataset='little-g-ai/Sys1Cal-v1';revision=$g.sha;split='evaluation-calibration-only';url='https://raw.githubusercontent.com/little-g-ai/Sys1Cal-v1/'+$g.sha+'/'+$s.path;path='raw/github/little-g-ai/Sys1Cal-v1/'+$s.path;expected_bytes=$s.size})
}
$g=Get-Content -LiteralPath (Join-Path $OutputRoot 'metadata/github/s1mb-tree.json') -Raw|ConvertFrom-Json
$gs=@($g.tree|Where-Object {$_.path -in @('README.md','LICENSE','evaluator/dataset-source.json')})
for ($i=0;$i -lt $gs.Count;$i++) {
 $s=$gs[$i];$assets.Add([pscustomobject]@{name='S1MB/'+$s.path;kind='github-source-file';dataset='hotchpotch/S1MB';revision=$g.sha;split='evaluation-only';url='https://raw.githubusercontent.com/hotchpotch/S1MB/'+$g.sha+'/'+$s.path;path='raw/github/hotchpotch/S1MB/'+$s.path;expected_bytes=$s.size})
}
# Documentation/source declarations are downloaded first to review identities,
# licenses and origins before any formal conversion or training.
$ordered=@($assets|Sort-Object @{Expression={if ($_.name -match '(?i)(README|SOURCES|manifest|license|\.py$|schema|task-catalog|score-policy|checksums)') {0} else {1}}},@{Expression={if ($_.dataset -in @('hotchpotch/s1mb-dataset','Praveenrajus/jev-bench','little-g-ai/Sys1Cal-v1')) {0} else {1}}},dataset,name)
$catalog|ConvertTo-Json -Depth 14|Set-Content -LiteralPath (Join-Path $ResearchRoot 'source-catalog.json') -Encoding utf8
$ordered|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $OutputRoot 'raw-download-plan.json') -Encoding utf8
# Separate a finite phase for metadata/documentation; follow-up batches cover
# every remaining source file rather than imposing a coverage cap.
$documentation=@($ordered|Where-Object { $_.name -match '(?i)(README|SOURCES|manifest|license|\.py$|schema|task-catalog|score-policy|checksums)' })
$documentation|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $OutputRoot 'documentation-plan.json') -Encoding utf8
[pscustomobject]@{sources=$catalog.Count;verified=@($catalog|Where-Object verification -eq metadata_frozen).Count;files=$ordered.Count;expected_bytes=($ordered|Measure-Object expected_bytes -Sum).Sum;documentation_files=$documentation.Count;plan=Join-Path $OutputRoot 'raw-download-plan.json'}|ConvertTo-Json
