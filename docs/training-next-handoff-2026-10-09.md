# 修题后8192固定训练与独立验证交接

更新：2026-10-09，Asia/Shanghai。规范工作区 `D:\source\Sezika`，本会话 `01a11f25-335a-7571-a735-7171ace8c3b9` 独占Git/API/GPU。**用户已选择直接8192、原215题保持；工程与唯一固定训练完成，独立验证因GPU驱动故障失败，原32回归未启动。不得再次训练。** 用户已明确批准GPU重置及必要主机重启，10:56:29 UTC已请求一次reboot；软件reset及同版本驱动重载均未恢复GPU。11:24:57 UTC已确认新boot_id和SSH恢复；11:27:35 UTC显卡仍No devices/GSP初始化失败，需服务器管理方处理GPU故障；两容器running、翻译health200/ASR503，保存物六项SHA保持，不重复reboot/reset或索要已获许可。

本轮事实、原始证据与具体恢复范围见 [8192证据](evidence/token-limit-8192-2026-10-09.md)，最新状态见根HANDOFF.md；ROADMAP.md仍为唯一阶段计划。仅本地commit，不push、发布、部署或上传模型平台。此前“等待删题”“strict1024”“训练未启动”是历史状态，由用户8192选择和本轮回执取代。

## 已完成，不重跑

原v3合同/256全账本/实际215分母、24项反例/访问参考、旧v1八项/v2十六项兼容、Linux10.0.12工程已完成。strict1024全215为214通过/1题1037tokens失败，旧证据原样保持。

显式8192 profile及CLI/runtime/feature边界、v4报告encoder manifest SHA绑定本地a5f4d9c/44b4082；215/215新长度通过、最长1037。默认1024及旧报告保持。H4全8192最大误差3.5762786865234375E-07、真实mmBERT完整8192 GPU前向通过，owned峰4813921280bytes/host2741837824bytes、结束归零；8k边界独立预算6GiB，正式实验仍3GiB。

Linux容量首次保存失败原件/tmp保持；statx稳定dev/inode/birthtime及handle/path归属修复167470a、六项真实Linux检查和第二容量保存加载成功。不要重跑历史失败或合成容量训练。

唯一24576 GPU updates、LR0.001、seed17、strict8192、零head、encoder冻结已经完成。46.5468512秒、231forwards、同进程磁盘roundtrip通过；train教师一致118/162、dev27/53、换序7/16，换序选中ID改变6/16。访问总24576，151完整顺序周期+114前缀，每题151或152次；commit回调重放不是直接kernel ID观测，不称128完整epoch或正式准确率。

原数据永久包 `docs/evidence/teacher-repair-20261009-final/data-v3`，manifest `E8E74CE0BBF1A72DA5FCDF4DCAD9D302513DC5DC42CB548BFD21D2569621AEB6`、账本 `55F3226946831C4B9CBD7F6EE10D1194764860C8343A9386CAA40C678CE24EC0`。178复用+37修订、41删除，全256源/state/source gold/family/split保持，实际215=train162/dev53、Choice140/Boolean75。未删超长题；Score无新监督。旧72/768训练、32/256审核、修题/盲审及教师窗口均结束，不重跑或再调用教师。

## 当前阻塞与必要恢复

`stage-validate` payload exit1、runner/SSH真实127，最后日志开始211/215，不能称211题已完成；报错cuMemFree999，Dispose可能覆盖原始错误。完整 `expanded-validation.json` 不存在，原32回归stage不存在。内核2026-10-09 10:10:03 UTC Xid119/GSP45秒超时及Xid154 GPU Reset Required。Linux46身份live0/unknown0只是进程退出；CUDA释放失败、NVML compute apps N/A，不能称最终GPU清理正常。

服务器 `root@211.149.170.42:2222` / `digisyn-gpu01`，GPU UUID `GPU-e786e313-bc92-a63d-a833-f4e48cbafb66`。10:28:20 UTC只读核共享容器仍running：翻译 `digisyn-hearon-hymt2-gguf-translation-1`（PID3055/starttick1542/父2990→2938），语音识别 `digisyn-hearon-gigaam-asr-1`（PID3068/starttick1586/父2982→2937）。完整容器ID、argv、image、策略在证据中。上述10:28快照之后已按用户许可停止并恢复两容器、执行一次软件reset及同版本驱动重载；两原容器重启前running、翻译health200/ASR503。一次reboot已请求，见新证据；尚无新的GPU训练/验证。

1. 先读最新回执、HANDOFF与证据。用户已经明确批准GPU reset及必要服务器reboot；主智能体已发一次reboot/request exit0。已经取得新boot_id 31951b0f-232d-40c2-aec0-d7e7dd5215a6，但显卡仍GSP初始化失败；只观察，不重复执行重启或询问该许可。
2. 重连后必须验证新的boot_id、准确GPU UUID/资源、两原容器启动及健康和模型保存物SHA。旧boot的PID/starttick不能冒充重启后的OS身份。若仍无GPU或SSH，记录实际状态并等待进一步恢复，不自动额外reset/reboot、升级驱动或更改业务配置。
3. 资源准入通过后，仅继续已保存head的独立新进程验证：磁盘加载、重新GPU编码215原题+16训练Choice固定换序=231 forwards，完整CE/逐题/hash及实际回执。保留失败stage，用准确新阶段名记录恢复尝试。
4. 随后原修正32题回归，≤32 forwards/300秒，保留已曝光身份；不作为独立封存或新监督。
5. 核实际exit/资源/清理与结果后只本地分项commit，更新交接与heartbeat。固定实验完整结束后停止新增训练，等待用户；不按dev调参、加训、扩量、LoRA或encoder反传。

已保存身份：encoder manifest `B8FB9CECC0912F034E0FEA184E8AA78871D11533093DCCF4F5AAD74646007551`；report `C6F3AE942FB90CA842FD49AFF7AE6505EB02A71128264EDC2EF08BC323A48905`；head `4205457556736AB07799B462D42C377710E765CB14EDB9AAE508C1558C5E9D64`；checkpoint `4709E847A135265F71C092BB18C66CF291DE87D90970CD8932ADD6FBC7F4264F`。本机独立逐行/CE/访问及hash核验通过，不能代替未完成的新进程验证。

## 执行与保留边界

任务根 `.artifacts/token-limit-20261009-8192`，远端 `/root/sezika-experiments/20261009-repaired-training-8192`；永久镜像 `docs/evidence/token-limit-20261009-8192-final`。完整原始tar本机保留，省略编译缓存的bytes/SHA在inventory列明。原encoder package `/root/sezika-experiments/20261008-real-head-v1/package`仅只读来源；旧扩量root仅bootstrap，不能冒用旧身份。

每阶段重核资源：GPU并发1，正式验证owned≤3GiB、host≤4GiB、free≥5120MiB，每阶段≤384forwards，CLI≤1500秒；SGD≤300秒的唯一训练已结束。旧显存快照不是当前值。C#模型/算子/调度、正式批量GPU；CPU仅tokenizer、合同及小参考。

固定PowerShell7 `C:\Program Files\PowerShell\7\pwsh.exe`，核版本；发现限PATH/明确配置/固定路径。SSH单引号here-string，经 `C:\Users\mysti\.codex\skills\usm-gateshell-ssh\scripts\invoke-ssh-bash.ps1` stdin wrapper传播实际exit。循环/批处理数量和墙钟双界、取消/进度及小输入；登记PID/start/argv/父链，finally仅回收准确任务树，禁止按名称批杀。

专属temp日志按hash保留与空目录非递归清理，不清共享TEMP，不删Linux失败asset、暂停源码、ROADMAP旧诊断hunk或用户文章/研究文件。短命guardian历史CleanupFailed/unknown、reader沿用2048记录及全部失败原件保持，后来absent不补造旧argv。禁止Graphify。正式人审、许可发布、质量、校准、独立封存、新head AOT/性能仍pending/false。

heartbeat `sezika` 每30分钟核实际交接；状态不变安静，仅实质完成/失败、证据损坏或新的用户处理事项通知，周期触发本身不是通知理由。
