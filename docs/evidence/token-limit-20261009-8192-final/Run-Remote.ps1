param([Parameter(Mandatory)][string]$Phase,[ValidateRange(20,1750)][int]$Seconds=90)
$ErrorActionPreference='Stop'
$task=$PSScriptRoot
if($PSVersionTable.PSVersion.Major -lt 7 -or $Phase -cnotmatch '^[a-z0-9-]+$'){throw 'PS7/named phase required'}
$label='remote-'+$Phase
if(Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -File -Filter ('*'+$label+'.result.json')|Select-Object -First 1){throw 'phase already attempted; inspect result instead of repeating'}
try {
 & (Join-Path $task 'Invoke-BoundedProcess.ps1') -FilePath 'C:\Program Files\PowerShell\7\pwsh.exe' -ArgumentList @('-NoProfile','-File',(Join-Path $task 'Invoke-Ssh.ps1'),'-ScriptFile',(Join-Path $task ('remote/'+$Phase+'.sh'))) -TimeoutSeconds $Seconds -LogName $label -ProcessQueryTimeoutSeconds 2 -CancelFile (Join-Path $task 'cancel.request') | Out-Null
}catch{Write-Output ('Guardian result retained: '+$_.Exception.Message)}
$receipts=@(Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -File -Filter ('*'+$label+'.result.json')|Select-Object -First 2)
if($receipts.Count -ne 1){throw 'single real receipt required'}
$r=Get-Content -Raw -LiteralPath $receipts[0].FullName|ConvertFrom-Json
$r|Select-Object Status,ExitCode,Pid,ElapsedSeconds,VerifiedLiveCount,UnknownCount|ConvertTo-Json
Get-Content -LiteralPath $r.StdoutPath -Tail 30
Get-Content -LiteralPath $r.StderrPath -Tail 15
if($null -eq $r.ExitCode -or $r.ExitCode -ne 0){throw 'real SSH/stage exit failed; no repeated payload'}
