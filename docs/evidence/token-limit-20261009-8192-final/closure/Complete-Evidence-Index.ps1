#requires -Version 7.0
param([ValidateRange(1,1400)][int]$MaxItems=1400,[switch]$Preview)
$ErrorActionPreference='Stop'
$root='D:\source\Sezika'
$task=$PSScriptRoot
$archive='docs/evidence/token-limit-20261009-8192-final'
$clock=[Diagnostics.Stopwatch]::StartNew()
function Check {if($clock.Elapsed.TotalSeconds -ge 170 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'index verification deadline/cancellation'}}
function Git([string]$Label,[string[]]$Arguments,[int[]]$Expected=@(0)){
 Check
 $name=if($Preview){'index-preview-'+$Label}else{'index-complete-'+$Label}
 $seen=@(Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -File -Filter ('*'+$name+'.result.json')|Select-Object -First 2)
 if($seen.Count -ne 0){throw 'unique index operation receipt required'}
 try{& (Join-Path $task 'Invoke-BoundedProcess.ps1') -FilePath 'C:\Program Files\Git\cmd\git.exe' -ArgumentList $Arguments -TimeoutSeconds 25 -LogName $name -ProcessQueryTimeoutSeconds 2 -CancelFile (Join-Path $task 'cancel.request')|Out-Null}catch{Write-Output ('guardian retained: '+$_.Exception.Message)}
 $files=@(Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -File -Filter ('*'+$name+'.result.json')|Select-Object -First 2)
 if($files.Count -ne 1){throw 'exact single receipt required'}
 $r=Get-Content -Raw -LiteralPath $files[0].FullName|ConvertFrom-Json
 if($null -eq $r.ExitCode -or $r.ExitCode -notin $Expected){throw ('Git actual exit failed: '+$name)}
 return $r
}
$all=[Collections.Generic.List[object]]::new()
$inventoryPaths=@('inventory.json','supplement/inventory.json','closure/inventory.json')
foreach($relative in $inventoryPaths){
 Check
 $path=Join-Path $root ($archive+'/'+$relative)
 if(-not [IO.File]::Exists($path)){if($Preview -and $relative -ceq 'closure/inventory.json'){continue};throw 'inventory missing'}
 $inv=Get-Content -Raw -LiteralPath $path|ConvertFrom-Json
 if($inv.entries.Count -ne $inv.files -or $inv.files -gt 1024){throw 'inventory count cap/mismatch'}
 $prefix=if($relative -eq 'inventory.json'){$archive}else{$archive+'/'+[IO.Path]::GetDirectoryName($relative).Replace('\','/')}
 foreach($entry in $inv.entries){Check;$all.Add([pscustomobject]@{path=($prefix+'/'+$entry.path);bytes=$entry.bytes;sha256=$entry.sha256})}
 $all.Add([pscustomobject]@{path=($archive+'/'+$relative);bytes=(Get-Item -LiteralPath $path).Length;sha256=(Get-FileHash -LiteralPath $path).Hash})
}
if($all.Count -gt 1400){throw 'archive item cap1400'}
$paths=@{}
foreach($entry in $all){Check;if($paths.ContainsKey($entry.path)){throw 'duplicate archive path'};$paths[$entry.path]=$entry}
$list=Git 'names-before' @('ls-files','--stage','--',$archive)
$indexed=@{}
foreach($line in [IO.File]::ReadAllLines($list.StdoutPath)){
 Check
 if($line -cnotmatch '^100644 ([0-9a-f]{40}) 0\t(.+)$'){throw 'unexpected index identity format'}
 $indexed[$Matches[2]]=$Matches[1]
}
$missing=@($all|Where-Object {-not $indexed.ContainsKey($_.path)})
Write-Output "inventoried=$($all.Count) indexed=$($indexed.Count) missing=$($missing.Count)"
$sample=@($all|Select-Object -First $MaxItems)
for($i=0;$i -lt $sample.Count -and $i -lt 1400;$i++){
 Check
 $entry=$sample[$i];$full=[IO.Path]::GetFullPath((Join-Path $root $entry.path))
 if(-not $full.StartsWith((Join-Path $root $archive)+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'escaped archive path'}
 if((Get-Item -LiteralPath $full).Length -ne $entry.bytes -or (Get-FileHash -LiteralPath $full).Hash -cne $entry.sha256){throw ('archive inventory hash differs: '+$entry.path)}
}
if($Preview){Write-Output "preview_verified=$($sample.Count) no_index_mutation=true";exit 0}
if($sample.Count -ne $all.Count){throw 'full verification required before mutation'}
$pathspec=Join-Path $task 'archive-index-pathspec.nul'
[IO.File]::WriteAllText($pathspec,(@($all.path) -join [char]0)+[char]0,[Text.UTF8Encoding]::new($false))
[void](Git 'force-owned-archive' @('--literal-pathspecs','add','--force',('--pathspec-from-file='+$pathspec),'--pathspec-file-nul'))
$after=Git 'names-after' @('ls-files','--stage','--',$archive)
$indexed=@{}
foreach($line in [IO.File]::ReadAllLines($after.StdoutPath)){
 Check
 if($line -cnotmatch '^100644 ([0-9a-f]{40}) 0\t(.+)$'){throw 'unexpected archive index format'}
 $indexed[$Matches[2]]=$Matches[1]
}
if($indexed.Count -ne $all.Count){throw 'archive index count differs from exact inventories'}
for($i=0;$i -lt $all.Count -and $i -lt 1400;$i++){
 Check
 $entry=$all[$i];$bytes=[IO.File]::ReadAllBytes((Join-Path $root $entry.path))
 $header=[Text.Encoding]::UTF8.GetBytes(('blob '+$bytes.Length+[char]0))
 $sha=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA1)
 try{$sha.AppendData($header);$sha.AppendData($bytes);$object=[Convert]::ToHexString($sha.GetHashAndReset()).ToLowerInvariant()}finally{$sha.Dispose()}
 if(-not $indexed.ContainsKey($entry.path) -or $indexed[$entry.path] -cne $object){throw ('Git object bytes differ from archive SHA-bound file: '+$entry.path)}
 if($i%150 -eq 0){Write-Output "index_byte_verified=$i/$($all.Count)"}
}
$docs=@('HANDOFF.md','CHANGELOG.md','docs/training-next-handoff-2026-10-09.md','docs/evidence/token-limit-8192-2026-10-09.md')
[void](Git 'updated-owned-docs' (@('add','--')+$docs))
$roadmap=Join-Path $task 'roadmap-owned-index.txt'
if(-not [IO.File]::Exists($roadmap)){throw 'reviewed owned roadmap index content missing'}
$blob=Git 'updated-roadmap-blob' @('hash-object','-w','--path=ROADMAP.md',$roadmap)
$object=[IO.File]::ReadAllText($blob.StdoutPath).Trim()
if($object -cnotmatch '^[0-9a-f]{40}$'){throw 'roadmap object differs'}
[void](Git 'updated-roadmap-index' @('update-index','--cacheinfo',('100644,'+$object+',ROADMAP.md')))
$names=Git 'staged-names' @('diff','--cached','--name-only')
$staged=[IO.File]::ReadAllLines($names.StdoutPath)
if($staged.Count -ne ($all.Count+5)){throw 'staged count differs from archive plus five owned docs'}
foreach($name in $staged){Check;if($name -notin ($docs+@('ROADMAP.md')) -and -not $paths.ContainsKey($name)){throw ('unowned staged path: '+$name)}}
$own=Git 'owned-doc-whitespace' (@('diff','--cached','--check','--')+$docs+@('ROADMAP.md'))
$raw=Git 'raw-whitespace' @('diff','--cached','--check') @(0,2)
$exceptions=@([IO.File]::ReadAllLines($raw.StdoutPath)|Where-Object {$_ -match '^[^+].*:[0-9]+: '})
if($exceptions.Count -gt 30){throw 'raw whitespace exception cap30'}
foreach($line in $exceptions){Check;if(-not $line.StartsWith($archive+'/')){throw 'whitespace outside archive'}}
$remaining=Git 'preserved-roadmap-hunk' @('diff','--','ROADMAP.md')
$diff=[IO.File]::ReadAllText($remaining.StdoutPath)
if(-not $diff.Contains('+S4-12/S4-15独立CLI资产准备同步登记：') -or $diff.Contains('+2026-10-09 用户明确选择8192')){throw 'roadmap owned split/paused hunk differs'}
$identity=Get-Content -Raw -LiteralPath (Join-Path $task 'task-identity.json')|ConvertFrom-Json
$paused=0
foreach($file in $identity.paused){Check;if($file.path -ceq 'ROADMAP.md'){continue};if((Get-FileHash -LiteralPath (Join-Path $root $file.path)).Hash -cne $file.sha256){throw 'paused source/user bytes changed'};$paused++}
foreach($expected in @(@{path='docs/evidence/teacher-repair-20261009-final/data-v3/experiment-manifest.json';sha='E8E74CE0BBF1A72DA5FCDF4DCAD9D302513DC5DC42CB548BFD21D2569621AEB6'},@{path='docs/evidence/teacher-repair-20261009-final/data-v3/derivation-ledger.json';sha='55F3226946831C4B9CBD7F6EE10D1194764860C8343A9386CAA40C678CE24EC0'})){
 Check
 if((Get-FileHash -LiteralPath (Join-Path $root $expected.path)).Hash -cne $expected.sha){throw 'original data identity changed'}
}
$result=@{utc=[DateTime]::UtcNow.ToString('o');archive_files=$all.Count;archive_bytes=($all|Measure-Object bytes -Sum).Sum;missing_before=$missing.Count;index_byte_identity_verified=$true;original_inventory_sha_verified=$true;staged_files=$staged.Count;paused_files_verified=$paused;owned_doc_whitespace_exit=$own.ExitCode;raw_whitespace_exit=$raw.ExitCode;raw_whitespace_exceptions=$exceptions;roadmap_old_hunk_preserved_unstaged=$true;original_data_ledger_verified=$true;scope='exact inventories only; local docs/evidence commit; no GPU work or publication'}
[IO.File]::WriteAllText((Join-Path $task 'index-completion-review.json'),($result|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
Write-Output "staged=$($staged.Count) archive=$($all.Count) bytes_verified=true paused=$paused whitespace_raw=$($raw.ExitCode) exceptions=$($exceptions.Count)"
