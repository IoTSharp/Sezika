[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateCount(1,32)][string[]]$ReportPaths,
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateRange(1,60)][int]$TimeoutSeconds = 30
)

# This validator is intentionally read-only with respect to model execution. It
# checks the bounded schema-v3 profile output and derives observations from raw
# samples; it never claims an optimisation win or a quality result.
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or newer is required.' }
$watch = [Diagnostics.Stopwatch]::StartNew()
$maximumFileBytes = 4 * 1024 * 1024
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$reports = [Collections.Generic.List[object]]::new()

function Assert-Report([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Assert-Time {
    if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw "Profile validation timeout (${TimeoutSeconds}s)." }
}
function Assert-Text($Value, [string]$Name) {
    Assert-Report ($Value -is [string] -and -not [string]::IsNullOrWhiteSpace($Value)) "Missing text: $Name."
}
function Assert-Number($Value, [string]$Name) {
    Assert-Report (($Value -is [long] -or $Value -is [int] -or $Value -is [double] -or $Value -is [decimal]) -and
        [double]::IsFinite([double]$Value) -and [double]$Value -ge 0) "Invalid nonnegative number: $Name."
}
function Assert-Integer($Value, [string]$Name, [long]$Minimum, [long]$Maximum) {
    Assert-Number $Value $Name
    Assert-Report ([double]$Value -eq [Math]::Truncate([double]$Value) -and $Value -ge $Minimum -and $Value -le $Maximum) "Invalid bounded integer: $Name."
}
function Assert-Hash($Value, [string]$Name) {
    Assert-Report ($Value -is [string] -and $Value -cmatch '^[0-9a-f]{64}$') "Invalid SHA-256: $Name."
}
function Assert-Near($Actual, [double]$Expected, [string]$Name) {
    Assert-Number $Actual $Name
    Assert-Report ([Math]::Abs([double]$Actual - $Expected) -le 1e-12 * [Math]::Max(1, [Math]::Abs($Expected))) "Derived number mismatch: $Name."
}
function Percentile([double[]]$Samples, [double]$Probability) {
    Assert-Report ($Samples.Count -gt 0) 'Cannot calculate a percentile without samples.'
    $ordered = @($Samples | Sort-Object)
    $index = [int][Math]::Ceiling($Probability * $ordered.Count) - 1
    return [double]$ordered[$index]
}
function Assert-UniqueJsonProperties([string]$Json) {
    $options = [Text.Json.JsonDocumentOptions]::new(); $options.MaxDepth = 64
    $document = [Text.Json.JsonDocument]::Parse($Json, $options)
    try {
        $pending = [Collections.Generic.Stack[Text.Json.JsonElement]]::new()
        $pending.Push($document.RootElement)
        $maximumNodes = 131072; $enumerated = 1
        for ($nodeIndex = 0; $nodeIndex -lt $maximumNodes -and $pending.Count -gt 0; $nodeIndex++) {
            Assert-Time
            $node = $pending.Pop()
            if ($node.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
                $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($property in $node.EnumerateObject()) {
                    Assert-Time
                    Assert-Report ($names.Add($property.Name)) "Duplicate JSON property: $($property.Name)."
                    Assert-Report (++$enumerated -le $maximumNodes) 'JSON node limit exceeded.'
                    $pending.Push($property.Value)
                }
            } elseif ($node.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
                foreach ($element in $node.EnumerateArray()) {
                    Assert-Time
                    Assert-Report (++$enumerated -le $maximumNodes) 'JSON node limit exceeded.'
                    $pending.Push($element)
                }
            }
        }
        Assert-Report ($pending.Count -eq 0) 'JSON node traversal limit exceeded.'
    } finally { $document.Dispose() }
}

$destination = [IO.Path]::GetFullPath($OutputPath)
Assert-Report (-not [IO.File]::Exists($destination) -and -not [IO.Directory]::Exists($destination)) 'Output already exists.'
try {
    for ($fileIndex = 0; $fileIndex -lt $ReportPaths.Count; $fileIndex++) {
        Assert-Time
        $path = [IO.Path]::GetFullPath($ReportPaths[$fileIndex])
        Assert-Report ($seen.Add($path)) "Duplicate report path: $path."
        Write-Progress -Activity '校验 S5-06 画像' -Status "$($fileIndex + 1)/$($ReportPaths.Count): $path" -PercentComplete (100 * $fileIndex / $ReportPaths.Count)
        $stream = [IO.File]::OpenRead($path)
        try {
            Assert-Report ($stream.Length -gt 0 -and $stream.Length -le $maximumFileBytes) "Report must be 1 byte..4 MiB: $path."
            $bytes = [byte[]]::new([int]$stream.Length)
            $stream.ReadExactly($bytes)
            Assert-Report ($stream.ReadByte() -eq -1) "Report grew during read: $path."
        } finally { $stream.Dispose() }
        Assert-Time
        $json = [Text.UTF8Encoding]::new($false, $true).GetString($bytes).TrimStart([char]0xFEFF)
        Assert-UniqueJsonProperties $json
        $report = $json | ConvertFrom-Json -AsHashtable -Depth 64
        Assert-Report ($report -is [Collections.IDictionary]) "Report must be a JSON object: $path."
        Assert-Integer $report['schema_version'] 'report.schema_version' 1 1
        Assert-Report ($report['schema_version'] -eq 1 -and $report['status'] -ceq 'passed' -and $report['phase'] -ceq 'complete') "Only completed schema-1 passed reports are accepted: $path."
        Assert-Text $report['backend'] 'backend'; Assert-Text $report['model_revision'] 'model_revision'; Assert-Text $report['environment_label'] 'environment_label'
        Assert-Report ($report['backend'] -cin @('scalar','simd','int8','cuda')) "Unsupported backend: $path."
        foreach ($name in @('rid','precision','runtime','cpu')) { Assert-Text $report[$name] $name }
        foreach ($name in @('weights_sha256','tokenizer_sha256','manifest_sha256','executable_sha256')) { Assert-Hash $report[$name] $name }
        Assert-Report ($report['native_aot'] -is [bool]) 'native_aot must be a JSON boolean.'
        Assert-Integer $report['samples'] 'samples' 1 30
        Assert-Integer $report['warmup'] 'warmup' 0 5
        Assert-Integer $report['inference_threads'] 'inference_threads' 1 1024
        Assert-Integer $report['request_token_budget'] 'request_token_budget' 1 1048576
        Assert-Number $report['request_deadline_seconds'] 'request_deadline_seconds'
        Assert-Report ($report['request_deadline_seconds'] -gt 0 -and $report['request_deadline_seconds'] -le 1800) 'Invalid request deadline.'
        $artifacts = @($report['code_artifacts'])
        Assert-Report ($artifacts.Count -eq $(if ($report['native_aot']) { 1 } else { 4 })) 'Incomplete code artifact identities.'
        $artifactPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($artifact in $artifacts) {
            Assert-Time
            Assert-Text $artifact['path'] 'code_artifact.path'; Assert-Hash $artifact['sha256'] 'code_artifact.sha256'
            Assert-Report ($artifactPaths.Add($artifact['path'])) 'Duplicate code artifact path.'
            Assert-Report ($artifact['role'] -cin @('process_executable','managed_assembly')) 'Invalid code artifact role.'
        }
        $executables = @($artifacts | Where-Object { $_['role'] -ceq 'process_executable' })
        Assert-Report ($executables.Count -eq 1 -and $executables[0]['sha256'] -ceq $report['executable_sha256']) 'Executable identity mismatch.'
        if (-not $report['native_aot']) {
            $assemblyNames = @($artifacts | Where-Object { $_['role'] -ceq 'managed_assembly' } | ForEach-Object { ($_.path -split '[\\/]')[-1] })
            Assert-Report (@($assemblyNames | Sort-Object -Unique).Count -eq 3 -and @($assemblyNames | Where-Object { $_ -cnotin @('Sezika.Benchmarks.dll','Sezika.dll','Sezika.Cuda.dll') }).Count -eq 0) 'Managed application identity mismatch.'
        }
        $profile = $report['profile']
        Assert-Report ($profile -is [Collections.IDictionary] -and $profile['schema_version'] -eq 3) "Missing profile schema v3: $path."
        Assert-Integer $profile['schema_version'] 'profile.schema_version' 3 3
        foreach ($name in @('planned_cases','successful_cases','rejected_cases','cases_with_stage_timings')) { Assert-Integer $profile[$name] $name 0 9 }
        Assert-Integer $profile['planned_questions'] 'planned_questions' 1 123
        Assert-Text $profile['input_set_version'] 'input_set_version'; Assert-Text $profile['rendering_version'] 'rendering_version'
        Assert-Report ($profile['detail'] -cin @('full','end_to_end')) 'Invalid profile detail.'
        $lengths = @($profile['selected_lengths']); $questions = @($profile['selected_questions']); $cases = @($profile['cases'])
        Assert-Report ($lengths.Count -ge 1 -and $lengths.Count -le 3 -and $questions.Count -ge 1 -and $questions.Count -le 3) "Invalid selected profile dimensions: $path."
        Assert-Report (@($lengths | Sort-Object -Unique).Count -eq $lengths.Count -and @($lengths | Where-Object { $_ -cnotin @('short','medium','long') }).Count -eq 0) 'Invalid or duplicate selected lengths.'
        foreach ($count in $questions) { Assert-Integer $count 'selected_questions' 1 32; Assert-Report ($count -in @(1,8,32)) 'Invalid selected question count.' }
        Assert-Report (@($questions | Sort-Object -Unique).Count -eq $questions.Count) 'Duplicate selected question counts.'
        Assert-Report ($cases.Count -eq $profile['planned_cases'] -and $cases.Count -eq ($lengths.Count * $questions.Count)) "Profile case plan mismatch: $path."
        $caseRows = [Collections.Generic.List[object]]::new()
        $caseIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $measuredQuestions = 0; $stageCases = 0
        for ($caseIndex = 0; $caseIndex -lt $cases.Count; $caseIndex++) {
            Assert-Time
            $case = $cases[$caseIndex]
            Assert-Report ($case -is [Collections.IDictionary]) 'Profile case must be an object.'
            Assert-Text $case['id'] 'case.id'; Assert-Text $case['status'] 'case.status'
            Assert-Report ($case['status'] -cin @('measured','rejected')) "Unexpected case status: $path / $($case['id'])."
            Assert-Integer $case['questions'] 'case.questions' 1 32
            Assert-Report ($case['questions'] -in $questions -and $case['length_preset'] -cin $lengths -and $case['id'] -ceq "$($case['length_preset'])-$($case['questions'])" -and $caseIds.Add($case['id'])) 'Case does not uniquely cover the declared matrix.'
            Assert-Integer $case['state_repetitions'] 'state_repetitions' 1 100
            Assert-Integer $case['candidates_per_question'] 'candidates_per_question' 2 2
            foreach ($name in @('discovery_completed_forwards','instrumented_entered_forwards','instrumented_completed_forwards')) { Assert-Integer $case[$name] $name 0 32 }
            Assert-Report ($case['state_repetitions'] -eq @{short=4;medium=20;long=100}[$case['length_preset']] -and $case['candidates_per_question'] -eq 2) 'Input preset mismatch.'
            Assert-Text $case['input_json'] 'input_json'; Assert-Hash $case['input_sha256'] 'input_sha256'
            $inputHash = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($case['input_json'])))
            Assert-Report ($inputHash -ceq $case['input_sha256']) 'Input JSON hash mismatch.'
            Assert-Number $case['tokenizer_and_rendering_milliseconds'] 'tokenizer_and_rendering_milliseconds'
            if ($case['status'] -ceq 'measured') {
                $samples = @($case['end_to_end_milliseconds'])
                Assert-Report ($samples.Count -eq $report['samples'] -and $samples.Count -ge 1 -and $samples.Count -le 30) "E2E sample count mismatch: $path / $($case['id'])."
                $sum = 0.0
                foreach ($sample in $samples) { Assert-Time; Assert-Number $sample 'end_to_end_milliseconds'; $sum += [double]$sample }
                Assert-Report ([double]::IsFinite($sum) -and $sum -gt 0) 'Measured total latency must be finite and positive.'
                $allocated = @($case['allocated_bytes'])
                Assert-Report ($allocated.Count -eq $samples.Count) 'Allocation sample count mismatch.'
                foreach ($value in $allocated) { Assert-Time; Assert-Integer $value 'allocated_bytes' 0 ([long]::MaxValue) }
                $latency = $case['latency']; Assert-Report ($latency -is [Collections.IDictionary] -and $latency['count'] -eq $samples.Count) "Invalid latency block: $path / $($case['id'])."
                Assert-Near $latency['minimum'] (($samples | Measure-Object -Minimum).Minimum) 'latency.minimum'
                Assert-Near $latency['maximum'] (($samples | Measure-Object -Maximum).Maximum) 'latency.maximum'
                foreach ($rank in @('p50','p95','p99')) {
                    Assert-Number $latency[$rank] "latency.$rank"
                    Assert-Report ([double]$latency[$rank] -eq (Percentile $samples (@{p50=.5;p95=.95;p99=.99}[$rank]))) "Nearest-rank $rank mismatch: $path / $($case['id'])."
                }
                Assert-Number $case['requests_per_second'] 'requests_per_second'; Assert-Number $case['questions_per_second'] 'questions_per_second'
                Assert-Near $case['requests_per_second'] ($samples.Count * 1000 / $sum) 'requests_per_second'
                Assert-Near $case['questions_per_second'] ($samples.Count * 1000 * $case['questions'] / $sum) 'questions_per_second'
                Assert-Report ($case['rendering_verification'] -ceq 'exact_token_marker_type_match' -and $case['discovery_completed_forwards'] -eq $case['questions'] -and @($case['actual_sequences']).Count -eq $case['questions']) 'Measured discovery was not fully verified.'
                $rendered = @($case['rendered_sequences']); $actual = @($case['actual_sequences']); $tokenTotal = 0
                Assert-Report ($rendered.Count -eq $case['questions']) 'Rendered sequence count mismatch.'
                for ($sequenceIndex = 0; $sequenceIndex -lt $rendered.Count; $sequenceIndex++) {
                    Assert-Time
                    $proposal = $rendered[$sequenceIndex]; $observed = $actual[$sequenceIndex]
                    Assert-Hash $proposal['tokens_sha256'] 'rendered.tokens_sha256'
                    Assert-Integer $proposal['token_count'] 'rendered.token_count' 1 8192
                    Assert-Text $proposal['question_id'] 'rendered.question_id'
                    Assert-Integer $proposal['type_id'] 'rendered.type_id' 0 2
                    Assert-Hash $observed['tokens_sha256'] 'actual.tokens_sha256'
                    Assert-Integer $observed['token_count'] 'actual.token_count' 1 8192
                    Assert-Integer $observed['type_id'] 'actual.type_id' 0 2
                    foreach ($field in @('question_id','type_id','token_count','tokens_sha256')) { Assert-Report ($proposal[$field] -ceq $observed[$field]) "Observed sequence differs: $field." }
                    $markers = @($proposal['markers']); $observedMarkers = @($observed['markers'])
                    Assert-Report ($markers.Count -eq 2 -and $observedMarkers.Count -eq 2) 'Marker count mismatch.'
                    for ($markerIndex = 0; $markerIndex -lt 2; $markerIndex++) {
                        Assert-Integer $markers[$markerIndex] 'marker' 0 ($proposal['token_count'] - 1)
                        Assert-Integer $observedMarkers[$markerIndex] 'actual.marker' 0 ($observed['token_count'] - 1)
                        Assert-Report ($markers[$markerIndex] -eq $observedMarkers[$markerIndex]) 'Observed marker position differs.'
                    }
                    $tokenTotal += [int]$proposal['token_count']
                }
                Assert-Integer $case['rendered_total_tokens'] 'rendered_total_tokens' 1 262144
                Assert-Integer $case['response']['usage']['token_count'] 'response.usage.token_count' 1 262144
                Assert-Report ($tokenTotal -eq $case['rendered_total_tokens'] -and $case['response']['usage']['token_count'] -eq $tokenTotal) 'Rendered or response token total mismatch.'
                Assert-Report (@($case['resource_observation_errors']).Count -eq 0 -and $case['phase'] -ceq 'complete' -and $null -eq $case['error_code']) 'Measured case has unresolved failures.'
                $stage = $null; $stageMs = $null
                if ($profile['detail'] -ceq 'full') {
                    Assert-Number $case['encoder_and_head_milliseconds'] 'encoder_and_head_milliseconds'
                    Assert-Number $case['instrumented_end_to_end_milliseconds'] 'instrumented_end_to_end_milliseconds'
                    Assert-Report ($case['encoder_and_head_status'] -ceq 'measured_independent_instrumented_request' -and $case['instrumented_entered_forwards'] -eq $case['questions'] -and $case['instrumented_completed_forwards'] -eq $case['questions']) 'Incomplete independent stage measurement.'
                    $stage = 'encoder_and_head'; $stageMs = [double]$case['encoder_and_head_milliseconds']; $stageCases++
                } else {
                    Assert-Report ($null -eq $case['encoder_and_head_milliseconds'] -and $null -eq $case['instrumented_end_to_end_milliseconds'] -and $case['encoder_and_head_status'] -ceq 'not_requested_end_to_end_detail' -and $case['instrumented_entered_forwards'] -eq 0 -and $case['instrumented_completed_forwards'] -eq 0) 'E2E-only case includes undeclared stage measurements.'
                }
                $measuredQuestions += [int]$case['questions']
                $caseRows.Add([ordered]@{ id=$case['id']; length=$case['length_preset']; questions=[int]$case['questions']; status='measured'; input_sha256=$case['input_sha256']; samples=$samples.Count; p50_ms=[double]$latency['p50']; p95_ms=[double]$latency['p95']; p99_ms=[double]$latency['p99']; tokenizer_ms=[double]$case['tokenizer_and_rendering_milliseconds']; stage=$stage; stage_ms=$stageMs; instrumented_end_to_end_ms=$case['instrumented_end_to_end_milliseconds']; requests_per_second=[double]$case['requests_per_second']; questions_per_second=[double]$case['questions_per_second'] })
            } else {
                Assert-Text $case['error_code'] 'rejected case.error_code'
                Assert-Report ($case['error_code'] -cin @('decision_token_budget_exceeded','decision_token_limit_exceeded') -and @($case['end_to_end_milliseconds']).Count -eq 0 -and @($case['allocated_bytes']).Count -eq 0 -and $null -eq $case['latency'] -and $null -eq $case['requests_per_second'] -and $null -eq $case['questions_per_second'] -and $null -eq $case['encoder_and_head_milliseconds'] -and $null -eq $case['instrumented_end_to_end_milliseconds']) 'Rejected case has unsupported error semantics or measurement claims.'
                $caseRows.Add([ordered]@{ id=$case['id']; length=$case['length_preset']; questions=[int]$case['questions']; status='rejected'; error_code=$case['error_code'] })
            }
        }
        Assert-Report ($profile['successful_cases'] -eq @($cases | Where-Object { $_['status'] -ceq 'measured' }).Count) "Successful case count mismatch: $path."
        Assert-Report ($profile['rejected_cases'] -eq @($cases | Where-Object { $_['status'] -ceq 'rejected' }).Count) "Rejected case count mismatch: $path."
        Assert-Report ($profile['status'] -ceq $(if ($profile['rejected_cases'] -eq 0) { 'complete' } else { 'complete_with_rejections' })) 'Profile status disagrees with rejection count.'
        $plannedQuestions = $lengths.Count * ($questions | Measure-Object -Sum).Sum
        Assert-Report ($profile['planned_questions'] -eq $plannedQuestions -and $profile['cases_with_stage_timings'] -eq $stageCases) 'Profile question or stage denominator mismatch.'
        Assert-Near $profile['request_coverage'] ($profile['successful_cases'] / $cases.Count) 'request_coverage'
        Assert-Near $profile['question_coverage'] ($measuredQuestions / $plannedQuestions) 'question_coverage'
        $reports.Add([ordered]@{ source_path=$path; source_sha256=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($bytes)); backend=$report['backend']; rid=$report['rid']; native_aot=$report['native_aot']; precision=$report['precision']; runtime=$report['runtime']; operating_system=$report['operating_system']; cpu=$report['cpu']; gpu=$report['gpu']; logical_processors=$report['logical_processors']; environment_label=$report['environment_label']; inference_threads=$report['inference_threads']; samples=$report['samples']; warmup=$report['warmup']; request_deadline_seconds=$report['request_deadline_seconds']; request_token_budget=$report['request_token_budget']; model_revision=$report['model_revision']; weights_sha256=$report['weights_sha256']; tokenizer_sha256=$report['tokenizer_sha256']; manifest_sha256=$report['manifest_sha256']; code_artifacts=$artifacts; input_set_version=$profile['input_set_version']; rendering_version=$profile['rendering_version']; detail=$profile['detail']; profile_status=$profile['status']; planned_cases=[int]$profile['planned_cases']; successful_cases=[int]$profile['successful_cases']; rejected_cases=[int]$profile['rejected_cases']; request_coverage=[double]$profile['request_coverage']; question_coverage=[double]$profile['question_coverage']; cases=$caseRows.ToArray() })
    }
    Assert-Time
    $summary = [ordered]@{ schema_version=1; status='passed'; generated_utc=[DateTimeOffset]::UtcNow.ToString('O'); report_count=$reports.Count; reports=$reports.ToArray(); scope=[ordered]@{ purpose='Schema and timing consistency for S5-06 profile outputs; derived stage fields are observations only.'; samples='Raw E2E samples and nearest-rank p50/p95/p99 are checked per case; samples are never pooled.'; rejected='Rejected cases remain in the declared plan denominator and are reported with their error code.'; optimisation='No speedup, quality, AOT or calibration claim is made by this validator.' } }
    $parentDirectory = [IO.Path]::GetDirectoryName($destination); [void][IO.Directory]::CreateDirectory($parentDirectory)
    $output = [IO.FileStream]::new($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try { $encoded = [Text.UTF8Encoding]::new($false).GetBytes(($summary | ConvertTo-Json -Depth 64)); $output.Write($encoded); $output.Flush($true) } finally { $output.Dispose() }
    Write-Output "Validated $($reports.Count) profile report(s): $destination"
} finally { Write-Progress -Activity '校验 S5-06 画像' -Completed }
