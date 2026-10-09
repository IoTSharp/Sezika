#requires -Version 7.0
$ErrorActionPreference='Stop'
$root='D:\source\Sezika'
$task=$PSScriptRoot
$utf8=[Text.UTF8Encoding]::new($false)
$status=@'
当前状态：用户已明确采用8192、保留215/train162/dev53，并批准GPU重置及必要主机重启。8192工程/长度/真实8k前向和唯一24576updates训练已完成，head/report/checkpoint已核SHA保存。首次独立验证因Xid119/154失败，原32回归未运行。NVIDIA reset实际exit6/No devices；既有580.173.02驱动卸载重载exit0、原文件/参数保持，但GPU仍exit6。两原容器已恢复启动，重启前翻译health200/ASR503，不称GPU健康。10:56:29 UTC已请求一次主机reboot/request exit0；SSH有界观察尚未取得不同boot_id或GPU恢复证明。禁止重复reboot/reset/训练；只观察恢复，连通及资源准入后仅接续保存head的独立验证和32回归。用户许可已取得，不重复询问。见[本轮完整证据](docs/evidence/token-limit-8192-2026-10-09.md)。
'@
$path=Join-Path $root 'HANDOFF.md'
$text=[IO.File]::ReadAllText($path).Replace("`r`n","`n")
if([regex]::Matches($text,'(?m)^当前状态：[^\r\n]*').Count -ne 1){throw 'handoff current status seam'}
$text=[regex]::Replace($text,'(?m)^当前状态：[^\r\n]*',[Text.RegularExpressions.MatchEvaluator]{param($m)$status})
$text=$text.Replace('### 2026-10-09 8192固定训练完成；独立验证等待GPU恢复许可','### 2026-10-09 8192固定训练完成；GPU恢复及主机重启已获批准')
$text=$text.Replace('当前许可请求只涉及短暂停这两个服务、准确UUID reset及原配置恢复，不覆盖主机重启或新训练。','以上10:28快照之后用户已明确批准GPU重置及必要主机重启；软件恢复未成功，已发一次reboot并等待新的boot_id。授权不包括重复训练。')
$text=$text.Replace('相同heartbeat阻塞安静，不重复通知/索要许可。','相同heartbeat阻塞安静，不重复通知/索要已取得许可；主智能体已有重启请求时不得重复操作。')
$text=$text.Replace('未停止共享服务、reset或reboot。','该10:28快照尚未停止服务或reset/reboot；随后真实恢复动作见顶部当前状态与新证据。')
[IO.File]::WriteAllText($path,$text,$utf8)
$paragraph=@'
2026-10-09 用户明确选择8192，原215/train162/dev53及全部原字节/账本保持。显式8192工程/长度/H4数值/真实8k前向通过；唯一24576updates/LR0.001/seed17/strict8192/零head/encoder冻结训练完成并保存，231forwards/46.5468512s。首次独立验证payload1/runner127，开始211/215后Xid119/154，完整验证报告不存在、32回归未启动。用户已批准GPU reset和必要主机reboot；NVIDIA reset命令exit6，既有580.173.02驱动重载exit0/原配置保持但GPU仍No devices。10:56:29 UTC请求一次主机重启exit0，当前SSH尚未返回，新的boot_id/显卡恢复待实际观察。不重复训练/reboot/reset或询问已得许可；恢复并核资源后仅继续保存head的独立验证和32回归。工程a5f4d9c/167470a/44b4082，只本地commit；正式质量/校准/封存/AOT/性能仍pending。原1024等待状态为历史，暂停诊断hunk保持。见[8192训练与恢复证据](docs/evidence/token-limit-8192-2026-10-09.md)。
'@
$path=Join-Path $root 'ROADMAP.md'
$text=[IO.File]::ReadAllText($path)
$pattern='(?m)^2026-10-09 用户明确选择8192[^\r\n]*'
if([regex]::Matches($text,$pattern).Count -ne 1){throw 'roadmap owned seam'}
$text=[regex]::Replace($text,$pattern,[Text.RegularExpressions.MatchEvaluator]{param($m)$paragraph})
[IO.File]::WriteAllText($path,$text,$utf8)
[IO.File]::WriteAllText((Join-Path $task 'roadmap-owned-paragraph.txt'),$paragraph,$utf8)
$path=Join-Path $root 'CHANGELOG.md'
$text=[IO.File]::ReadAllText($path)
$text=$text.Replace('等待共享翻译/ASR停止及GPU恢复许可。','用户已批准软件恢复及必要主机重启；软件恢复失败后请求一次reboot，显卡恢复待实际观察。')
[IO.File]::WriteAllText($path,$text,$utf8)
$path=Join-Path $root 'docs/training-next-handoff-2026-10-09.md'
$text=[IO.File]::ReadAllText($path)
$text=$text.Replace('当前等待用户明确许可临时停止共享Hearon翻译/ASR并重置准确GPU，许可前只核交接/证据，禁止GPU工作或自动reset/reboot。','用户已明确批准GPU重置及必要主机重启，10:56:29 UTC已请求一次reboot；软件reset及同版本驱动重载均未恢复GPU。当前SSH尚未重连，先观察新的boot_id及显卡/共享服务状态，不重复reboot/reset或索要已获许可。')
$text=$text.Replace('没有停止它们、reset、reboot或启动新GPU任务。','上述10:28快照之后已按用户许可停止并恢复两容器、执行一次软件reset及同版本驱动重载；两原容器重启前running、翻译health200/ASR503。一次reboot已请求，见新证据；尚无新的GPU训练/验证。')
$text=$text.Replace('1. 先读最新回执、HANDOFF与证据，确认用户是否明确允许临时停这两个服务、重置该UUID并恢复原容器。8192选择不等于共享服务运维许可；相同阻塞安静，不重复索要许可。','1. 先读最新回执、HANDOFF与证据。用户已经明确批准GPU reset及必要服务器reboot；主智能体已发一次reboot/request exit0。有界重连尚未取得新的boot_id；只观察，不重复执行重启或询问该许可。')
$text=$text.Replace('2. 获准后重新核容器/PID/start/argv/父链与实时GPU使用者；若出现其他受影响服务，不扩大操作。短暂停两容器、尝试一次GPU reset、恢复原容器并核健康/资源。不得自动主机reboot、重装驱动或改业务配置。','2. 重连后必须验证新的boot_id、准确GPU UUID/资源、两原容器启动及健康和模型保存物SHA。旧boot的PID/starttick不能冒充重启后的OS身份。若仍无GPU或SSH，记录实际状态并等待进一步恢复，不自动额外reset/reboot、升级驱动或更改业务配置。')
[IO.File]::WriteAllText($path,$text,$utf8)
Write-Output 'latest_reboot_authorization_and_actual_recovery_state_documented=true'
