param(
    [string]$HostName, [ValidateRange(1,65535)][int]$Port=22, [string]$User,
    [string]$SshCommand, [string]$IdentityFile='', [Security.SecureString]$Password,
    [string]$RemoteBaseDirectory='home', [string]$GpuUuid='auto',
    [string]$HostKeyFingerprint='',
    [string]$TaskDirectory='D:\source\Sezika\.artifacts\rental-training-20261009-01a12099',
    [switch]$PreflightOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 required' }
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$task=[IO.Path]::GetFullPath($TaskDirectory)
if (-not $task.StartsWith((Join-Path $repo '.artifacts\rental-training-'),[StringComparison]::OrdinalIgnoreCase)) { throw 'Task directory outside rental scope' }
$clock=[Diagnostics.Stopwatch]::StartNew(); $calls=0; $runTag=[Guid]::NewGuid().ToString('N').Substring(0,8)
$cancel=Join-Path $task 'cancel.request'; $logs=Join-Path $task 'process-logs'
$summary=[ordered]@{started_utc=[DateTime]::UtcNow.ToString('o');status='started';remote='';stages=[ordered]@{};receipts=@();error='';new_training=$false;teacher_calls=0}
$remoteRoot=''; $remoteReady=$false; $failure=$null; $currentPhase=''
if ($SshCommand) {
    if ($HostName -or $User -or $IdentityFile -or $SshCommand.Length -gt 2048 -or $SshCommand -match '[\r\n;&|`$<>]') { throw 'Use explicit parameters or a plain ssh command' }
    $parts=@([regex]::Matches($SshCommand,'"[^"]*"|[^\s]+') | ForEach-Object { $_.Value.Trim('"') })
    if ($parts.Count -lt 2 -or $parts.Count -gt 16 -or $parts[0] -notin @('ssh','ssh.exe')) { throw 'Expected plain ssh command' }
    for ($index=1; $index -lt $parts.Count -and $index -lt 16; $index++) {
        if ($clock.Elapsed.TotalSeconds -ge 5) { throw 'SSH parsing deadline' }
        switch ($parts[$index]) {
            '-p' { $index++; if ($index -ge $parts.Count) { throw 'Missing port' }; $Port=[int]$parts[$index] }
            '-i' { $index++; if ($index -ge $parts.Count) { throw 'Missing key path' }; $IdentityFile=$parts[$index] }
            default {
                if ($HostName -or $parts[$index] -cnotmatch '^([A-Za-z0-9_.+-]+)@([A-Za-z0-9.-]+)$') { throw 'Only -p/-i and one user@host are accepted' }
                $User=$Matches[1]; $HostName=$Matches[2]
            }
        }
    }
}
if ($HostName -cnotmatch '^[A-Za-z0-9.-]+$' -or $User -cnotmatch '^[A-Za-z0-9_.+-]+$' -or $Port -lt 1 -or $Port -gt 65535) { throw 'Valid SSH host/user/port required' }
if ($RemoteBaseDirectory -cne 'home' -and ($RemoteBaseDirectory -cnotmatch '^/[A-Za-z0-9_./+-]+$' -or $RemoteBaseDirectory.Contains('..'))) { throw 'Safe absolute remote base required' }
if ($GpuUuid -cne 'auto' -and $GpuUuid -cnotmatch '^GPU-[A-Fa-f0-9-]+$') { throw 'GPU UUID invalid' }
if ($IdentityFile) { $IdentityFile=(Resolve-Path -LiteralPath $IdentityFile).Path; if ((Get-Item -LiteralPath $IdentityFile).PSIsContainer) { throw 'Key must be a file' } }
[void][IO.Directory]::CreateDirectory($logs)
$connection=[ordered]@{host_name=$HostName;user=$User;port=$Port;identity_file=$IdentityFile;known_hosts=(Join-Path $task 'known_hosts');first_contact_trust='OpenSSH accept-new task-scoped; independent fingerprint not supplied'}
$connectionPath=Join-Path $task 'connection.json'
if (Test-Path -LiteralPath $connectionPath) {
    $previous=Get-Content -Raw -LiteralPath $connectionPath | ConvertFrom-Json
    if ($previous.host_name -cne $HostName -or $previous.user -cne $User -or $previous.port -ne $Port) { throw 'Task already belongs to another connection; preserve it' }
}
[IO.File]::WriteAllText($connectionPath,($connection|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$taskEnvironment=@{}
if ($Password) {
    $pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
    try { $taskEnvironment.SEZIKA_SSH_PASSWORD=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
} elseif ($env:SEZIKA_SSH_PASSWORD) { $taskEnvironment.SEZIKA_SSH_PASSWORD=$env:SEZIKA_SSH_PASSWORD }
if ($taskEnvironment.ContainsKey('SEZIKA_SSH_PASSWORD')) {
    $taskEnvironment.SSH_ASKPASS=Join-Path $PSScriptRoot 'Askpass.cmd'
    $taskEnvironment.SSH_ASKPASS_REQUIRE='force'; $taskEnvironment.DISPLAY='sezika-local-askpass'
}
function Guard {
    if ($clock.Elapsed.TotalSeconds -ge 7200 -or [IO.File]::Exists($cancel)) { throw 'Pipeline cancelled/deadline7200s' }
    $deadlinePath=Join-Path $task 'deadline.json'
    if(Test-Path -LiteralPath $deadlinePath){$deadline=Get-Content -Raw -LiteralPath $deadlinePath|ConvertFrom-Json;if([DateTime]::UtcNow -ge ([DateTime]$deadline.deadline_utc).ToUniversalTime()){throw 'User three-hour deadline reached'}}
}
function Invoke-TaskProcess([string]$Name,[string]$Executable,[string[]]$Arguments,[int]$Seconds,[switch]$CleanupCall) {
    if (-not $CleanupCall) { Guard }
    $deadlinePath=Join-Path $task 'deadline.json'
    if(Test-Path -LiteralPath $deadlinePath){
        $deadline=Get-Content -Raw -LiteralPath $deadlinePath|ConvertFrom-Json
        $end=if($CleanupCall){([DateTime]$deadline.rental_end_utc).ToUniversalTime()}else{([DateTime]$deadline.deadline_utc).ToUniversalTime()}
        $remaining=[Math]::Floor(($end-[DateTime]::UtcNow).TotalSeconds)
        if($remaining -lt 5){throw 'Insufficient absolute deadline for an external step'}
        $Seconds=[Math]::Min($Seconds,[int]$remaining)
    }
    $script:calls++; if ($script:calls -gt 40) { throw 'Pipeline external-call cap40' }
    $label=$runTag+'-'+$Name; $caught=$null
    try {
        & (Join-Path $PSScriptRoot 'Invoke-BoundedProcess.ps1') -FilePath $Executable -ArgumentList $Arguments -Environment $taskEnvironment -TimeoutSeconds $Seconds -LogName $label -LogDirectory $logs -WorkingDirectory $repo -CancelFile $(if($CleanupCall){''}else{$cancel}) | Out-Host
    } catch { $caught=$_ }
    $matches=@(Get-ChildItem -LiteralPath $logs -File -Filter ('*'+$label+'.result.json') | Select-Object -First 2)
    if ($matches.Count -ne 1) { throw 'Unique process receipt missing' }
    $receipt=Get-Content -Raw -LiteralPath $matches[0].FullName | ConvertFrom-Json
    $summary.receipts+=@{name=$Name;path=$matches[0].FullName;exit=$receipt.ExitCode;status=$receipt.Status;final_check_complete=$receipt.FinalCheckComplete;unknown=$receipt.UnknownCount}
    if ($caught) { throw $caught }
    return $receipt
}
function Invoke-Remote([string]$Name,[string]$LiteralScript,[int]$Seconds,[switch]$CleanupCall) {
    $path=Join-Path $task ($runTag+'-'+$Name+'.sh')
    if (Test-Path -LiteralPath $path) { throw 'Unique remote script collision' }
    [IO.File]::WriteAllText($path,$LiteralScript.Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
    $receipt=Invoke-TaskProcess $Name 'C:\Program Files\PowerShell\7\pwsh.exe' @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Invoke-Ssh.ps1'),'-ConnectionFile',$connectionPath,'-ScriptFile',$path) $Seconds -CleanupCall:$CleanupCall
    return [IO.File]::ReadAllText($receipt.StdoutPath)
}
function Scp-Arguments {
    $options=@('-P',$Port.ToString(),'-o','ConnectTimeout=10','-o','StrictHostKeyChecking=accept-new','-o',('UserKnownHostsFile='+$connection.known_hosts),'-o','ServerAliveInterval=10','-o','ServerAliveCountMax=2','-o','NumberOfPasswordPrompts=1','-o',$(if($taskEnvironment.ContainsKey('SEZIKA_SSH_PASSWORD')){'BatchMode=no'}else{'BatchMode=yes'}))
    if ($IdentityFile) { $options+=@('-i',$IdentityFile) }; return $options
}
function Expand-Results([string]$Archive,[string]$Directory) {
    [void][IO.Directory]::CreateDirectory($Directory)
    $stream=[IO.File]::OpenRead($Archive); $reader=[System.Formats.Tar.TarReader]::new($stream,$false)
    $extractClock=[Diagnostics.Stopwatch]::StartNew(); $bytes=0L
    try {
        for ($item=0; $item -lt 1200 -and $extractClock.Elapsed.TotalSeconds -lt 90; $item++) {
            $entry=$reader.GetNextEntry($false); if ($null -eq $entry) { break }
            $name=if($entry.Name.StartsWith('./',[StringComparison]::Ordinal)){$entry.Name.Substring(2)}else{$entry.Name}
            if ($name -cnotmatch '^[A-Za-z0-9_./-]+$' -or $name.Contains('..') -or $entry.EntryType -notin @([System.Formats.Tar.TarEntryType]::RegularFile,[System.Formats.Tar.TarEntryType]::V7RegularFile,[System.Formats.Tar.TarEntryType]::Directory)) { throw 'Unsafe result tar member' }
            $target=[IO.Path]::GetFullPath((Join-Path $Directory $name))
            if (-not $target.StartsWith($Directory+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Result path escaped destination' }
            if ($entry.EntryType -eq [System.Formats.Tar.TarEntryType]::Directory) { [void][IO.Directory]::CreateDirectory($target); continue }
            $bytes+=$entry.Length; if ($entry.Length -gt 64MB -or $bytes -gt 128MB) { throw 'Result bytes cap' }
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
            $entry.ExtractToFile($target,$false)
        }
        if ($item -ge 1200 -or $extractClock.Elapsed.TotalSeconds -ge 90) { throw 'Result extraction count/time bound' }
    } finally { $reader.Dispose(); $stream.Dispose() }
}
try {
    if ($HostKeyFingerprint) {
        if ($HostKeyFingerprint -cnotmatch '^SHA256:[A-Za-z0-9+/]+$') { throw 'Fingerprint syntax' }
        $scan=Invoke-TaskProcess keyscan 'C:\Windows\System32\OpenSSH\ssh-keyscan.exe' @('-T','8','-t','ed25519','-p',$Port.ToString(),$HostName) 15
        $fingerprints=Invoke-TaskProcess fingerprint 'C:\Windows\System32\OpenSSH\ssh-keygen.exe' @('-lf',$scan.StdoutPath) 15
        if (-not [IO.File]::ReadAllText($fingerprints.StdoutPath).Contains($HostKeyFingerprint)) { throw 'Host key fingerprint mismatch' }
        [IO.File]::Copy($scan.StdoutPath,$connection.known_hosts,$true)
        $connection.first_contact_trust='Matched explicitly supplied fingerprint '+$HostKeyFingerprint
        [IO.File]::WriteAllText($connectionPath,($connection|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    }
    $preflight=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'remote\preflight.sh')).Replace('__REMOTE_BASE__',$RemoteBaseDirectory).Replace('__GPU_UUID__',$GpuUuid)
    $preflightText=Invoke-Remote preflight $preflight 65
    $baseMatches=[regex]::Matches($preflightText,'(?m)^remote_base=([^\r\n]+)'); $gpuMatches=[regex]::Matches($preflightText,'(?m)^selected_gpu_uuid=([^\r\n]+)')
    if ($baseMatches.Count -ne 1 -or $gpuMatches.Count -ne 1 -or -not $preflightText.Contains('preflight=passed_read_only')) { throw 'Preflight admission fields missing' }
    $base=$baseMatches[0].Groups[1].Value; $selected=$gpuMatches[0].Groups[1].Value
    if ($base -cnotmatch '^/[A-Za-z0-9_./+-]+$' -or $base.Contains('..') -or $selected -cnotmatch '^GPU-[A-Fa-f0-9-]+$') { throw 'Preflight returned unsafe path/UUID' }
    $remoteRoot=$base+'/sezika-rental-20261009-01a12099'; $summary.remote=$remoteRoot; $summary.gpu_uuid=$selected
    if ($PreflightOnly) { $summary.status='preflight_passed'; return }
    $freeze=Get-Content -Raw -LiteralPath (Join-Path $task 'package-preparation.json') | ConvertFrom-Json
    $archive=Join-Path $task 'upload.tar'
    if ((Get-Item -LiteralPath $archive).Length -ne $freeze.archive_bytes -or (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() -cne $freeze.archive_sha256) { throw 'Frozen local archive changed' }
    $archiveHash=$freeze.archive_sha256
    $bootstrap=@'
set -euo pipefail
umask 077
task='__TASK__'; base='__BASE__'; expected='__SHA__'; gpu='__GPU__'
[[ ! -L $base && ! -L $task ]]
mkdir -p "$base"
if [[ ! -e $task ]]; then
  mkdir "$task"; mkdir "$task/logs" "$task/output" "$task/.artifacts"
  printf '%s\n' sezika-rental-20261009-01a12099 > "$task/task-identity"
  printf '%s\n' "$expected" > "$task/archive-sha256"
  printf '%s\n' "$gpu" > "$task/gpu-uuid"
fi
[[ $(timeout 2s cat "$task/task-identity") == sezika-rental-20261009-01a12099 && $(timeout 2s cat "$task/archive-sha256") == "$expected" && $(timeout 2s cat "$task/gpu-uuid") == "$gpu" ]]
[[ ! -e $task/cancel.request ]]
if [[ -f $task/upload.tar ]]; then
  printf '%s  %s\n' "$expected" "$task/upload.tar" | timeout 60s sha256sum -c --quiet -
  printf 'upload_state=verified\n'
elif [[ -f $task/upload.tar.partial && ! -L $task/upload.tar.partial ]]; then
  actual=$(timeout 60s sha256sum "$task/upload.tar.partial"); actual=${actual%% *}
  if [[ $actual == "$expected" ]]; then mv -T "$task/upload.tar.partial" "$task/upload.tar"; printf 'upload_state=verified\n'; else printf 'upload_state=incomplete\n'; fi
else printf 'upload_state=missing\n'; fi
'@
    $bootstrap=$bootstrap.Replace('__TASK__',$remoteRoot).Replace('__BASE__',$base).Replace('__SHA__',$archiveHash).Replace('__GPU__',$selected)
    $state=Invoke-Remote bootstrap $bootstrap 85
    $remoteReady=$true
    if (-not $state.Contains('upload_state=verified')) {
        $target=$User+'@'+$HostName+':'+$remoteRoot+'/upload.tar.partial'
        [void](Invoke-TaskProcess upload 'C:\Windows\System32\OpenSSH\scp.exe' ((Scp-Arguments)+@($archive,$target)) 1800)
        $state=Invoke-Remote confirm-upload $bootstrap 85
        if (-not $state.Contains('upload_state=verified')) { throw 'Uploaded archive SHA not admitted' }
    }
    # Copy only the two reviewed bootstrap scripts; the full archive then verifies these bytes too.
    $bootstrapFiles=@((Join-Path $PSScriptRoot 'remote\lib.sh'),(Join-Path $PSScriptRoot 'remote\install.sh'))
    $createRemote=@'
set -euo pipefail
task='__TASK__'
[[ $(timeout 2s cat "$task/task-identity") == sezika-rental-20261009-01a12099 && ! -L $task/remote ]]
mkdir -p "$task/remote"
'@
    [void](Invoke-Remote create-remote ($createRemote.Replace('__TASK__',$remoteRoot)) 15)
    [void](Invoke-TaskProcess upload-install 'C:\Windows\System32\OpenSSH\scp.exe' ((Scp-Arguments)+$bootstrapFiles+@($User+'@'+$HostName+':'+$remoteRoot+'/remote/')) 30)
    $install=@'
set -euo pipefail
bash '__TASK__/remote/install.sh' '__TASK__' '__SHA__'
'@
    [void](Invoke-Remote install ($install.Replace('__TASK__',$remoteRoot).Replace('__SHA__',$archiveHash)) 220)
    $phases=@('contract','tiny','train','validate','regress')
    foreach ($phase in $phases) {
        Guard; $currentPhase=$phase
        $deadlinePath=Join-Path $task 'deadline.json'
        if($phase -cne 'contract' -and (Test-Path -LiteralPath $deadlinePath)){
            $deadline=Get-Content -Raw -LiteralPath $deadlinePath|ConvertFrom-Json
            $remaining=(([DateTime]$deadline.deadline_utc).ToUniversalTime()-[DateTime]::UtcNow).TotalSeconds
            $needed=switch($phase){tiny{4400};train{4320};validate{2740};regress{1360}}
            if([DateTime]::UtcNow -ge ([DateTime]$deadline.new_gpu_launch_cutoff_utc).ToUniversalTime() -or $remaining -lt $needed){throw 'GPU stage admission would consume the bounded download reserve'}
        }
        $stagePath='remote/stage.sh'
        if(Test-Path -LiteralPath (Join-Path $task 'execution-override.json')){
            $override=Get-Content -Raw -LiteralPath (Join-Path $task 'execution-override.json')|ConvertFrom-Json
            if($override.stage_path -cne 'remote/stage-admission-v2.sh'){throw 'Unknown execution overlay'}
            $stagePath=$override.stage_path
        }
        $stage=@'
set -euo pipefail
bash '__TASK__/__STAGE_PATH__' '__TASK__' '__PHASE__'
'@
        $seconds=switch($phase){contract{100};tiny{120};train{1620};validate{1620};regress{410}}
        $text=Invoke-Remote ('stage-'+$phase) ($stage.Replace('__TASK__',$remoteRoot).Replace('__PHASE__',$phase).Replace('__STAGE_PATH__',$stagePath)) $seconds
        $summary.stages[$phase]='passed_or_previously_completed'
        if ($phase -ceq 'train') {
            if($text.Contains('stage_completed=train')) { $summary.new_training=$true }
            [void](Invoke-TaskProcess download-model 'C:\Program Files\PowerShell\7\pwsh.exe' @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Download-Model.ps1'),'-RemoteRoot',$remoteRoot,'-TaskDirectory',$task) 150)
        }
    }
    $summary.status='experiment_completed'
} catch { $failure=$_; $summary.status='failed'; $summary.error=$_.Exception.Message }
finally {
    if ($remoteReady) {
        try {
            if ([IO.File]::Exists($cancel) -and $currentPhase) {
                $cancelScript=@'
set -euo pipefail
task='__TASK__'; phase='__PHASE__'
[[ $(timeout 2s cat "$task/task-identity") == sezika-rental-20261009-01a12099 ]]
[[ -d $task/.artifacts/stage-$phase && ! -L $task/.artifacts/stage-$phase ]]
: > "$task/.artifacts/stage-$phase/cancel.request"
'@
                [void](Invoke-Remote cancel ($cancelScript.Replace('__TASK__',$remoteRoot).Replace('__PHASE__',$currentPhase)) 15 -CleanupCall)
            }
            $collect=@'
set -euo pipefail
bash '__TASK__/remote/collect.sh' '__TASK__' '__TAG__'
'@
            $result=Invoke-Remote collect ($collect.Replace('__TASK__',$remoteRoot).Replace('__TAG__',$runTag)) 120 -CleanupCall
            $hashMatch=[regex]::Matches($result,('(?m)^([a-f0-9]{64})  results-'+$runTag+'\.tar\r?$'))
            if ($hashMatch.Count -ne 1) { throw 'Remote result SHA missing' }
            $destination=Join-Path $task ('results-'+$hashMatch[0].Groups[1].Value.Substring(0,12)+'.tar')
            $remoteResult=$User+'@'+$HostName+':'+$remoteRoot+'/results-'+$runTag+'.tar'
            if (-not (Test-Path -LiteralPath $destination)) { [void](Invoke-TaskProcess results-download 'C:\Windows\System32\OpenSSH\scp.exe' ((Scp-Arguments)+@($remoteResult,$destination)) 150 -CleanupCall) }
            if ((Get-Item -LiteralPath $destination).Length -gt 128MB -or (Get-FileHash -LiteralPath $destination).Hash.ToLowerInvariant() -cne $hashMatch[0].Groups[1].Value) { throw 'Downloaded result SHA/size mismatch' }
            $extracted=Join-Path $task ('results-'+$hashMatch[0].Groups[1].Value.Substring(0,12))
            if (-not (Test-Path -LiteralPath $extracted)) { Expand-Results $destination $extracted }
            $summary.results_archive=$destination; $summary.results_sha256=$hashMatch[0].Groups[1].Value; $summary.results_directory=$extracted
        } catch { $summary.collection_error=$_.Exception.Message; if (-not $failure) { $failure=$_; $summary.status='collection_failed' } }
    }
    $summary.finished_utc=[DateTime]::UtcNow.ToString('o'); $summary.elapsed_seconds=$clock.Elapsed.TotalSeconds
    [IO.File]::WriteAllText((Join-Path $task ('pipeline-'+$runTag+'.json')),($summary|ConvertTo-Json -Depth 7),[Text.UTF8Encoding]::new($false))
    $taskEnvironment.Clear()
}
if ($failure) { throw $failure }
$summary|ConvertTo-Json -Depth 5
