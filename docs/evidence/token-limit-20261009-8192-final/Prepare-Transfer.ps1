param(
  [ValidateSet('Prepare','Upload','Download')][string]$Mode='Prepare',
  [string]$BundleDirectory,
  [string]$CapacityDirectory,
  [string]$DataDirectory,
  [string]$EvidenceSha256
)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required'}
$task='D:\source\Sezika\.artifacts\token-limit-20261009-8192'
if([IO.Path]::GetFullPath($PSScriptRoot) -cne $task){throw 'Transfer task root mismatch'}
$remote='/root/sezika-experiments/20261009-repaired-training-8192'
$runner=Join-Path $task 'Invoke-BoundedProcess.ps1'
$clock=[Diagnostics.Stopwatch]::StartNew()
function Assert-Budget([int]$Seconds){
  if($clock.Elapsed.TotalSeconds -ge $Seconds -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'Transfer cancelled/deadline expired'}
}
function Require-File([string]$Path,[long]$MaximumBytes){
  $item=Get-Item -LiteralPath $Path
  if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $item.Length -lt 1 -or $item.Length -gt $MaximumBytes){throw "Unacceptable regular transfer file: $Path"}
  return $item
}
function Freeze-Directory([string]$Directory,[int]$MaximumFiles,[long]$MaximumBytes){
  Assert-Budget 120
  $resolved=[IO.Path]::GetFullPath($Directory)
  $item=Get-Item -LiteralPath $resolved
  if(-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Nonlink flat directory required'}
  $entries=@(Get-ChildItem -LiteralPath $resolved -Force | Select-Object -First ($MaximumFiles+1))
  if($entries.Count -lt 1 -or $entries.Count -gt $MaximumFiles){throw 'Transfer file cap'}
  $records=[Collections.Generic.List[object]]::new(); $total=0L; $index=0
  foreach($entry in $entries){
    if($entry.PSIsContainer -and $entry.Name -ceq 'repair-evidence'){continue}
    Assert-Budget 120
    if($entry.Name -cnotmatch '^[A-Za-z0-9_.-]+$' -or $entry.Name -ceq 'files.sha256'){throw 'Unsafe or reserved transfer name'}
    $file=Require-File $entry.FullName $MaximumBytes
    $total+=$file.Length
    if($total -gt $MaximumBytes){throw 'Transfer bytes cap'}
    $records.Add([ordered]@{name=$file.Name;path=$file.FullName;bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()})
    $index++
    if($index % 32 -eq 0){Write-Output "transfer_freeze_progress=$index maximum=$MaximumFiles" | Out-Host}
  }
  return ,$records.ToArray()
}
$options=@('-P','2222','-o','BatchMode=yes','-o','ConnectTimeout=8','-o','StrictHostKeyChecking=yes','-o','ServerAliveInterval=5','-o','ServerAliveCountMax=2')
$logs=Join-Path $task 'process-logs'
if($Mode -eq 'Prepare'){
  if(-not $BundleDirectory -or -not $CapacityDirectory -or -not $DataDirectory){throw 'Prepare requires three exact published/data directory inputs'}
  $staging=Join-Path $task 'upload-staging'
  $archive=Join-Path $task 'upload.tar'
  foreach($path in @($staging,$archive,(Join-Path $task 'upload.sha256'),(Join-Path $task 'transfer-preparation.json'))){if(Test-Path -LiteralPath $path){throw "Fresh transfer required; existing evidence preserved: $path"}}
  $bundle=Freeze-Directory $BundleDirectory 400 120MB
  $capacity=Freeze-Directory $CapacityDirectory 200 100MB
  $boundary=Freeze-Directory (Join-Path $task 'boundary-linux') 200 100MB
  $profile=Freeze-Directory (Join-Path $task 'profile') 1 16384
  $regression=Freeze-Directory (Join-Path $task 'regression-linux') 200 100MB
  $regressionInput=Freeze-Directory (Join-Path $task 'regression_input') 2 2MB
  $expectedData=@('experiment-manifest.json','experiment-records.jsonl','input-provenance.json','source-catalog.json','teacher-requests.json','teacher-responses.json','derivation-ledger.json','parent-records.jsonl')
  $dataInventory=Freeze-Directory $DataDirectory 11 100MB
  $repairInventory=Freeze-Directory (Join-Path $DataDirectory 'repair-evidence') 28 10MB
  $allowedSidecars=@('package-writer.identity.json','package-summary.json')
  $sidecars=@($dataInventory | Where-Object {$_.name -cnotin $expectedData})
  if($sidecars.Count -gt 2 -or @($sidecars | Where-Object {$_.name -cnotin $allowedSidecars}).Count -ne 0){throw 'Only the two named package diagnostics may accompany eight-file data'}
  $data=@($dataInventory | Where-Object {$_.name -cin $expectedData})
  if($data.Count -ne 8 -or @(Compare-Object ($data.name | Sort-Object) ($expectedData | Sort-Object)).Count -ne 0){throw 'Exact eight-file v3 input package required'}
  if($bundle.name -cnotcontains 'Sezika.ExperimentTrainer' -or $capacity.name -cnotcontains 'CapacityCheck'){throw 'Expected linux-x64 apphosts absent'}
  $expectedScripts=@('Invoke-BoundedLinux.sh','bootstrap.sh','runner-probe.sh','phase-common.sh','capacity.sh','train.sh','validate.sh','regress.sh','final-evidence.sh','boundary-tiny.sh','boundary-full.sh','boundary-real.sh')
  $shell=[Collections.Generic.List[object]]::new()
  foreach($name in $expectedScripts){
    Assert-Budget 120
    $file=Require-File (Join-Path $task "remote/$name") 128KB
    $shell.Add([ordered]@{name=$name;path=$file.FullName;bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()})
  }
  if(($shell | Where-Object name -ceq 'Invoke-BoundedLinux.sh').sha256 -cne '32167e8cf7396ac6d1f8f54a8d664dfbfaaef71fccba871433b0fdf73e24f2f1'){throw 'Reviewed expanded runner hash changed'}
  $groups=[ordered]@{bundle=$bundle;capacity=$capacity;boundary=$boundary;profile=$profile;regression=$regression;regression_input=$regressionInput;data=$data;'data/repair-evidence'=$repairInventory;remote=$shell.ToArray()}
  [void][IO.Directory]::CreateDirectory($staging)
  try{
    foreach($kind in $groups.Keys){
      Assert-Budget 150
      $destination=Join-Path $staging $kind
      [void][IO.Directory]::CreateDirectory($destination)
      $copied=0
      foreach($file in $groups[$kind]){
        Assert-Budget 150
        if((Get-FileHash -LiteralPath $file.path).Hash.ToLowerInvariant() -cne $file.sha256){throw 'Source changed during transfer preparation'}
        [IO.File]::Copy($file.path,(Join-Path $destination $file.name),$false)
        $copied++
        if($copied % 32 -eq 0){Write-Output "transfer_copy_progress=$kind/$copied"}
      }
      [IO.File]::WriteAllLines((Join-Path $destination 'files.sha256'),@($groups[$kind] | ForEach-Object {$_.sha256+'  '+$_.name}),[Text.UTF8Encoding]::new($false))
    }
    & $runner -FilePath 'C:\Windows\System32\tar.exe' -ArgumentList @('-czf',$archive,'-C',$staging,'.') -TimeoutSeconds 45 -ProcessQueryTimeoutSeconds 5 -LogName 'expansion-pack' -LogDirectory $logs
    Assert-Budget 210
    $archiveItem=Require-File $archive 256MB
    $hash=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $task 'upload.sha256'),$hash+'  upload.tar'+"`n",[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $task 'transfer-preparation.json'),([ordered]@{task='sezika-token-limit-20261009-8192';thread='01a11f25-335a-7571-a735-7171ace8c3b9';utc=[DateTime]::UtcNow.ToString('o');destination=$remote;archive_sha256=$hash;archive_bytes=$archiveItem.Length;runner_original_sha256='534a54a2136e6e27a49e69c3f7b792ec90ec2fd8f89fdecf9a96b9065507fc79';runner_expanded_sha256='32167e8cf7396ac6d1f8f54a8d664dfbfaaef71fccba871433b0fdf73e24f2f1';groups=$groups}|ConvertTo-Json -Depth 7),[Text.UTF8Encoding]::new($false))
    Write-Output "prepared_archive_sha256=$hash bytes=$($archiveItem.Length)"
  }finally{
    # Staging is a frozen delivery artifact, not disposable compiler temp. Retain on failure.
    if([IO.Path]::GetFullPath($staging) -cne (Join-Path $task 'upload-staging')){throw 'Staging ownership mismatch'}
    Write-Output 'owned_upload_staging_preserved_for_review=true'
  }
}elseif($Mode -eq 'Upload'){
  $freeze=Get-Content -Raw -LiteralPath (Join-Path $task 'transfer-preparation.json') | ConvertFrom-Json
  if($freeze.task -cne 'sezika-token-limit-20261009-8192' -or $freeze.destination -cne $remote){throw 'Frozen transfer identity mismatch'}
  $archive=Require-File (Join-Path $task 'upload.tar') 256MB
  if((Get-FileHash -LiteralPath $archive.FullName).Hash.ToLowerInvariant() -cne $freeze.archive_sha256 -or $archive.Length -ne $freeze.archive_bytes){throw 'Frozen upload archive changed'}
  [void](Require-File (Join-Path $task 'upload.sha256') 128)
  Assert-Budget 20
  & $runner -FilePath 'C:\Windows\System32\OpenSSH\scp.exe' -ArgumentList ($options+@($archive.FullName,(Join-Path $task 'upload.sha256'),('root@211.149.170.42:'+$remote+'/'))) -TimeoutSeconds 180 -ProcessQueryTimeoutSeconds 5 -LogName 'expansion-upload' -LogDirectory $logs
}else{
  if($EvidenceSha256 -cnotmatch '^[A-Fa-f0-9]{64}$'){throw 'Download requires independently observed remote evidence SHA256'}
  $destination=Join-Path $task 'evidence-bundle.tar'
  if(Test-Path -LiteralPath $destination){throw 'Downloaded evidence already exists; inspect without repeating'}
  Assert-Budget 10
  & $runner -FilePath 'C:\Windows\System32\OpenSSH\scp.exe' -ArgumentList ($options+@(('root@211.149.170.42:'+$remote+'/evidence-bundle.tar'),$destination)) -TimeoutSeconds 120 -ProcessQueryTimeoutSeconds 5 -LogName 'expansion-evidence-download' -LogDirectory $logs
  $item=Require-File $destination 64MB
  if((Get-FileHash -LiteralPath $destination).Hash -ine $EvidenceSha256){throw 'Downloaded evidence identity mismatch; preserve for diagnosis'}
  [IO.File]::WriteAllText((Join-Path $task 'evidence-download.json'),([ordered]@{utc=[DateTime]::UtcNow.ToString('o');remote=$remote;path=$destination;sha256=$EvidenceSha256.ToLowerInvariant();bytes=$item.Length;extracted=$false}|ConvertTo-Json))
  Write-Output 'evidence_download_hash_verified_no_extract=true'
}
