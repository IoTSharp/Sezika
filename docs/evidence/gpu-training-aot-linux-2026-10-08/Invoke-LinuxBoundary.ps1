param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]{1,48}$')][string]$Name,
    [ValidateRange(1,600)][int]$TimeoutSeconds=3,
    [ValidateRange(0,600)][int]$CancelAfterSeconds=0,
    [Parameter(Mandatory)][string[]]$LinuxArgumentList
)
$ErrorActionPreference='Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7+ required.' }
if ($CancelAfterSeconds -gt $TimeoutSeconds -or $LinuxArgumentList.Count -lt 1 -or $LinuxArgumentList.Count -gt 256) { throw 'Argument/cancellation bound exceeded.' }
$boundary=[IO.Path]::GetFullPath($PSScriptRoot)
$expected='D:\source\Sezika\.artifacts\independent-training-aot-20261008\linux-v1'
if ($boundary -cne $expected) { throw 'Unexpected exclusive artifact root.' }
$run=[IO.Path]::GetFullPath((Join-Path $boundary ('runs/'+$Name)))
if (-not $run.StartsWith($boundary+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Run escaped root.' }
if (Test-Path -LiteralPath $run) { throw 'Run is CreateNew; existing run cannot be overwritten.' }
[IO.Directory]::CreateDirectory($run) | Out-Null
$linuxRoot='/mnt/d/source/Sezika/.artifacts/independent-training-aot-20261008/linux-v1'
$linuxArgs=@('-d','Ubuntu','--exec','/bin/bash',($linuxRoot+'/bounded-linux.sh'),($linuxRoot+'/runs/'+$Name),[string]$TimeoutSeconds,[string]$CancelAfterSeconds,'--')+$LinuxArgumentList
# Linux budget includes readiness. Eight-second trap cleanup plus a twelve-second
# margin keeps the outer runner from killing wsl before Linux cleanup can finish.
$outer=if ($TimeoutSeconds -le 3) { 15 } else { $TimeoutSeconds+12 }
& 'D:\source\Sezika\tools\Invoke-BoundedProcess.ps1' -FilePath 'C:\Windows\System32\wsl.exe' -ArgumentList $linuxArgs -TimeoutSeconds $outer -LogName ('linux-'+$Name) -WorkingDirectory 'D:\source\Sezika' -LogDirectory (Join-Path $boundary 'windows-logs')
