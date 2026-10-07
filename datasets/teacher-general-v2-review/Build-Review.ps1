#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $OutputDirectory,
    [ValidateSet(1,32)][int] $MaximumRecords = 32,
    [ValidateRange(1,120)][int] $TimeoutSeconds = 120,
    [string] $CancellationFile,
    [Threading.CancellationToken] $CancellationToken = [Threading.CancellationToken]::None
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$clock = [Diagnostics.Stopwatch]::StartNew()
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$inputRoot = [IO.Path]::GetFullPath('D:\source\Sezika\.artifacts\teacher-gpu-prompt-v2-20261008\simple-run-20261007-2019\result-all-32')
$recordsHash = 'deb5d8ec8e2c8f1d3f772c42cde1bde9f6141e975d4b20e942b52e1ab09a08e4'
$manifestHash = '5151fa98f0496e76c4a9b6cd64f6230792f078e695aae7311e41300b556093c2'
$stage = $null
$ownsStage = $false
function Guard {
    $CancellationToken.ThrowIfCancellationRequested()
    if ($CancellationFile -and [IO.File]::Exists([IO.Path]::GetFullPath($CancellationFile))) { throw [OperationCanceledException]::new('Review generation cancelled.') }
    if ($clock.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw [TimeoutException]::new('Review generation deadline expired.') }
}
function Require([bool] $Condition, [string] $Message) { Guard; if (-not $Condition) { throw [IO.InvalidDataException]::new($Message) } }
function Sha([byte[]] $Bytes) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant() }
function Check-DirectoryChain([string] $Path) {
    $directory = [IO.DirectoryInfo]::new($Path)
    for ($depth = 0; $depth -lt 16 -and $null -ne $directory; $depth++) {
        Guard
        Require ($directory.Exists -and (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0)) "Missing or linked directory: $($directory.FullName)"
        $directory = $directory.Parent
    }
    Require ($null -eq $directory) 'Directory ancestor bound exceeded.'
}
function Read-Bytes([string] $Path, [int] $Limit) {
    Guard
    $file = [IO.FileInfo]::new($Path)
    Require ($file.Exists -and $file.Length -gt 0 -and $file.Length -le $Limit -and (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0)) "Unsafe or oversized input: $Path"
    $bytes = [IO.File]::ReadAllBytes($Path)
    Require ($bytes.Length -eq $file.Length -and $bytes.Length -le $Limit) "Changed input length: $Path"
    return ,$bytes
}
function Read-Reference($Reference, [int] $Index, [string] $Kind, [string] $AliasHash) {
    Guard
    $suffix = if ($Kind -eq 'request') { 'decision.json' } else { 'http-response.bin' }
    $relative = 'items/{0:D2}.{1}' -f $Index, $suffix
    Require ($Reference.path -is [string] -and $Reference.path -ceq $relative -and -not [IO.Path]::IsPathRooted($Reference.path)) 'Referenced relative path or order differs.'
    $path = [IO.Path]::GetFullPath([IO.Path]::Combine($inputRoot, $Reference.path))
    Require ($path.StartsWith($inputRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Reference escaped source root.'
    $bytes = Read-Bytes $path 131072
    $hash = Sha $bytes
    Require ($Reference.bytes -eq $bytes.Length -and $Reference.sha256 -is [string] -and $hash -ceq $Reference.sha256 -and $hash -ceq $AliasHash) "Original $Kind byte identity differs."
    return @{ bytes = $bytes; path = $relative; sha256 = $hash; length = $bytes.Length }
}
function Nonempty($Value) { return ($Value -is [string] -and -not [string]::IsNullOrWhiteSpace($Value) -and $Value.Length -le 16384) }
function Line([Text.StringBuilder] $Builder, [string] $Value = '') { [void]$Builder.Append($Value).Append("`n") }
function Block([Text.StringBuilder] $Builder, [string] $Value) {
    # Fence choice is bounded, and source text is never truncated or interpreted as Markdown.
    $fence = '~~~~'
    for ($length = 4; $length -le 20; $length++) { Guard; if (-not $Value.Contains($fence, [StringComparison]::Ordinal)) { break }; $fence += '~' }
    Require (-not $Value.Contains($fence, [StringComparison]::Ordinal)) 'Source text exceeds safe fence bound.'
    Line $Builder ($fence + 'text'); Line $Builder ($Value.Replace("`r`n", "`n").Replace("`r", "`n")); Line $Builder $fence; Line $Builder
}
function Write-Text([string] $Name, [string] $Text) {
    Guard
    Require (-not $Text.Contains("`r", [StringComparison]::Ordinal)) 'Output must use fixed LF.'
    [IO.File]::WriteAllBytes([IO.Path]::Combine($stage, $Name), $utf8.GetBytes($Text))
}
try {
    Guard
    Check-DirectoryChain $inputRoot
    Check-DirectoryChain ([IO.Path]::Combine($inputRoot, 'items'))
    $output = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $parent = [IO.Path]::GetDirectoryName($output)
    $name = [IO.Path]::GetFileName($output)
    Require ($parent -and $name -match '^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$') 'Output must be a named child of an existing directory.'
    Check-DirectoryChain $parent
    Require (-not $output.Equals($inputRoot, [StringComparison]::OrdinalIgnoreCase) -and -not $output.StartsWith($inputRoot + '\', [StringComparison]::OrdinalIgnoreCase) -and -not $inputRoot.StartsWith($output + '\', [StringComparison]::OrdinalIgnoreCase)) 'Input/output roots overlap.'
    Require (-not [IO.Directory]::Exists($output) -and -not [IO.File]::Exists($output)) 'Output exists; refusing overwrite.'
    $sourceManifestBytes = Read-Bytes ([IO.Path]::Combine($inputRoot, 'manifest.json')) 65536
    $sourceRecordsBytes = Read-Bytes ([IO.Path]::Combine($inputRoot, 'records.jsonl')) 1048576
    Require ((Sha $sourceManifestBytes) -ceq $manifestHash -and (Sha $sourceRecordsBytes) -ceq $recordsHash -and $sourceRecordsBytes.Length -eq 54198) 'Pinned source manifest/records identity differs.'
    $sourceManifest = $utf8.GetString($sourceManifestBytes) | ConvertFrom-Json -AsHashtable -Depth 32
    Require ($sourceManifest.schema_version -eq 1 -and $sourceManifest.total -eq 32 -and $sourceManifest.pending -eq 32 -and $sourceManifest.failed -eq 0 -and $sourceManifest.abstained -eq 0 -and $sourceManifest.status -ceq 'complete_pending_human_review' -and $sourceManifest.training_admission -eq $false -and $sourceManifest.quality_evidence -eq $false -and $sourceManifest.teacher.output_training_permitted -eq $false -and $sourceManifest.teacher.independently_verified -eq $false) 'Source is not the approved pending-only engineering collection.'
    Require ($sourceManifest.records.path -ceq 'records.jsonl' -and $sourceManifest.records.bytes -eq 54198 -and $sourceManifest.records.sha256 -ceq $recordsHash) 'Source record descriptor differs.'
    $recordLines = $utf8.GetString($sourceRecordsBytes).TrimEnd("`n").Split("`n")
    Require ($recordLines.Count -eq 32) 'Expected exactly 32 source records.'
    $records = [Collections.Generic.List[object]]::new(32)
    $recordIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $familyIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $sourceGroups = [ordered]@{}
    for ($index = 0; $index -lt 32; $index++) {
        Guard
        $record = $recordLines[$index] | ConvertFrom-Json -AsHashtable -Depth 32
        Require ($record.record_id -is [string] -and $record.record_id -match '^general-(zh|en)-(choice|score|boolean)-[0-9]{2}$' -and $recordIds.Add($record.record_id) -and (Nonempty $record.family_id) -and $familyIds.Add($record.family_id)) 'Duplicate or invalid record/family identity.'
        Require ($record.language -cin @('zh','en') -and $record.type -cin @('choice','score','boolean') -and $record.purpose -ceq 'pilot' -and $record.split -ceq 'audit' -and $record.origin -ceq 'AI drafted' -and $record.review_status -ceq 'pending' -and $record.training_admission -eq $false -and $record.human_review_required -eq $true -and $record.teacher_status -ceq 'answered' -and $record.http_status -eq 200 -and $record.finish_reason -ceq 'stop' -and $null -eq $record.failure_code) 'Record is not pending answered audit/pilot data.'
        Require ($record.model_id -ceq $sourceManifest.teacher.model -and $record.model_revision -ceq $sourceManifest.teacher.revision -and $record.model_sha256 -ceq $sourceManifest.teacher.model_sha256) 'Teacher model identity differs.'
        $group = $record.language + '/' + $record.type
        if (-not $sourceGroups.Contains($group)) { $sourceGroups[$group] = 0 }
        $sourceGroups[$group]++
        $records.Add($record)
    }
    Require ($familyIds.Count -eq 32 -and $sourceGroups.Count -eq 6) 'Expected 32 families and six groups.'
    foreach ($language in @('zh','en')) { Guard; Require ($sourceGroups[$language + '/choice'] -eq 6 -and $sourceGroups[$language + '/score'] -eq 5 -and $sourceGroups[$language + '/boolean'] -eq 5) 'Source group count differs.' }
    $stage = [IO.Path]::Combine($parent, '.' + $name + '.review-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    Require (-not [IO.Directory]::Exists($stage) -and -not [IO.File]::Exists($stage)) 'Unique stage already exists.'
    [void][IO.Directory]::CreateDirectory($stage)
    $ownsStage = $true
    $markdown = [Text.StringBuilder]::new()
    $jsonl = [Text.StringBuilder]::new()
    $references = [Collections.Generic.List[object]]::new(64)
    $groups = [ordered]@{}
    Line $markdown '# 通用文本教师建议：逐题人工审核'
    Line $markdown
    Line $markdown '状态：全部 pending；教师建议不等于人工答案。先自行阅读原文并写下候选 ID，再展开教师建议。没有任何题已获得训练准入；本包已曝光，不能充当封存测试。'
    Line $markdown
    for ($index = 0; $index -lt $MaximumRecords -and $index -lt 32; $index++) {
        Guard
        $record = $records[$index]
        $requestRef = Read-Reference $record.request $index 'request' $record.request_sha256
        $responseRef = Read-Reference $record.response $index 'response' $record.response_sha256
        $request = $utf8.GetString($requestRef.bytes) | ConvertFrom-Json -AsHashtable -Depth 32
        $response = $utf8.GetString($responseRef.bytes) | ConvertFrom-Json -AsHashtable -Depth 32
        Require ($request.schema_version -eq 1 -and $request.model_id -ceq 'sezika/mmbert-base-independent-s4' -and (Nonempty $request.state.text) -and $request.questions.Count -eq 1) 'Decision request shape differs.'
        $question = $request.questions[0]
        Require ($question.id -ceq $record.question_id -and $question.type -ceq $record.type -and (Nonempty $question.instruction)) 'Question identity/type/instruction differs.'
        $candidates = [Collections.Generic.List[object]]::new(32)
        switch -CaseSensitive ($record.type) {
            'choice' {
                Require ($question.candidates.Count -ge 2 -and $question.candidates.Count -le 32) 'Choice count invalid.'
                for ($candidate = 0; $candidate -lt $question.candidates.Count -and $candidate -lt 32; $candidate++) { Guard; $item = $question.candidates[$candidate]; Require ((Nonempty $item.id) -and (Nonempty $item.text)) 'Choice ID/text empty.'; $candidates.Add([ordered]@{ id=$item.id; text=$item.text }) }
            }
            'score' {
                Require ($question.levels.Count -ge 2 -and $question.levels.Count -le 10) 'Score levels invalid.'
                for ($candidate = 0; $candidate -lt $question.levels.Count -and $candidate -lt 10; $candidate++) { Guard; Require (Nonempty $question.levels[$candidate]) 'Score level empty.'; $candidates.Add([ordered]@{ id=$candidate.ToString([Globalization.CultureInfo]::InvariantCulture); text=$question.levels[$candidate] }) }
            }
            'boolean' {
                Require ((Nonempty $question.statement) -and (Nonempty $question.when_false) -and (Nonempty $question.when_true)) 'Boolean statement/mapping empty.'
                $candidates.Add([ordered]@{ id='false'; text=$question.when_false }); $candidates.Add([ordered]@{ id='true'; text=$question.when_true })
            }
            default { throw 'Unknown question type.' }
        }
        Require ($record.candidate_ids.Count -eq $candidates.Count -and ($record.candidate_index -is [long] -or $record.candidate_index -is [int])) 'Candidate descriptor invalid.'
        $candidateIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        for ($candidate = 0; $candidate -lt $candidates.Count -and $candidate -lt 32; $candidate++) { Guard; Require ($candidateIds.Add($candidates[$candidate].id) -and $record.candidate_ids[$candidate] -is [string] -and $record.candidate_ids[$candidate] -ceq $candidates[$candidate].id) 'Candidate ID order/uniqueness differs.' }
        Require ($record.candidate_index -ge 0 -and $record.candidate_index -lt $candidates.Count -and $record.selected_candidate_id -is [string] -and $record.selected_candidate_id -ceq $candidates[$record.candidate_index].id) 'Teacher selected ID/index differs.'
        Require ($response.model -ceq $record.model_id -and $response.choices.Count -eq 1 -and $response.choices[0].finish_reason -ceq 'stop' -and $response.choices[0].message.role -ceq 'assistant' -and (Nonempty $response.choices[0].message.content)) 'Teacher response is absent or invalid; no guessed label allowed.'
        $teacherAnswer = $response.choices[0].message.content.Trim() | ConvertFrom-Json -AsHashtable -Depth 8
        Require ($teacherAnswer.question_id -ceq $question.id -and $teacherAnswer.status -ceq 'answered' -and $teacherAnswer.selected_candidate_id -is [string] -and $teacherAnswer.selected_candidate_id -ceq $record.selected_candidate_id) 'Actual teacher response differs from recorded suggestion.'
        $meaning = $candidates[$record.candidate_index].text
        Line $markdown ('## {0:D2}. {1}（{2} / {3}）' -f ($index + 1), $record.record_id, $record.language, $record.type)
        Line $markdown
        Line $markdown ('Family：`' + $record.family_id + '`；问题 ID：`' + $question.id + '`；状态：pending。')
        Line $markdown
        Line $markdown '原文 state.text：'; Block $markdown $request.state.text
        Line $markdown '任务说明 instruction：'; Block $markdown $question.instruction
        if ($record.type -ceq 'boolean') { Line $markdown '待判断陈述 statement：'; Block $markdown $question.statement }
        Line $markdown '完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：'
        Line $markdown
        for ($candidate = 0; $candidate -lt $candidates.Count -and $candidate -lt 32; $candidate++) { Guard; Line $markdown ('候选 ID：`' + $candidates[$candidate].id + '`'); Block $markdown $candidates[$candidate].text }
        Line $markdown '人工独立答案（候选 ID）：________'
        Line $markdown '审核人 / 身份 / 审核时间：________'
        Line $markdown '人工判定（同意 / 纠正 / 不明确 / 题目问题）：________'
        Line $markdown '纠正后的候选 ID（如需）：________'
        Line $markdown '备注（包括是否已看过教师建议）：________'
        Line $markdown
        Line $markdown '<details><summary>填完独立答案后展开：实际教师建议</summary>'
        Line $markdown
        Line $markdown ('教师选中的 ID：`' + $record.selected_candidate_id + '`；零起始索引：' + $record.candidate_index)
        Block $markdown $meaning
        Line $markdown '</details>'
        Line $markdown
        Line $markdown ('来源 request：`' + $requestRef.path + '`，SHA-256 `' + $requestRef.sha256 + '`，bytes=' + $requestRef.length)
        Line $markdown ('来源 response：`' + $responseRef.path + '`，SHA-256 `' + $responseRef.sha256 + '`，bytes=' + $responseRef.length)
        Line $markdown
        $template = [ordered]@{
            schema_version=1; record_id=$record.record_id; family_id=$record.family_id; language=$record.language; type=$record.type; purpose='pilot'; split='audit'; origin=$record.origin; review_status='pending'; training_admission=$false; quality_evidence=$false; sealed_test_eligible=$false; teacher_answer_exposed=$true; eligible_as_sealed_test=$false
            source=[ordered]@{ manifest_sha256=$manifestHash; records_sha256=$recordsHash; record_index=$index; request=[ordered]@{path=$requestRef.path;sha256=$requestRef.sha256;bytes=$requestRef.length}; response=[ordered]@{path=$responseRef.path;sha256=$responseRef.sha256;bytes=$responseRef.length} }
            question=$request; candidate_mapping=$candidates.ToArray()
            teacher_suggestion=[ordered]@{question_id=$question.id;status=$teacherAnswer.status;selected_candidate_id=$record.selected_candidate_id;candidate_index=$record.candidate_index;meaning=$meaning;model_id=$record.model_id;revision=$record.model_revision;model_sha256=$record.model_sha256;output_training_permitted=$false}
            human=[ordered]@{reviewer=$null;reviewer_id=$null;reviewed_at_utc=$null;independent_label=$null;decision=$null;correction=$null;notes=$null;teacher_suggestion_seen=$null}
        }
        Line $jsonl ($template | ConvertTo-Json -Depth 32 -Compress)
        $references.Add([ordered]@{record_id=$record.record_id;family_id=$record.family_id;kind='request';path=$requestRef.path;sha256=$requestRef.sha256;bytes=$requestRef.length})
        $references.Add([ordered]@{record_id=$record.record_id;family_id=$record.family_id;kind='response';path=$responseRef.path;sha256=$responseRef.sha256;bytes=$responseRef.length})
        $group = $record.language + '/' + $record.type
        if (-not $groups.Contains($group)) { $groups[$group] = 0 }; $groups[$group]++
        Write-Host ('Review progress {0}/{1}; references={2}; human=pending; GPU=false' -f ($index + 1), $MaximumRecords, $references.Count)
    }
    Guard
    $readme = @'
# 通用文本人工审核包

这 32 条中英文本题覆盖分类、程度评分、真假判断；它们来自 AI 草拟题和真实教师回答，全部仍待人工审核。本包只提供审核入口，没有训练导入或自动准入功能。教师答案已在包内曝光（teacher_answer_exposed=true），即使界面折叠也不能认定盲评或独立金标；eligible_as_sealed_test=false。

1. 打开 review.md，逐题阅读完整原文、说明和候选，先写自己的候选 ID，再展开教师建议核对。若已看到建议，请如实备注，不能声称做过独立盲审。
2. 复制 review-template.jsonl 后填写每题 human：真实 reviewer、reviewer_id、reviewed_at_utc、independent_label、decision、correction 和 notes。不得把教师答案直接当作人工答案。Boolean ID 只能是字符串 "false" / "true"；Score ID 是从零开始的字符串，例如 "0"，不能把第一个等级写成 "1"。不明确或题目有问题时如实记录，不强行选择。
3. 保留 record_id、family_id、source 和 teacher_suggestion 原样；先在副本记录审核，避免破坏本包 manifest 绑定的原始待审核产物。完成审核不等于训练获准，training_admission 和 quality_evidence 仍为 false。

教师模型标称 Apache-2.0，但模型许可不自动代表输出训练许可；当前来源证据 output_training_permitted=false、independently_verified=false。正式训练仍需要真实逐条复核和许可条件确认，不能由脚本填写批准。已在本包曝光的题及其 family 不能再充当封存测试；封存测试应另建、独立保管且不提供给教师或训练流程。

manifest.json 绑定原始 records/manifest、全部 request/response 原字节身份、生成器及本包输出 SHA。源文件不随包复制，其路径以 source.directory 为根。原始采集结果保持只读。

在 Windows PowerShell 7 中运行（目标目录必须不存在；先创建一个明确的空父目录）：

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -File .\Build-Review.ps1 -OutputDirectory D:\source\Sezika\.artifacts\my-review-parent\probe -MaximumRecords 1 -TimeoutSeconds 120
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -File .\Build-Review.ps1 -OutputDirectory D:\source\Sezika\.artifacts\my-review-parent\complete -MaximumRecords 32 -TimeoutSeconds 120
```

运行前确认 $PSVersionTable.PSVersion 为 7 或更高。生成器固定核对已验收源身份，最多 32 题/64 个引用、120 秒，支持 CancellationFile 和 CancellationToken；Ctrl+C 也可终止。存在的目标一律拒绝覆盖。输出使用 UTF-8 无 BOM、LF；失败仅清理自己创建的唯一临时目录。没有网络请求、教师/GPU 调用或标签猜测。
'@
    # These four files plus manifest form the exact published package.
    $generatorBytes = Read-Bytes $PSCommandPath 131072
    Require ($generatorBytes.Length -gt 0 -and -not ($generatorBytes.Length -ge 3 -and $generatorBytes[0] -eq 239 -and $generatorBytes[1] -eq 187 -and $generatorBytes[2] -eq 191)) 'Generator must be UTF-8 without BOM.'
    Require (-not $utf8.GetString($generatorBytes).Contains("`r", [StringComparison]::Ordinal)) 'Generator must use fixed LF.'
    [IO.File]::WriteAllBytes([IO.Path]::Combine($stage, 'Build-Review.ps1'), $generatorBytes)
    Write-Text 'README.md' ($readme.Replace("`r`n", "`n") + "`n")
    Write-Text 'review.md' $markdown.ToString()
    Write-Text 'review-template.jsonl' $jsonl.ToString()
    $outputs = [Collections.Generic.List[object]]::new(4)
    foreach ($fileName in @('Build-Review.ps1','README.md','review.md','review-template.jsonl')) { Guard; $bytes = Read-Bytes ([IO.Path]::Combine($stage, $fileName)) 1048576; $outputs.Add([ordered]@{path=$fileName;sha256=(Sha $bytes);bytes=$bytes.Length}) }
    $packageManifest = [ordered]@{
        schema_version=1; status='pending_human_review'; record_count=$MaximumRecords; family_count=$MaximumRecords; group_counts=$groups; referenced_file_count=$references.Count
        training_admission=$false; quality_evidence=$false; sealed_test_eligible=$false; teacher_answer_exposed=$true; eligible_as_sealed_test=$false; human_answers_filled=0; output_training_permitted=$false; teacher_independently_verified=$false
        generator=[ordered]@{path='Build-Review.ps1';sha256=(Sha $generatorBytes);maximum_records=32;maximum_referenced_files=64;timeout_seconds=$TimeoutSeconds;encoding='utf-8-no-bom';newline='LF'}
        source=[ordered]@{directory=$inputRoot;manifest=[ordered]@{path='manifest.json';sha256=$manifestHash;bytes=$sourceManifestBytes.Length};records=[ordered]@{path='records.jsonl';sha256=$recordsHash;bytes=$sourceRecordsBytes.Length};total=32;families=32;group_counts=$sourceGroups;teacher=$sourceManifest.teacher}
        references=$references.ToArray(); outputs=$outputs.ToArray()
    }
    Write-Text 'manifest.json' (($packageManifest | ConvertTo-Json -Depth 32 -Compress) + "`n")
    Guard
    [IO.Directory]::Move($stage, $output)
    $ownsStage = $false
    Write-Host ('Review package complete: records={0}; references={1}; elapsed_seconds={2:F3}; output={3}' -f $MaximumRecords, $references.Count, $clock.Elapsed.TotalSeconds, $output)
}
finally {
    if ($ownsStage -and $stage) {
        $resolved = [IO.Path]::GetFullPath($stage)
        if ($resolved -cne $stage -or [IO.Path]::GetDirectoryName($resolved) -cne $parent -or [IO.Path]::GetFileName($resolved) -notmatch ('^\.' + [Regex]::Escape($name) + '\.review-[0-9a-f]{32}\.tmp$')) { throw 'Refusing cleanup outside this task-owned stage.' }
        if ([IO.Directory]::Exists($resolved)) { [IO.Directory]::Delete($resolved, $true) }
    }
}
