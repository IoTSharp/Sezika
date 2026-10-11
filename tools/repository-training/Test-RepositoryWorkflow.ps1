param([ValidatePattern('^fixture-[a-z0-9-]{3,48}$')][string]$RunId='fixture-repository-20261011')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required.'}
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'));$watch=[Diagnostics.Stopwatch]::StartNew();$calls=0;$checks=0
$result=& (Join-Path $PSScriptRoot 'New-TrainingTask.ps1') -RunId $RunId;$task=$result.task_directory
$bare=Join-Path $task 'fixture-bare.git';$repository=Join-Path $task 'fixture-checkout';[void][IO.Directory]::CreateDirectory($bare);[void][IO.Directory]::CreateDirectory($repository)
function Guard{if($watch.Elapsed.TotalSeconds -ge 600 -or (Test-Path -LiteralPath (Join-Path $task 'CANCEL'))){throw 'Fixture deadline600s/cancellation.'}}
function LocalGit([string[]]$Arguments){
    Guard;$script:calls++;if($script:calls -gt 30){throw 'Local fixture Git count30.'};$gate=Join-Path $task ('setup-gate-'+$calls);[void][IO.Directory]::CreateDirectory($gate)
    $path=Join-Path $gate 'command.json';[IO.File]::WriteAllText($path,(@{git='C:\Program Files\Git\cmd\git.exe';directory=$repository;arguments=$Arguments;gate=$gate}|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
    $tag='fixture-setup-'+$calls
    & (Join-Path $root 'tools/rental-training/Invoke-BoundedProcess.ps1') -FilePath 'C:\Program Files\PowerShell\7\pwsh.exe' -ArgumentList @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Invoke-GitPayload.ps1'),'-CommandFile',$path) -StartGateDirectory $gate -TimeoutSeconds 30 -LogName $tag -LogDirectory (Join-Path $task 'setup-logs') | Out-Host
    $files=@(Get-ChildItem -LiteralPath (Join-Path $task 'setup-logs') -File -Filter ('*'+$tag+'.result.json')|Select-Object -First 2);if($files.Count -ne 1){throw 'Fixture Git receipt missing.'};$receipt=Get-Content -LiteralPath $files[0].FullName -Raw|ConvertFrom-Json;return [IO.File]::ReadAllText($receipt.StdoutPath).Trim()
}
[void](LocalGit @('init','--bare',$bare));[void](LocalGit @('init','--initial-branch=master',$repository));[void](LocalGit @('config','user.name','Sezika local fixture'));[void](LocalGit @('config','user.email','fixture@invalid.local'))
[IO.File]::WriteAllText((Join-Path $repository '.gitattributes'),"*.safetensors filter=lfs diff=lfs merge=lfs -text`n",[Text.UTF8Encoding]::new($false))
[void](LocalGit @('add','--','.gitattributes'));[void](LocalGit @('commit','-m','fixture base'))
$url='https://moark.com/maikebing/Sezika-mmBERT-8192-experimental';$fileUrl='file:///'+$bare.Replace('\','/')
[void](LocalGit @('remote','add','origin',$url));[void](LocalGit @('config',('url.'+$fileUrl+'.insteadOf'),$url));[void](LocalGit @('push','origin','master'))
$expected=LocalGit @('rev-parse','HEAD');$checks++
$owned=Join-Path $repository ('runs/'+$RunId);[void][IO.Directory]::CreateDirectory($owned)
$package=@{DatasetCommit=('a'*40);Inputs=@{DataManifestSha256=('b'*64)}}
[IO.File]::WriteAllText((Join-Path $owned 'package.json'),($package|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $owned 'training-report.json'),(@{ProcessId=123;ProcessStartUtc='2020-01-01T00:00:00Z';fixture_only=$true}|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
$validationPath=Join-Path $task 'fixture-validation.json'
$validation=@{RunId=$RunId;Status='PASS_new_package_GPU_experimental_validation_only';SavedBytesVerified=$true;QualityAccepted=$false;PackageSha256=(Get-FileHash -LiteralPath (Join-Path $owned 'package.json')).Hash.ToLowerInvariant();ProcessId=456;ProcessStartUtc='2020-01-02T00:00:00Z';fixture_only=$true}
[IO.File]::WriteAllText($validationPath,($validation|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$manifestResult=& (Join-Path $PSScriptRoot 'New-PublicationManifest.ps1') -RepositoryDirectory $repository -Kind model -RunId $RunId -TaskDirectory $task -ValidationReceipt $validationPath -PublishedDatasetCommit ('a'*40)
$manifest=$manifestResult.owned_manifest
$preview=& (Join-Path $PSScriptRoot 'Publish-RepositoryRun.ps1') -RepositoryDirectory $repository -Kind model -RunId $RunId -OwnedManifest $manifest -ExpectedRemoteCommit $expected -TaskDirectory $task -PreviewOnly -LocalFixture
if($preview.status -cne 'PASS_preview_paths_hashes_destination_and_ref_only'){throw 'Local fixture preview failed.'};$checks++
$bad=Get-Content -LiteralPath $manifest -Raw|ConvertFrom-Json;$bad.files[0].sha256='0'*64;$badPath=Join-Path $task 'fixture-bad-manifest.json';[IO.File]::WriteAllText($badPath,($bad|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
$rejected=$false;try{& (Join-Path $PSScriptRoot 'Publish-RepositoryRun.ps1') -RepositoryDirectory $repository -Kind model -RunId $RunId -OwnedManifest $badPath -ExpectedRemoteCommit $expected -TaskDirectory $task -PreviewOnly -LocalFixture|Out-Host}catch{if($_.Exception.Message -notlike '*SHA mismatch*'){throw};$rejected=$true}
if(-not $rejected){throw 'Tampered fixture was accepted.'};$checks++
$published=& (Join-Path $PSScriptRoot 'Publish-RepositoryRun.ps1') -RepositoryDirectory $repository -Kind model -RunId $RunId -OwnedManifest $manifest -ExpectedRemoteCommit $expected -TaskDirectory $task -LocalFixture
if($published.status -cne 'PASS_git_ref_pushed_full_public_readback_still_required'){throw 'Local fixture publish failed.'};$checks++
$actual=LocalGit @('--git-dir',$bare,'rev-parse','master');if($actual -cne $published.publication_commit){throw 'Local bare ref readback differs.'};$checks++
$fixtureReceipt=@{status='PASS_local_fixture_only';checks=$checks;remote_transport='file_only';real_gpu_validation=$false;real_external_publication=$false;fixture_commit=$actual;elapsed_seconds=$watch.Elapsed.TotalSeconds}
[IO.File]::WriteAllText((Join-Path $task 'fixture-result.json'),($fixtureReceipt|ConvertTo-Json),[Text.UTF8Encoding]::new($false));$fixtureReceipt
