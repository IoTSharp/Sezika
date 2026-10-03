[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateRange(1,60)][int]$TimeoutSeconds = 30
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or newer is required.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$validator = Join-Path $root 'tools/Validate-S5Profile.ps1'
$sources = @('profile-simd-short1.json','profile-simd-long1.json','profile-cuda-short1.json','profile-cuda-long32.json')
$sourceDirectory = Join-Path $root 'docs/evidence/s346-continuation-2026-09-26'
$destination = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $destination) { throw 'Output already exists.' }
$watch = [Diagnostics.Stopwatch]::StartNew()
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporary = [IO.Path]::GetFullPath((Join-Path $temporaryParent ('sezika-profile-checks-' + [Guid]::NewGuid().ToString('N'))))
if (-not $temporary.StartsWith($temporaryParent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Temporary path escaped its owned parent.' }
[void][IO.Directory]::CreateDirectory($temporary)
$rows = [Collections.Generic.List[object]]::new()
function Assert-Time {
    if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw 'Profile regression wall-clock limit reached.' }
}
function Validate([string[]]$Paths, [string]$Name) {
    Assert-Time
    $remaining = [int][Math]::Max(1, [Math]::Ceiling($TimeoutSeconds - $watch.Elapsed.TotalSeconds))
    & $validator -ReportPaths $Paths -OutputPath (Join-Path $temporary ($Name + '-summary.json')) -TimeoutSeconds $remaining | Out-Null
    Assert-Time
}
try {
    # Exit conditions are comparisons; four archival positives, at most 32
    # bounded mutations. Run the single-positive smoke before the mutation batch.
    Validate @((Join-Path $sourceDirectory $sources[0])) 'smoke'
    $rows.Add([ordered]@{ name='single_real_report_smoke'; status='passed' })
    $paths = @($sources | ForEach-Object { Join-Path $sourceDirectory $_ })
    Validate $paths 'real-four'
    $rows.Add([ordered]@{ name='four_archival_real_reports'; status='passed'; sources=$sources })
    $sourceText = [IO.File]::ReadAllText($paths[0])
    $checks = @(
        @{ Name='string_sample'; Expected='Invalid nonnegative number'; Mutate={ param($r) $r.profile.cases[0].end_to_end_milliseconds[0] = '1' } },
        @{ Name='boolean_sample'; Expected='Invalid nonnegative number'; Mutate={ param($r) $r.profile.cases[0].end_to_end_milliseconds[0] = $true } },
        @{ Name='negative_sample'; Expected='Invalid nonnegative number'; Mutate={ param($r) $r.profile.cases[0].end_to_end_milliseconds[0] = -1 } },
        @{ Name='duplicate_dimension'; Expected='duplicate selected lengths'; Mutate={ param($r) $r.profile.selected_lengths = @('short','short') } },
        @{ Name='wrong_dimension'; Expected='declared matrix'; Mutate={ param($r) $r.profile.selected_lengths = @('medium') } },
        @{ Name='wrong_case_id'; Expected='declared matrix'; Mutate={ param($r) $r.profile.cases[0].id = 'short-8' } },
        @{ Name='request_coverage'; Expected='request_coverage'; Mutate={ param($r) $r.profile.request_coverage = 0 } },
        @{ Name='question_coverage'; Expected='question_coverage'; Mutate={ param($r) $r.profile.question_coverage = 0 } },
        @{ Name='planned_questions'; Expected='denominator mismatch'; Mutate={ param($r) $r.profile.planned_questions = 8 } },
        @{ Name='requests_throughput'; Expected='requests_per_second'; Mutate={ param($r) $r.profile.cases[0].requests_per_second *= 2 } },
        @{ Name='questions_throughput'; Expected='questions_per_second'; Mutate={ param($r) $r.profile.cases[0].questions_per_second *= 2 } },
        @{ Name='minimum_latency'; Expected='latency.minimum'; Mutate={ param($r) $r.profile.cases[0].latency.minimum = 0 } },
        @{ Name='input_hash'; Expected='Input JSON hash mismatch'; Mutate={ param($r) $r.profile.cases[0].input_json += ' ' } },
        @{ Name='code_identity'; Expected='Executable identity mismatch'; Mutate={ param($r) $r.executable_sha256 = 'a' * 64 } },
        @{ Name='nonboolean_aot'; Expected='JSON boolean'; Mutate={ param($r) $r.native_aot = 'false' } },
        @{ Name='incomplete_discovery'; Expected='discovery was not fully verified'; Mutate={ param($r) $r.profile.cases[0].discovery_completed_forwards = 0 } },
        @{ Name='actual_token_hash'; Expected='Observed sequence differs'; Mutate={ param($r) $r.profile.cases[0].actual_sequences[0].tokens_sha256 = 'a' * 64 } },
        @{ Name='token_denominator'; Expected='token total mismatch'; Mutate={ param($r) $r.profile.cases[0].rendered_total_tokens = 1 } },
        @{ Name='incomplete_instrumented'; Expected='Incomplete independent stage'; Mutate={ param($r) $r.profile.cases[0].instrumented_completed_forwards = 0 } },
        @{ Name='wrong_detail'; Expected='E2E-only case includes'; Mutate={ param($r) $r.profile.detail = 'end_to_end' } },
        @{ Name='missing_stage'; Expected='Invalid nonnegative number'; Mutate={ param($r) $r.profile.cases[0].encoder_and_head_milliseconds = $null } },
        @{ Name='resource_failure'; Expected='unresolved failures'; Mutate={ param($r) $r.profile.cases[0].resource_observation_errors = @(@{error_code='fixture'}) } },
        @{ Name='status_rejections'; Expected='disagrees with rejection count'; Mutate={ param($r) $r.profile.status = 'complete_with_rejections' } },
        @{ Name='stage_denominator'; Expected='denominator mismatch'; Mutate={ param($r) $r.profile.cases_with_stage_timings = 0 } },
        @{ Name='string_schema'; Expected='Invalid nonnegative number'; Mutate={ param($r) $r.schema_version = '1' } },
        @{ Name='duplicate_cases'; Expected='declared matrix'; Mutate={ param($r) $r.profile.selected_lengths = @('short','medium'); $r.profile.planned_cases = 2; $r.profile.cases = @($r.profile.cases[0],$r.profile.cases[0]) } },
        @{ Name='rejection_with_samples'; Expected='unsupported error semantics'; Mutate={ param($r) $r.profile.cases[0].status = 'rejected'; $r.profile.cases[0].error_code = 'decision_token_limit_exceeded' } },
        @{ Name='string_repetitions'; Expected='Invalid nonnegative number'; Mutate={ param($r) $r.profile.cases[0].state_repetitions = '4' } },
        @{ Name='string_candidates'; Expected='Invalid nonnegative number'; Mutate={ param($r) $r.profile.cases[0].candidates_per_question = '2' } },
        @{ Name='string_forward'; Expected='Invalid nonnegative number'; Mutate={ param($r) $r.profile.cases[0].discovery_completed_forwards = '1' } }
    )
    if ($checks.Count -gt 32) { throw 'Mutation item limit exceeded.' }
    for ($index = 0; $index -lt $checks.Count; $index++) {
        Assert-Time
        $check = $checks[$index]
        Write-Progress -Activity 'S5 profile validator regression' -Status "$($index + 1)/$($checks.Count): $($check.Name)" -PercentComplete (100 * $index / $checks.Count)
        $report = $sourceText | ConvertFrom-Json -AsHashtable -Depth 64
        & $check.Mutate $report
        $path = Join-Path $temporary ($check.Name + '.json')
        [IO.File]::WriteAllText($path, ($report | ConvertTo-Json -Depth 64))
        $rejection = $null
        try { Validate @($path) $check.Name } catch { $rejection = $_.Exception.Message }
        if ($null -eq $rejection -or -not $rejection.Contains($check.Expected, [StringComparison]::OrdinalIgnoreCase)) { throw "Expected targeted rejection for $($check.Name); received: $rejection" }
        $rows.Add([ordered]@{ name=$check.Name; status='rejected_as_expected'; reason=$rejection })
    }
    # Raw fixtures preserve duplicate members, unlike ConvertTo-Json mutations.
    $rawChecks = @(
        @{ Name='duplicate_root_schema'; From='"schema_version": 1,'; To='"schema_version": 0, "schema_version": 1,' },
        @{ Name='duplicate_escaped_status'; From='"status": "passed",'; To='"sta\u0074us": "failed", "status": "passed",' },
        @{ Name='duplicate_nested_questions'; From='"questions": 1,'; To='"ques\u0074ions": 8, "questions": 1,' }
    )
    if ($rawChecks.Count -gt 3) { throw 'Raw fixture item limit exceeded.' }
    for ($index = 0; $index -lt $rawChecks.Count; $index++) {
        Assert-Time
        $check = $rawChecks[$index]
        Write-Progress -Activity 'S5 duplicate JSON regression' -Status "$($index + 1)/$($rawChecks.Count): $($check.Name)" -PercentComplete (100 * $index / $rawChecks.Count)
        if (-not $sourceText.Contains($check.From, [StringComparison]::Ordinal)) { throw 'Raw fixture source pattern missing.' }
        $path = Join-Path $temporary ($check.Name + '.json')
        [IO.File]::WriteAllText($path, $sourceText.Replace($check.From, $check.To))
        $rejection = $null
        try { Validate @($path) $check.Name } catch { $rejection = $_.Exception.Message }
        if ($null -eq $rejection -or -not $rejection.Contains('Duplicate JSON property:', [StringComparison]::Ordinal)) { throw "Expected duplicate JSON rejection for $($check.Name); received: $rejection" }
        $rows.Add([ordered]@{ name=$check.Name; status='rejected_as_expected'; reason=$rejection })
    }
    Assert-Time
    $baselineSummary = [IO.File]::ReadAllText((Join-Path $temporary 'smoke-summary.json')) | ConvertFrom-Json -AsHashtable -Depth 64
    if ($baselineSummary.reports[0].cases[0].stage -cne 'encoder_and_head' -or $null -eq $baselineSummary.reports[0].cases[0].stage_ms) { throw 'Measured stage observation was lost in summary.' }
} finally {
    Write-Progress -Activity 'S5 profile validator regression' -Completed
    Write-Progress -Activity 'S5 duplicate JSON regression' -Completed
    # This GUID directory was created by this invocation. Resolve again before
    # removal; no user files, shared caches or deliverables are under this path.
    $resolved = [IO.Path]::GetFullPath($temporary)
    if ($resolved -cne $temporary -or -not $resolved.StartsWith($temporaryParent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup ownership verification failed.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Assert-Time
$summary = [ordered]@{ schema_version=1; status='passed'; scope='Read-only validator regression using four archived real reports and explicitly mutated invalid fixtures. No model execution or new performance evidence.'; check_count=$rows.Count; elapsed_seconds=$watch.Elapsed.TotalSeconds; checks=$rows.ToArray(); temporary_directory_removed=(-not [IO.Directory]::Exists($temporary)) }
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
$output = [IO.FileStream]::new($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
try { $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($summary | ConvertTo-Json -Depth 64)); $output.Write($bytes) } finally { $output.Dispose() }
Write-Output "Passed $($rows.Count) validator checks: $destination"
