param(
    [Parameter(Mandatory)][string]$RepositoryDirectory,
    [Parameter(Mandatory)][ValidateSet('model','dataset')][string]$Kind,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{2,63}$')][string]$RunId,
    [Parameter(Mandatory)][string]$TaskDirectory,
    [Parameter(Mandatory)][string]$ValidationReceipt,
    [string]$ModelPackageDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$PublishedDatasetCommit
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required.'}
$repository=(Resolve-Path -LiteralPath $RepositoryDirectory).Path;$task=(Resolve-Path -LiteralPath $TaskDirectory).Path
$prefix=if($Kind -eq 'model'){'runs/'+$RunId+'/'}else{'derived/'+$RunId+'/'}
$owned=Join-Path $repository $prefix;$watch=[Diagnostics.Stopwatch]::StartNew()
function Guard{if($watch.Elapsed.TotalSeconds -ge 180 -or (Test-Path -LiteralPath (Join-Path $task 'CANCEL'))){throw 'Publication manifest cancelled/deadline180s.'}}
$validation=Get-Content -LiteralPath $ValidationReceipt -Raw|ConvertFrom-Json
if($validation.RunId -cne $RunId -or $validation.Status -cne 'PASS_new_package_GPU_experimental_validation_only' -or -not $validation.SavedBytesVerified -or $validation.QualityAccepted){throw 'Experimental independent validation receipt required; never infer formal quality.'}
if($Kind -eq 'model'){
    $packagePath=Join-Path $owned 'package.json';$package=Get-Content -LiteralPath $packagePath -Raw|ConvertFrom-Json
    if((Get-FileHash -LiteralPath $packagePath).Hash.ToLowerInvariant() -cne $validation.PackageSha256){throw 'Validation package identity differs.'}
    $report=Get-Content -LiteralPath (Join-Path $owned 'training-report.json') -Raw|ConvertFrom-Json
    if($validation.ProcessId -eq $report.ProcessId -and $validation.ProcessStartUtc -eq $report.ProcessStartUtc){throw 'Validation shares training process identity.'}
    $binding=@{schema_version=1;run_id=$RunId;published_dataset_commit=$PublishedDatasetCommit;original_dataset_commit=$package.DatasetCommit;derived_manifest_sha256=$package.Inputs.DataManifestSha256;validation_sha256=(Get-FileHash -LiteralPath $ValidationReceipt).Hash.ToLowerInvariant();experimental_only=$true;formal_quality=$false;calibration='uncalibrated';aot_and_performance='NOT_ACCEPTED'}
    $bindingPath=Join-Path $owned 'publication-binding.json';if(Test-Path -LiteralPath $bindingPath){throw 'Publication binding exists; inspect it instead of overwriting.'}
    [IO.File]::WriteAllText($bindingPath,($binding|ConvertTo-Json -Depth 5 -Compress),[Text.UTF8Encoding]::new($false))
}
else{
    if(-not $ModelPackageDirectory){throw 'Dataset manifest requires the independently validated model package.'}
    $packagePath=Join-Path $ModelPackageDirectory 'package.json';$package=Get-Content -LiteralPath $packagePath -Raw|ConvertFrom-Json
    if((Get-FileHash -LiteralPath $packagePath).Hash.ToLowerInvariant() -cne $validation.PackageSha256 -or (Get-FileHash -LiteralPath (Join-Path $owned 'data-manifest.json')).Hash.ToLowerInvariant() -cne $package.Inputs.DataManifestSha256){throw 'Derived dataset is not the input bound to the validated model.'}
}
$directories=@(Get-ChildItem -LiteralPath $owned -Directory|Select-Object -First 6);if($directories.Count -gt 5){throw 'Owned directory bound5.'}
$files=@(Get-ChildItem -LiteralPath $owned -File|Select-Object -First 1001)
foreach($directory in $directories){Guard;if($directory.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Owned links rejected.'};if(@(Get-ChildItem -LiteralPath $directory.FullName -Directory|Select-Object -First 1).Count){throw 'Package depth exceeds2.'};$files+=@(Get-ChildItem -LiteralPath $directory.FullName -File|Select-Object -First 1001)}
if($files.Count -lt 1 -or $files.Count -gt 1000){throw 'Publication file bound1..1000.'}
$entries=@();$large=@()
foreach($file in ($files|Sort-Object FullName)){Guard;if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'File link rejected.'};$relative=[IO.Path]::GetRelativePath($repository,$file.FullName).Replace('\','/');$entries+=@{path=$relative;bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()};if($file.Length -ge 10MB){$large+=@($relative)}}
$attributePath=Join-Path $repository '.gitattributes';$old=[IO.File]::ReadAllBytes($attributePath);$append="`n";$verbatim=@($entries|Where-Object {$_.bytes -lt 10MB}|ForEach-Object {$_.path})
foreach($path in $large){Guard;$append+=$path+' filter=lfs diff=lfs merge=lfs -text'+"`n"}
foreach($path in $verbatim){Guard;$append+=$path+' -text'+"`n"}
$bytes=[byte[]]($old+[Text.UTF8Encoding]::new($false).GetBytes($append));$change=@{before_sha256=(Get-FileHash -LiteralPath $attributePath).Hash.ToLowerInvariant();after_sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant();paths=$large;verbatim_paths=$verbatim}
$manifestPath=Join-Path $task ('owned-'+$Kind+'.json');if(Test-Path -LiteralPath $manifestPath){throw 'Owned manifest exists.'}
[IO.File]::WriteAllText($manifestPath,(@{schema_version=1;run_id=$RunId;kind=$Kind;files=$entries;attribute_change=$change}|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
$gatePath=Join-Path $task ('independent-validation-'+$Kind+'.json');$gate=@{run_id=$RunId;status='PASS';new_process=$true;saved_bytes_verified=$true;owned_manifest_sha256=(Get-FileHash -LiteralPath $manifestPath).Hash.ToLowerInvariant();original_validation_sha256=(Get-FileHash -LiteralPath $ValidationReceipt).Hash.ToLowerInvariant()}
if(Test-Path -LiteralPath $gatePath){throw 'Publication validation gate exists.'}
[IO.File]::WriteAllText($gatePath,($gate|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
[pscustomobject]@{owned_manifest=$manifestPath;validation_gate=$gatePath;files=$files.Count;large_lfs_additions=$large.Count;status='prepared_only_NOT_PUSHED'}
