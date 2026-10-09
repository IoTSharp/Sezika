param([Parameter(Mandatory)][string]$RemoteRoot,[string]$TaskDirectory='D:\source\Sezika\.artifacts\rental-training-20261009-01a12099',[switch]$WaitForTraining)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required'}
$task=[IO.Path]::GetFullPath($TaskDirectory)
if($RemoteRoot -cnotmatch '^/[A-Za-z0-9_./+-]+/sezika-rental-20261009-01a12099$' -or $RemoteRoot.Contains('..')){throw 'Remote task path invalid'}
$connectionPath=Join-Path $task 'connection.json';$connection=Get-Content -Raw -LiteralPath $connectionPath|ConvertFrom-Json
$logs=Join-Path $task 'process-logs';$clock=[Diagnostics.Stopwatch]::StartNew();$tag=[Guid]::NewGuid().ToString('N').Substring(0,8)
$environment=@{}
$localLock=$null
if($env:SEZIKA_SSH_PASSWORD){$environment.SEZIKA_SSH_PASSWORD=$env:SEZIKA_SSH_PASSWORD;$environment.SSH_ASKPASS=Join-Path $PSScriptRoot 'Askpass.cmd';$environment.SSH_ASKPASS_REQUIRE='force';$environment.DISPLAY='sezika-local-askpass'}
function Guard {
    if($clock.Elapsed.TotalSeconds -ge 1800 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'Model retrieval cancelled/deadline1800s'}
    $deadlinePath=Join-Path $task 'deadline.json'
    if(Test-Path -LiteralPath $deadlinePath){$deadline=Get-Content -Raw -LiteralPath $deadlinePath|ConvertFrom-Json;if([DateTime]::UtcNow -ge ([DateTime]$deadline.deadline_utc).ToUniversalTime()){throw 'Three-hour model retrieval deadline reached'}}
}
function Run([string]$Name,[string]$Executable,[string[]]$Arguments,[int]$Seconds){
    Guard;$label=$tag+'-'+$Name
    & (Join-Path $PSScriptRoot 'Invoke-BoundedProcess.ps1') -FilePath $Executable -ArgumentList $Arguments -Environment $environment -TimeoutSeconds $Seconds -LogName $label -LogDirectory $logs -CancelFile (Join-Path $task 'cancel.request')|Out-Host
    $files=@(Get-ChildItem -LiteralPath $logs -File -Filter ('*'+$label+'.result.json')|Select-Object -First 2)
    if($files.Count -ne 1){throw 'Unique model retrieval receipt required'}
    return Get-Content -Raw -LiteralPath $files[0].FullName|ConvertFrom-Json
}
$probe=@'
set -euo pipefail
task='__TASK__'
[[ $(timeout 2s cat "$task/task-identity") == sezika-rental-20261009-01a12099 ]]
receipt="$task/.artifacts/stage-train/result.tsv"
if [[ ! -f $receipt ]]; then printf 'model_state=pending\n'; exit 0; fi
exec {downloadfd}> "$task/model-download.lock"; timeout 10s flock -w 8 "$downloadfd" || exit 73
begin=$SECONDS
for field in 'status completed' 'payload_exit 0' 'final_check_complete 1' 'verified_live_count 0' 'unknown_count 0' 'cleanup_incomplete 0'; do
  ((SECONDS-begin<6)) || exit 124
  read -r key value <<< "$field"
  [[ $(timeout 1s awk -F '\t' -v k="$key" '$1==k {print $2}' "$receipt") == "$value" ]] || { printf 'model_state=training_failed\n'; exit 73; }
done
source="$task/output/run-24576"
[[ ! -L $source ]]
if [[ ! -f $task/trained-assets.tar ]]; then
  [[ ! -L $task/model-download ]]
  mkdir -p "$task/model-download"
  for name in expanded-training-report.json expanded-head.asset expanded-training.checkpoint; do
    ((SECONDS-begin<15)) || exit 124
    [[ -f $source/$name && ! -L $source/$name && $(timeout 1s stat -c %s "$source/$name") -le 8388608 ]]
    if [[ -f $task/model-download/$name ]]; then timeout 2s cmp "$source/$name" "$task/model-download/$name"; else timeout 2s cp -- "$source/$name" "$task/model-download/$name"; fi
  done
  if [[ -f $task/model-download/model.json ]]; then timeout 2s cmp "$task/package/model.json" "$task/model-download/model.json"; else timeout 2s cp -- "$task/package/model.json" "$task/model-download/model.json"; fi
  (cd "$task/model-download" && timeout 5s sha256sum expanded-training-report.json expanded-head.asset expanded-training.checkpoint model.json > files.sha256)
  timeout 5s tar -cf "$task/trained-assets.tar.partial" -C "$task/model-download" .
  mv -T "$task/trained-assets.tar.partial" "$task/trained-assets.tar"
  (cd "$task" && timeout 5s sha256sum trained-assets.tar > trained-assets.sha256)
fi
(cd "$task" && timeout 5s sha256sum -c --quiet trained-assets.sha256)
printf 'model_state=ready\n'
timeout 2s cat "$task/trained-assets.sha256"
'@
$probe=$probe.Replace('__TASK__',$RemoteRoot)
$probePath=Join-Path $task ($tag+'-model-probe.sh');[IO.File]::WriteAllText($probePath,$probe,[Text.UTF8Encoding]::new($false))
try {
    $lockPath=Join-Path $task 'model-download.lock'
    for($lockAttempt=0;$lockAttempt -lt 120 -and $clock.Elapsed.TotalSeconds -lt 120;$lockAttempt++){
        Guard
        try{$localLock=[IO.FileStream]::new($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);break}catch [IO.IOException]{Start-Sleep -Seconds 1}
    }
    if($null -eq $localLock){throw 'Model download already active beyond120s; preserve it'}
    $ready=$false;$hash=''
    for($attempt=0;$attempt -lt 60 -and $clock.Elapsed.TotalSeconds -lt 1740;$attempt++){
        Guard
        $receipt=Run ('model-probe-'+$attempt) 'C:\Program Files\PowerShell\7\pwsh.exe' @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Invoke-Ssh.ps1'),'-ConnectionFile',$connectionPath,'-ScriptFile',$probePath) 35
        $text=[IO.File]::ReadAllText($receipt.StdoutPath)
        if($text.Contains('model_state=ready')){$match=[regex]::Matches($text,'(?m)^([a-f0-9]{64})  trained-assets\.tar\r?$');if($match.Count -ne 1){throw 'Model tar SHA missing'};$hash=$match[0].Groups[1].Value;$ready=$true;break}
        if(-not $WaitForTraining){throw 'Training artifacts not ready'}
        for($pause=0;$pause -lt 30;$pause++){Guard;Start-Sleep -Seconds 1}
    }
    if(-not $ready){throw 'Model wait item/time bound reached'}
    $options=@('-P',$connection.port.ToString(),'-o','ConnectTimeout=10','-o','StrictHostKeyChecking=accept-new','-o',('UserKnownHostsFile='+$connection.known_hosts),'-o','ServerAliveInterval=10','-o','ServerAliveCountMax=2','-o',$(if($environment.Count -gt 0){'BatchMode=no'}else{'BatchMode=yes'}),'-o','NumberOfPasswordPrompts=1')
    if($connection.identity_file){$options+=@('-i',$connection.identity_file)}
    $archive=Join-Path $task ('trained-assets-'+$hash.Substring(0,12)+'.tar')
    $remoteArchive=$connection.user+'@'+$connection.host_name+':'+$RemoteRoot+'/trained-assets.tar'
    if(-not(Test-Path -LiteralPath $archive)){[void](Run 'trained-assets-download' 'C:\Windows\System32\OpenSSH\scp.exe' ($options+@($remoteArchive,$archive)) 90)}
    if((Get-Item -LiteralPath $archive).Length -gt 16MB -or (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant() -cne $hash){throw 'Model archive identity/bytes differ'}
    $model=Join-Path $task 'trained-model'
    if(-not(Test-Path -LiteralPath (Join-Path $task 'model-download.json'))){
        [void][IO.Directory]::CreateDirectory($model)
        $stream=[IO.File]::OpenRead($archive);$reader=[System.Formats.Tar.TarReader]::new($stream,$false)
        try{
            for($item=0;$item -lt 12 -and $clock.Elapsed.TotalSeconds -lt 1800;$item++){
                Guard;$entry=$reader.GetNextEntry($false);if($null -eq $entry){break}
                if($entry.EntryType -eq [System.Formats.Tar.TarEntryType]::Directory -and $entry.Name -ceq './'){continue}
                $name=if($entry.Name.StartsWith('./',[StringComparison]::Ordinal)){$entry.Name.Substring(2)}else{$entry.Name}
                if($name -cnotin @('expanded-training-report.json','expanded-head.asset','expanded-training.checkpoint','model.json','files.sha256') -or $entry.EntryType -notin @([System.Formats.Tar.TarEntryType]::RegularFile,[System.Formats.Tar.TarEntryType]::V7RegularFile) -or $entry.Length -gt 8MB){throw 'Unexpected trained asset tar member'}
                $target=Join-Path $model $name
                if(-not(Test-Path -LiteralPath $target)){$entry.ExtractToFile($target,$false)}
            }
            if($item -ge 12){throw 'Model archive item bound'}
        }finally{$reader.Dispose();$stream.Dispose()}
        $shaLines=[IO.File]::ReadAllLines((Join-Path $model 'files.sha256'));if($shaLines.Length -ne 4){throw 'Four saved model metadata/assets required'}
        foreach($line in $shaLines){Guard;if($line -cnotmatch '^([a-f0-9]{64})  ([A-Za-z0-9_.-]+)$'){throw 'Model SHA syntax'};if((Get-FileHash -LiteralPath (Join-Path $model $Matches[2])).Hash.ToLowerInvariant() -cne $Matches[1]){throw 'Downloaded model file SHA mismatch'}}
        $source=Join-Path $task 'prepared\package'
        if(-not(Test-Path -LiteralPath (Join-Path $model 'encoder.safetensors'))){[IO.File]::Copy((Join-Path $source 'encoder.safetensors'),(Join-Path $model 'encoder.safetensors'),$false)}
        [void][IO.Directory]::CreateDirectory((Join-Path $model 'tokenizer'))
        if(-not(Test-Path -LiteralPath (Join-Path $model 'tokenizer\tokenizer.json'))){[IO.File]::Copy((Join-Path $source 'tokenizer\tokenizer.json'),(Join-Path $model 'tokenizer\tokenizer.json'),$false)}
    }
    Guard
    $report=Get-Content -Raw -LiteralPath (Join-Path $model 'expanded-training-report.json')|ConvertFrom-Json
    if($report.completed_steps -ne 24576 -or $report.max_tokens -ne 8192 -or -not $report.encoder_frozen -or $report.train_count -ne 162 -or $report.development_count -ne 53){throw 'Saved training report contract differs'}
    foreach($pair in @(@('encoder.safetensors','6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528'),@('tokenizer\tokenizer.json','197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356'),@('model.json','b8fb9cecc0912f034e0fea184e8aa78871d11533093dccf4f5aad74646007551'),@('expanded-head.asset',$report.head_sha256.ToLowerInvariant()),@('expanded-training.checkpoint',$report.checkpoint_sha256.ToLowerInvariant()))){Guard;if((Get-FileHash -LiteralPath (Join-Path $model $pair[0])).Hash.ToLowerInvariant() -cne $pair[1]){throw 'Complete local trained model SHA differs'}}
    $inventory=@(Get-ChildItem -LiteralPath $model -File|Select-Object -First 10|ForEach-Object{[ordered]@{name=$_.Name;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
    $summary=[ordered]@{utc=[DateTime]::UtcNow.ToString('o');status='trained_model_downloaded_and_complete_local_assets_verified';model_directory=$model;remote=$RemoteRoot;download_archive=$archive;archive_sha256=$hash;files=$inventory;tokenizer_sha256=$report.identity.tokenizer_sha256;encoder_frozen=$true;completed_steps=24576;formal_acceptance=$false}
    [IO.File]::WriteAllText((Join-Path $task 'model-download.json'),($summary|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
    $summary|ConvertTo-Json -Depth 4
}finally{if($localLock){$localLock.Dispose()};$environment.Clear()}
