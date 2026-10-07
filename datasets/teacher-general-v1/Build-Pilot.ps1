param(
    [ValidateRange(1, 32)][int]$MaximumRecords = 32,
    [ValidateRange(1, 60)][int]$TimeoutSeconds = 10,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7+ required.' }
$root = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$draftPath = Join-Path $root 'drafts.jsonl'
$sourceId = 'sezika-ai-drafted-general-pilot-20261008'
$studentModelId = 'sezika/mmbert-base-independent-s4'
$watch = [Diagnostics.Stopwatch]::StartNew()
$cancel = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($TimeoutSeconds))
$created = [Collections.Generic.List[string]]::new()
$completed = $false

function Check-Budget {
    $cancel.Token.ThrowIfCancellationRequested()
    if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw 'Pilot assembly wall-clock budget expired.' }
}

function Hash-Bytes([byte[]]$bytes) {
    Check-Budget
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Write-NewUtf8([string]$relativePath, [string]$text) {
    Check-Budget
    $path = [IO.Path]::GetFullPath((Join-Path $root $relativePath))
    if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output path escaped candidate package.' }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($text)
    $file = [IO.FileStream]::new($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $created.Add($path)
    try { $file.Write($bytes) } finally { $file.Dispose() }
    return [ordered]@{ path = $relativePath.Replace('\', '/'); sha256 = (Hash-Bytes $bytes); bytes = $bytes.Length }
}

function Assert-Draft($item) {
    Check-Budget
    if ($item.record_id -notmatch '^general-(zh|en)-(choice|score|boolean)-[0-9]{2}$' -or
        $item.language -notin @('zh', 'en') -or $item.type -notin @('choice', 'score', 'boolean') -or
        $item.family_id -notmatch '^general-(zh|en)-[a-z0-9-]+$' -or [string]::IsNullOrWhiteSpace($item.topic) -or
        $item.state -isnot [System.Collections.IDictionary] -or [string]::IsNullOrWhiteSpace($item.state.text)) { throw 'Invalid candidate identity/state.' }
    $question = $item.question
    if ($question.type -cne $item.type -or [string]::IsNullOrWhiteSpace($question.id) -or [string]::IsNullOrWhiteSpace($question.instruction)) { throw 'Invalid decision-v1 question.' }
    $allowed = @('id', 'type', 'instruction')
    if ($item.type -eq 'choice') {
        $allowed += 'candidates'
        if ($question.candidates.Count -lt 2 -or $question.candidates.Count -gt 32) { throw 'Choice count outside decision-v1 budget.' }
        $candidateIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        for ($index = 0; $index -lt $question.candidates.Count -and $index -lt 32; $index++) {
            Check-Budget
            $candidate = $question.candidates[$index]
            if ($candidate.Count -ne 2 -or -not $candidate.Contains('id') -or -not $candidate.Contains('text') -or
                [string]::IsNullOrWhiteSpace($candidate.id) -or [string]::IsNullOrWhiteSpace($candidate.text) -or -not $candidateIds.Add($candidate.id)) { throw 'Invalid choice candidate.' }
        }
    } elseif ($item.type -eq 'score') {
        $allowed += 'levels'
        if ($question.levels.Count -lt 2 -or $question.levels.Count -gt 10) { throw 'Score count outside decision-v1 budget.' }
        for ($index = 0; $index -lt $question.levels.Count -and $index -lt 10; $index++) {
            Check-Budget
            if ([string]::IsNullOrWhiteSpace($question.levels[$index])) { throw 'Empty score level.' }
        }
    } else {
        $allowed += @('statement', 'when_false', 'when_true')
        if ([string]::IsNullOrWhiteSpace($question.statement) -or [string]::IsNullOrWhiteSpace($question.when_false) -or [string]::IsNullOrWhiteSpace($question.when_true)) { throw 'Invalid boolean text.' }
    }
    if ($question.Count -ne $allowed.Count) { throw 'Unexpected question property count.' }
    foreach ($key in $allowed) {
        Check-Budget
        if (-not $question.Contains($key)) { throw 'Missing or unknown question property.' }
    }
}

try {
    $draftInfo = [IO.FileInfo]::new($draftPath)
    if (-not $draftInfo.Exists -or $draftInfo.Length -le 0 -or $draftInfo.Length -gt 131072) { throw 'Draft input byte budget exceeded.' }
    $draftBytes = [IO.File]::ReadAllBytes($draftPath)
    $draftText = [Text.UTF8Encoding]::new($false, $true).GetString($draftBytes)
    $lines = $draftText.TrimEnd("`r", "`n").Split("`n")
    if ($lines.Count -ne 32) { throw 'Exactly 32 draft lines required.' }
    $drafts = [Collections.Generic.List[object]]::new()
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $families = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $counts = [ordered]@{ zh = [ordered]@{ choice = 0; score = 0; boolean = 0 }; en = [ordered]@{ choice = 0; score = 0; boolean = 0 } }
    # Reviewed exit conditions: at most MaximumRecords<=32 and one shared wall-clock deadline.
    for ($index = 0; $index -lt $MaximumRecords -and $index -lt 32; $index++) {
        Check-Budget
        $item = $lines[$index] | ConvertFrom-Json -AsHashtable -Depth 16
        Assert-Draft $item
        if (-not $ids.Add($item.record_id) -or -not $families.Add($item.family_id)) { throw 'Duplicate record or family identity.' }
        $counts[$item.language][$item.type]++
        $drafts.Add($item)
        if ($index -eq 0 -or (($index + 1) % 8) -eq 0) { Write-Output "Draft validation $($index + 1)/$MaximumRecords" }
    }
    if ($ValidateOnly) {
        Write-Output "Structural pilot passed: records=$MaximumRecords; teacher_called=false; human_review=pending; elapsed=$($watch.Elapsed.TotalSeconds)"
        $completed = $true
        return
    }
    if ($MaximumRecords -ne 32 -or $counts.zh.choice -ne 6 -or $counts.en.choice -ne 6 -or
        $counts.zh.score -ne 5 -or $counts.en.score -ne 5 -or $counts.zh.boolean -ne 5 -or $counts.en.boolean -ne 5) { throw 'Full assembly must match the 32-question bilingual allocation.' }
    $records = [Collections.Generic.List[string]]::new()
    $assets = [Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $drafts.Count -and $index -lt 32; $index++) {
        Check-Budget
        $item = $drafts[$index]
        $request = [ordered]@{ schema_version = 1; model_id = $studentModelId; state = $item.state; questions = @($item.question) }
        $requestJson = $request | ConvertTo-Json -Depth 16 -Compress
        $requestRelative = 'requests/' + $item.record_id + '.json'
        $promptRelative = 'prompts/' + $item.record_id + '.txt'
        $requestAsset = Write-NewUtf8 $requestRelative $requestJson
        $assets.Add($requestAsset)
        $mapping = if ($item.type -eq 'choice') { @($item.question.candidates | ForEach-Object { $_.id }) } elseif ($item.type -eq 'score') { @(0..($item.question.levels.Count - 1) | ForEach-Object { [string]$_ }) } else { @('false', 'true') }
        $mappingText = $mapping -join ', '
        if ($item.language -eq 'zh') {
            $prompt = @(
                '你是离线教师，正在回答一条未经人工审核的通用文本 pilot 候选题。'
                '只依据请求中的 state、instruction 和候选定义作答；不调用工具，不补充外部事实。'
                'request.model_id 是学生请求身份，不是教师身份。'
                ('仅可选 candidate_id：' + $mappingText + '。Score 的 candidate_id 是 levels 的零起始索引；Boolean 使用 false/true。')
                '只输出一个 JSON 对象，字段为 question_id、status、selected_candidate_id。status 只能为 answered 或 abstained。'
                '能唯一判定时返回 answered 和一个候选 ID；无法判定时返回 abstained，selected_candidate_id 为 null。'
                '不要输出解释、正确率、自报置信度或概率。教师回答须另经人工复核，不能自行批准进入训练。'
                'decision-v1 request:'
                $requestJson
            ) -join "`n"
        } else {
            $prompt = @(
                'You are an offline teacher answering an unreviewed general-text pilot candidate.'
                'Use only the request state, instruction, and candidate definitions. Do not use tools or add external facts.'
                'request.model_id identifies the student request, not the teacher.'
                ('Allowed candidate_id values: ' + $mappingText + '. For Score use the zero-based levels index; for Boolean use false/true.')
                'Return one JSON object with question_id, status, and selected_candidate_id. status must be answered or abstained.'
                'If a unique decision can be made, return answered and one candidate ID. Otherwise return abstained with selected_candidate_id null.'
                'Do not return explanations, accuracy claims, self-reported confidence, or probabilities. Teacher answers require separate human review before training.'
                'decision-v1 request:'
                $requestJson
            ) -join "`n"
        }
        $promptAsset = Write-NewUtf8 $promptRelative ($prompt + "`n")
        $assets.Add($promptAsset)
        $record = [ordered]@{ record_id = $item.record_id; language = $item.language; type = $item.type; family_id = $item.family_id;
            source = $sourceId; origin = 'AI drafted'; purpose = 'pilot'; split = 'audit'; review_status = 'pending';
            training_admission = 'pending'; teacher_status = 'not_called'; topic = $item.topic;
            request_path = $requestAsset.path; request_sha256 = $requestAsset.sha256; prompt_path = $promptAsset.path; prompt_sha256 = $promptAsset.sha256 }
        $records.Add(($record | ConvertTo-Json -Depth 8 -Compress))
        if ($index -eq 0 -or (($index + 1) % 8) -eq 0) { Write-Output "Candidate assembly $($index + 1)/32" }
    }
    $indexAsset = Write-NewUtf8 'candidates.jsonl' (($records.ToArray() -join "`n") + "`n")
    $manifest = [ordered]@{ schema_version = 1; dataset_id = 'teacher-general-v1-20261008'; protocol = 'decision-v1'; student_model_id = $studentModelId;
        total_records = 32; counts = $counts; source = $sourceId; origin = 'AI drafted'; purpose = 'pilot'; split = 'audit';
        human_review = 'pending'; training_admission = 'pending'; quality_evidence = $false; teacher_called = $false;
        license_review = 'pending'; teacher_identity = $null; teacher_responses_present = $false; gold_labels_present = $false;
        source_drafts = [ordered]@{ path = 'drafts.jsonl'; sha256 = (Hash-Bytes $draftBytes); bytes = $draftBytes.Length };
        generator = [ordered]@{ path = 'Build-Pilot.ps1'; sha256 = (Hash-Bytes ([IO.File]::ReadAllBytes($PSCommandPath))) };
        candidates = $indexAsset; assets = @($assets.ToArray());
        family_policy = 'Each base candidate has a unique family_id. Any later translation, paraphrase, counterfactual or entity variant inherits its base family and must stay in one split. This exposed pilot is audit-only and is not sealed_test.';
        response_contract = [ordered]@{ fields = @('question_id', 'status', 'selected_candidate_id'); status_values = @('answered', 'abstained'); score_candidate_id = 'zero-based levels index'; boolean_candidate_ids = @('false', 'true'); validation_status = 'proposal_pending_collector_implementation' } }
    [void](Write-NewUtf8 'manifest.json' (($manifest | ConvertTo-Json -Depth 12) + "`n"))
    $report = [ordered]@{ schema_version = 1; status = 'structural_checks_passed'; maximum_records = 32; actual_records = $records.Count;
        unique_families = $families.Count; generated_request_prompt_files = $assets.Count; counts = $counts;
        elapsed_seconds = $watch.Elapsed.TotalSeconds; powershell_version = $PSVersionTable.PSVersion.ToString();
        verification_scope = 'PowerShell structural checks, UTF-8 bytes and SHA-256; actual C# parser/teacher call/human review/quality not executed';
        teacher_called = $false; human_review = 'pending'; quality_evidence = $false }
    [void](Write-NewUtf8 'assembly-validation.json' (($report | ConvertTo-Json -Depth 8) + "`n"))
    $completed = $true
    Write-Output "Pilot package assembled: records=32; request/prompt files=64; elapsed=$($watch.Elapsed.TotalSeconds); teacher_called=false"
} finally {
    if (-not $completed) {
        $cleanup = [Diagnostics.Stopwatch]::StartNew()
        if ($created.Count -gt 67) { throw 'Created-file cleanup item bound exceeded.' }
        for ($index = 0; $index -lt $created.Count -and $index -lt 67; $index++) {
            if ($cleanup.Elapsed.TotalSeconds -ge 10) { throw 'Created-file cleanup timeout.' }
            $path = [IO.Path]::GetFullPath($created[$index])
            if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Created-file cleanup ownership boundary failed.' }
            if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) }
        }
    }
    $cancel.Dispose()
}
