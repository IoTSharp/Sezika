param([Parameter(Mandatory)][string]$Destination, [ValidateRange(1,1500)][int]$MaxFiles=1500)
$ErrorActionPreference='Stop'
$watch=[Diagnostics.Stopwatch]::StartNew()
$source=[IO.Path]::GetFullPath($PSScriptRoot)
$destination=[IO.Path]::GetFullPath($Destination)
if([IO.Path]::Exists($destination)){throw 'Destination must be new.'}
[IO.Directory]::CreateDirectory($destination) | Out-Null
$files=[Collections.Generic.List[object]]::new()
function Add-File([string]$Path,[bool]$Metrics=$false){
    if($files.Count -ge 1500 -or $watch.Elapsed.TotalSeconds -ge 60){throw 'Archive enumeration bound exceeded.'}
    if(Test-Path -LiteralPath $Path -PathType Leaf){$files.Add([pscustomobject]@{Path=[IO.Path]::GetFullPath($Path);Metrics=$Metrics})}
}
# Only derived reports, indexes, and runner logs. Never copy external raw captures or model assets.
foreach($name in @('trace-smoke.json','trace-core18.json','trace-remaining5.json','trace-complete-index.json','trace-complete-summary.json','trace-partial-index.json','trace-partial-summary.json','trace-tokenizer-regression.json','aggregate-negative-checks.json','aggregate-baseline-index.json','near-tie-smoke.json','near-tie-full.json','near-tie-negative-check.json','profile-simd-short1.json','profile-cuda-short1.json','profile-cuda-deadline.json','profile-simd-long1.json','profile-simd-long32.json','profile-negative-check.json','implementation.json','validation-summary.json')) {Add-File (Join-Path $source $name)}
foreach($file in @(Get-ChildItem -LiteralPath $source -Filter '*-quality.json' -File | Select-Object -First 30)){Add-File $file.FullName $true}
Add-File (Join-Path $source 'aggregate-baseline.json') $true
$processDirs=[Collections.Generic.List[string]]::new()
foreach($name in @('nimble-complete-references.json','profile-long32-preflight.json','profile-cuda-long32.json','Archive-Evidence.ps1','Verify-Results.ps1','Invoke-ProfileRun.ps1')){Add-File (Join-Path $source $name)}
$processDirs.Add((Join-Path $source 'processes'))
foreach($name in @('aggregate-duplicate-logs','aggregate-wrong-hash-logs')){$processDirs.Add((Join-Path $source $name))}
foreach($name in @('paws-full','nimble-full','nimble-remaining','language-smoke','language-full','paws-final','nimble-final','language-final','language-final-smoke')){
    $dir=Join-Path $source $name
    if(-not (Test-Path -LiteralPath $dir -PathType Container)){continue}
    foreach($report in @('run.json','references.json','actuals.json')){Add-File (Join-Path $dir $report)}
    for($i=1;$i -le 8;$i++){Add-File (Join-Path $dir ('batch-{0:D3}/comparison.json' -f $i))}
    $processDirs.Add((Join-Path $dir 'processes'))
}
foreach($dir in $processDirs){
    if(-not (Test-Path -LiteralPath $dir -PathType Container)){continue}
    $items=@(Get-ChildItem -LiteralPath $dir -File | Select-Object -First 801)
    if($items.Count -gt 800){throw 'Too many runner files.'}
    foreach($file in $items){if($file.Name -match '\.(result|identity)\.json$|\.(stdout|stderr)\.log$'){Add-File $file.FullName}}
}
$index=[Collections.Generic.List[object]]::new()
$totalBytes=0L
for($i=0;$i -lt [Math]::Min($MaxFiles,$files.Count);$i++){
    if($watch.Elapsed.TotalSeconds -ge 60){throw 'Archive copy deadline exceeded.'}
    $item=$files[$i]
    $relative=[IO.Path]::GetRelativePath($source,$item.Path)
    if($relative.StartsWith('..')){throw 'Source escaped task directory.'}
    $target=Join-Path $destination $relative
    $size=(Get-Item -LiteralPath $item.Path).Length
    $totalBytes+=$size
    if($size -gt 8388608 -or $totalBytes -gt 104857600){throw 'Archive byte bound exceeded.'}
    $originalHash=(Get-FileHash -LiteralPath $item.Path -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
    if($item.Metrics){
        $report=Get-Content -LiteralPath $item.Path -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
        foreach($property in @('Rows','ByTarget','ByDomain','BySourceFamily')){$report.Remove($property)}
        $report['ArchiveScope']='Derived aggregate metrics only; raw rows and external label/domain text remain in the local ignored source report.'
        [IO.File]::WriteAllText($target,($report | ConvertTo-Json -Depth 32),[Text.UTF8Encoding]::new($false))
    }else{[IO.File]::Copy($item.Path,$target,$false)}
    $archivedHash=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    if(-not $item.Metrics -and $archivedHash -cne $originalHash){throw 'Byte-preserving copy mismatch.'}
    $index.Add([ordered]@{artifact=$relative.Replace('\','/');source=$item.Path;source_sha256=$originalHash;artifact_sha256=$archivedHash;transformation=if($item.Metrics){'aggregate_without_external_rows_and_labels'}else{'byte_preserving_copy'}})
    if($i -eq 0 -or ($i+1)%50 -eq 0){Write-Output "Archived $($i+1)/$([Math]::Min($MaxFiles,$files.Count))"}
}
$result=[ordered]@{schema_version='sezika.continuation-evidence-index.v1';files=$index.Count;available=$files.Count;scope='Bounded whitelist archive. No external raw captures, question manifests, model weights or tokenizer asset copied.';artifacts=$index.ToArray()}
[IO.File]::WriteAllText((Join-Path $destination 'index.json'),($result | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
