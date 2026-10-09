#requires -Version 7.0
param([switch]$Final,[switch]$Final16)
$ErrorActionPreference='Stop';$root=$PSScriptRoot;$watch=[Diagnostics.Stopwatch]::StartNew()
function Check {if($watch.Elapsed.TotalSeconds -gt 60 -or (Test-Path -LiteralPath "$root/cancel.request")){throw 'Repair review60s/cancellation'}}
$summary=Get-Content -Raw -LiteralPath (Join-Path $root $(if($Final16){'final16-review-summary.json'}elseif($Final){'teacher-review-final-summary.json'}else{'teacher-review-progress.json'}))|ConvertFrom-Json
$sourceHash=(Get-FileHash -LiteralPath "$root/prepared-records.jsonl").Hash
if($sourceHash -cne '17946E4ED202F45832C4E0E90B62F2CD7AAEDED096896E97268CCDB82DF3C937'){throw 'Frozen source differs'}
$sources=@{};$lines=@(Get-Content -LiteralPath "$root/prepared-records.jsonl" -TotalCount 257)
if($lines.Count -ne 256){throw 'Source denominator differs'}
for($i=0;$i -lt 256;$i++){Check;$row=$lines[$i]|ConvertFrom-Json;if($sources.ContainsKey($row.record_id)){throw 'Source ID repeated'};$sources[$row.record_id]=$row}
$items=[Collections.Generic.List[object]]::new();$md=[Text.StringBuilder]::new()
[void]$md.AppendLine('# 教师审核问题题目与待修订建议')
[void]$md.AppendLine('')
[void]$md.AppendLine("已审核 $($summary.reviewed)/256；当前 $($summary.quality_blockers) 题不能进入本轮固定训练。下列建议尚未应用，原始source gold、题文、family、split及响应保持；修订后须作为新数据版本重新审核相关题目。")
$blockers=@($summary.blockers)
for($i=0;$i -lt $blockers.Count -and $i -lt 256;$i++) {
  Check;$review=$blockers[$i];$row=$sources[$review.record_id];if(-not $row){throw 'Review source absent'}
  $items.Add([ordered]@{record_id=$row.record_id;source_canonical_id=$row.source_canonical_id;family_id=$row.family_id;language=$row.language;split=$row.split;source_gold_index=$row.target_index;original_request=$row.request;teacher_review=$review;repair_status='proposal_only_not_applied';requires_reaudit=$true;training_admitted=$false})
  [void]$md.AppendLine('');[void]$md.AppendLine("## $($row.record_id)（$($row.split) / $($row.language)）")
  [void]$md.AppendLine('');[void]$md.AppendLine("审核结论：$($review.answer_status)；题目清晰度：$($review.clarity_status)。")
  [void]$md.AppendLine('');[void]$md.AppendLine([string]$review.reason)
  if($review.suggested_text_revision){[void]$md.AppendLine('');[void]$md.AppendLine('教师修订建议：'+$review.suggested_text_revision)}
}
$stem=if($Final16){'final16-repair-proposals'}elseif($Final){'teacher-repair-proposals-final'}else{'teacher-repair-proposals-progress'}
if(($Final -or $Final16) -and ((Test-Path -LiteralPath "$root/$stem.json") -or (Test-Path -LiteralPath "$root/$stem.md"))){throw 'Final repair evidence exists'}
[IO.File]::WriteAllText("$root/$stem.json",(@{source_sha256=$sourceHash;reviewed=$summary.reviewed;total=256;blocked=$items.Count;applied=$false;records=@($items)}|ConvertTo-Json -Depth 50),[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText("$root/$stem.md",$md.ToString(),[Text.UTF8Encoding]::new($false))
Write-Output "Prepared $($items.Count) explicit repair proposals; source unchanged."
