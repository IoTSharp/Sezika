# 教师提示字符串 ID 合同 v2 准备证据

监督者发现旧教师提示未突出 JSON ID 的字符串类型，本次只准备新的 [teacher-general-v2](../../datasets/teacher-general-v2/README.md) 候选包。保留旧 v1 全部字节，复制相同 32 题请求并修订提示：Choice、Score、Boolean 的 `answered.selected_candidate_id` 一律为字符串；Score 明确使用 `"3"` 而非数字 `3`，Boolean 明确使用 `"true"/"false"` 而非布尔值。提示模板为非答案占位符，输出严格限定三字段，无解释或其他文本。采集器 parser 未修改。

每条新候选绑定 prompt 的新 hash/bytes、旧 prompt hash/bytes、v1 manifest hash 和提示合同 ID/hash；manifest 绑定候选索引、64 个 request/prompt 和来源快照。固定 v1 manifest SHA-256 为 `b2ab11e5cabf1967b87e5a06fa9d0e270a1aec0a6df8b8f55e3c670f767fa8fa`，v1 候选索引为 `0e9852639587417bfabf2ac04f43555cf82dbb03ba57e03b27febbafcd50b0d8`。

| 核验 | 内部 / 外层上限 | 实际结果 |
| --- | --- | --- |
| 1 题新目录生成 | 20 / 30 秒 | PID 72376，exit 0；生成 8 文件，内部约 0.186 秒，runner 3.108 秒 |
| 32 题正式候选包生成 | 120 / 150 秒 | PID 55504，exit 0；生成 70 文件，内部约 0.612 秒，runner 4.140 秒 |
| 独立文件复验 | 20 / 60 秒 | PID 59360，exit 0；约 0.447 秒核 67 项 v1 绑定、69 项 v2 绑定及 v2 manifest；runner 5.325 秒 |

PowerShell 为 7.6.6，未发现或安装新工具。生成前人工核对所有循环的比较退出条件；1 题实际生成成功后才运行 32 题。两阶段都使用明确记录数、文件数、取消/墙钟预算与进度，完整包有 32 个唯一 family，中英 Choice 各 6、Score 各 5、Boolean 各 5。独立复验逐字节比较 32 个 request，逐属性核候选来源字段、题序和 family，核 32 组提示中的带引号候选映射与原请求快照。没有 C# parser 执行或真实模型调用。

| 固定产物 | SHA-256 |
| --- | --- |
| v2 manifest | `c028901b7b380bcb565896d967e5f69f24a57f7038c5161575167f82b479daa9` |
| v2 candidates | `c713b7aada476aa392e02251f20f0b3ae9d484b0654a2827dc0e52699d74e291` |
| v2 prompt contract | `340d2038d8fc6f541d0a1b33ead712fa1e0310cc52f8d58eda2de3a9fa86b9af` |
| v2 generator | `bcaf0cd68d2f7c4f5fb6f6e242ff1fc4d014a9117effdbcb9599d4097f522ded` |

本阶段输出是准备状态，全部为 `AI drafted/audit/pilot`、`human_review/training_admission:pending`、`teacher_called:false`，没有响应或金标。文件检查不构成质量、训练、GPU 性能或人工审核结论，实际 GPU 教师采集由监督者另行执行与验收。

三个外层 runner 均为 `Succeeded/exit0`，`CleanupErrors` 均为空；进程启动时间、命令行、PID 和父链保留于 `.artifacts/teacher-prompt-v2-20261008/processes`。生成器和复验脚本没有启动自己的子进程、后台作业、服务、下载或独占 MCP 服务。结束时精确查询三个执行 PID 及三个 launcher PID，无对应存活对象；没有终止共享进程。1 题试运行包、完整交付物、检查脚本与日志保留为本任务证据，不清共享缓存，不删除旧失败或用户文件。
