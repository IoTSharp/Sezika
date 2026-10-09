param([Parameter(Mandatory)][string]$ConnectionFile, [Parameter(Mandatory)][string]$ScriptFile)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 required' }
$connection = Get-Content -Raw -LiteralPath $ConnectionFile | ConvertFrom-Json
$script = [IO.File]::ReadAllText($ScriptFile)
if ($script.Length -gt 262144 -or $script.Contains([char]0)) { throw 'Remote script size/NUL rejected' }
$options = @('ConnectTimeout=10','StrictHostKeyChecking=accept-new',('UserKnownHostsFile='+$connection.known_hosts),'ServerAliveInterval=10','ServerAliveCountMax=2','NumberOfPasswordPrompts=1')
$options += if ($env:SEZIKA_SSH_PASSWORD) { 'BatchMode=no' } else { 'BatchMode=yes' }
$arguments = @{ Destination=($connection.user+'@'+$connection.host_name); Port=[int]$connection.port; Script=$script; Option=$options; SshExecutable='C:\Windows\System32\OpenSSH\ssh.exe' }
if ($connection.identity_file) { $arguments.IdentityFile=$connection.identity_file }
& 'C:\Users\mysti\.codex\skills\usm-gateshell-ssh\scripts\invoke-ssh-bash.ps1' @arguments
exit $LASTEXITCODE
