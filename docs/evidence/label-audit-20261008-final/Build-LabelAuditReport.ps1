#requires -Version 7.0
$ErrorActionPreference='Stop'
$root='D:\source\Sezika\.artifacts\label-audit-20261008'
$destination='D:\source\Sezika\docs\evidence\label-audit-20261008-final'
$source='D:\source\Sezika\.artifacts\real-gpu-experiment-20261008\data\experiment-records.jsonl'
$watch=[Diagnostics.Stopwatch]::StartNew()
function Check-ReportBudget {
    if ($watch.Elapsed.TotalSeconds -ge 60 -or [IO.File]::Exists((Join-Path $root 'cancel.request'))) { throw 'Report deadline/cancellation.' }
}
function Save-ReportJson([string]$Path,$Value) { [IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 70),[Text.UTF8Encoding]::new($false)) }
function Format-AuditMarkdown([object]$Value) { return ([string]$Value).Replace('|','\|').Replace("`r",'').Replace("`n",'<br>') }
. (Join-Path $root 'Test-LabelAuditPayload.ps1')
if ([IO.Directory]::Exists($destination)) { throw 'Permanent report directory already exists; do not overwrite.' }
$sourceHash=(Get-FileHash -LiteralPath $source).Hash
if ($sourceHash -cne 'A015A8FA182EE62223CAFC307C618C515A19E90F843290DB9DFB53A56B9A041C') { throw 'Original source hash changed.' }
$lines=@(Get-Content -LiteralPath $source -TotalCount 33)
if ($lines.Count -ne 32) { throw 'Source denominator must be32.' }
$rows=[Collections.Generic.List[object]]::new()
for ($i=0;$i -lt 32;$i++) { Check-ReportBudget; $rows.Add(($lines[$i]|ConvertFrom-Json -Depth 40 -DateKind String)) }
$batches=@(@{Name='tiny-max-01';Start=0;Count=1},@{Name='batch-max-02';Start=1;Count=8},@{Name='batch-max-03';Start=9;Count=8},@{Name='batch-max-04';Start=17;Count=8},@{Name='batch-max-05';Start=25;Count=7})
$all=[Collections.Generic.List[object]]::new();$requests=[Collections.Generic.List[object]]::new()
$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$responseIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$inputTokens=0L;$outputTokens=0L;$totalTokens=0L;$cachedTokens=0L;$reasoningTokens=0L
for ($b=0;$b -lt 5;$b++) {
    Check-ReportBudget
    $batch=$batches[$b];$directory=Join-Path $root $batch.Name
    $request=Get-Content -LiteralPath (Join-Path $directory 'request.json') -Raw|ConvertFrom-Json -Depth 70 -DateKind String
    $provenance=Get-Content -LiteralPath (Join-Path $directory 'provenance.json') -Raw|ConvertFrom-Json -Depth 40 -DateKind String
    $result=Get-Content -LiteralPath (Join-Path $directory 'result.json') -Raw|ConvertFrom-Json -Depth 50 -DateKind String
    $response=Get-Content -LiteralPath (Join-Path $directory 'response.json') -Raw|ConvertFrom-Json -Depth 80 -DateKind String
    if ($result.status -cne 'completed_and_validated' -or $result.record_count -ne $batch.Count -or $result.start_index -ne $batch.Start -or @($result.cleanup_errors).Where({$null -ne $_}).Count -gt 0) { throw 'Batch result/cleanup failed.' }
    if ($request.model -cne 'gpt-6.1-sol' -or $request.reasoning.effort -cne 'max' -or $response.model -cne 'gpt-6.1-sol' -or $response.reasoning.effort -cne 'max' -or $response.status -cne 'completed') { throw 'Model/effort/status mismatch.' }
    if ([string]::IsNullOrWhiteSpace($response.id) -or -not $responseIds.Add($response.id)) { throw 'Response ID absent or duplicated.' }
    $requestHash=(Get-FileHash -LiteralPath (Join-Path $directory 'request.json')).Hash
    if ($provenance.request_sha256 -cne $requestHash -or $provenance.source_sha256 -cne $sourceHash -or -not $provenance.source_labels_hidden) { throw 'Batch provenance mismatch.' }
    $submitted=$request.input[1].content[0].text|ConvertFrom-Json -Depth 50 -DateKind String
    if (@($submitted.rows).Count -ne $batch.Count -or $request.input[1].content[0].text.Contains('target_index',[StringComparison]::Ordinal)) { throw 'Blind input denominator/label exposure mismatch.' }
    $selected=$rows.GetRange($batch.Start,$batch.Count).ToArray()
    for ($i=0;$i -lt $batch.Count;$i++) {
        Check-ReportBudget
        if ($submitted.rows[$i].record_id -cne $selected[$i].record_id -or ($submitted.rows[$i].request|ConvertTo-Json -Depth 40 -Compress) -cne ($selected[$i].request|ConvertTo-Json -Depth 40 -Compress)) { throw 'Submitted question differs from original source.' }
    }
    $texts=[Collections.Generic.List[string]]::new();$items=@($response.output)
    if ($items.Count -gt 50) { throw 'Response item bound.' }
    for ($i=0;$i -lt $items.Count;$i++) {
        Check-ReportBudget
        if ($items[$i].type -cne 'message') { continue }
        $parts=@($items[$i].content);if ($parts.Count -gt 20) { throw 'Response content bound.' }
        for ($j=0;$j -lt $parts.Count;$j++) { Check-ReportBudget; if ($parts[$j].type -ceq 'output_text') { $texts.Add([string]$parts[$j].text) } }
    }
    if ($texts.Count -ne 1) { throw 'Expected exactly one complete output text.' }
    $validated=@(Test-LabelAuditPayload -Json $texts[0] -SelectedRows $selected)
    for ($i=0;$i -lt $validated.Count;$i++) { Check-ReportBudget; if (-not $seen.Add($validated[$i].record_id)) { throw 'Duplicate across batches.' }; $all.Add($validated[$i]) }
    $inputTokens+=[long]$response.usage.input_tokens;$outputTokens+=[long]$response.usage.output_tokens;$totalTokens+=[long]$response.usage.total_tokens
    $cachedTokens+=[long]$response.usage.input_tokens_details.cached_tokens;$reasoningTokens+=[long]$response.usage.output_tokens_details.reasoning_tokens
    $requests.Add([ordered]@{batch=$batch.Name;count=$batch.Count;response_id=$response.id;requested_model=$request.model;requested_effort=$request.reasoning.effort;response_model=$response.model;response_effort=$response.reasoning.effort;request_sha256=$requestHash;response_sha256=(Get-FileHash -LiteralPath (Join-Path $directory 'response.json')).Hash;provenance_sha256=(Get-FileHash -LiteralPath (Join-Path $directory 'provenance.json')).Hash;result_sha256=(Get-FileHash -LiteralPath (Join-Path $directory 'result.json')).Hash;usage=$response.usage;source_labels_hidden=$true})
}
if ($all.Count -ne 32 -or $seen.Count -ne 32) { throw 'Incomplete32 denominator.' }
$orderedReviews=[Collections.Generic.List[object]]::new()
for ($i=0;$i -lt 32;$i++) { Check-ReportBudget; $review=@($all|Where-Object record_id -CEQ $rows[$i].record_id);if ($review.Count -ne 1) { throw 'Source coverage mismatch.' };$review[0]|Add-Member -NotePropertyName original_request -NotePropertyValue $rows[$i].request;$orderedReviews.Add($review[0]) }
$changes=@($orderedReviews|Where-Object change_original_label -EQ $true)
$indeterminate=@($orderedReviews|Where-Object answer_status -CNE 'determinate')
$revision=@($orderedReviews|Where-Object question_revision_needed -EQ $true)
$baselinePath='D:\source\Sezika\docs\evidence\gpu-learnability-20261008\stage-c-evaluation.json'
$baseline=Get-Content -LiteralPath $baselinePath -Raw|ConvertFrom-Json -Depth 80 -DateKind String
if ($baseline.records_sha256 -cne $sourceHash -or $baseline.completed_steps -ne 768) { throw 'Existing GPU baseline identity mismatch.' }
$originalPredictions=@($baseline.observations.rows|Where-Object variant -CEQ 'original')
if ($originalPredictions.Count -ne 32) { throw 'Existing GPU predictions denominator mismatch.' }
$baselineAgreement=[Collections.Generic.List[object]]::new()
foreach ($split in @('train','development')) {
    Check-ReportBudget
    $scope=@($originalPredictions|Where-Object split -CEQ $split);$oldCount=0;$reviewCount=0
    for ($i=0;$i -lt $scope.Count -and $i -lt 32;$i++) { Check-ReportBudget;$review=@($orderedReviews|Where-Object record_id -CEQ $scope[$i].record_id);if ($review.Count -ne 1) { throw 'Prediction/audit identity mismatch.' };if ($scope[$i].agrees_with_teacher) { $oldCount++ };if ($review[0].answer_status -ceq 'determinate' -and $scope[$i].selected_candidate_id -ceq $review[0].suggested_correct_id) { $reviewCount++ } }
    $baselineAgreement.Add([ordered]@{split=$split;denominator=$scope.Count;agreement_with_original_teacher=$oldCount;agreement_with_ai_review=$reviewCount;weights_retrained=$false;use='posthoc_ai_label_comparison_not_gold_accuracy'})
}
$summary=[ordered]@{records=32;determinate=32-$indeterminate.Count;labels_retained=32-$changes.Count-$indeterminate.Count;label_changes_suggested=$changes.Count;indeterminate=$indeterminate.Count;question_revisions_suggested=$revision.Count;successful_requests=5;input_tokens=$inputTokens;output_tokens=$outputTokens;total_tokens=$totalTokens;cached_input_tokens=$cachedTokens;reasoning_output_tokens=$reasoningTokens}
$report=[ordered]@{contract='sezika/independent-ai-label-audit/v1';status='completed_and_strictly_validated';source_sha256=$sourceHash;provider='sonnet_vip';requested_model='gpt-6.1-sol';requested_effort='max';initially_requested_effort='ultra';upstream_identity_verification='gateway_response_declaration_only';reviewer_kind='ai';human_review='pending';formal=$false;eligible_as_sealed_test=$false;original_data_modified=$false;summary=$summary;requests=@($requests);existing_gpu_baseline_sha256=(Get-FileHash -LiteralPath $baselinePath).Hash;existing_gpu_prediction_comparison=@($baselineAgreement);reviews=@($orderedReviews)}
Check-ReportBudget
[IO.Directory]::CreateDirectory($destination)|Out-Null
Save-ReportJson (Join-Path $destination 'audit-report.json') $report
Copy-Item -LiteralPath $source -Destination (Join-Path $destination 'experiment-records.original.jsonl')
$copyNames=@('model-selection.json','Test-LabelAuditPayload.ps1','Invoke-LabelAuditBatch.ps1','Invoke-LabelAuditBatch.max-v1.ps1','Invoke-LabelAuditBatch.ultra-v1.ps1','Build-LabelAuditReport.ps1','validator-offline.result.json','cli-probe-schema.json')
for ($i=0;$i -lt $copyNames.Count;$i++) { Check-ReportBudget;Copy-Item -LiteralPath (Join-Path $root $copyNames[$i]) -Destination (Join-Path $destination $copyNames[$i]) }
for ($b=0;$b -lt 5;$b++) { Check-ReportBudget;$to=Join-Path $destination $batches[$b].Name;[IO.Directory]::CreateDirectory($to)|Out-Null;foreach ($name in @('request.json','response.json','provenance.json','result.json')) { Check-ReportBudget;Copy-Item -LiteralPath (Join-Path (Join-Path $root $batches[$b].Name) $name) -Destination (Join-Path $to $name) } }
$md=[Collections.Generic.List[string]]::new()
$md.Add('# 32题独立AI盲审（2026-10-08）');$md.Add('')
$md.Add("使用当前Codex sonnet.vip凭据，实际5次成功请求完成32题：gpt-6.1-sol / max。最初ultra请求被HTTP400拒绝，用户明确批准改用max；未修改全局配置。保留标签$($summary.labels_retained)题，建议改标$($changes.Count)题，不确定$($indeterminate.Count)题，建议改题$($revision.Count)题。每题原标签在请求中隐藏，返回结果重新通过严格本地合同校验。");$md.Add('')
$md.Add('本结果是AI审题侧车，human_review=pending、formal=false、sealed_test=false。原32题、24train/8已曝光development划分、旧教师来源与历史GPU报告保持原字节；没有改标训练、重跑GPU或把已曝光题称为封存测试。返回型号为网关声明，并非独立核实上游身份。');$md.Add('')
$md.Add('| 题ID | 类型/划分 | 原ID → 审核ID | 判定 | 理由 |');$md.Add('| --- | --- | --- | --- | --- |')
for ($i=0;$i -lt 32;$i++) { Check-ReportBudget;$v=$orderedReviews[$i];$flag=if ($null -eq $v.change_original_label){'不确定'}elseif($v.change_original_label){'建议改标'}else{'保留'};$md.Add("| $(Format-AuditMarkdown $v.record_id) | $($v.type)/$($v.split) | $(Format-AuditMarkdown $v.original_target_id) → $(Format-AuditMarkdown $v.suggested_correct_id) | $flag | $(Format-AuditMarkdown $v.reason) |") }
$md.Add('');$md.Add('## 既有权重与新审核标签的事后比较');$md.Add('')
foreach ($comparison in $baselineAgreement) { $md.Add("- $($comparison.split)：原教师一致$($comparison.agreement_with_original_teacher)/$($comparison.denominator)，AI审核标签一致$($comparison.agreement_with_ai_review)/$($comparison.denominator)。") }
$md.Add('');$md.Add('以上复用已保存GPU逐题预测，仅改变比较对象，没有训练或新增前向；不称人工金标正确率，旧CE及报告未改写。');$md.Add('')
$md.Add('## 拟合与质量的下一步');$md.Add('')
$md.Add('先把建议修正作为新数据版本及独立来源合同，保留原始32题。后续若启动新实验，固定24训练题与8开发题，先检查训练损失、梯度及更新是否有效，再验证训练24题能否全部拟合；Choice换序需文本与ID一起移动，并比较所选ID，避免学习位置。若线性head达到优化边界仍不能拟合，再单独评估可训练中间层/非线性head；encoder反传或LoRA需要另行实现和验收。每次只做有界且预先固定的实验，开发题不选步数。');$md.Add('')
$md.Add('32题可作为固定样本回归验收；拟合24/24、开发表现、换序稳定和正式泛化质量分别报告。审题完成或训练集满分本身不会使正式质量门槛通过。当前请求止于审题与分析，没有自动开始新训练。');$md.Add('')
$md.Add("使用量（网关报告）：input=$inputTokens、output=$outputTokens、total=$totalTokens；cached input=$cachedTokens、reasoning output=$reasoningTokens。原始请求/响应、来源与hash见[audit-report.json](label-audit-20261008-final/audit-report.json)。ultra失败及CLI未发请求失败保留在本地任务目录。");$md.Add('')
$md.Add('首次报告生成因Md与PowerShell内置别名冲突失败，原失败回执保留；函数已更名为Format-AuditMarkdown。旧的部分归档目录以及本任务意外创建的两个空目录general-zh-choice-01、delivery保留：自动审批拒绝了归档/清理命令，仅返回blocked by policy，未绕过执行。后续成功归档使用独立final目录。');$md.Add('')
[IO.File]::WriteAllText('D:\source\Sezika\docs\evidence\label-audit-2026-10-08.md',($md -join [Environment]::NewLine),[Text.UTF8Encoding]::new($false))
if ((Get-FileHash -LiteralPath $source).Hash -cne $sourceHash -or (Get-FileHash -LiteralPath (Join-Path $destination 'experiment-records.original.jsonl')).Hash -cne $sourceHash) { throw 'Original/copy byte identity failed.' }
Save-ReportJson (Join-Path $root 'report-build.result.json') ([ordered]@{status='completed';summary=$summary;baseline_comparison=@($baselineAgreement);source_hash_preserved=$true;elapsed_seconds=$watch.Elapsed.TotalSeconds;report_sha256=(Get-FileHash -LiteralPath (Join-Path $destination 'audit-report.json')).Hash})
$summary|ConvertTo-Json -Compress
$baselineAgreement|ConvertTo-Json -Compress
