param([string]$TaskDirectory='D:\source\Sezika\.artifacts\rental-training-20261009-01a12099', [switch]$VerifyOnly, [switch]$Pilot)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 required' }
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$task=[IO.Path]::GetFullPath($TaskDirectory)
if (-not $task.StartsWith((Join-Path $repo '.artifacts\rental-training-'),[StringComparison]::OrdinalIgnoreCase)) { throw 'Task directory outside rental artifact scope' }
$clock=[Diagnostics.Stopwatch]::StartNew(); $count=0; $total=0L
$cancel=Join-Path $task 'cancel.request'; $staging=Join-Path $task 'prepared'
$records=[Collections.Generic.List[object]]::new()
function Guard {
    if ($clock.Elapsed.TotalSeconds -ge 900 -or [IO.File]::Exists($cancel)) { throw 'Preparation cancelled/deadline900s' }
}
function Hash-File([string]$Path) {
    Guard
    $file=Get-Item -LiteralPath $Path
    if ($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $file.Length -gt 2500MB) { throw 'Regular bounded file required' }
    $stream=[IO.File]::OpenRead($file.FullName); $hash=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    $buffer=[byte[]]::new(4MB); $hashClock=[Diagnostics.Stopwatch]::StartNew(); $read=0L
    try {
        for ($chunk=0; $chunk -lt 641 -and $hashClock.Elapsed.TotalSeconds -lt 120; $chunk++) {
            Guard; $bytes=$stream.Read($buffer,0,$buffer.Length); if ($bytes -eq 0) { break }
            $hash.AppendData($buffer,0,$bytes); $read+=$bytes
        }
        if ($read -ne $file.Length -or $chunk -ge 641 -or $hashClock.Elapsed.TotalSeconds -ge 120) { throw 'Hash byte/item/time bound' }
        return [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
    } finally { $hash.Dispose(); $stream.Dispose() }
}
function Add-File([string]$Source,[string]$Relative,[string]$Expected='') {
    Guard; $script:count++; if ($script:count -gt 1200 -or $Relative -cnotmatch '^[A-Za-z0-9_./-]+$' -or $Relative.Contains('..')) { throw 'Package count/path rejected' }
    $sourceFile=Get-Item -LiteralPath $Source
    $sha=Hash-File $sourceFile.FullName
    if ($Expected -and $sha -cne $Expected.ToLowerInvariant()) { throw "Pinned source mismatch: $Relative" }
    $script:total+=$sourceFile.Length; if ($script:total -gt 2500MB) { throw 'Package bytes2500MiB cap' }
    $target=[IO.Path]::GetFullPath((Join-Path $staging $Relative))
    if (-not $target.StartsWith($staging+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Target escaped staging' }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
    $inputStream=[IO.File]::OpenRead($sourceFile.FullName); $outputStream=[IO.FileStream]::new($target,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    $copyClock=[Diagnostics.Stopwatch]::StartNew(); $copied=0L; $buffer=[byte[]]::new(4MB)
    try {
        for ($chunk=0; $chunk -lt 641 -and $copyClock.Elapsed.TotalSeconds -lt 150; $chunk++) {
            Guard; $bytes=$inputStream.Read($buffer,0,$buffer.Length); if ($bytes -eq 0) { break }
            $outputStream.Write($buffer,0,$bytes); $copied+=$bytes
            if ($copied % 64MB -eq 0) { Write-Output "copy=$Relative bytes=$copied" }
        }
        if ($copied -ne $sourceFile.Length -or $chunk -ge 641 -or $copyClock.Elapsed.TotalSeconds -ge 150) { throw 'Copy byte/item/time bound' }
    } finally { $outputStream.Dispose(); $inputStream.Dispose() }
    if ((Hash-File $target) -cne $sha) { throw 'Copy SHA mismatch' }
    $records.Add([ordered]@{path=$Relative;bytes=$sourceFile.Length;sha256=$sha;source=$sourceFile.FullName})
    if ($script:count % 64 -eq 0) { Write-Output "package_progress=$script:count bytes=$script:total" }
}
function Add-FrozenGroup([string]$Directory,[string]$Relative) {
    $lines=[IO.File]::ReadAllLines((Join-Path $Directory 'files.sha256'))
    if ($lines.Length -gt 400) { throw 'Frozen flat group cap400' }
    foreach ($line in $lines) {
        Guard
        if ($line -cnotmatch '^([a-f0-9]{64})  ([A-Za-z0-9_.-]+)$') { throw 'Frozen group syntax' }
        Add-File (Join-Path $Directory $Matches[2]) ($Relative+'/'+$Matches[2]) $Matches[1]
    }
}
if ($VerifyOnly) {
    $manifest=Get-Content -Raw -LiteralPath (Join-Path $task 'package-preparation.json') | ConvertFrom-Json
    if ($manifest.files.Count -gt 1200) { throw 'Manifest cap' }
    foreach ($file in $manifest.files) {
        Guard
        if ($file.path -cnotmatch '^[A-Za-z0-9_./-]+$' -or $file.path.Contains('..')) { throw 'Manifest path unsafe' }
        $path=Join-Path $staging $file.path
        if ((Get-Item -LiteralPath $path).Length -ne $file.bytes -or (Hash-File $path) -cne $file.sha256) { throw 'Prepared file changed' }
    }
    if ((Hash-File (Join-Path $task 'upload.tar')) -cne $manifest.archive_sha256) { throw 'Archive changed' }
    Write-Output "verified_files=$($manifest.files.Count) archive_bytes=$($manifest.archive_bytes) archive_sha256=$($manifest.archive_sha256)"
    return
}
[void][IO.Directory]::CreateDirectory($task)
if (Test-Path -LiteralPath $staging) { throw 'Prepared directory exists; use -VerifyOnly; failed copies are retained' }
if ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($task)).AvailableFreeSpace -lt 6GB) { throw 'Need6GiB local free space' }
[void][IO.Directory]::CreateDirectory($staging)
if ($Pilot) {
    Add-File (Join-Path $repo '.artifacts\models\mmbert-independent\source\README.md') 'provenance/model-card.md' '35724415037ee3159aa733c16050620cc19a76219112a6ceeb3949572e0b37e9'
    Add-File (Join-Path $repo 'model-manifests\mmbert-independent-8192\model.json') 'package/model.json' 'b8fb9cecc0912f034e0fea184e8aa78871d11533093dccf4f5aad74646007551'
    [IO.File]::WriteAllText((Join-Path $task 'pilot.json'),($records|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
    Write-Output "pilot_verified_files=$($records.Count) bytes=$total elapsed_seconds=$($clock.Elapsed.TotalSeconds)"
    return
}
$old=Join-Path $repo '.artifacts\token-limit-20261009-8192\upload-staging'
Add-FrozenGroup (Join-Path $old 'bundle') 'bundle'
Add-FrozenGroup (Join-Path $old 'regression') 'regression'
Add-FrozenGroup (Join-Path $old 'regression_input') 'regression_input'
Add-FrozenGroup (Join-Path $old 'data') 'data'
Add-FrozenGroup (Join-Path $old 'data\repair-evidence') 'data/repair-evidence'
Add-File (Join-Path $repo 'model-manifests\mmbert-independent-8192\model.json') 'package/model.json' 'b8fb9cecc0912f034e0fea184e8aa78871d11533093dccf4f5aad74646007551'
Add-File (Join-Path $repo '.artifacts\models\mmbert-independent\converted\encoder.safetensors') 'package/encoder.safetensors' '6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528'
Add-File (Join-Path $repo '.artifacts\models\mmbert-independent\converted\tokenizer\tokenizer.json') 'package/tokenizer/tokenizer.json' '197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356'
Add-File (Join-Path $repo 'model-manifests\mmbert-independent\source_lock.json') 'provenance/model-source-lock.json'
Add-File (Join-Path $repo '.artifacts\models\mmbert-independent\source\README.md') 'provenance/model-card.md' '35724415037ee3159aa733c16050620cc19a76219112a6ceeb3949572e0b37e9'
Add-File (Join-Path $repo '.artifacts\models\mmbert-independent\source\config.json') 'provenance/source-config.json' '47b40fd2e1df8299426dd5f4bb18c28f028cfafcb51b73645f83e596d187eb37'
Add-File (Join-Path $repo 'LICENSE') 'provenance/Sezika-LICENSE'
Add-File (Join-Path $repo '.artifacts\teacher-expansion-20261008\source-research.md') 'provenance/data-source-research.md'
$sources=Get-Content -Raw -LiteralPath (Join-Path $old 'data\source-catalog.json') | ConvertFrom-Json
foreach ($property in @($sources.source_files.PSObject.Properties)) {
    Guard; $source=$property.Value
    Add-File $source.path ('provenance/data-sources/'+[IO.Path]::GetFileName($source.path)) $source.sha256
}
$kernelFiles=@(Get-ChildItem -LiteralPath (Join-Path $repo 'src\Sezika.Cuda\Generated') -File | Select-Object -First 21)
if ($kernelFiles.Count -gt 20) { throw 'Generated kernel count cap20' }
foreach ($file in $kernelFiles) { Guard; Add-File $file.FullName ('kernels/'+$file.Name) }
$remoteFiles=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'remote') -File | Select-Object -First 11)
if ($remoteFiles.Count -gt 10) { throw 'Remote scripts cap10' }
foreach ($file in $remoteFiles) { Guard; Add-File $file.FullName ('remote/'+$file.Name) }
foreach ($name in @('independent-assets-restore-2026-10-08.md','teacher-expansion-2026-10-09.md','teacher-repair-2026-10-09.md','token-limit-8192-2026-10-09.md')) { Guard; Add-File (Join-Path $repo ('docs\evidence\'+$name)) ('provenance/'+$name) }
Add-File (Join-Path $repo 'docs\rental-training-2026-10-09.md') 'README.md'
foreach ($name in @('source-freeze.json','publication-fix-source.json')) { Guard; Add-File (Join-Path $repo ('.artifacts\token-limit-20261009-8192\'+$name)) ('provenance/'+$name) }
$scanFiles=@($records | Where-Object { $_.path -cmatch '^(data|regression_input|provenance)/' -and $_.bytes -lt 10MB })
foreach ($file in $scanFiles) {
    Guard
    $content=[IO.File]::ReadAllText((Join-Path $staging $file.path))
    if ($content -match '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|Bearer [A-Za-z0-9_\-]{24,}|\bsk-[A-Za-z0-9_\-]{24,}') { throw ('Potential credential in package file: '+$file.path) }
}
$ordered=@($records | Sort-Object path)
[IO.File]::WriteAllLines((Join-Path $staging 'files.sha256'),@($ordered | ForEach-Object { $_.sha256+'  '+$_.path }),[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $staging 'package-identity.json'),([ordered]@{task='sezika-rental-20261009-01a12099';utc=[DateTime]::UtcNow.ToString('o');file_count=$ordered.Count;payload_bytes=$total;native_aot=$false;runtime='linux-x64 self-contained .NET10.0.12';experiment=[ordered]@{total=215;train=162;development=53;updates=24576;learning_rate=0.001;seed=17;max_tokens=8192;encoder_frozen=$true;head_initialization='zeros';teacher_calls=0;quality_passed=$false};files_sha256=(Hash-File (Join-Path $staging 'files.sha256'))}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
$archive=Join-Path $task 'upload.tar'
& (Join-Path $PSScriptRoot 'Invoke-BoundedProcess.ps1') -FilePath 'C:\Windows\System32\tar.exe' -ArgumentList @('-cf',$archive,'-C',$staging,'.') -TimeoutSeconds 150 -LogName prepare-tar -LogDirectory (Join-Path $task 'process-logs') -CancelFile $cancel
Guard; $archiveBytes=(Get-Item -LiteralPath $archive).Length
if ($archiveBytes -gt 2500MB) { throw 'Archive cap2500MiB' }
$archiveHash=Hash-File $archive
[IO.File]::WriteAllText((Join-Path $task 'upload.sha256'),$archiveHash+'  upload.tar'+"`n",[Text.UTF8Encoding]::new($false))
$manifest=[ordered]@{task='sezika-rental-20261009-01a12099';utc=[DateTime]::UtcNow.ToString('o');elapsed_seconds=$clock.Elapsed.TotalSeconds;archive_bytes=$archiveBytes;archive_sha256=$archiveHash;payload_bytes=$total;files=$ordered;files_sha256=(Hash-File (Join-Path $staging 'files.sha256'));package_identity_sha256=(Hash-File (Join-Path $staging 'package-identity.json'));gpu_training='NOT_RUN';native_aot=$false;secrets_included=$false;external_model_assets=$true}
[IO.File]::WriteAllText((Join-Path $task 'package-preparation.json'),($manifest|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
Write-Output "prepared_files=$($ordered.Count) archive_bytes=$archiveBytes archive_sha256=$archiveHash elapsed_seconds=$($clock.Elapsed.TotalSeconds)"
