# 租用 GPU 实验交接

2026-10-09，Asia/Shanghai。本会话 `01a12099-57a1-7bf0-bd69-7d87e8576d61` 已完成用户授权的新机固定实验。创建时间北京时间20:22，四小时租期截止2026-10-10 00:22，提前完成目标2026-10-09 23:22。完整模型于21:17:08保存到本机并核SHA；所有远端实验阶段及结果下载于21:18:14结束，本机独立证据核验于21:22:31完成，10/10 PASS。

本机模型：`D:\source\Sezika\.artifacts\rental-training-20261009-01a12099\trained-model`，包含encoder、tokenizer、8192 manifest、新head、checkpoint、训练report与SHA清单。新训练产物来自租用机；冻结encoder/tokenizer由上传前已核的本机完整资产补齐，所有字节重新核验。任务根保存连接和known_hosts，认证仅经内存/专属askpass；不把登录标识或本机SSH argv镜像进Git。

新机为Ubuntu24.04.1 x64/glibc2.39、RTX5090、driver610.43.02，GPU `GPU-c582dc42-1fed-0727-5a1e-dc57f5e314a4`，boot_id `0e74f433-d83d-44bb-8063-56c2e7bbea6f`。远端根 `/root/sezika-experiments/sezika-rental-20261009-01a12099`。475文件完整上传包SHA `1ff848a6fdaec54a10747672ed500f1f07d5426701806bf9206153ef23dc3434`、1,437,381,120 bytes；包、逐文件、依赖、数据合同和tiny GPU均通过。

唯一固定24576updates/LR0.001/seed17/strict8192/零head/encoder冻结训练35.2160222秒、231 forwards；新进程独立验证28.9658498秒、231 forwards/fresh_disk_outputs_matched；原32回归8.6122972秒、32 forwards。train教师一致118/162、dev27/53，换序7/16/selected ID改变6/16；原32 Boolean8/10、Choice4/12、Score0/10，Score无新监督。均不构成正式质量通过。

原失败pipeline `42feeb2e` 在合同runner初始化exit64，未执行payload或GPU；原具体失败行仍未知。保留原包、失败目录和回执，以单独SHA绑定v2 overlay将btime读取短窗改为单调3000ms/最多1024行；tiny `/usr/bin/true`及新pipeline `bd304689`通过。原训练目录一经创建不能重复运行。本机结果解包最初丢失`.artifacts`前导点，原目录保留，从相同SHA tar重新安全解包到 `results-07c97e181aa8-verified-layout`，2文件tiny探针通过。独立reader最初按源行序对比报告train→dev顺序，已按唯一record_id核全部215题，保留82处旧错序诊断和固定sample_visits顺序重放；原报告、数据、模型未改。

新pipeline本机守护Succeeded/exit0/live0/unknown0，五阶段Linux回执完整，正式三阶段资源清理返回且owned归零、结束free32149MiB。旧下载监护原unknown1历史保留，追加精确核验21登记身份：20已退出、1被无关MCP复用；当前旧任务owned live0/unknown0，没有终止无关进程。两个子智能体完成上述进程审查及独立验收，root保持唯一SSH/GPU/Git调度。

永久证据见[evidence/rental-training-2026-10-09.md](evidence/rental-training-2026-10-09.md)，95份远端原字节镜像与SHA inventory；新head/checkpoint和完整weights留在ignored外部模型目录，本机含连接信息的原argv回执只保留任务根。head/checkpoint与旧确定性实验自然同SHA，新任务/root/GPU/PID/report绑定证明实际新训练，不能以同SHA误判为旧输出复用。

训练、下载和回归已停止，其他ROADMAP暂停。不得自动扩量、加训、按dev调参、LoRA、encoder反传、教师调用或重复旧机恢复。正式人审、许可发布、质量、校准、封存、新head AOT/性能保持pending。PS7、Bash单引号here-string/stdin wrapper、循环项目与墙钟双界、取消/进度/backoff、准确进程身份及精准清理规则继续适用；禁止Graphify。仅本地分项commit，不push、发布或部署。