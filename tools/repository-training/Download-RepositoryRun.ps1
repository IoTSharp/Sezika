param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{2,63}$')][string]$RunId,
    [Parameter(Mandatory)][string]$TaskDirectory,
    [Parameter(Mandatory)][string]$RemoteRoot,
    [ValidatePattern('^[A-Za-z0-9_-]+$')][string]$OutputDirectory='trained',
    [ValidateRange(30,1500)][int]$ArchiveTimeoutSeconds=600,
    [string]$ArtifactDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required.'}
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'));$task=(Resolve-Path -LiteralPath $TaskDirectory).Path
if((Split-Path $task -Leaf) -cne ('repository-training-'+$RunId) -or $RemoteRoot -cnotmatch '^/[A-Za-z0-9_./-]+/sezika-repository-[a-z0-9-]+$' -or $RemoteRoot.Contains('..') -or -not $RemoteRoot.EndsWith('/sezika-repository-'+$RunId,[StringComparison]::Ordinal)){throw 'Matching local and remote task roots required.'}
$artifacts=if($ArtifactDirectory){[IO.Path]::GetFullPath($ArtifactDirectory)}else{$task}
$externalArtifacts=-not [string]::Equals($artifacts,$task,[StringComparison]::OrdinalIgnoreCase)
if($externalArtifacts -and ((Split-Path $artifacts -Leaf) -cne $RunId -or (Test-Path -LiteralPath $artifacts))){throw 'External artifacts require a fresh dedicated directory named exactly for this run.'}
$pathWatch=[Diagnostics.Stopwatch]::StartNew();$cursor=$artifacts;$ancestryComplete=$false
for($depth=0;$depth -lt 64;$depth++){
    if($pathWatch.Elapsed.TotalSeconds -ge 5){throw 'Artifact ancestry deadline5s.'}
    if((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Artifact linked ancestry rejected.'}
    $parent=[IO.Path]::GetDirectoryName($cursor);if($null -eq $parent){$ancestryComplete=$true;break};$cursor=$parent
}
if(-not $ancestryComplete){throw 'Artifact ancestry bound64.'}
$connectionPath=Join-Path $task 'connection.json';$connection=Get-Content -LiteralPath $connectionPath -Raw|ConvertFrom-Json
$deadline=Get-Content -LiteralPath (Join-Path $task 'deadline.json') -Raw|ConvertFrom-Json
$watch=[Diagnostics.Stopwatch]::StartNew();$logs=Join-Path $task 'download-processes';$tag=[Guid]::NewGuid().ToString('N').Substring(0,8);$environment=@{}
if($env:SEZIKA_SSH_PASSWORD){$environment.SEZIKA_SSH_PASSWORD=$env:SEZIKA_SSH_PASSWORD;$environment.SSH_ASKPASS=Join-Path $root 'tools/rental-training/Askpass.cmd';$environment.SSH_ASKPASS_REQUIRE='force';$environment.DISPLAY='sezika-task-askpass'}
function Remaining{
    $remaining=[Math]::Min(1800-$watch.Elapsed.TotalSeconds,(([DateTime]$deadline.deadline_utc).ToUniversalTime()-[DateTime]::UtcNow).TotalSeconds-30)
    if($remaining -lt 10 -or (Test-Path -LiteralPath (Join-Path $task 'CANCEL'))){throw 'Download deadline/cancellation observed.'};return [int][Math]::Floor($remaining)
}
function Run([string]$Name,[string]$File,[string[]]$Arguments,[int]$CommandTimeoutSeconds=600){
    $label=$tag+'-'+$Name
    & (Join-Path $root 'tools/rental-training/Invoke-BoundedProcess.ps1') -FilePath $File -ArgumentList $Arguments -Environment $environment -TimeoutSeconds ([Math]::Min($CommandTimeoutSeconds,(Remaining))) -LogName $label -LogDirectory $logs -CancelFile (Join-Path $task 'CANCEL') | Out-Host
    $receipt=@(Get-ChildItem -LiteralPath $logs -File -Filter ('*'+$label+'.result.json')|Select-Object -First 2);if($receipt.Count -ne 1){throw 'Download receipt identity differs.'};return Get-Content -LiteralPath $receipt[0].FullName -Raw|ConvertFrom-Json
}
$probe=@'
set -euo pipefail
root='__ROOT__'
output='__OUTPUT__'
[[ -f $root/OWNED_BY_SEZIKA_REPOSITORY_TASK && ! -L $root && -f $root/$output.tar.sha256 ]]
timeout 5s cat -- "$root/$output.tar.sha256"
timeout 5s stat -c 'archive_bytes %s' -- "$root/$output.tar"
'@
$probe=$probe.Replace('__ROOT__',$RemoteRoot).Replace('__OUTPUT__',$OutputDirectory);$probePath=Join-Path $task ($tag+'-download-probe.sh');[IO.File]::WriteAllText($probePath,$probe,[Text.UTF8Encoding]::new($false))
$receipt=Run 'probe' 'C:\Program Files\PowerShell\7\pwsh.exe' @('-NoProfile','-File',(Join-Path $root 'tools/rental-training/Invoke-Ssh.ps1'),'-ConnectionFile',$connectionPath,'-ScriptFile',$probePath)
$text=[IO.File]::ReadAllText($receipt.StdoutPath);$matches=[regex]::Matches($text,'(?m)^([a-f0-9]{64})  '+[regex]::Escape($OutputDirectory)+ '\.tar\r?$');if($matches.Count -ne 1){throw 'Remote archive hash is missing or ambiguous.'};$hash=$matches[0].Groups[1].Value
$sizeMatches=[regex]::Matches($text,'(?m)^archive_bytes ([0-9]{1,11})\r?$');if($sizeMatches.Count -ne 1){throw 'Remote archive byte count is missing or ambiguous.'}
[long]$archiveBytes=$sizeMatches[0].Groups[1].Value;if($archiveBytes -lt 1 -or $archiveBytes -gt 16GB){throw 'Remote archive byte bound1..16GiB.'}
$drive=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($artifacts));[long]$requiredBytes=2*$archiveBytes+1GB
if(-not $drive.IsReady -or $drive.AvailableFreeSpace -lt $requiredBytes){throw ('Artifact volume requires archive + extraction +1GiB: '+$requiredBytes+' bytes.')}
if($externalArtifacts){[void][IO.Directory]::CreateDirectory($artifacts)}
$archive=Join-Path $artifacts ($OutputDirectory+'-'+$hash.Substring(0,12)+'.tar');if(Test-Path -LiteralPath $archive){throw 'Archive exists; verify it and resume extraction separately.'}
[IO.File]::WriteAllText((Join-Path $task ($tag+'-download-storage.json')),(@{run_id=$RunId;artifact_directory=$artifacts;archive_bytes=$archiveBytes;required_free_bytes=$requiredBytes;observed_free_bytes=$drive.AvailableFreeSpace;external_artifacts=$externalArtifacts}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$options=@('-P',[string]$connection.port,'-o','ConnectTimeout=10','-o','StrictHostKeyChecking=accept-new','-o',('UserKnownHostsFile='+$connection.known_hosts),'-o','ServerAliveInterval=10','-o','ServerAliveCountMax=2','-o',$(if($environment.Count){'BatchMode=no'}else{'BatchMode=yes'}),'-o','NumberOfPasswordPrompts=1')
if($connection.identity_file){$options+=@('-i',[string]$connection.identity_file)}
$archiveArguments=$options+@(($connection.user+'@'+$connection.host_name+':'+$RemoteRoot+'/'+$OutputDirectory+'.tar'),$archive)
[void](Run 'archive' 'C:\Windows\System32\OpenSSH\scp.exe' $archiveArguments $ArchiveTimeoutSeconds)
if((Get-Item -LiteralPath $archive).Length -ne $archiveBytes){throw 'Downloaded archive byte count differs.'}
$result=& (Join-Path $PSScriptRoot 'Expand-RepositoryArchive.ps1') -Archive $archive -ExpectedSha256 $hash -Destination (Join-Path $artifacts 'retrieved-model') -TimeoutSeconds (Remaining) -CancelFile (Join-Path $task 'CANCEL')
[IO.File]::WriteAllText((Join-Path $task 'model-download.json'),($result|ConvertTo-Json),[Text.UTF8Encoding]::new($false));$result
