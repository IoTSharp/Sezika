#requires -Version 7.0
$ErrorActionPreference='Stop'
$root='D:\source\Sezika'
$utf8=[Text.UTF8Encoding]::new($false)
$path=Join-Path $root 'HANDOFF.md'
$old=[IO.File]::ReadAllText($path)
$timestamp=$old.IndexOf('更新：2026-10-09，Asia/Shanghai。',[StringComparison]::Ordinal)
if($timestamp -lt 0 -or $timestamp -gt 5000){throw 'handoff prefix differs'}
$prefix=@'
# Sezika 监督交接

当前状态：用户已明确选择8192，原215/train162/dev53保持。工程及215/215长度、H4/真实完整8192 GPU前向通过；唯一固定24576 updates训练完成并保存head/checkpoint/report。独立新进程验证失败，最后日志为开始第211/215题，payload exit1/runner及SSH exit127；完整验证报告不存在，原32回归未启动。2026-10-09 10:10:03 UTC内核Xid119/GSP超时、Xid154 GPU Reset Required。**禁止再次训练或自动启动GPU任务；等待用户明确许可临时停止共享Hearon翻译/ASR并重置GPU。** 恢复及资源准入后仅接续保存head的独立验证和原32回归。见[本轮完整证据](docs/evidence/token-limit-8192-2026-10-09.md)。

'@
$content=$prefix+"`n"+$old.Substring($timestamp)
$start=$content.IndexOf('### 2026-10-09 修题后固定训练：新会话已接管',[StringComparison]::Ordinal)
$end=$content.IndexOf('### 2026-10-09 修题/盲审完成：215题候选包，新会话接续训练',[StringComparison]::Ordinal)
if($start -lt 0 -or $end -le $start){throw 'handoff result markers differ'}
$result=@'
### 2026-10-09 8192固定训练完成；独立验证等待GPU恢复许可

本轮8192工程本地提交 a5f4d9c / 167470a / 44b4082，未push。新profile仅改变runtime_max_tokens并绑定encoder manifest B8FB9CECC0912F034E0FEA184E8AA78871D11533093DCCF4F5AAD74646007551；历史1024包/旧v2v3报告不变。215/215长度通过、最长1037；H4完整8192最大误差3.5762786865234375E-07，真实mmBERT8192 GPU前向owned峰4813921280bytes/host2741837824bytes、结束归零。该独立边界≤6GiB，正式215实验仍≤3GiB GPU/4GiB host，CLI1500s/SGD300s/每阶段384forwards。

唯一训练为24576updates/LR0.001/seed17/strict8192/零head/encoder冻结，实际46.5468512秒、231encoder forwards。同进程磁盘roundtrip通过；train教师一致118/162、dev27/53、换序7/16、换序ID改变6/16；train CE1.1844641080609075→0.8185527480586812，dev CE1.10233565996278→1.0354540944099426。实际commit回调及固定顺序重放为151周期+114前缀，每题151或152次，不称128完整epoch或直接kernel ID观测。report SHA C6F3AE942FB90CA842FD49AFF7AE6505EB02A71128264EDC2EF08BC323A48905，head4205457556736AB07799B462D42C377710E765CB14EDB9AAE508C1558C5E9D64；checkpoint及完整身份见新证据。教师一致性不等于正式准确率或质量通过，Score无新监督。

准确新任务根 .artifacts/token-limit-20261009-8192、远端 /root/sezika-experiments/20261009-repaired-training-8192，本会话独占Git/API/GPU。永久镜像 docs/evidence/token-limit-20261009-8192-final 的inventory绑定原始报告/保存物/回执/日志/脚本/源码；完整13,496,320bytes tar本机保留。Linux46登记身份live0/unknown0，但验证CUDA释放失败及NVML进程N/A，不能宣称最终GPU正常回收。10:28:20 UTC只读预检确认Hearon翻译PID3055及ASR PID3068仍running，准确容器/启动/argv/父链与恢复方案已记录；未停止共享服务、reset或reboot。当前许可请求只涉及短暂停这两个服务、准确UUID reset及原配置恢复，不覆盖主机重启或新训练。相同heartbeat阻塞安静，不重复通知/索要许可。

本机当前登记身份及直系子进程补充只读检查matching0/unknown0/unproven_children0，正在观察的launcher/console另后核；历史短命unknown及第一次收口误计当前launcher的失败保持。16份专属编译日志按hash保留，仅非递归移除36个确切空目录，未清共享TEMP/失败Linux临时asset。暂停源码、ROADMAP旧诊断hunk及用户文件保持，仅本地commit。原1024准入、合同afcca10/e808191及旧工程镜像为已完成历史，见 repaired-training-engineering-2026-10-09.md；旧“等待删题/未训练”状态已被明确8192选择与本轮实际训练取代。正式质量、人审、许可发布、校准、封存及新head AOT/性能仍pending。以下各小节记录此前快照，不作为当前在途任务。

'@
$content=$content.Substring(0,$start)+$result+"`n"+$content.Substring($end)
[IO.File]::WriteAllText($path,($content.Replace("`r`n","`n").Replace("`n","`r`n")),$utf8)
$paragraph=@'
2026-10-09 用户明确选择8192，保留原215/train162/dev53及全部原字节/账本。显式8192工程/215题长度/H4数值/真实8k GPU前向通过；唯一固定24576updates/LR0.001/seed17/strict8192/零head/encoder冻结训练完成并保存，231forwards/46.5468512s。同进程train教师一致118/162、dev27/53、换序7/16；仅实验结果，不是正式质量。独立新进程验证payload exit1/runner127，最后日志为开始211/215，完整报告不存在、原32回归未启动。10:10:03 UTC Xid119/GSP超时及Xid154 GPU Reset Required；共享Hearon翻译/ASR仍在，等待用户明确停这两个服务及准确GPU重置/恢复许可。许可前不启动GPU、不自动reset/reboot；恢复后仅接续保存head的独立验证与32回归，禁止再次训练。工程本地a5f4d9c/167470a/44b4082，原1024等待状态属于此前历史；暂停诊断hunk保持，只本地commit不发布部署。见[8192完整证据](docs/evidence/token-limit-8192-2026-10-09.md)。
'@
$roadmapPath=Join-Path $root 'ROADMAP.md'
$roadmap=[IO.File]::ReadAllText($roadmapPath)
$pattern='(?m)^2026-10-09 用户明确选择直接8192[^\r\n]*'
if([regex]::Matches($roadmap,$pattern).Count -ne 1){throw 'owned roadmap paragraph differs'}
$new=[regex]::Replace($roadmap,$pattern,[Text.RegularExpressions.MatchEvaluator]{param($m)$paragraph})
[IO.File]::WriteAllText($roadmapPath,$new,$utf8)
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'roadmap-owned-paragraph.txt'),$paragraph,$utf8)
Write-Output 'handoff_and_owned_roadmap_current_status_updated=true'
