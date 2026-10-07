param(
    [ValidateRange(1, 32)][int]$MaximumRecords = 32,
    [ValidateRange(1, 120)][int]$TimeoutSeconds = 120,
    [string]$OutputDirectory,
    [string]$CancelFile
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7+ required.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $repository 'datasets/teacher-general-v1'))
$targetRoot = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { [IO.Path]::GetFullPath($PSScriptRoot) }
if (-not $targetRoot.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $targetRoot.StartsWith($sourceRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be a new task-owned directory within this repository, outside v1.' }
$sourceManifestHash = 'b2ab11e5cabf1967b87e5a06fa9d0e270a1aec0a6df8b8f55e3c670f767fa8fa'
$sourceCandidatesHash = '0e9852639587417bfabf2ac04f43555cf82dbb03ba57e03b27febbafcd50b0d8'
$contractId = 'teacher-decision-json-string-id-v2'
$utf8 = [Text.UTF8Encoding]::new($false, $true)
$watch = [Diagnostics.Stopwatch]::StartNew()
$cancellation = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($TimeoutSeconds))
$created = [Collections.Generic.List[object]]::new()
$createdDirectories = [Collections.Generic.List[string]]::new()
$completed = $false
$cleanupErrors = [Collections.Generic.List[string]]::new()

function Check-Budget {
    $cancellation.Token.ThrowIfCancellationRequested()
    if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw 'Prompt v2 preparation deadline expired.' }
    if ($CancelFile -and [IO.File]::Exists([IO.Path]::GetFullPath($CancelFile))) { throw 'Prompt v2 preparation cancelled.' }
}
function Hash-Bytes([byte[]]$bytes) {
    Check-Budget
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}
function Contained-Path([string]$base, [string]$relative) {
    Check-Budget
    if ($relative -notmatch '^[a-zA-Z0-9_./-]+$' -or $relative.Contains('..')) { throw 'Unexpected relative asset path.' }
    $path = [IO.Path]::GetFullPath((Join-Path $base $relative))
    if (-not $path.StartsWith($base + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Asset escapes package.' }
    $cursor = $path
    # Reviewed: at most eight named path components, with the shared wall-clock bound.
    for ($depth = 0; $depth -lt 8; $depth++) {
        Check-Budget
        if ([IO.File]::Exists($cursor) -or [IO.Directory]::Exists($cursor)) {
            if (([IO.File]::GetAttributes($cursor) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse point is not allowed.' }
        }
        if ($cursor -eq $base) { return $path }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    throw 'Asset ancestor depth limit exceeded.'
}
function Read-Bound([string]$relative, [string]$hash, [long]$length, [int]$limit = 1048576) {
    $path = Contained-Path $sourceRoot $relative
    $info = [IO.FileInfo]::new($path)
    if (-not $info.Exists -or $info.Length -ne $length -or $length -le 0 -or $length -gt $limit) { throw ('Source byte bound differs: ' + $relative) }
    $bytes = [IO.File]::ReadAllBytes($path)
    if ((Hash-Bytes $bytes) -cne $hash) { throw ('Source hash differs: ' + $relative) }
    return ,$bytes
}
function Write-NewBytes([string]$relative, [byte[]]$bytes) {
    Check-Budget
    if ($created.Count -ge 100) { throw 'Created-file bound exceeded.' }
    $path = Contained-Path $targetRoot $relative
    $directory = [IO.Path]::GetDirectoryName($path)
    if (-not [IO.Directory]::Exists($directory)) {
        if ($createdDirectories.Count -ge 8) { throw 'Created-directory bound exceeded.' }
        [void][IO.Directory]::CreateDirectory($directory)
        $createdDirectories.Add($directory)
    }
    $file = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $entry = [ordered]@{ path = $path; sha256 = (Hash-Bytes $bytes); bytes = $bytes.Length }
    $created.Add($entry)
    try { $file.Write($bytes) } finally { $file.Dispose() }
    return [ordered]@{ path = $relative.Replace('\', '/'); sha256 = $entry.sha256; bytes = $entry.bytes }
}
function Write-NewText([string]$relative, [string]$text) { return Write-NewBytes $relative ($utf8.GetBytes($text)) }

try {
    $sourceManifestPath = Join-Path $sourceRoot 'manifest.json'
    $sourceManifestInfo = [IO.FileInfo]::new($sourceManifestPath)
    $sourceManifestBytes = Read-Bound 'manifest.json' $sourceManifestHash $sourceManifestInfo.Length
    $sourceManifest = $utf8.GetString($sourceManifestBytes) | ConvertFrom-Json -AsHashtable -Depth 24
    if ($sourceManifest.dataset_id -cne 'teacher-general-v1-20261008' -or $sourceManifest.total_records -ne 32 -or
        $sourceManifest.assets.Count -ne 64 -or $sourceManifest.origin -cne 'AI drafted' -or $sourceManifest.split -cne 'audit' -or
        $sourceManifest.human_review -cne 'pending' -or $sourceManifest.training_admission -cne 'pending' -or
        $sourceManifest.teacher_called -ne $false -or $sourceManifest.teacher_responses_present -ne $false -or
        $sourceManifest.gold_labels_present -ne $false -or $sourceManifest.quality_evidence -ne $false) { throw 'Source v1 status/denominator differs.' }
    $sourceCandidatesBytes = Read-Bound $sourceManifest.candidates.path $sourceCandidatesHash $sourceManifest.candidates.bytes
    if ($sourceManifest.candidates.sha256 -cne $sourceCandidatesHash) { throw 'Source manifest candidates hash differs.' }
    $sourceLines = $utf8.GetString($sourceCandidatesBytes).TrimEnd("`n").Split("`n")
    if ($sourceLines.Count -ne 32) { throw 'Source index must contain 32 lines.' }
    $assetMap = @{}
    # Reviewed: exactly 64 source assets, with cancellation and deadline on every iteration.
    for ($index = 0; $index -lt $sourceManifest.assets.Count -and $index -lt 64; $index++) {
        Check-Budget
        $asset = $sourceManifest.assets[$index]
        if ($assetMap.ContainsKey($asset.path)) { throw 'Duplicate source asset.' }
        $assetMap[$asset.path] = $asset
    }
    $sourceSnapshot = Write-NewBytes 'provenance/v1-manifest.json' $sourceManifestBytes
    $candidateSnapshot = Write-NewBytes 'provenance/v1-candidates.jsonl' $sourceCandidatesBytes
    $contract = [ordered]@{
        schema_version = 1; contract_id = $contractId; version = 2;
        source_v1 = [ordered]@{ dataset_id = $sourceManifest.dataset_id; repository_path = 'datasets/teacher-general-v1/manifest.json'; sha256 = $sourceManifestHash; bytes = $sourceManifestBytes.Length; candidates_sha256 = $sourceCandidatesHash };
        scope = 'Prompt-only revision; request bytes, record_id, family_id, candidate ordering and audit split unchanged.';
        fields = @('question_id', 'status', 'selected_candidate_id'); status_values = @('answered', 'abstained');
        answered_selected_candidate_id_json_type = 'string'; abstained_selected_candidate_id_json_type = 'null';
        choice_candidate_id = 'Original candidate ID, encoded as JSON string.';
        score_candidate_id = 'Zero-based levels index, encoded as JSON string (e.g. "3", never JSON number 3).';
        boolean_candidate_ids = @('false', 'true'); boolean_candidate_id_json_type = 'string ("false" or "true", never JSON boolean false or true)';
        example_policy = 'Placeholders describe format only; they are not an answer, candidate, label or gold.';
        origin = 'AI drafted'; purpose = 'pilot'; split = 'audit'; human_review = 'pending'; training_admission = 'pending';
        teacher_called = $false; teacher_responses_present = $false; gold_labels_present = $false; quality_evidence = $false;
        validation_status = 'prompt_preparation_only_real_teacher_not_called'
    }
    $contractAsset = Write-NewText 'prompt_contract.json' (($contract | ConvertTo-Json -Depth 16) + "`n")
    $records = [Collections.Generic.List[string]]::new()
    $assets = [Collections.Generic.List[object]]::new()
    $checks = [Collections.Generic.List[object]]::new()
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $families = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $counts = [ordered]@{ zh = [ordered]@{ choice = 0; score = 0; boolean = 0 }; en = [ordered]@{ choice = 0; score = 0; boolean = 0 } }
    # Reviewed: maximum 32 records; all exit conditions use comparisons, not assignment.
    for ($index = 0; $index -lt $MaximumRecords -and $index -lt 32; $index++) {
        Check-Budget
        $row = $sourceLines[$index] | ConvertFrom-Json -AsHashtable -Depth 16
        if (-not $ids.Add($row.record_id) -or -not $families.Add($row.family_id) -or
            $row.language -notin @('zh', 'en') -or $row.type -notin @('choice', 'score', 'boolean') -or
            $row.origin -cne 'AI drafted' -or $row.purpose -cne 'pilot' -or $row.split -cne 'audit' -or
            $row.review_status -cne 'pending' -or $row.training_admission -cne 'pending' -or $row.teacher_status -cne 'not_called') { throw 'Source candidate identity/status differs.' }
        $requestInfo = $assetMap[$row.request_path]
        $oldPromptInfo = $assetMap[$row.prompt_path]
        if ($null -eq $requestInfo -or $null -eq $oldPromptInfo -or $row.request_sha256 -cne $requestInfo.sha256 -or $row.prompt_sha256 -cne $oldPromptInfo.sha256) { throw 'Source candidate asset binding differs.' }
        $requestBytes = Read-Bound $row.request_path $requestInfo.sha256 $requestInfo.bytes 65536
        $oldPromptBytes = Read-Bound $row.prompt_path $oldPromptInfo.sha256 $oldPromptInfo.bytes 65536
        $requestText = $utf8.GetString($requestBytes)
        $request = $requestText | ConvertFrom-Json -AsHashtable -Depth 16
        if ($request.questions.Count -ne 1 -or $request.questions[0].type -cne $row.type -or $request.model_id -cne $sourceManifest.student_model_id) { throw 'Source question identity differs.' }
        $question = $request.questions[0]
        $mapping = [Collections.Generic.List[string]]::new()
        if ($row.type -eq 'choice') {
            if ($question.candidates.Count -lt 2 -or $question.candidates.Count -gt 32) { throw 'Choice candidate count outside budget.' }
            for ($item = 0; $item -lt $question.candidates.Count -and $item -lt 32; $item++) { Check-Budget; $mapping.Add([string]$question.candidates[$item].id) }
        } elseif ($row.type -eq 'score') {
            if ($question.levels.Count -lt 2 -or $question.levels.Count -gt 10) { throw 'Score level count outside budget.' }
            for ($item = 0; $item -lt $question.levels.Count -and $item -lt 10; $item++) { Check-Budget; $mapping.Add([string]$item) }
        } else { $mapping.Add('false'); $mapping.Add('true') }
        $mappingText = ConvertTo-Json -InputObject $mapping.ToArray() -Compress
        if ($row.language -eq 'zh') {
            $instructions = @(
                '你是离线教师，正在回答一条未经人工审核的通用文本 pilot 候选题。'
                '只依据请求中的 state、instruction 和候选定义作答；不调用工具，不补充外部事实。'
                'request.model_id 是学生请求身份，不是教师身份。'
                ('候选 ID 按原顺序列为 JSON 字符串数组：' + $mappingText + '。只允许选择其中一个字符串 ID。')
                '对于 Choice、Score、Boolean 所有题型，answered 时 selected_candidate_id 必须是带双引号的 JSON 字符串。'
                'Score ID 是 levels 的零起始索引字符串，例如 "3"，禁止输出 JSON 数字 3；这只说明编码格式，并未指定本题答案。'
                'Boolean ID 只允许字符串 "false" 或 "true"，禁止输出 JSON 布尔值 false 或 true；这只说明编码格式，并未指定本题答案。'
                '只输出一个严格 JSON 对象，恰好包含 question_id、status、selected_candidate_id 三个字段；不加代码围栏或其他文本。'
                'question_id 必须是与请求中题目 id 完全一致的 JSON 字符串；status 只能为字符串 "answered" 或 "abstained"。'
                '能唯一判定时返回 "answered" 和合法字符串候选 ID；无法判定时返回 "abstained"，selected_candidate_id 必须为 JSON null。'
                '格式模板：{"question_id":"<题目ID占位符>","status":"answered","selected_candidate_id":"<候选ID占位符>"}。模板中的占位符仅展示格式，不是答案、候选、标签或金标，禁止照抄。'
                '不要输出解释、正确率、自报置信度或概率。教师回答须另经人工复核，不能自行批准进入训练。'
                'decision-v1 request:'
            )
        } else {
            $instructions = @(
                'You are an offline teacher answering an unreviewed general-text pilot candidate.'
                'Use only the request state, instruction, and candidate definitions. Do not use tools or add external facts.'
                'request.model_id identifies the student request, not the teacher.'
                ('Candidate IDs in original order, encoded as a JSON array of strings: ' + $mappingText + '. Select only one of these string IDs.')
                'For ALL Choice, Score and Boolean questions, selected_candidate_id for answered MUST be a JSON string enclosed in double quotes.'
                'Score IDs are zero-based levels index strings: for example "3", NEVER the JSON number 3. This describes encoding only and does not select the answer to this question.'
                'Boolean IDs are ONLY the strings "false" or "true", NEVER the JSON booleans false or true. This describes encoding only and does not select the answer to this question.'
                'Return exactly one strict JSON object with exactly three fields: question_id, status, selected_candidate_id. No code fences or surrounding text.'
                'question_id MUST be a JSON string identical to the request question id. status MUST be the string "answered" or "abstained".'
                'If a unique decision can be made, return "answered" and one allowed string ID. Otherwise return "abstained" and JSON null for selected_candidate_id.'
                'Format template: {"question_id":"<QUESTION_ID_PLACEHOLDER>","status":"answered","selected_candidate_id":"<CANDIDATE_ID_PLACEHOLDER>"}. The placeholders illustrate format only; they are not an answer, candidate, label or gold. Never copy the placeholders.'
                'Do not return explanations, accuracy claims, self-reported confidence, or probabilities. Teacher answers require separate human review before training.'
                'decision-v1 request:'
            )
        }
        $promptText = ($instructions -join "`n") + "`n" + $requestText + "`n"
        if (-not $promptText.Contains($requestText, [StringComparison]::Ordinal)) { throw 'Exact request snapshot missing from prompt.' }
        $requestAsset = Write-NewBytes $row.request_path $requestBytes
        $promptAsset = Write-NewText $row.prompt_path $promptText
        if ($promptAsset.sha256 -ceq $oldPromptInfo.sha256) { throw 'Prompt v2 unexpectedly identical to v1.' }
        $assets.Add($requestAsset); $assets.Add($promptAsset)
        $row.request_bytes = $requestAsset.bytes
        $row.prompt_sha256 = $promptAsset.sha256; $row.prompt_bytes = $promptAsset.bytes
        $row.prompt_contract_id = $contractId; $row.prompt_contract_sha256 = $contractAsset.sha256
        $row.source_v1_manifest_sha256 = $sourceManifestHash
        $row.source_v1_prompt_sha256 = $oldPromptInfo.sha256; $row.source_v1_prompt_bytes = $oldPromptBytes.Length
        $records.Add(($row | ConvertTo-Json -Depth 16 -Compress))
        $counts[$row.language][$row.type]++
        $checks.Add([ordered]@{ record_id = $row.record_id; family_id = $row.family_id; request_bytes_unchanged = $true; request_sha256 = $requestAsset.sha256; prompt_sha256 = $promptAsset.sha256; prompt_bytes = $promptAsset.bytes; candidate_ids = $mapping.ToArray(); prompt_contains_exact_request = $true })
        if ($index -eq 0 -or (($index + 1) % 8) -eq 0) { Write-Output "Prompt v2 preparation $($index + 1)/$MaximumRecords" }
    }
    if ($MaximumRecords -eq 32 -and ($counts.zh.choice -ne 6 -or $counts.en.choice -ne 6 -or $counts.zh.score -ne 5 -or $counts.en.score -ne 5 -or $counts.zh.boolean -ne 5 -or $counts.en.boolean -ne 5)) { throw 'Bilingual allocation differs.' }
    $indexAsset = Write-NewText 'candidates.jsonl' (($records.ToArray() -join "`n") + "`n")
    $generatorBytes = [IO.File]::ReadAllBytes($PSCommandPath)
    $manifest = [ordered]@{
        schema_version = 1; dataset_id = if ($MaximumRecords -eq 32) { 'teacher-general-v2-20261008' } else { 'teacher-general-v2-20261008-trial' };
        protocol = 'decision-v1'; student_model_id = $sourceManifest.student_model_id; total_records = $MaximumRecords; counts = $counts;
        source = $sourceManifest.source; origin = 'AI drafted'; purpose = 'pilot'; split = 'audit'; human_review = 'pending'; training_admission = 'pending';
        quality_evidence = $false; teacher_called = $false; license_review = 'pending'; teacher_identity = $null; teacher_responses_present = $false; gold_labels_present = $false;
        source_v1 = [ordered]@{ dataset_id = $sourceManifest.dataset_id; repository_path = 'datasets/teacher-general-v1/manifest.json'; sha256 = $sourceManifestHash; bytes = $sourceManifestBytes.Length; manifest_snapshot = $sourceSnapshot; candidates_snapshot = $candidateSnapshot };
        prompt_contract = $contractAsset; generator = [ordered]@{ repository_path = 'datasets/teacher-general-v2/Build-PromptV2.ps1'; sha256 = (Hash-Bytes $generatorBytes); bytes = $generatorBytes.Length };
        candidates = $indexAsset; assets = $assets.ToArray(); family_policy = $sourceManifest.family_policy;
        response_contract = [ordered]@{ fields = @('question_id', 'status', 'selected_candidate_id'); status_values = @('answered', 'abstained'); answered_selected_candidate_id_json_type = 'string'; abstained_selected_candidate_id_json_type = 'null'; score_candidate_id = 'zero-based levels index string'; boolean_candidate_ids = @('false', 'true'); validation_status = 'prompt_preparation_only_real_teacher_not_called' }
    }
    $manifestAsset = Write-NewText 'manifest.json' (($manifest | ConvertTo-Json -Depth 20) + "`n")
    # Independent reread of every self-created asset, <=69 files and shared deadline.
    for ($index = 0; $index -lt $created.Count -and $index -lt 100; $index++) {
        Check-Budget
        $entry = $created[$index]
        $actual = [IO.File]::ReadAllBytes($entry.path)
        if ($actual.Length -ne $entry.bytes -or (Hash-Bytes $actual) -cne $entry.sha256) { throw 'Generated asset reread differs.' }
    }
    if ((Hash-Bytes ([IO.File]::ReadAllBytes($sourceManifestPath))) -cne $sourceManifestHash -or
        (Hash-Bytes ([IO.File]::ReadAllBytes((Join-Path $sourceRoot 'candidates.jsonl')))) -cne $sourceCandidatesHash) { throw 'Source v1 changed during preparation.' }
    $report = [ordered]@{
        schema_version = 1; status = 'prompt_preparation_verified'; actual_records = $MaximumRecords; unique_families = $families.Count;
        request_prompt_files = $assets.Count; counts = $counts; manifest_sha256 = $manifestAsset.sha256; candidates_sha256 = $indexAsset.sha256; prompt_contract_sha256 = $contractAsset.sha256;
        source_v1_manifest_sha256 = $sourceManifestHash; source_v1_candidates_sha256 = $sourceCandidatesHash;
        source_v1_unchanged = $true; request_bytes_unchanged = $true; record_and_family_ids_unchanged = $true; candidate_order_unchanged = $true;
        verified_self_created_files_before_report = $created.Count; maximum_created_files = 100; maximum_records = 32; timeout_seconds = $TimeoutSeconds; elapsed_seconds = $watch.Elapsed.TotalSeconds;
        powershell_version = $PSVersionTable.PSVersion.ToString(); teacher_called = $false; teacher_responses_present = $false; gold_labels_present = $false; quality_evidence = $false; human_review = 'pending'; training_admission = 'pending';
        verification_scope = 'UTF-8, hash/bytes, exact source request and prompt contract preparation only; no C# parser, teacher call, GPU, human review or answer-quality evaluation.';
        records = $checks.ToArray()
    }
    [void](Write-NewText 'file-verification.json' (($report | ConvertTo-Json -Depth 20) + "`n"))
    $completed = $true
    Write-Output "Prompt v2 prepared: records=$MaximumRecords; files=$($created.Count); elapsed=$($watch.Elapsed.TotalSeconds); manifest=$($manifestAsset.sha256); teacher_called=false"
} finally {
    $cancellation.Dispose()
    if (-not $completed) {
        $cleanupWatch = [Diagnostics.Stopwatch]::StartNew()
        # Failure rollback is restricted to recorded CreateNew paths and exact hash/bytes.
        for ($index = 0; $index -lt $created.Count -and $index -lt 100; $index++) {
            if ($cleanupWatch.Elapsed.TotalSeconds -ge 10) { $cleanupErrors.Add('Cleanup deadline expired.'); break }
            $entry = $created[$index]
            $path = [IO.Path]::GetFullPath($entry.path)
            if (-not $path.StartsWith($targetRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { $cleanupErrors.Add('Cleanup ownership mismatch.'); continue }
            if ([IO.File]::Exists($path)) {
                $info = [IO.FileInfo]::new($path)
                $actualHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($path))).ToLowerInvariant()
                if (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and $info.Length -eq $entry.bytes -and $actualHash -ceq $entry.sha256) { [IO.File]::Delete($path) }
                else { $cleanupErrors.Add('Retained changed or incomplete task file: ' + $path) }
            }
        }
        for ($index = $createdDirectories.Count - 1; $index -ge 0 -and $index -lt 8; $index--) {
            if ($cleanupWatch.Elapsed.TotalSeconds -ge 10) { break }
            $path = [IO.Path]::GetFullPath($createdDirectories[$index])
            if ($path.StartsWith($targetRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and [IO.Directory]::Exists($path) -and
                ([IO.File]::GetAttributes($path) -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and @([IO.Directory]::EnumerateFileSystemEntries($path) | Select-Object -First 1).Count -eq 0) { [IO.Directory]::Delete($path, $false) }
        }
    }
    if ($cleanupErrors.Count -gt 0) { Write-Warning ($cleanupErrors -join '; ') }
}
