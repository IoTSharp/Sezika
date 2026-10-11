param(
 [string]$OutputRoot='C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10',
 [string]$ResearchRoot=$PSScriptRoot,
 [ValidateRange(10,180)][int]$MaxSeconds=60
)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required'}
$watch=[Diagnostics.Stopwatch]::StartNew()
$known=@{};$latest=@{}
$ledgers=@(Get-ChildItem -LiteralPath $OutputRoot -Filter '*ledger*.jsonl' -File)
for($l=0;$l -lt $ledgers.Count;$l++){
 $lines=@(Get-Content -LiteralPath $ledgers[$l].FullName)
 for($i=0;$i -lt $lines.Count;$i++){
  if($watch.Elapsed.TotalSeconds -ge $MaxSeconds){throw 'Completeness ledger deadline'}
  $row=$lines[$i]|ConvertFrom-Json
  if($row.status -eq 'downloaded' -and(Test-Path -LiteralPath $row.path)){$known[$row.url]=$row}
  if(-not $latest.ContainsKey($row.url) -or $latest[$row.url].finished_utc -lt $row.finished_utc){$latest[$row.url]=$row}
 }
}
$names=@(
 (Join-Path $OutputRoot 'raw-download-plan.json'),
 (Join-Path $OutputRoot 'extra-raw-plan.json'),
 (Join-Path $ResearchRoot 'upstream-plan.json'),
 (Join-Path $OutputRoot 'additional-formats-plan.json'),
 (Join-Path $OutputRoot 'tool-and-socialiqa-plan.json')
)
$expected=@{}
for($n=0;$n -lt $names.Count;$n++){
 if(-not(Test-Path -LiteralPath $names[$n])){continue}
 $rows=@(Get-Content -LiteralPath $names[$n] -Raw|ConvertFrom-Json)
 for($i=0;$i -lt $rows.Count;$i++){
  if($watch.Elapsed.TotalSeconds -ge $MaxSeconds){throw 'Completeness plan deadline'}
  $row=$rows[$i]
  if($row.dataset -and $row.dataset -ne 'google-research-datasets/discofuse'){$expected[$row.url]=$row}
 }
}
$missing=[Collections.Generic.List[object]]::new()
$groups=@($expected.Values|Group-Object dataset)
$sources=[Collections.Generic.List[object]]::new()
for($g=0;$g -lt $groups.Count;$g++){
 if($watch.Elapsed.TotalSeconds -ge $MaxSeconds){throw 'Completeness grouping deadline'}
 $group=$groups[$g];$done=0;$bytes=0L;$shaChecks=0;$remaining=0L;$partial=0L
 for($i=0;$i -lt $group.Group.Count;$i++){
  $row=$group.Group[$i]
  if($known.ContainsKey($row.url)){$done++;$bytes+=$known[$row.url].bytes;if($row.expected_sha256){$shaChecks++};continue}
  $remaining+=[long]$row.expected_bytes
  $part=Join-Path $OutputRoot ($row.path+'.range-partial');$partBytes=0L
  if(Test-Path -LiteralPath $part){$partBytes=(Get-Item -LiteralPath $part).Length;$partial+=$partBytes}
  $event=$latest[$row.url]
  $missing.Add([pscustomobject]@{dataset=$row.dataset;name=$row.name;url=$row.url;path=$row.path;revision=$row.revision;expected_bytes=$row.expected_bytes;expected_sha256=$row.expected_sha256;partial_bytes=$partBytes;latest_attempt_status=$event.status;latest_attempt_error=$event.error})
 }
 $sources.Add([pscustomobject]@{dataset=$group.Name;planned_files=$group.Count;downloaded_files=$done;downloaded_bytes=$bytes;files_checked_against_upstream_sha256=$shaChecks;missing_files=$group.Count-$done;missing_whole_file_bytes=$remaining;resumable_partial_bytes=$partial;complete=($done -eq $group.Count);formal_training_admitted=$false})
}
$snapshot=[pscustomobject]@{snapshot_utc=[DateTime]::UtcNow.ToString('o');selected_sources=$sources.Count;planned_files=$expected.Count;complete_sources=@($sources|Where-Object complete).Count;missing_files=$missing.Count;all_selected_files_downloaded=($missing.Count -eq 0);excluded_source='google-research-datasets/discofuse: relevance, no selected decision labels';sources=@($sources|Sort-Object dataset);elapsed_seconds=$watch.Elapsed.TotalSeconds}
$snapshot|ConvertTo-Json -Depth 9|Set-Content -LiteralPath (Join-Path $ResearchRoot 'source-completeness.json') -Encoding utf8
$missing|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $ResearchRoot 'remaining-assets.json') -Encoding utf8
$sources|Sort-Object dataset|Export-Csv -LiteralPath (Join-Path $ResearchRoot 'source-completeness.csv') -NoTypeInformation -Encoding utf8
[pscustomobject]@{selected_sources=$sources.Count;planned_files=$expected.Count;complete_sources=$snapshot.complete_sources;missing_files=$missing.Count;all_selected_files_downloaded=$snapshot.all_selected_files_downloaded;remaining_bytes=($missing|Measure-Object expected_bytes -Sum).Sum;partial_bytes=($missing|Measure-Object partial_bytes -Sum).Sum}|ConvertTo-Json -Compress
