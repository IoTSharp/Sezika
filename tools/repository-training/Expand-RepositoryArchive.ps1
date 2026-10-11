param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{64}$')][string]$ExpectedSha256,
    [Parameter(Mandatory)][string]$Destination,
    [ValidateRange(30,1800)][int]$TimeoutSeconds=900,
    [string]$CancelFile
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required.'}
$archivePath=(Resolve-Path -LiteralPath $Archive).Path;$destinationPath=[IO.Path]::GetFullPath($Destination)
$watch=[Diagnostics.Stopwatch]::StartNew();$count=0;[long]$total=0
function Guard{if($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds -or ($CancelFile -and (Test-Path -LiteralPath $CancelFile))){throw 'Extraction cancelled/deadline reached.'}}
function Safe([string]$Relative){
    Guard;if($Relative -cnotmatch '^[A-Za-z0-9_./-]+$' -or $Relative.Contains('..') -or [IO.Path]::IsPathRooted($Relative)){throw 'Archive relative path rejected.'}
    $full=[IO.Path]::GetFullPath((Join-Path $destinationPath $Relative))
    if(-not $full.StartsWith($destinationPath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Archive path escaped destination.'}
    $cursor=$full
    for($depth=0;$depth -lt 64;$depth++){Guard;if((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Extraction linked ancestry rejected.'};$parent=[IO.Path]::GetDirectoryName($cursor);if($null -eq $parent){return $full};$cursor=$parent}
    throw 'Extraction ancestry bound64.'
}
if((Get-Item -LiteralPath $archivePath).Length -gt 16GB -or (Get-FileHash -LiteralPath $archivePath).Hash.ToLowerInvariant() -cne $ExpectedSha256){throw 'Archive bytes/SHA mismatch.'}
if(Test-Path -LiteralPath $destinationPath){throw 'Extraction destination exists; preserve it.'}
[void](Safe 'preflight');[void][IO.Directory]::CreateDirectory($destinationPath)
$stream=$null;$reader=$null;$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
try{
    $stream=[IO.File]::OpenRead($archivePath);$reader=[System.Formats.Tar.TarReader]::new($stream,$false);$buffer=[byte[]]::new(1MB)
    for($entryIndex=0;$entryIndex -lt 1006;$entryIndex++){
        Guard;$entry=$reader.GetNextEntry($false);if($null -eq $entry){break}
        $relative=$entry.Name;if($relative.StartsWith('./',[StringComparison]::Ordinal)){$relative=$relative.Substring(2)}
        if($relative -eq ''){if($entry.EntryType -ne [System.Formats.Tar.TarEntryType]::Directory){throw 'Invalid root tar entry.'};continue}
        $relative=$relative.TrimEnd('/');$path=Safe $relative
        if($entry.EntryType -eq [System.Formats.Tar.TarEntryType]::Directory){[void][IO.Directory]::CreateDirectory($path);continue}
        if($entry.EntryType -notin @([System.Formats.Tar.TarEntryType]::RegularFile,[System.Formats.Tar.TarEntryType]::V7RegularFile) -or -not $seen.Add($relative)){throw 'Only unique regular files are extractable.'}
        $count++;$total+=$entry.Length;if($count -gt 1000 -or $total -gt 16GB -or $entry.Length -lt 0){throw 'Archive count/byte bound1000/16GiB.'}
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path));$output=[IO.FileStream]::new($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try{[long]$remaining=$entry.Length;$blocks=[long][Math]::Ceiling($remaining/1MB);for([long]$block=0;$block -lt $blocks;$block++){Guard;$size=[int][Math]::Min(1MB,$remaining);$entry.DataStream.ReadExactly($buffer,0,$size);$output.Write($buffer,0,$size);$remaining-=$size};if($remaining -ne 0){throw 'Truncated archive member.'};$output.Flush($true)}finally{$output.Dispose()}
    }
    if($entryIndex -ge 1006){throw 'Tar entry bound1006.'}
    $manifestPath=Safe 'files.sha256';$lines=[IO.File]::ReadAllLines($manifestPath);if($lines.Count -gt 1000){throw 'Manifest line bound1000.'}
    $checked=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($line in $lines){Guard;if($line -cnotmatch '^([a-f0-9]{64})  ([A-Za-z0-9_./-]+)$'){throw 'Malformed extracted SHA manifest.'};$hash=$Matches[1];$relative=$Matches[2];if(-not $checked.Add($relative) -or -not $seen.Contains($relative) -or (Get-FileHash -LiteralPath (Safe $relative)).Hash.ToLowerInvariant() -cne $hash){throw 'Extracted file SHA/identity mismatch.'}}
    if($checked.Count+1 -ne $seen.Count -or -not $seen.Contains('files.sha256')){throw 'Manifest does not cover the complete extracted file set.'}
    [pscustomobject]@{status='PASS_safe_extract_and_full_file_sha';directory=$destinationPath;archive_sha256=$ExpectedSha256;files=$count;bytes=$total;elapsed_seconds=$watch.Elapsed.TotalSeconds}
}finally{if($reader){$reader.Dispose()};if($stream){$stream.Dispose()}}
