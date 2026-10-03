param(
    [ValidateRange(1,3)][int]$MaximumCases = 3,
    [ValidateRange(10,120)][int]$TimeoutSeconds = 90
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7+ required.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$dotnet = (Get-Command dotnet -CommandType Application).Source
$tool = Join-Path $PSScriptRoot 'bin/Release/net10.0/Sezika.IndependentModelTool.dll'
$manifest = [IO.File]::ReadAllText((Join-Path $root 'model-manifests/mmbert-independent/model.json'))
$artifactParent = [IO.Path]::GetFullPath((Join-Path $root '.artifacts'))
$owned = [IO.Path]::GetFullPath((Join-Path $artifactParent ('independent-preflight-' + [Guid]::NewGuid().ToString('N'))))
if (-not $owned.StartsWith($artifactParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Owned path escaped artifacts.' }
$package = Join-Path $owned 'temporary-package'
$outside = Join-Path $owned 'temporary-link-target'
$tokenizerDirectory = Join-Path $package 'tokenizer'
$watch = [Diagnostics.Stopwatch]::StartNew()
$rows = [Collections.Generic.List[object]]::new()
$cases = @(
    @{name='manifest_attention_identity'; error='numerical configuration differs'},
    @{name='pinned_asset_size'; error='size differs from the fixed revision'},
    @{name='junction_escape'; error='links and junctions are not supported'}
)
try {
    [void][IO.Directory]::CreateDirectory($tokenizerDirectory)
    [void][IO.Directory]::CreateDirectory($outside)
    [IO.File]::WriteAllBytes((Join-Path $package 'encoder.safetensors'), [byte[]]@(0))
    [IO.File]::WriteAllText((Join-Path $tokenizerDirectory 'tokenizer.json'), '{}')
    for ($index=0; $index -lt $MaximumCases; $index++) {
        if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) { throw 'Preflight regression wall-clock limit reached.' }
        $case = $cases[$index]
        Write-Output "Preflight $($index + 1)/${MaximumCases}: $($case.name)"
        $text = if ($index -eq 0) { $manifest.Replace('"head_count": 12', '"head_count": 8') } else { $manifest }
        if ($index -eq 0 -and $text -ceq $manifest) { throw 'Mutation no longer matches manifest fixture.' }
        [IO.File]::WriteAllText((Join-Path $package 'model.json'), $text)
        if ($index -eq 2) {
            [IO.File]::Delete((Join-Path $tokenizerDirectory 'tokenizer.json'))
            [IO.Directory]::Delete($tokenizerDirectory)
            if ($IsWindows) { New-Item -ItemType Junction -Path $tokenizerDirectory -Target $outside | Out-Null }
            else { [void][IO.Directory]::CreateSymbolicLink($tokenizerDirectory, $outside) }
        }
        $logDirectory = Join-Path $owned $case.name
        $remaining = [int][Math]::Min(20, [Math]::Ceiling($TimeoutSeconds - $watch.Elapsed.TotalSeconds))
        if ($remaining -lt 1) { throw 'No preflight process budget remains.' }
        try {
            & (Join-Path $root 'tools/Invoke-BoundedProcess.ps1') -FilePath $dotnet -ArgumentList @($tool,'smoke','--package',$package) -TimeoutSeconds $remaining -ProcessQueryTimeoutSeconds 5 -LogName $case.name -LogDirectory $logDirectory | Out-Null
        } catch { Write-Verbose "Expected tool rejection: $($_.Exception.Message)" }
        $results = @(Get-ChildItem -LiteralPath $logDirectory -Filter '*.result.json' | Select-Object -First 2)
        if ($results.Count -ne 1) { throw 'Expected exactly one process result.' }
        $result = [IO.File]::ReadAllText($results[0].FullName) | ConvertFrom-Json
        $stderr = [IO.File]::ReadAllText($result.StderrPath)
        if ($result.ExitCode -ne 1 -or $result.CleanupErrors.Count -ne 0 -or -not $stderr.Contains($case.error, [StringComparison]::Ordinal)) {
            throw "Unexpected rejection for $($case.name): $stderr"
        }
        $rows.Add([ordered]@{name=$case.name; status='expected_rejection'; process_result=$results[0].FullName; pid=$result.Pid; error=$stderr.Trim()})
    }
} finally {
    # Only exact task-owned files/directories are removed. Delete a junction as
    # a link before its target, without traversing it or using recursive removal.
    if ($owned.StartsWith($artifactParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($owned).StartsWith('independent-preflight-', [StringComparison]::Ordinal)) {
        if ([IO.Directory]::Exists($tokenizerDirectory)) {
            if (([IO.File]::GetAttributes($tokenizerDirectory) -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
                [IO.File]::Delete((Join-Path $tokenizerDirectory 'tokenizer.json'))
            }
            [IO.Directory]::Delete($tokenizerDirectory)
        }
        [IO.File]::Delete((Join-Path $package 'model.json'))
        [IO.File]::Delete((Join-Path $package 'encoder.safetensors'))
        if ([IO.Directory]::Exists($package)) { [IO.Directory]::Delete($package) }
        if ([IO.Directory]::Exists($outside)) { [IO.Directory]::Delete($outside) }
    }
}
$summary = [ordered]@{status='passed'; scope='synthetic_asset_preflight_no_model_inference'; cases=$rows.ToArray(); elapsed_seconds=$watch.Elapsed.TotalSeconds; temporary_package_removed=(-not [IO.Directory]::Exists($package)); temporary_link_target_removed=(-not [IO.Directory]::Exists($outside))}
[IO.File]::WriteAllText((Join-Path $owned 'summary.json'), ($summary | ConvertTo-Json -Depth 6))
Write-Output "Independent preflight: $($rows.Count) checks passed; summary $owned/summary.json"
