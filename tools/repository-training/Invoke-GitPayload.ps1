param([Parameter(Mandatory)][string]$CommandFile)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required.'}
$command=Get-Content -LiteralPath $CommandFile -Raw | ConvertFrom-Json
$watch=[Diagnostics.Stopwatch]::StartNew();$released=$false
for($attempt=0;$attempt -lt 100 -and $watch.Elapsed.TotalSeconds -lt 10;$attempt++){
    $go=Join-Path $command.gate 'go'
    if(Test-Path -LiteralPath $go){if([IO.File]::ReadAllText($go) -cne [string]$PID){throw 'Gate PID differs.'};$released=$true;break}
    Start-Sleep -Milliseconds 100
}
if(-not $released){throw 'Guardian did not confirm payload identity within10s.'}
Set-Location -LiteralPath $command.directory
$arguments=@($command.arguments)
& $command.git @arguments
exit $LASTEXITCODE
