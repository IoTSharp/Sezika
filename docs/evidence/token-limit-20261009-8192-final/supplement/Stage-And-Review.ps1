#requires -Version 7.0
$ErrorActionPreference='Stop'
$task=$PSScriptRoot
$root='D:\source\Sezika'
$clock=[Diagnostics.Stopwatch]::StartNew()
function Check {if($clock.Elapsed.TotalSeconds -ge 150 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'stage/review deadline/cancellation'}}
function Git([string]$Label,[string[]]$Arguments,[int[]]$Expected=@(0)){
 Check
 try {& (Join-Path $task 'Invoke-BoundedProcess.ps1') -FilePath 'C:\Program Files\Git\cmd\git.exe' -ArgumentList $Arguments -TimeoutSeconds 25 -LogName $Label -ProcessQueryTimeoutSeconds 2 -CancelFile (Join-Path $task 'cancel.request') | Out-Null}catch{Write-Output ('guardian retained: '+$_.Exception.Message)}
 $files=@(Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -File -Filter ('*'+$Label+'.result.json') | Select-Object -First 2)
 if($files.Count -ne 1){throw 'exact Git receipt required'}
 $r=Get-Content -Raw -LiteralPath $files[0].FullName | ConvertFrom-Json
 if($null -eq $r.ExitCode -or $r.ExitCode -notin $Expected){throw ('Git actual exit differs: '+$Label)}
 return $r
}
$initial=Git 'stage-initial-index' @('diff','--cached','--name-only')
if(-not [string]::IsNullOrWhiteSpace([IO.File]::ReadAllText($initial.StdoutPath))){throw 'existing index must be preserved; review before adding'}
$docs=@('HANDOFF.md','CHANGELOG.md','docs/training-next-handoff-2026-10-09.md','docs/evidence/token-limit-8192-2026-10-09.md','docs/evidence/token-limit-20261009-8192-final')
[void](Git 'stage-owned-evidence' (@('add','--')+$docs))
$head=Git 'roadmap-original-head' @('show','HEAD:ROADMAP.md')
$text=[IO.File]::ReadAllText($head.StdoutPath)
$marker="## 当前执行顺序与门槛`n`n"
if($text.Contains("`r`n")){$text=$text.Replace("`r`n","`n")}
if([regex]::Matches($text,[regex]::Escape($marker)).Count -ne 1){throw 'roadmap HEAD seam differs'}
$paragraph=[IO.File]::ReadAllText((Join-Path $task 'roadmap-owned-paragraph.txt'))
$indexText=$text.Replace($marker,$marker+$paragraph+"`n`n")
$indexPath=Join-Path $task 'roadmap-owned-index.txt'
[IO.File]::WriteAllText($indexPath,$indexText,[Text.UTF8Encoding]::new($false))
$blob=Git 'roadmap-owned-blob' @('hash-object','-w','--path=ROADMAP.md',$indexPath)
$hash=[IO.File]::ReadAllText($blob.StdoutPath).Trim()
if($hash -cnotmatch '^[0-9a-f]{40,64}$'){throw 'invalid index object identity'}
[void](Git 'roadmap-owned-index' @('update-index','--cacheinfo',('100644,'+$hash+',ROADMAP.md')))
$namesReceipt=Git 'review-staged-names' @('diff','--cached','--name-only')
$names=@([IO.File]::ReadAllLines($namesReceipt.StdoutPath))
if($names.Count -gt 1200){throw 'staged file cap1200'}
foreach($name in $names){
 Check
 if($name -notin @('HANDOFF.md','ROADMAP.md','CHANGELOG.md','docs/training-next-handoff-2026-10-09.md','docs/evidence/token-limit-8192-2026-10-09.md') -and -not $name.StartsWith('docs/evidence/token-limit-20261009-8192-final/')){throw ('unowned staged path: '+$name)}
}
$docCheck=Git 'review-owned-doc-whitespace' @('diff','--cached','--check','--','HANDOFF.md','ROADMAP.md','CHANGELOG.md','docs/training-next-handoff-2026-10-09.md','docs/evidence/token-limit-8192-2026-10-09.md')
$rawCheck=Git 'review-all-evidence-whitespace' @('diff','--cached','--check') @(0,2)
$rawOutput=[IO.File]::ReadAllText($rawCheck.StdoutPath)
$lines=@($rawOutput -split "`n" | Where-Object {$_ -match '^[^+].*:[0-9]+: '})
if($lines.Count -gt 128){throw 'whitespace exception cap128'}
foreach($line in $lines){if(-not $line.StartsWith('docs/evidence/token-limit-20261009-8192-final/')){throw ('unexpected own-document whitespace failure: '+$line)}}
$roadmapWorking=Git 'review-preserved-roadmap-hunk' @('diff','--','ROADMAP.md')
$remaining=[IO.File]::ReadAllText($roadmapWorking.StdoutPath)
if(-not $remaining.Contains('+S4-12/S4-15独立CLI资产准备同步登记：') -or $remaining.Contains('+2026-10-09 用户明确选择8192')){throw 'paused roadmap hunk or owned split differs'}
$identity=Get-Content -Raw -LiteralPath (Join-Path $task 'task-identity.json') | ConvertFrom-Json
$count=0
foreach($file in $identity.paused){Check;if($file.path -ceq 'ROADMAP.md'){continue};if((Get-FileHash -LiteralPath (Join-Path $root $file.path)).Hash -cne $file.sha256){throw 'paused source changed'};$count++}
$manifest=Join-Path $root 'docs/evidence/teacher-repair-20261009-final/data-v3/experiment-manifest.json'
$ledger=Join-Path $root 'docs/evidence/teacher-repair-20261009-final/data-v3/derivation-ledger.json'
if((Get-FileHash -LiteralPath $manifest).Hash -cne 'E8E74CE0BBF1A72DA5FCDF4DCAD9D302513DC5DC42CB548BFD21D2569621AEB6' -or (Get-FileHash -LiteralPath $ledger).Hash -cne '55F3226946831C4B9CBD7F6EE10D1194764860C8343A9386CAA40C678CE24EC0'){throw 'original data/ledger changed'}
[IO.File]::WriteAllText((Join-Path $task 'staged-review.json'),(@{utc=[DateTime]::UtcNow.ToString('o');staged_files=$names.Count;paused_files_verified=$count;original_data_and_ledger_verified=$true;owned_doc_whitespace_exit=$docCheck.ExitCode;all_raw_evidence_whitespace_exit=$rawCheck.ExitCode;raw_whitespace_exceptions=$lines;raw_exception_policy='original evidence bytes preserved and SHA-verified; only exact owned archive paths permitted';roadmap_index_only_owned_paragraph=$true;user_diagnostic_hunk_unstaged=$true;scope='local documentation/evidence only, no source edits, GPU work, push or publication'} | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
Write-Output "staged_files=$($names.Count) paused=$count docs_check=$($docCheck.ExitCode) raw_check=$($rawCheck.ExitCode) raw_exceptions=$($lines.Count)"
