#requires -Version 7.0
$ErrorActionPreference='Stop'
$task=$PSScriptRoot
$root='D:\source\Sezika'
$archive='docs/evidence/token-limit-20261009-8192-final'
$clock=[Diagnostics.Stopwatch]::StartNew()
function Check {if($clock.Elapsed.TotalSeconds -ge 105 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'commit verification deadline/cancellation'}}
function Git([string]$Label,[string[]]$Arguments){
 Check
 $name='commit-final-'+$Label
 if(Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -File -Filter ('*'+$name+'.result.json')|Select-Object -First 1){throw 'operation already recorded'}
 try{& (Join-Path $task 'Invoke-BoundedProcess.ps1') -FilePath 'C:\Program Files\Git\cmd\git.exe' -ArgumentList $Arguments -TimeoutSeconds 25 -LogName $name -ProcessQueryTimeoutSeconds 2 -CancelFile (Join-Path $task 'cancel.request')|Out-Null}catch{Write-Output ('guardian retained: '+$_.Exception.Message)}
 $files=@(Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -File -Filter ('*'+$name+'.result.json')|Select-Object -First 2)
 if($files.Count -ne 1){throw 'exact commit receipt required'}
 $r=Get-Content -Raw -LiteralPath $files[0].FullName|ConvertFrom-Json
 if($null -eq $r.ExitCode -or $r.ExitCode -ne 0){throw ('actual Git exit failed: '+$name)}
 return $r
}
$review=Get-Content -Raw -LiteralPath (Join-Path $task 'index-completion-review.json')|ConvertFrom-Json
if(-not $review.index_byte_identity_verified -or -not $review.original_data_ledger_verified -or -not $review.roadmap_old_hunk_preserved_unstaged -or $review.paused_files_verified -ne 10 -or $review.owned_doc_whitespace_exit -ne 0){throw 'review requirements incomplete'}
$indexReceipt=Git 'index-before' @('ls-files','--stage','--',$archive)
$indexLines=[IO.File]::ReadAllLines($indexReceipt.StdoutPath)
if($indexLines.Count -ne $review.archive_files){throw 'review index count changed'}
$before=Git 'head-before' @('rev-parse','HEAD')
$beforeHash=[IO.File]::ReadAllText($before.StdoutPath).Trim()
if($beforeHash -cnotmatch '^44b4082[0-9a-f]{33}$'){throw 'reviewed HEAD changed'}
$names=Git 'names-before' @('diff','--cached','--name-only')
$staged=[IO.File]::ReadAllLines($names.StdoutPath)
if($staged.Count -ne $review.staged_files){throw 'staged review count changed'}
foreach($name in $staged){Check;if($name -notin @('HANDOFF.md','ROADMAP.md','CHANGELOG.md','docs/training-next-handoff-2026-10-09.md','docs/evidence/token-limit-8192-2026-10-09.md') -and -not $name.StartsWith($archive+'/')){throw 'unowned staged path'}}
$commit=Git 'create' @('commit','-m','docs: archive 8192 experiment and approved recovery evidence')
$after=Git 'head-after' @('rev-parse','HEAD','HEAD^')
$hashes=[IO.File]::ReadAllLines($after.StdoutPath)
if($hashes.Count -ne 2 -or $hashes[0] -ceq $beforeHash -or $hashes[1] -cne $beforeHash){throw 'commit identity or parent differs'}
$tree=Git 'tree-after' @('ls-tree','-r','HEAD','--',$archive)
$treeLines=[IO.File]::ReadAllLines($tree.StdoutPath)
if($treeLines.Count -ne $indexLines.Count){throw 'committed archive count differs'}
$expected=@{}
foreach($line in $indexLines){Check;if($line -cnotmatch '^100644 ([0-9a-f]{40}) 0\t(.+)$'){throw 'index mode differs'};$expected[$Matches[2]]=$Matches[1]}
foreach($line in $treeLines){Check;if($line -cnotmatch '^100644 blob ([0-9a-f]{40})\t(.+)$' -or -not $expected.ContainsKey($Matches[2]) -or $expected[$Matches[2]] -cne $Matches[1]){throw 'committed evidence object bytes differ'}}
$empty=Git 'index-after' @('diff','--cached','--name-only')
if(-not [string]::IsNullOrWhiteSpace([IO.File]::ReadAllText($empty.StdoutPath))){throw 'index not empty after exact commit'}
$roadmap=Git 'roadmap-after' @('diff','--','ROADMAP.md')
$diff=[IO.File]::ReadAllText($roadmap.StdoutPath)
if(-not $diff.Contains('+S4-12/S4-15独立CLI资产准备同步登记：') -or $diff.Contains('+2026-10-09 用户明确选择8192')){throw 'paused roadmap hunk not preserved'}
$identity=Get-Content -Raw -LiteralPath (Join-Path $task 'task-identity.json')|ConvertFrom-Json
foreach($file in $identity.paused){Check;if($file.path -ceq 'ROADMAP.md'){continue};if((Get-FileHash -LiteralPath (Join-Path $root $file.path)).Hash -cne $file.sha256){throw 'paused source/user bytes changed'}}
$result=@{utc=[DateTime]::UtcNow.ToString('o');commit=$hashes[0];parent=$beforeHash;commit_actual_exit=$commit.ExitCode;archive_files=$treeLines.Count;reviewed_files=$staged.Count;committed_archive_bytes_match_verified_index=$true;index_empty=$true;paused_files_verified=10;roadmap_diagnostic_hunk_preserved=$true;training_repeated=$false;ssh_recovered=$false;gpu_recovered=$null;scope='one local docs/evidence commit; no push/publication/deployment; subsequent final process check remains local'}
[IO.File]::WriteAllText((Join-Path $task 'commit-final-verification.json'),($result|ConvertTo-Json -Depth 3),[Text.UTF8Encoding]::new($false))
Write-Output "commit=$($hashes[0]) parent=$beforeHash files=$($staged.Count) archive=$($treeLines.Count) paused_preserved=true index_empty=true"
