param([ValidateSet('win','linux')][string]$Platform, [ValidateSet('scalar','simd','cuda')][string]$Backend, [switch]$CompareOnly)
$ErrorActionPreference = 'Stop'
$root = 'D:\source\Sezika'
$taskRoot = Join-Path $root '.artifacts/s3-aot-20260926'
$watch = [Diagnostics.Stopwatch]::StartNew()
$runner = Join-Path $root 'tools/Invoke-BoundedProcess.ps1'
$reference = Join-Path $root 'tests/fixtures/laya-oracle/reference.cpu-fp32.v1.json'
$contract = Join-Path $root 'tests/fixtures/laya-oracle/contract.v1.json'
$cases = Join-Path $root 'tests/fixtures/laya-oracle/cases.v1.json'
$comparer = Join-Path $taskRoot 'build-compare/bin/Sezika.OracleCompare/release/Sezika.OracleCompare.dll'
$selected = if ($Backend -eq 'scalar') { @('sequence-1024','score-en-long','boolean-zh-long') } else {
    $manifest = Get-Content -LiteralPath $cases -Raw -Encoding utf8 | ConvertFrom-Json
    if ($manifest.cases.Count -ne 46) { throw 'Frozen manifest count changed.' }
    @($manifest.cases.id | Where-Object { $_ -notin @('choice-one-candidate','choice-33-candidates','score-one-level','score-eleven-levels') })
}
function LinuxPath([string]$path) { $path.Replace('D:\','/mnt/d/').Replace('\','/') }
function Capture([string]$label, [int]$maxCases, [int]$seconds, [string[]]$ids) {
    if ($watch.Elapsed.TotalSeconds + $seconds + 30 -gt 2400) { throw 'Batch wall-clock budget exceeded.' }
    $output = Join-Path $taskRoot $label
    $arguments = @('--reference',$reference,'--model',(Join-Path $root '.artifacts/models/laya-mmbert'),
        '--cases',$cases,'--contract',$contract,'--output',$output,'--backend',$Backend,
        '--max-cases',"$maxCases",'--timeout-seconds',"$seconds",'--require-aot')
    if ($ids.Count) { $arguments += @('--case-ids',($ids -join ',')) }
    if ($Platform -eq 'win') {
        & $runner -FilePath (Join-Path $taskRoot 'native-final/win-x64/Sezika.OracleCapture.exe') -ArgumentList $arguments -TimeoutSeconds ($seconds+30) -LogName "s3-aot-$label"
    } else {
        $linuxArguments = @($arguments | ForEach-Object { LinuxPath $_ })
        $processEvidence = LinuxPath (Join-Path $taskRoot "$label-linux-process.json")
        & $runner -FilePath 'C:\Windows\system32\wsl.exe' -ArgumentList (@('-d','Ubuntu','--cd','/mnt/d/source/Sezika',
            '--exec','/usr/bin/timeout','--signal=TERM','--kill-after=10s',"$($seconds+20)s",'/usr/bin/python3',
            (LinuxPath (Join-Path $taskRoot 'linux-process.py')),"$($seconds+10)",$processEvidence,
            (LinuxPath (Join-Path $taskRoot 'native-final/linux-x64/Sezika.OracleCapture'))) + $linuxArguments) -TimeoutSeconds ($seconds+30) -LogName "s3-aot-$label"
    }
}
function Invoke-CaptureComparison([string]$label, [string[]]$ids, [switch]$ExpectedContractDifference) {
    if ($watch.Elapsed.TotalSeconds + 45 -gt 2400) { throw 'Batch wall-clock budget exceeded.' }
    $suffix = if ($ExpectedContractDifference) { 'full-comparison' } else { 'comparison' }
    $output = Join-Path $taskRoot "$label-$suffix.json"
    $arguments = @($comparer,$reference,(Join-Path $taskRoot "$label/capture.json"),$contract,$cases,$output,'30')
    if ($ids.Count) { $arguments += ($ids -join ',') }
    try {
        & $runner -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList $arguments -TimeoutSeconds 45 -LogName "s3-aot-$label-$suffix"
        if ($ExpectedContractDifference) { throw 'Expected frozen API differences were not detected.' }
    } catch {
        if (-not $ExpectedContractDifference -or $_.Exception.Message -ne 'Process failed with exit code 1.') { throw }
        $report = Get-Content -LiteralPath $output -Raw -Encoding utf8 | ConvertFrom-Json
        if ($report.issues.Count -ne 16) { throw 'Unexpected full contract differences.' }
    }
}
# Five operations at most, each with its own deadline/cancellation and progress;
# one real input is verified before the explicitly bounded larger selection.
if (-not $CompareOnly) { Capture "$Platform-$Backend-final-smoke" 1 180 @() }
Invoke-CaptureComparison "$Platform-$Backend-final-smoke" @('choice-en-short')
$seconds = if ($Backend -eq 'cuda') { 300 } else { 1700 }
$maxCases = if ($Backend -eq 'scalar') { 3 } else { 46 }
$ids = if ($Backend -eq 'scalar') { $selected } else { @() }
if (-not $CompareOnly) { Capture "$Platform-$Backend-final" $maxCases $seconds $ids }
Invoke-CaptureComparison "$Platform-$Backend-final" $selected
if ($Backend -ne 'scalar') { Invoke-CaptureComparison "$Platform-$Backend-final" @() -ExpectedContractDifference }
Write-Output "Completed $Platform/$Backend final AOT capture and comparison in $($watch.Elapsed.TotalSeconds) seconds."
