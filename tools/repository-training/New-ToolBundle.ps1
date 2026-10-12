param([Parameter(Mandatory)][string]$TaskDirectory,[ValidatePattern('^tool-bundle(?:-[a-z0-9-]{1,20})?$')][string]$BundleName='tool-bundle')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required.'}
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$task=(Resolve-Path -LiteralPath $TaskDirectory).Path
if(-not $task.StartsWith((Join-Path $root '.artifacts\repository-training-'),[StringComparison]::OrdinalIgnoreCase)){throw 'Task-owned artifact directory required.'}
$bundle=Join-Path $task $BundleName
if(Test-Path -LiteralPath $bundle){throw 'Frozen bundle exists; preserve it.'}
[void][IO.Directory]::CreateDirectory($bundle)
$watch=[Diagnostics.Stopwatch]::StartNew();$calls=0
function Guard{if($watch.Elapsed.TotalSeconds -ge 1200 -or (Test-Path -LiteralPath (Join-Path $task 'CANCEL'))){throw 'Bundle cancelled/deadline1200s.'}}
foreach($project in @('Sezika.RepositoryTrainer','Sezika.FullTrainingChecks')){
    Guard;$calls++;if($calls -gt 2){throw 'Publish count bound2.'}
    $arguments=@('publish',('tools/'+$project+'/'+$project+'.csproj'),'-c','Release','-r','linux-x64','--self-contained','true','-p:UseAppHost=true','--disable-build-servers','-p:UseSharedCompilation=false','-o',(Join-Path $bundle $project))
    & (Join-Path $root 'tools/rental-training/Invoke-BoundedProcess.ps1') -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList $arguments -TimeoutSeconds ([Math]::Min(540,[int](1200-$watch.Elapsed.TotalSeconds))) -LogName ('bundle-'+$project) -LogDirectory (Join-Path $task 'process-logs') -WorkingDirectory $root -CancelFile (Join-Path $task 'CANCEL') | Out-Host
}
# Keep the historical guardian bytes intact. Derive this task's larger bounded budget
# from one reviewed literal; the rest, including ownership and cleanup, is identical.
$guardian=[IO.File]::ReadAllText((Join-Path $root 'tools/rental-training/remote/Invoke-BoundedLinux-v2.sh'))
$needle='budget <= 1520'
if(([regex]::Matches($guardian,[regex]::Escape($needle))).Count -ne 1){throw 'Guardian source changed; review before deriving.'}
$guardian=$guardian.Replace($needle,'budget <= 86400')
[IO.File]::WriteAllText((Join-Path $bundle 'guardian.sh'),$guardian.Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
foreach($file in @('clone-inputs.sh','pull-full-corpus.sh','pack-result.sh')){Guard;Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('remote/'+$file)) -Destination (Join-Path $bundle $file)}
foreach($name in @('LICENSE','NOTICE')){Guard;Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $bundle ('code-'+$name+'.txt'))}
$sourceManifest=Join-Path $bundle 'engineering-sources.json'
$sourcePaths=@('Directory.Build.props','src/Sezika/TokenizerJson.cs','src/Sezika/IndependentDecisionV1.cs','src/Sezika.Cuda/CudaKernels.cs','src/Sezika.Cuda/CudaFullEncoderTrainer.cs','src/Sezika.Cuda/CudaEncoderBackwardKernels.cs','src/Sezika.Kernels/EncoderBackwardKernels.cs')
foreach($folder in @('tools/Sezika.RepositoryTrainer','tools/Sezika.FullTrainingChecks','tools/Sezika.BackwardKernelCompiler','src/Sezika.Cuda/Generated/EncoderBackward')){
    Guard
    $selected=@(Get-ChildItem -LiteralPath (Join-Path $root $folder) -File | Sort-Object Name | Select-Object -First 41)
    if($selected.Count -gt 40){throw 'Engineering source folder exceeded40; refuse silently incomplete bundle identity.'}
    foreach($file in $selected){$sourcePaths+=($folder+'/'+$file.Name)}
}
$sources=@();foreach($relative in $sourcePaths){Guard;$path=Join-Path $root $relative;$sources+=@{path=$relative;bytes=(Get-Item -LiteralPath $path).Length;sha256=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()}}
$head=(& git -C $root rev-parse HEAD).Trim();if($LASTEXITCODE -ne 0){throw 'Source ref failed.'}
[IO.File]::WriteAllText($sourceManifest,(@{source_commit=$head;dirty_owned_sources_separately_hashed=$true;files=$sources}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
# Only this newly created small bundle is enumerated: depth2, <=600 entries and 1200s.
$files=@(Get-ChildItem -LiteralPath $bundle -File)+@(Get-ChildItem -LiteralPath $bundle -Directory | Select-Object -First 4 | ForEach-Object {Get-ChildItem -LiteralPath $_.FullName -File | Select-Object -First 300})
if($files.Count -gt 600){throw 'Bundle file bound600.'}
$lines=[Collections.Generic.List[string]]::new()
foreach($file in ($files|Sort-Object FullName)){Guard;$relative=[IO.Path]::GetRelativePath($bundle,$file.FullName).Replace('\','/');$lines.Add((Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()+'  '+$relative)}
[IO.File]::WriteAllLines((Join-Path $bundle 'files.sha256'),$lines,[Text.UTF8Encoding]::new($false))
Guard
[pscustomobject]@{bundle=$bundle;files=$files.Count;manifest_sha256=(Get-FileHash -LiteralPath (Join-Path $bundle 'files.sha256')).Hash.ToLowerInvariant();source_commit=$head;real_linux_execution='NOT_RUN'}
