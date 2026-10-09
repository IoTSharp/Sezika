#requires -Version 7.0
param([switch]$Preview)
$ErrorActionPreference='Stop'
$task=$PSScriptRoot
$root='D:\source\Sezika'
$base=Join-Path $root 'docs/evidence/token-limit-20261009-8192-final'
$destination=Join-Path $base 'postreboot'
$archive='docs/evidence/token-limit-20261009-8192-final/postreboot'
$utf=[Text.UTF8Encoding]::new($false)
$clock=[Diagnostics.Stopwatch]::StartNew()
function Check {if($clock.Elapsed.TotalSeconds -ge 140 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'postreboot finalization deadline/cancellation'}}
function ReplaceOnce([string]$Path,[string]$Old,[string]$New){
 Check
 $text=[IO.File]::ReadAllText($Path)
 if([regex]::Matches($text,[regex]::Escape($Old)).Count -ne 1){throw ('document exact seam differs: '+$Path)}
 [IO.File]::WriteAllText($Path,$text.Replace($Old,$New),$utf)
}
function Git([string]$Label,[string[]]$Arguments,[int[]]$Expected=@(0)){
 Check
 $name='postreboot-final-'+$Label
 if(Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -File -Filter ('*'+$name+'.result.json')|Select-Object -First 1){throw 'operation receipt already exists'}
 try{& (Join-Path $task 'Invoke-BoundedProcess.ps1') -FilePath 'C:\Program Files\Git\cmd\git.exe' -ArgumentList $Arguments -TimeoutSeconds 25 -LogName $name -ProcessQueryTimeoutSeconds 2 -CancelFile (Join-Path $task 'cancel.request')|Out-Null}catch{Write-Output ('guardian retained: '+$_.Exception.Message)}
 $files=@(Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -File -Filter ('*'+$name+'.result.json')|Select-Object -First 2)
 if($files.Count -ne 1){throw 'exact Git receipt required'}
 $r=Get-Content -Raw -LiteralPath $files[0].FullName|ConvertFrom-Json
 if($null -eq $r.ExitCode -or $r.ExitCode -notin $Expected){throw ('actual Git exit failed: '+$name)}
 return $r
}
if(Test-Path -LiteralPath $destination){throw 'prior postreboot evidence retained; no overwrite'}
$old=@{}
$prior=[Collections.Generic.List[object]]::new()
foreach($name in @('inventory.json','supplement/inventory.json','closure/inventory.json')){
 Check
 $path=Join-Path $base $name
 $inv=Get-Content -Raw -LiteralPath $path|ConvertFrom-Json
 if($inv.files -gt 1024 -or $inv.entries.Count -ne $inv.files){throw 'prior inventory cap/mismatch'}
 $prior.Add(@{path=$name;sha256=(Get-FileHash -LiteralPath $path).Hash})
 foreach($entry in $inv.entries){Check;$old[$entry.path]=$entry.sha256}
}
$candidates=[Collections.Generic.List[object]]::new()
foreach($directory in @($task,(Join-Path $task 'remote'),(Join-Path $task 'process-logs'))){
 Check
 $files=@(Get-ChildItem -LiteralPath $directory -File -Force|Select-Object -First 1025)
 if($files.Count -gt 1024){throw 'directory cap1024'}
 foreach($file in $files){
  Check
  $relative=[IO.Path]::GetRelativePath($task,$file.FullName).Replace('\','/')
  if($relative -match '\.tar$' -or $file.Name -ceq 'archive-index-pathspec.nul'){continue}
  if($relative.StartsWith('process-logs/')){
   $stem=[regex]::Replace($file.Name,'\.(result\.json|identity\.json|stdout\.log|stderr\.log)$','')
   if($stem -ceq $file.Name -or -not [IO.File]::Exists((Join-Path $directory ($stem+'.result.json')))){continue}
  }
  $candidates.Add($file)
 }
}
$stack=[Collections.Generic.Stack[object]]::new()
$stack.Push(@{path=(Join-Path $task 'postreboot-retrieved');depth=0})
for($dir=0;$dir -lt 32 -and $stack.Count -gt 0;$dir++){
 Check
 $entry=$stack.Pop()
 $children=@(Get-ChildItem -LiteralPath $entry.path -Force|Select-Object -First 33)
 if($children.Count -gt 32){throw 'diagnosis directory item cap32'}
 foreach($child in $children){
  Check
  if(($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'reparse point'}
  if($child.PSIsContainer){if($entry.depth -ge 4){throw 'diagnosis depth cap4'};$stack.Push(@{path=$child.FullName;depth=$entry.depth+1})}else{$candidates.Add($child)}
 }
}
if($stack.Count -gt 0 -or $candidates.Count -gt 2048){throw 'archive total/depth cap'}
if($Preview){
 $file=$candidates[0];$relative=[IO.Path]::GetRelativePath($task,$file.FullName).Replace('\','/')
 $hash=(Get-FileHash -LiteralPath $file.FullName).Hash
 Write-Output "preview_checked=1 candidates=$($candidates.Count) original_bindings=$($prior.Count) no_mutation=true path=$relative sha256=$hash"
 exit 0
}
$head=Git 'head-before' @('rev-parse','HEAD')
$before=[IO.File]::ReadAllText($head.StdoutPath).Trim()
if($before -cne '81a325741b04c0b6eaf7f29882a1e0ad245ab9e2'){throw 'reviewed parent changed'}
$empty=Git 'index-before' @('diff','--cached','--name-only')
if(-not [string]::IsNullOrWhiteSpace([IO.File]::ReadAllText($empty.StdoutPath))){throw 'existing index must be preserved'}
$latest='11:24:57 UTC（北京时间19:24:57）已确认SSH恢复及新boot_id 31951b0f-232d-40c2-aec0-d7e7dd5215a6；11:27:35 UTC诊断确认PCI仍识别同一RTX4090/UUID，但nvidia-smi仍exit6/No devices，GSP固件初始化失败，显卡未恢复。两容器running、翻译health200/ASR503；六项保存物/数据/encoder SHA通过。当前需要服务器管理方处理GPU初始化故障。'
ReplaceOnce (Join-Path $root 'HANDOFF.md') '截至2026-10-09 11:15:39 UTC（北京时间19:15:39），最新SSH exit255与直接TCP探针均被拒绝，尚未取得不同boot_id或GPU恢复证明，需管理控制台/机房检查启动与网络。' $latest
ReplaceOnce (Join-Path $root 'HANDOFF.md') '软件恢复未成功，已发一次reboot并等待新的boot_id。' '软件恢复未成功；一次reboot已经完成，11:24:57 UTC新boot_id已确认，11:27:35 UTC显卡仍GSP初始化失败，独立验证/32回归仍阻塞。'
ReplaceOnce (Join-Path $root 'docs/training-next-handoff-2026-10-09.md') '截至2026-10-09 11:15:39 UTC（北京时间19:15:39）最新SSH及直接TCP仍被拒绝，需管理控制台/机房检查启动与网络；连通后观察新的boot_id及显卡/共享服务状态' '11:24:57 UTC已确认新boot_id和SSH恢复；11:27:35 UTC显卡仍No devices/GSP初始化失败，需服务器管理方处理GPU故障；两容器running、翻译health200/ASR503，保存物六项SHA保持'
ReplaceOnce (Join-Path $root 'docs/training-next-handoff-2026-10-09.md') '有界重连尚未取得新的boot_id；只观察，不重复执行重启或询问该许可。' '已经取得新boot_id 31951b0f-232d-40c2-aec0-d7e7dd5215a6，但显卡仍GSP初始化失败；只观察，不重复执行重启或询问该许可。'
ReplaceOnce (Join-Path $root 'CHANGELOG.md') '截至11:15:39 UTC最新SSH和直接TCP仍被拒绝，显卡恢复待实际观察。' '11:24:57 UTC已确认主机完成重启及SSH恢复，但显卡仍No devices/GSP初始化失败，保存物六项SHA保持。'
$oldRoadmap='截至11:15:39 UTC最新SSH及直接TCP仍被拒绝，新的boot_id/显卡恢复待实际观察，需管理控制台/机房检查。'
$newRoadmap='11:24:57 UTC已确认SSH恢复和新boot_id；11:27:35 UTC PCI仍识别RTX4090但显卡No devices/GSP初始化失败，六项保存物SHA保持，需服务器管理方处理GPU故障。'
foreach($path in @((Join-Path $root 'ROADMAP.md'),(Join-Path $task 'roadmap-owned-index.txt'),(Join-Path $task 'roadmap-owned-paragraph.txt'))){Check;ReplaceOnce $path $oldRoadmap $newRoadmap}
ReplaceOnce (Join-Path $root 'docs/evidence/token-limit-8192-2026-10-09.md') '10:56:29 UTC已请求重启，启动后另验。' '10:56:29 UTC已请求重启；11:24:57 UTC确认新boot_id及SSH恢复，但11:27:35 UTC显卡仍GSP固件初始化失败。'
$evidence=Join-Path $root 'docs/evidence/token-limit-8192-2026-10-09.md'
$paragraph=@'
## 主机已完成重启，显卡仍未恢复

2026-10-09 11:24:57 UTC（北京时间19:24:57），唯一新phase `reboot-observe-postcommit`取得新boot_id `31951b0f-232d-40c2-aec0-d7e7dd5215a6`，不同于重启前`d5489509-b67a-436d-b331-05390e3048cc`，SSH已经恢复。GPU查询仍exit6/`No devices were found`，SSH实际exit6由该GPU失败传播，不能误记为连接失败。两原容器running；新init PID翻译2974/ASR2973，启动时间均为11:19:27 UTC；翻译health200、ASR503且明确CUDA不可用，不把翻译health200当GPU使用证明。

随后`stage-postreboot-diagnosis`只读诊断，payload/runner/SSH均exit0，但这是诊断成功，不是GPU恢复。11:27:35 UTC仍识别同一RTX4090/UUID/PCI65:00.0，vendor0x10de/device0x2684、enable1、power D0、kernel driver nvidia；nvidia-smi exit6。Video BIOS为未知、GPU firmware为N/A；内核反复报告`unexpected WPR2 already up`、`Cannot initialize GSP firmware RM`及`RmInitAdapter failed (0x62:0x40:2028)`。重启后系统内核为6.8.0-142-generic，此前驱动操作使用6.8.0-136-generic；驱动版本仍580.173.02。本会话没有安装内核或驱动，也不能据此确认故障根因或GPU硬件损坏。

重启后远端训练report/head/checkpoint、原215数据manifest/256账本和8192 encoder manifest六项SHA全部通过。未上传postreboot验证helper，未再训练、独立编码、运行32回归、重置、重启或停止共享服务。下一步由服务器管理方处理GSP初始化故障；恢复且资源准入通过后，才接续保存head的独立231 forwards及原32回归。

Linux诊断登记2个唯一身份，final_check_complete1/live0/unknown0/unchecked0/cleanup_budget_exhausted0；短命中间进程的通用观测局限保留，不称所有历史子命令均被捕获。40,960bytes诊断tar，SHA `B560F670054FC36987D65E86FE58814C1C9E65BBF4B86711B49B4216E83040A3`，10成员已按普通文件/目录安全核验解包；首次下载误用了不存在的文件名、真实scp exit1，原件保留后准确路径v2下载/解包exit0。永久新增`postreboot/inventory.json`绑定本轮新证据及前一81a3257本地证据commit回执；三个旧inventory/原件不改写。提交与最终进程回执另留本机任务根，不制造循环封存声明。

'@
ReplaceOnce $evidence '## 原件、进程与临时资源' ($paragraph+"`n## 原件、进程与临时资源")
$entries=[Collections.Generic.List[object]]::new()
for($i=0;$i -lt $candidates.Count -and $i -lt 2048;$i++){
 Check
 $file=$candidates[$i];$file.Refresh()
 if($file.Length -gt 4MB -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'file cap/reparse point'}
 $relative=[IO.Path]::GetRelativePath($task,$file.FullName).Replace('\','/')
 $hash=(Get-FileHash -LiteralPath $file.FullName).Hash
 if($old.ContainsKey($relative) -and $old[$relative] -ceq $hash){continue}
 $target=[IO.Path]::GetFullPath((Join-Path $destination $relative))
 if(-not $target.StartsWith($destination+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'archive path escaped'}
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))|Out-Null
 [IO.File]::Copy($file.FullName,$target,$false)
 if((Get-FileHash -LiteralPath $target).Hash -cne $hash -or (Get-Item -LiteralPath $target).Length -ne $file.Length){throw 'copy bytes differ'}
 $entries.Add(@{path=$relative;bytes=$file.Length;sha256=$hash;source=$file.FullName})
 if($i%150 -eq 0){Write-Output "postreboot_seal=$i/$($candidates.Count) new=$($entries.Count)"}
}
$inventory=@{utc=[DateTime]::UtcNow.ToString('o');files=$entries.Count;bytes=($entries|Measure-Object bytes -Sum).Sum;entries=$entries;prior_inventories=$prior;scope='byte-identical new completed receipts/scripts and postreboot Linux diagnosis evidence; prior archive unchanged';limitation='this finalization invocation, its Git operations and later closure receipts remain local; no new training or GPU validation'}
[IO.File]::WriteAllText((Join-Path $destination 'inventory.json'),($inventory|ConvertTo-Json -Depth 6),$utf)
$paths=@($entries|ForEach-Object {$archive+'/'+$_.path})+@($archive+'/inventory.json')
if($paths.Count -gt 512){throw 'postreboot archive cap512'}
$pathspec=Join-Path $task 'postreboot-index-pathspec.nul'
[IO.File]::WriteAllText($pathspec,($paths -join [char]0)+[char]0,$utf)
[void](Git 'add-archive' @('--literal-pathspecs','add','--force',('--pathspec-from-file='+$pathspec),'--pathspec-file-nul'))
$index=Git 'index-evidence' @('ls-files','--stage','--',$archive)
$lines=[IO.File]::ReadAllLines($index.StdoutPath)
if($lines.Count -ne $paths.Count){throw 'index exact inventory count differs'}
$objects=@{}
foreach($line in $lines){Check;if($line -cnotmatch '^100644 ([0-9a-f]{40}) 0\t(.+)$'){throw 'index evidence format differs'};$objects[$Matches[2]]=$Matches[1]}
foreach($path in $paths){
 Check
 $bytes=[IO.File]::ReadAllBytes((Join-Path $root $path));$header=[Text.Encoding]::UTF8.GetBytes(('blob '+$bytes.Length+[char]0))
 $sha=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA1)
 try{$sha.AppendData($header);$sha.AppendData($bytes);$obj=[Convert]::ToHexString($sha.GetHashAndReset()).ToLowerInvariant()}finally{$sha.Dispose()}
 if($objects[$path] -cne $obj){throw 'index object bytes differ'}
}
$docs=@('HANDOFF.md','CHANGELOG.md','docs/training-next-handoff-2026-10-09.md','docs/evidence/token-limit-8192-2026-10-09.md')
[void](Git 'add-docs' (@('add','--')+$docs))
$blob=Git 'roadmap-blob' @('hash-object','-w','--path=ROADMAP.md',(Join-Path $task 'roadmap-owned-index.txt'))
$obj=[IO.File]::ReadAllText($blob.StdoutPath).Trim()
if($obj -cnotmatch '^[0-9a-f]{40}$'){throw 'roadmap object format differs'}
[void](Git 'roadmap-index' @('update-index','--cacheinfo',('100644,'+$obj+',ROADMAP.md')))
$names=Git 'staged-names' @('diff','--cached','--name-only')
$staged=[IO.File]::ReadAllLines($names.StdoutPath)
if($staged.Count -ne $paths.Count+5){throw 'staged exact count differs'}
foreach($name in $staged){Check;if($name -notin ($docs+@('ROADMAP.md')) -and $name -notin $paths){throw 'unowned staged path'}}
[void](Git 'doc-whitespace' (@('diff','--cached','--check','--')+$docs+@('ROADMAP.md')))
$raw=Git 'raw-whitespace' @('diff','--cached','--check') @(0,2)
$exceptions=@([IO.File]::ReadAllLines($raw.StdoutPath)|Where-Object {$_ -match '^[^+].*:[0-9]+: '})
if($exceptions.Count -gt 128){throw 'raw whitespace cap128'}
foreach($line in $exceptions){Check;if(-not $line.StartsWith($archive+'/')){throw 'whitespace outside owned raw archive'}}
$identity=Get-Content -Raw -LiteralPath (Join-Path $task 'task-identity.json')|ConvertFrom-Json
foreach($file in $identity.paused){Check;if($file.path -ceq 'ROADMAP.md'){continue};if((Get-FileHash -LiteralPath (Join-Path $root $file.path)).Hash -cne $file.sha256){throw 'paused/user bytes changed'}}
$commit=Git 'commit' @('commit','-m','docs: record reboot completion and persistent GPU initialization failure')
$after=Git 'head-after' @('rev-parse','HEAD','HEAD^')
$hashes=[IO.File]::ReadAllLines($after.StdoutPath)
if($hashes.Count -ne 2 -or $hashes[1] -cne $before -or $hashes[0] -ceq $before){throw 'commit parent differs'}
$tree=Git 'tree-after' @('ls-tree','-r','HEAD','--',$archive)
$treeLines=[IO.File]::ReadAllLines($tree.StdoutPath)
if($treeLines.Count -ne $paths.Count){throw 'committed archive count differs'}
foreach($line in $treeLines){Check;if($line -cnotmatch '^100644 blob ([0-9a-f]{40})\t(.+)$' -or $objects[$Matches[2]] -cne $Matches[1]){throw 'committed archive bytes differ'}}
$remaining=Git 'paused-roadmap' @('diff','--','ROADMAP.md')
$diff=[IO.File]::ReadAllText($remaining.StdoutPath)
if(-not $diff.Contains('+S4-12/S4-15独立CLI资产准备同步登记：') -or $diff.Contains('+2026-10-09 用户明确选择8192')){throw 'paused roadmap hunk differs'}
$empty=Git 'index-after' @('diff','--cached','--name-only')
if(-not [string]::IsNullOrWhiteSpace([IO.File]::ReadAllText($empty.StdoutPath))){throw 'index not empty'}
foreach($item in $prior){Check;if((Get-FileHash -LiteralPath (Join-Path $base $item.path)).Hash -cne $item.sha256){throw 'previous inventory changed'}}
$result=@{utc=[DateTime]::UtcNow.ToString('o');commit=$hashes[0];parent=$before;commit_actual_exit=$commit.ExitCode;archive_files=$paths.Count;archive_bytes=$inventory.bytes;archive_sha_and_git_bytes_verified=$true;old_inventories_preserved=$true;staged_files=$staged.Count;owned_doc_whitespace_exit=0;raw_whitespace_exit=$raw.ExitCode;raw_whitespace_exceptions=$exceptions;index_empty=$true;paused_files_verified=10;roadmap_old_hunk_preserved=$true;new_boot_id='31951b0f-232d-40c2-aec0-d7e7dd5215a6';ssh_recovered=$true;gpu_recovered=$false;saved_artifacts_sha_verified=6;training_repeated=$false;postreboot_validation_started=$false;regression_started=$false}
[IO.File]::WriteAllText((Join-Path $task 'postreboot-final-verification.json'),($result|ConvertTo-Json -Depth 4),$utf)
Write-Output "postreboot_commit=$($hashes[0]) files=$($staged.Count) archive=$($paths.Count) archive_bytes_verified=true index_empty=true gpu_recovered=false"
