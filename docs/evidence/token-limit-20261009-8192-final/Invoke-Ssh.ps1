param([Parameter(Mandatory)][string]$ScriptFile)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PS7 required'}
$task='D:\source\Sezika\.artifacts\token-limit-20261009-8192'
$path=[IO.Path]::GetFullPath($ScriptFile)
if([IO.Path]::GetDirectoryName($path) -cne (Join-Path $task 'remote') -or [IO.Path]::GetExtension($path) -cne '.sh' -or (Get-Item -LiteralPath $path).Length -gt 128KB){throw 'Remote script scope mismatch'}
if([IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'Cancelled'}
$literalScript=[IO.File]::ReadAllText($path)
& 'C:\Users\mysti\.codex\skills\usm-gateshell-ssh\scripts\invoke-ssh-bash.ps1' -Destination 'root@211.149.170.42' -Port 2222 -Option @('BatchMode=yes','ConnectTimeout=8','StrictHostKeyChecking=yes','ServerAliveInterval=5','ServerAliveCountMax=2') -SshExecutable 'C:\Windows\System32\OpenSSH\ssh.exe' -Script $literalScript
exit $LASTEXITCODE
