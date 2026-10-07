# 固定离线教师 GGUF 恢复：2026-10-08

**唯一的固定 Qwen3.5-9B Q4_K_M GGUF 已完整恢复，实际 5,680,522,464 字节与 SHA-256 均匹配，核验后才原子发布到指定教师目录。** 本轮只执行资产恢复及进程 / partial 清理，没有构建、启动服务、`pull` 登记、教师 HTTP 调用或生成教师答案。来源工程核查范围为内部 audit collection；训练、再分发和数据人工审核准入没有改变。

## 固定资产与范围

| 项目 | 固定值 |
| --- | --- |
| 来源仓库 | `unsloth/Qwen3.5-9B-GGUF` |
| revision | `3885219b6810b007914f3a7950a8d1b469d598a5` |
| 文件 | `Qwen3.5-9B-Q4_K_M.gguf` |
| 实际字节 | `5,680,522,464` |
| 实际 SHA-256 | `03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8` |
| 交付位置 | `D:\source\Sezika\.artifacts\teacher-tomur-data\models\text\qwen3.5-9b-q4-k-m\Qwen3.5-9B-Q4_K_M.gguf` |

固定 HTTPS URL 为 [该 revision 下的 GGUF](https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/3885219b6810b007914f3a7950a8d1b469d598a5/Qwen3.5-9B-Q4_K_M.gguf?download=true)。小文件源身份与许可证据见 `teacher-source-preflight-2026-10-08.md` 和 `teacher-runtime-readiness-2026-10-08.md`：官方 `Qwen/Qwen3.5-9B@c202236235762e1c871ad0ccb60c8ee5ba337b9a` 的完整 Apache-2.0 LICENSE 已取得并做 AI 来源工程核查。量化产物采用的 base revision 等价性未建立；没有伪造人工审批，`TrainingUseAllowed=false`、`RedistributionAllowed=false`，本轮不把资产下载成功写成输出质量 / 模型校准 / 训练准入验收。

## 小输入 pilot 与预算

专属脚本、ownership、进程结果与日志位于 `.artifacts/supervision-20261008/teacher-weight`。固定 PowerShell 为 `C:\Program Files\PowerShell\7\pwsh.exe`，实测 7.6.6；curl 复用 `C:\Windows\System32\curl.exe` 8.21.0，不安装或重新广域发现工具。

生成循环先核对为 `item -lt 1`、`attempt -lt 2`，同时检查 phase / 整体墙钟和取消；AST 解析通过后，先在同一 `Restore-TeacherWeight.ps1 -Mode Pilot` 复用已核验的固定 README：78,888 字节、SHA-256 `E50F8066E39C38CBC2DA5DD94B9A9A2712B64C4921F107A5ADB4B3CA5B6BBAC0`。同时核对固定 API 小快照的 hash、revision 与唯一目标 LFS 字节 / hash。pilot 未发网络请求或下载大权重，outer PID 70360 / exit 0 / 官方 result 3.414 秒，在 90 秒 outer 上限内完成。

预算为 1 个 GGUF、最多直连与唯一代理共 2 次请求、下载 phase 1,300 秒、整体任务 1,500 秒（25 分钟）、磁盘 8 GiB。整体计时从前一 readiness 子任务闭环的 `2026-10-07T17:35:03.2484872Z` 开始，包含脚本准备时间；不能只从网络请求开始省略准备消耗。初始 pilot 记录仅计自身 phase，下载前用 `overall-clock.json` 明确整体开始时间，原 pilot 回执保留。outer 下载实际上限据整体剩余时间缩短为 1,007 秒，记录于 `download-launch-budget.json`；代理最大 1,200 秒，实际请求 cap 951 秒，保留 45 秒供 hash / cleanup，没有扩展请求数或整体上限。

curl 直连上限 20 秒，失败后仅 1 次 `http://127.0.0.1:7890` 代理；单次连接期限 10 秒、`--retry 0`、HTTPS / HTTPS 重定向、`--max-redirs 3`、`--max-filesize 5680522464`。使用 per-command proxy，不改变全局环境。失败转代理前退避 500 ms；不会在该批次追加第三次请求。

## 实际请求与发布

| 操作 | PID | 启动 UTC | 退出码 | 官方 runner 用时 | 实际结果 |
| --- | ---: | --- | ---: | ---: | --- |
| 小 README pilot outer | 70360 | `2026-10-07T17:42:06.2900701Z` | 0 | 3.414 秒 | 固定小输入核验通过，无网络 |
| GGUF 下载 outer | 90832 | `2026-10-07T17:43:11.7081022Z` | 0 | 183.612 秒 | 唯一资产下载 / hash / 原子发布通过 |
| GGUF 直连 | 52496 | `2026-10-07T17:43:13.6370521Z` | 28 | 13.199 秒 | 连接超时，body 0 字节 |
| GGUF 唯一代理 | 23480 | `2026-10-07T17:43:27.0919418Z` | 0 | 152.927 秒 | HTTP 200，完整 5,680,522,464 字节 |

curl 自身记录代理传输 149.704034 秒，平均 37,945,021 字节/秒；这是本次文件传输记录，不是模型推理 benchmark。下载到 GUID partial 后，完整 SHA-256 核验用时 12.6659577 秒，匹配固定值后才以 `File.Move` 原子发布到目标目录，没有把未校验 partial 当作可用教师资产。

下载 phase 在 `2026-10-07T17:46:11.8870158Z` 完成，实际 179.6871984 秒，包含该 phase 的小输入再次核对、两个请求、hash 和 partial 收尾；整体从前一任务闭环到此为 668.6385702 秒，小于 1,500 秒。outer 随后完成进程收尾，也在实际 1,007 秒 outer 上限内。

原始记录为 `pilot-bfc2a6e784d4.json`、`download-82d4f3a4700a.json`，官方进程记录为对应 UTC 前缀的 `teacher-weight-{pilot,download,direct,proxy}*.result.json`。每个记录包含准确 executable / argv、PID、创建 / 启动时间、launcher 与子进程父链、stdout / stderr 和 cleanup；唯一请求使用 GUID label 精确选取，不依赖历史最后一次结果。

## 取消、诊断更正与最终清理

长进程复用仓库 `tools/Invoke-BoundedProcess.ps1` 的任务副本，加入 `cancel.request` marker 的有界轮询与保留取消状态，其原有 PID / 创建时间 / 完整命令行 / 父链核对和 `finally` 子树清理逻辑保持。执行时仓库 runner SHA-256 为 `92E4D8F983BF0104513A63E0A131F5F2E2E5085940AE73ADE0108DE9AEF80BC5`，任务副本为 `6B231239EB743323050CDF128BA28902B4A8B5E592752A1D10F976C272064889`，来源记录于 `runner-provenance.json`。轮询有最大迭代次数与墙钟上限，500 ms sleep；本次仓库进度计数还包含 CIM 开销，实际下载进度日志为约 45 / 84 / 128 秒，不把它写成精确每 30 秒采样。

任务副本曾保留初始化 `Cancelled` 状态，导致普通直连 curl 28 连接超时的历史 status 错写为 `Cancelled`，并未发生用户 / marker 取消。该次真实退出码、连接失败文字、0 字节和 cleanup 事实均保留，没有重写原始 result。修正将初始状态改为 `Pending`，只有实际 marker 分支保留 `Cancelled`；随后以 `pwsh -Command 'exit 7'` 单个小进程回归，PID 78364、10 秒上限、官方实际 2.456 秒，正确返回 `Failed` / exit 7，cleanup errors 为空。此试运行没有联网或重下权重。修正后任务 runner SHA-256 为 `269A462FB3251941AFF899D75009C6501CC2A09EC55F9EEEDBD7D4F3BB78940E`，见 `process-status-correction.json`。

每个 GUID partial 创建前都登记到 `ownership-82d4f3a4700a.json`，删除前解析绝对路径，确认属于准确目标目录、带任务专属名字并在该登记列表内；finally 最多清理 2 项 / 10 秒，不碰完整交付物、用户文件、其他任务或共享缓存。取消 / 失败 / 超时均只回收该任务树，不按进程名终止。

`verification-summary.json` 在 `2026-10-07T17:50:15.9893989Z` 最终复核 5 份官方进程记录：cleanup errors 全空，记录 PID / 子进程查询无存活对象，登记 partial 为 0，发布后实际长度再次匹配。完整 SHA-256 证据来自原子移动前对完整文件的实际计算，未把 API 的 LFS 元数据当作本地实际 hash。成功模型资产、所有小输入 / 原始诊断、任务脚本和日志保留；本任务资源已回收。

后续 `pull` 登记、CPU native 加载、1 题 / 32 题教师采集、输出质量和训练准入分别由监督者推进；本证据只闭环固定资产恢复。
