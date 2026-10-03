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
    Assert-Report ($null -ne $Value -and $Value -is [ValueType] -and $Value -isnot [bool] -and
        [double]::IsFinite([double]$Value) -and [double]$Value -ge 0) "Invalid nonnegative number: $Name."
}
function Percentile([double[]]$Samples, [double]$Probability) {
    Assert-Report ($Samples.Count -gt 0) 'Cannot calculate a percentile without samples.'
    $ordered = @($Samples | Sort-Object)
    $index = [int][Math]::Ceiling($Probability * $ordered.Count) - 1
    return [double]$ordered[$index]
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
        $report = [Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xFEFF) | ConvertFrom-Json -AsHashtable -Depth 64
        Assert-Report ($report -is [Collections.IDictionary]) "Report must be a JSON object: $path."
        Assert-Report ($report['schema_version'] -eq 1 -and $report['status'] -ceq 'passed' -and $report['phase'] -ceq 'complete') "Only completed schema-1 passed reports are accepted: $path."
        Assert-Text $report['backend'] 'backend'; Assert-Text $report['model_revision'] 'model_revision'; Assert-Text $report['environment_label'] 'environment_label'
        $profile = $report['profile']
        Assert-Report ($profile -is [Collections.IDictionary] -and $profile['schema_version'] -eq 3) "Missing profile schema v3: $path."
        $lengths = @($profile['selected_lengths']); $questions = @($profile['selected_questions']); $cases = @($profile['cases'])
        Assert-Report ($lengths.Count -ge 1 -and $lengths.Count -le 3 -and $questions.Count -ge 1 -and $questions.Count -le 3) "Invalid selected profile dimensions: $path."
        Assert-Report ($cases.Count -eq $profile['planned_cases'] -and $cases.Count -eq ($lengths.Count * $questions.Count)) "Profile case plan mismatch: $path."
        $caseRows = [Collections.Generic.List[object]]::new()
        for ($caseIndex = 0; $caseIndex -lt $cases.Count; $caseIndex++) {
            Assert-Time
            $case = $cases[$caseIndex]
            Assert-Text $case['id'] 'case.id'; Assert-Text $case['status'] 'case.status'
            Assert-Report ($case['status'] -in @('measured','rejected')) "Unexpected case status: $path / $($case['id'])."
            Assert-Report ($case['questions'] -in @(1,8,32)) "Unsupported question count: $path / $($case['id'])."
            Assert-Number $case['tokenizer_and_rendering_milliseconds'] 'tokenizer_and_rendering_milliseconds'
            if ($case['status'] -ceq 'measured') {
                $samples = @($case['end_to_end_milliseconds']) | ForEach-Object { [double]$_ }
                Assert-Report ($samples.Count -eq $report['samples'] -and $samples.Count -ge 1 -and $samples.Count -le 30) "E2E sample count mismatch: $path / $($case['id'])."
                foreach ($sample in $samples) { Assert-Number $sample 'end_to_end_milliseconds' }
                $latency = $case['latency']; Assert-Report ($latency -is [Collections.IDictionary] -and $latency['count'] -eq $samples.Count) "Invalid latency block: $path / $($case['id'])."
                foreach ($rank in @('p50','p95','p99')) {
                    Assert-Number $latency[$rank] "latency.$rank"
                    Assert-Report ([double]$latency[$rank] -eq (Percentile $samples (@{p50=.5;p95=.95;p99=.99}[$rank]))) "Nearest-rank $rank mismatch: $path / $($case['id'])."
                }
                Assert-Number $case['requests_per_second'] 'requests_per_second'; Assert-Number $case['questions_per_second'] 'questions_per_second'
                Assert-Report ([double]$case['requests_per_second'] -gt 0) "Measured case has nonpositive throughput: $path / $($case['id'])."
                $stage = $null
                if ($null -ne $case['encoder_and_head_milliseconds']) { Assert-Number $case['encoder_and_head_milliseconds'] 'encoder_and_head_milliseconds'; $stage = 'encoder_and_head' }
                elseif ($null -ne $case['instrumented_end_to_end_milliseconds']) { Assert-Number $case['instrumented_end_to_end_milliseconds'] 'instrumented_end_to_end_milliseconds'; $stage = 'instrumented_end_to_end' }
                $caseRows.Add([ordered]@{ id=$case['id']; length=$case['length_preset']; questions=[int]$case['questions']; status='measured'; samples=$samples.Count; p50_ms=[double]$latency['p50']; p95_ms=[double]$latency['p95']; p99_ms=[double]$latency['p99']; tokenizer_ms=[double]$case['tokenizer_and_rendering_milliseconds']; stage=$stage; stage_ms=$case['encoder_and_head_milliseconds']; requests_per_second=[double]$case['requests_per_second']; questions_per_second=[double]$case['questions_per_second'] })
            } else {
                Assert-Text $case['error_code'] 'rejected case.error_code'
                $caseRows.Add([ordered]@{ id=$case['id']; length=$case['length_preset']; questions=[int]$case['questions']; status='rejected'; error_code=$case['error_code'] })
            }
        }
        Assert-Report ($profile['successful_cases'] -eq @($cases | Where-Object { $_['status'] -ceq 'measured' }).Count) "Successful case count mismatch: $path."
        Assert-Report ($profile['rejected_cases'] -eq @($cases | Where-Object { $_['status'] -ceq 'rejected' }).Count) "Rejected case count mismatch: $path."
        Assert-Report ($profile['status'] -in @('complete','complete_with_rejections')) "Incomplete profile cannot be used as optimisation evidence: $path."
        $reports.Add([ordered]@{ source_path=$path; source_sha256=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($bytes)); backend=$report['backend']; rid=$report['rid']; native_aot=[bool]$report['native_aot']; model_revision=$report['model_revision']; profile_status=$profile['status']; planned_cases=[int]$profile['planned_cases']; successful_cases=[int]$profile['successful_cases']; rejected_cases=[int]$profile['rejected_cases']; request_coverage=[double]$profile['request_coverage']; cases=$caseRows.ToArray() })
    }
    Assert-Time
    $summary = [ordered]@{ schema_version=1; status='passed'; generated_utc=[DateTimeOffset]::UtcNow.ToString('O'); report_count=$reports.Count; reports=$reports.ToArray(); scope=[ordered]@{ purpose='Schema and timing consistency for S5-06 profile outputs; derived stage fields are observations only.'; samples='Raw E2E samples and nearest-rank p50/p95/p99 are checked per case; samples are never pooled.'; rejected='Rejected cases remain in the declared plan denominator and are reported with their error code.'; optimisation='No speedup, quality, AOT or calibration claim is made by this validator.' } }
    $parentDirectory = [IO.Path]::GetDirectoryName($destination); [void][IO.Directory]::CreateDirectory($parentDirectory)
    $output = [IO.FileStream]::new($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try { $encoded = [Text.UTF8Encoding]::new($false).GetBytes(($summary | ConvertTo-Json -Depth 64)); $output.Write($encoded); $output.Flush($true) } finally { $output.Dispose() }
    Write-Output "Validated $($reports.Count) profile report(s): $destination"
} finally { Write-Progress -Activity '校验 S5-06 画像' -Completed }
