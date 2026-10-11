param([string]$OutputRoot='C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10')
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required'}
$watch=[Diagnostics.Stopwatch]::StartNew()
$additions=@(
 @{id='Salesforce/xlam-function-calling-60k';task='tool selection and argument supervision';priority='P1';intended_use='train-schema-review'},
 @{id='Team-ACE/ToolACE';task='tool selection and argument supervision';priority='P1';intended_use='train-schema-review'},
 @{id='gorilla-llm/Berkeley-Function-Calling-Leaderboard';task='tool evaluation';priority='P1';intended_use='benchmark-only'}
)
$catalog=[Collections.Generic.List[object]]::new()
@(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source-catalog.json') -Raw|ConvertFrom-Json)|ForEach-Object {$catalog.Add($_)}
$seeds=[Collections.Generic.List[object]]::new()
@(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source-seeds.json') -Raw|ConvertFrom-Json)|ForEach-Object {$seeds.Add($_)}
$assets=[Collections.Generic.List[object]]::new()
for($i=0;$i -lt $additions.Count;$i++){
 if($watch.Elapsed.TotalSeconds -ge 30 -or(Test-Path -LiteralPath (Join-Path $OutputRoot 'CANCEL'))){throw 'Extension deadline/cancellation'}
 $seed=$additions[$i];$file=Join-Path $OutputRoot ('metadata/hf/'+$seed.id.Replace('/','--')+'.json')
 $meta=Get-Content -LiteralPath $file -Raw|ConvertFrom-Json
 if(@($seeds|Where-Object id -eq $seed.id).Count -eq 0){$seeds.Add([pscustomobject]@{id=$seed.id;task=$seed.task;priority=$seed.priority;intended_use=$seed.intended_use;platform='Hugging Face'})}
 if(@($catalog|Where-Object id -eq $meta.id).Count -eq 0){
  $catalog.Add([pscustomobject]@{id=$meta.id;platform='Hugging Face';task=$seed.task;priority=$seed.priority;intended_use=$seed.intended_use;verification='metadata_frozen';license=$meta.cardData.license;license_tags=@($meta.tags|Where-Object {$_ -like 'license:*'});revision=$meta.sha;files=$meta.siblings.Count;source_url='https://huggingface.co/datasets/'+$meta.id;metadata_path=$file;metadata_sha256=(Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant();training_admission='pending_schema_source_split_and_overlap_review';redistribution='pending_source_rights';collection='all_structured_data_and_documentation_at_pinned_revision'})
 }
 for($f=0;$f -lt $meta.siblings.Count;$f++){
  if($watch.Elapsed.TotalSeconds -ge 30){throw 'Extension file deadline'}
  $s=$meta.siblings[$f];$name=[string]$s.rfilename
  if($name -notmatch '(?i)(\.(jsonl?|parquet|arrow|csv|tsv|txt|gz|zip|zst|bz2|xz|7z|tar|jsonlines|ndjson|md|ya?ml|py|cff)$|(^|/)(LICENSE|NOTICE|COPYING)(\.[^/]+)?$)'){continue}
  $assets.Add([pscustomobject]@{name=$meta.id+'/'+$name;kind='hf-source-file';dataset=$meta.id;revision=$meta.sha;split=if($seed.intended_use -eq 'benchmark-only'){'evaluation-only'}else{'preserve-source-split-review-required'};url='https://huggingface.co/datasets/'+$meta.id+'/resolve/'+$meta.sha+'/'+$name;path='raw/hf/'+$meta.id+'/'+$name;expected_sha256=$s.lfs.sha256;expected_git_blob_sha1=if(-not $s.lfs){$s.blobId}else{$null};expected_bytes=$s.size})
 }
}
$assets.Add([pscustomobject]@{name='SocialIQA official train-dev archive';kind='upstream-data';dataset='allenai/social_i_qa';revision='loader-declared official artifact; content SHA frozen on download';split='official train-dev archive';url='https://storage.googleapis.com/ai2-mosaic/public/socialiqa/socialiqa-train-dev.zip';path='raw/upstream/socialiqa/socialiqa-train-dev.zip'})
$catalog|ConvertTo-Json -Depth 16|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'source-catalog.json') -Encoding utf8
$seeds|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'source-seeds.json') -Encoding utf8
$assets|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $OutputRoot 'tool-and-socialiqa-plan.json') -Encoding utf8
[pscustomobject]@{sources_added=$additions.Count;files=$assets.Count;expected_bytes=($assets|Measure-Object expected_bytes -Sum).Sum}|ConvertTo-Json -Compress
