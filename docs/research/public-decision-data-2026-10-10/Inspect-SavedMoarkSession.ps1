param()
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required'}
$profilePath='D:\source\Sezika\.artifacts\model-publishing-20261009-01a12099\moark-current\owned-chrome-profile'
$live=@(Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" -OperationTimeoutSec 5|Where-Object {$_.CommandLine -and $_.CommandLine.Contains($profilePath)})
if($live.Count -gt 0){throw 'Saved dedicated profile is in use; preserve the existing browser'}
$cli='C:\Users\mysti\.codex\skills\playwright\scripts\playwright_cli.ps1'
$session='sezika-data-moark-saved-20261010'
$watch=[Diagnostics.Stopwatch]::StartNew()
try{
 & $cli ('-s='+$session) open 'https://moark.com/datasets/new?namespace=maikebing' --browser chrome --persistent --profile $profilePath
 if($LASTEXITCODE -ne 0){throw 'Dedicated browser launch failed'}
 if($watch.Elapsed.TotalSeconds -ge 65){throw 'Browser inspection deadline'}
 & $cli ('-s='+$session) eval 'async () => { const a=await fetch("/api/auth/session"); const s=await a.json(); const b=await fetch("/api/base/maikebing/profile"); let p={};try{p=await b.json()}catch{};return {session_status:a.status,session_user:s.user?{id:s.user.id,name:s.user.name,username:s.user.username}:null,profile_status:b.status,profile:{id:p.id,name:p.name,path:p.path,username:p.username}}; }'
 if($LASTEXITCODE -ne 0){throw 'Session verification failed'}
}finally{& $cli ('-s='+$session) close}
