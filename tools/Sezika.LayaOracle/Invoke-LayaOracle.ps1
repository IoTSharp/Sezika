param(
    [Parameter(Mandatory)][string]$PythonPath,
    [Parameter(Mandatory)][string]$UpstreamDirectory,
    [Parameter(Mandatory)][string]$ModelDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1,64)][int]$MaxCases = 1,
    [ValidateRange(1,1800)][int]$TimeoutSeconds = 180,
    [string]$CancelFile
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or newer is required.' }
$utf8 = [Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $utf8
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$python = [IO.Path]::GetFullPath($PythonPath)
$source = [IO.Path]::GetFullPath($UpstreamDirectory)
$model = [IO.Path]::GetFullPath($ModelDirectory)
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (-not [IO.File]::Exists($python)) { throw 'PythonPath must name an existing explicit interpreter.' }
if (-not [IO.Directory]::Exists($source) -or -not [IO.Directory]::Exists($model)) { throw 'Local source and model directories must exist.' }
if ([IO.Path]::Exists($destination)) { throw 'OutputDirectory must be a new task-owned directory.' }
$arguments = @(
    '-B', (Join-Path $PSScriptRoot 'export_oracle.py'),
    '--upstream', $source, '--model', $model, '--output', $destination,
    '--cases', (Join-Path $root 'tests/fixtures/laya-oracle/cases.v1.json'),
    '--contract', (Join-Path $root 'tests/fixtures/laya-oracle/contract.v1.json'),
    '--max-cases', [string]$MaxCases, '--timeout-seconds', [string]$TimeoutSeconds
)
if ($CancelFile) { $arguments += @('--cancel-file', [IO.Path]::GetFullPath($CancelFile)) }
# The repository runner records root/descendant process identities and performs bounded cleanup.
# It also preserves stdout/stderr and timeout evidence if native Python work cannot be interrupted.
& (Join-Path $root 'tools/Invoke-BoundedProcess.ps1') -FilePath $python -ArgumentList $arguments `
    -TimeoutSeconds $TimeoutSeconds -LogName 'laya-oracle' -WorkingDirectory $root `
    -Environment @{
        PYTHONUTF8='1'; PYTHONDONTWRITEBYTECODE='1'; HF_HUB_OFFLINE='1'; TRANSFORMERS_OFFLINE='1'
        HF_HUB_DISABLE_TELEMETRY='1'; TOKENIZERS_PARALLELISM='false'; LAYA_CPU_AMP=''
    }
