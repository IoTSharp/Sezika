param(
    [Parameter(Mandatory)][ValidateSet('simd','cuda')][string]$Backend,
    [Parameter(Mandatory)][ValidateSet('short','long')][string]$Length,
    [ValidateSet(1,32)][int]$Questions=1,
    [ValidateRange(1,1740)][int]$TimeoutSeconds=180,
    [ValidateRange(1,1800)][int]$RequestDeadlineSeconds=300,
    [ValidateSet('full','end_to_end')][string]$Detail='full',
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$Name
)
$ErrorActionPreference='Stop'
$root='D:\source\Sezika'
$output=Join-Path $PSScriptRoot "$Name.json"
$arguments=@((Join-Path $root '.artifacts/build/s346-tokenizer-final-20260926/bin/Sezika.Benchmarks/release/Sezika.Benchmarks.dll'),
    '--model',(Join-Path $root '.artifacts/models/laya-mmbert'),'--backend',$Backend,'--mode','profile',
    '--lengths',$Length,'--questions',[string]$Questions,'--samples','1','--warmup','0','--cycles','1',
    '--timeout-seconds',[string]$TimeoutSeconds,'--request-deadline-seconds',[string]$RequestDeadlineSeconds,
    '--profile-detail',$Detail,'--cpu','Intel Core i9-13900HX','--environment','windows-local-serialized-task-model-runs',
    '--output',$output)
& (Join-Path $root 'tools/Invoke-BoundedProcess.ps1') -FilePath (Get-Command dotnet).Source -ArgumentList $arguments -TimeoutSeconds ([Math]::Min(1800,$TimeoutSeconds+40)) -LogName $Name -LogDirectory (Join-Path $PSScriptRoot 'processes') -WorkingDirectory $root
