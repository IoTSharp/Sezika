[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateCount(1,32)][string[]]$ReportPaths,
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateRange(1,60)][int]$TimeoutSeconds = 30,
    [switch]$VerifyMatrix
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or newer is required.' }
$watch = [Diagnostics.Stopwatch]::StartNew()
$maximumFileBytes = 4 * 1024 * 1024
$rows = [Collections.Generic.List[object]]::new()
$seenPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$inputHashes = @{}
$modelIdentity = $null

function Assert-Report([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Assert-Time {
    if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw "Summary timeout (${TimeoutSeconds}s)." }
    # PowerShell observes Ctrl+C between statements/pipeline operations; no
    # child process, retry loop, model load or benchmark is started here.
}
function Assert-Hash($Value, [string]$Name) {
    Assert-Report ($Value -is [string] -and $Value -cmatch '^[0-9a-f]{64}$') "Invalid SHA-256: $Name."
}
function Assert-Number($Value, [string]$Name) {
    Assert-Report ($null -ne $Value -and $Value -is [ValueType] -and $Value -isnot [bool] -and
        [double]::IsFinite([double]$Value) -and [double]$Value -ge 0) "Invalid nonnegative finite number: $Name."
}
function Assert-Text($Value, [string]$Name) {
    Assert-Report ($Value -is [string] -and -not [string]::IsNullOrWhiteSpace($Value)) "Missing text: $Name."
}

$destination = [IO.Path]::GetFullPath($OutputPath)
Assert-Report (-not [IO.File]::Exists($destination) -and -not [IO.Directory]::Exists($destination)) 'Output already exists.'
try {
    # Maximum 32 files and a shared 1..60-second wall-clock deadline. All nested
    # loops below have independently validated item counts and time checks.
    for ($fileIndex = 0; $fileIndex -lt $ReportPaths.Count; $fileIndex++) {
        Assert-Time
        $path = [IO.Path]::GetFullPath($ReportPaths[$fileIndex])
        Assert-Report ($seenPaths.Add($path)) "Duplicate report path: $path."
        Write-Progress -Activity '汇总 S5 实测报告' -Status "$($fileIndex + 1)/$($ReportPaths.Count): $path" -PercentComplete (100 * $fileIndex / $ReportPaths.Count)
        Write-Output "Reading $($fileIndex + 1)/$($ReportPaths.Count): $path"
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
        Assert-Report ($report['schema_version'] -eq 1 -and $report['status'] -ceq 'passed') "Only schema 1 passed reports are accepted: $path."
        Assert-Report ($report['phase'] -ceq 'complete') "Incomplete report: $path."
        Assert-Report ($report['backend'] -cin @('scalar','simd','int8','cuda')) "Unknown backend: $path."
        Assert-Report ($report['native_aot'] -is [bool]) "Invalid native_aot flag: $path."
        Assert-Report ($report['samples'] -is [ValueType] -and $report['samples'] -ge 1 -and $report['samples'] -le 30 -and
            [int]$report['samples'] -eq $report['samples']) "Invalid samples: $path."
        foreach ($field in @('rid','precision','runtime','operating_system','cpu','environment_label','model_revision','timing_scope','resource_scope','quality_scope')) {
            Assert-Time
            Assert-Text $report[$field] $field
        }
        foreach ($field in @('weights_sha256','tokenizer_sha256','manifest_sha256','executable_sha256')) {
            Assert-Time
            Assert-Hash $report[$field] $field
        }
        $identity = @($report['model_revision'], $report['weights_sha256'], $report['tokenizer_sha256'], $report['manifest_sha256']) -join '|'
        if ($null -eq $modelIdentity) { $modelIdentity = $identity }
        Assert-Report ($modelIdentity -ceq $identity) 'Model revision/weights/tokenizer/manifest differ across reports.'
        Assert-Report ($report['model_objects_collected'] -is [bool] -and $report['model_objects_collected']) "Model/session collection check did not pass: $path."
        foreach ($field in @('peak_working_set_bytes','managed_bytes_before','managed_bytes_after_collection','quantized_encoder_bytes','quantized_head_bytes')) {
            Assert-Time
            Assert-Number $report[$field] $field
        }
        $loads = @($report['loads'])
        Assert-Report ($loads.Count -ge 1 -and $loads.Count -le 2) "Expected one or two load cycles: $path."
        for ($cycle = 0; $cycle -lt $loads.Count; $cycle++) {
            Assert-Time
            Assert-Report ($loads[$cycle]['cycle'] -eq $cycle) "Load cycles must be ordered from zero: $path."
            foreach ($field in @('model_load_milliseconds','backend_load_milliseconds','first_request_milliseconds','total_to_first_response_milliseconds')) {
                Assert-Time
                Assert-Number $loads[$cycle][$field] $field
            }
        }
        $requests = @($report['requests'])
        Assert-Report ($requests.Count -ge 1 -and $requests.Count -le 3) "Expected one to three question-count rows: $path."
        $questionRows = [Collections.Generic.List[object]]::new()
        for ($requestIndex = 0; $requestIndex -lt $requests.Count; $requestIndex++) {
            Assert-Time
            $request = $requests[$requestIndex]
            $questionCount = $request['questions']
            Assert-Report ($questionCount -is [ValueType] -and $questionCount -ge 1 -and $questionCount -le 32 -and
                [int]$questionCount -eq $questionCount) "Invalid question count: $path."
            Assert-Hash $request['input_sha256'] 'input_sha256'
            Assert-Text $request['input_json'] 'input_json'
            $actualInputHash = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($request['input_json'])))
            Assert-Report ($actualInputHash -ceq $request['input_sha256']) "Input hash mismatch: $path."
            $questionKey = [int]$questionCount
            if ($inputHashes.ContainsKey($questionKey)) {
                Assert-Report ($inputHashes[$questionKey] -ceq $actualInputHash) "Question count $questionCount uses different inputs across reports."
            } else { $inputHashes[$questionKey] = $actualInputHash }
            $samples = @($request['milliseconds'])
            Assert-Report ($samples.Count -eq $report['samples'] -and $samples.Count -le 30) "Sample count differs from report header: $path."
            foreach ($sample in $samples) { Assert-Time; Assert-Number $sample 'milliseconds' }
            $sorted = @($samples | Sort-Object)
            $latency = $request['latency']
            Assert-Report ($null -ne $latency -and $latency['count'] -eq $samples.Count) "Invalid latency count: $path."
            $ranks = @{ p50 = 0.50; p95 = 0.95; p99 = 0.99 }
            foreach ($rank in @('p50','p95','p99')) {
                Assert-Time
                Assert-Number $latency[$rank] $rank
                $expected = $sorted[[int][Math]::Ceiling($ranks[$rank] * $sorted.Count) - 1]
                Assert-Report ([double]$latency[$rank] -eq [double]$expected) "Nearest-rank percentile differs from raw samples: $path / $rank."
            }
            $questionRows.Add([ordered]@{
                questions=$questionCount; input_sha256=$actualInputHash; token_count=$request['token_count']
                candidates_per_question=$request['candidates_per_question']; samples=$samples.Count
                p50=$latency['p50']; p95=$latency['p95']; p99=$latency['p99']; raw_milliseconds=$samples
                allocated_bytes=$request['allocated_bytes']; requests_per_second=$request['requests_per_second']
                questions_per_second=$request['questions_per_second']
            })
        }
        $alignment = $report['alignment']
        Assert-Report ($alignment -is [Collections.IDictionary]) "Missing numerical alignment: $path."
        $errorFields = @('encoder_max_absolute_error','logits_max_absolute_error','probability_max_absolute_error')
        $toleranceFields = @('encoder_tolerance','logits_tolerance','probability_tolerance')
        for ($index = 0; $index -lt 3; $index++) {
            Assert-Time
            Assert-Number $alignment[$errorFields[$index]] $errorFields[$index]
            Assert-Number $alignment[$toleranceFields[$index]] $toleranceFields[$index]
            Assert-Report ($alignment[$errorFields[$index]] -le $alignment[$toleranceFields[$index]]) "Numerical tolerance exceeded: $path."
        }
        if ($null -ne $alignment['head_only_max_absolute_error']) {
            Assert-Number $alignment['head_only_max_absolute_error'] 'head_only_max_absolute_error'
            Assert-Report ($alignment['head_only_max_absolute_error'] -le $alignment['logits_tolerance']) "Head-only tolerance exceeded: $path."
        }
        $cudaPeak = $null
        if ($report['backend'] -ceq 'cuda') {
            $before = $report['cuda_before_unload']
            $after = $report['cuda_after_unload']
            Assert-Report ($before -is [Collections.IDictionary] -and $after -is [Collections.IDictionary]) "Missing CUDA memory snapshots: $path."
            Assert-Number $before['peak_owned_bytes'] 'peak_owned_bytes'
            $cudaPeak = $before['peak_owned_bytes']
            foreach ($field in @('owned_bytes','owned_allocation_count','loaded_module_count','release_failure_count')) {
                Assert-Time
                Assert-Number $after[$field] $field
                Assert-Report ($after[$field] -eq 0) "CUDA unload did not reach zero: $path / $field."
            }
        }
        $rows.Add([ordered]@{
            source_path=$path; source_sha256=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($bytes))
            started_utc=$report['started_utc']; rid=$report['rid']; backend=$report['backend']; native_aot=$report['native_aot']
            runtime=$report['runtime']; precision=$report['precision']; samples=$report['samples']; warmup=$report['warmup']
            hardware=[ordered]@{ cpu=$report['cpu']; gpu=$report['gpu']; operating_system=$report['operating_system']
                environment_label=$report['environment_label']; logical_processors=$report['logical_processors']
                inference_threads=$report['inference_threads']; vector_float_width=$report['vector_float_width'] }
            model_revision=$report['model_revision']; weights_sha256=$report['weights_sha256']; tokenizer_sha256=$report['tokenizer_sha256']
            manifest_sha256=$report['manifest_sha256']; executable_sha256=$report['executable_sha256']
            process_first_request=$loads[0]; same_process_reload=@($loads | Select-Object -Skip 1)
            questions=$questionRows.ToArray(); peak_working_set_bytes=$report['peak_working_set_bytes']; cuda_peak_owned_bytes=$cudaPeak
            cuda_before_unload=$report['cuda_before_unload']; cuda_after_unload=$report['cuda_after_unload']
            cuda_load=$report['cuda_load']; cuda_profiled_forward=$report['cuda_profiled_forward']
            profiled_forward_wall_milliseconds=$report['profiled_forward_wall_milliseconds']
            managed_bytes_before=$report['managed_bytes_before']; managed_bytes_after_collection=$report['managed_bytes_after_collection']
            model_objects_collected=$report['model_objects_collected']; diagnostics=$report['diagnostics']; alignment=$alignment
            quantized_encoder_bytes=$report['quantized_encoder_bytes']; quantized_head_bytes=$report['quantized_head_bytes']
            timing_scope=$report['timing_scope']; resource_scope=$report['resource_scope']; quality_scope=$report['quality_scope']
        })
    }
    if ($VerifyMatrix) {
        foreach ($row in $rows) {
            Assert-Time
            Assert-Report ($row['native_aot']) 'The S5 acceptance matrix requires Native AOT for every row.'
        }
        foreach ($backend in @('scalar','simd','int8','cuda')) {
            Assert-Time
            $matches = @($rows | Where-Object { $_['rid'] -ceq 'win-x64' -and $_['backend'] -ceq $backend -and $_['samples'] -ge 5 })
            $complete = $false
            foreach ($candidate in $matches) {
                Assert-Time
                $counts = @($candidate['questions'] | ForEach-Object { $_['questions'] })
                if ($counts -contains 1 -and $counts -contains 8 -and $counts -contains 32) { $complete = $true; break }
            }
            Assert-Report $complete "Missing win-x64/$backend Native AOT row with at least five samples and q=1,8,32."
        }
        foreach ($backend in @('simd','cuda')) {
            Assert-Time
            $matches = @($rows | Where-Object { $_['rid'] -ceq 'linux-x64' -and $_['backend'] -ceq $backend })
            Assert-Report ($matches.Count -ge 1) "Missing linux-x64/$backend Native AOT smoke row."
        }
    }
    Assert-Time
    $summary = [ordered]@{
        schema_version=1; status='passed'; generated_utc=[DateTimeOffset]::UtcNow.ToString('O')
        matrix_verified=[bool]$VerifyMatrix; report_count=$rows.Count; rows=$rows.ToArray()
        scope=[ordered]@{
            aggregation='Each source report remains a separate row; samples are never pooled across RID, backend, executable or run.'
            cold='Cycle 0 is one first-request observation in one process. OS/driver caches were not flushed. Later cycles are same-process reload observations, not independent cold starts.'
            hot='Per-question-count raw samples and nearest-rank p50/p95/p99 are preserved. Small samples are exploratory; p95/p99 can coincide and do not establish stable tail latency.'
            cuda='CUDA event telemetry is from a separate instrumented pass. Module-load time is not pure PTX JIT. Owned peak excludes driver/context overhead; free/total memory is device-wide.'
            memory='Peak working set is the process lifetime high-water mark. W8A32 retains FP32 source weights. Collected model/session sentinels do not prove every array was checked or immediate OS RSS release.'
            validation='This tool validates supplied schema-1 passed reports and their consistency; it does not execute inference, download weights, calibrate probabilities or establish language quality.'
        }
    }
    $json = $summary | ConvertTo-Json -Depth 64
    Assert-Time
    $parentDirectory = [IO.Path]::GetDirectoryName($destination)
    [void][IO.Directory]::CreateDirectory($parentDirectory)
    # CreateNew is the final gate: neither validation failure nor an existing
    # destination can produce/overwrite a successful summary.
    $output = [IO.FileStream]::new($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try {
        $outputBytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
        $output.Write($outputBytes)
        $output.Flush($true)
    } finally { $output.Dispose() }
    Write-Output "Validated $($rows.Count) report(s); matrix_verified=$([bool]$VerifyMatrix): $destination"
} finally {
    Write-Progress -Activity '汇总 S5 实测报告' -Completed
}
