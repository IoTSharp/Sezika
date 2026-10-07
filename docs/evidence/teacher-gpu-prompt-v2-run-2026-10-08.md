# v2 GPU 教师 32 题格式试运行与资源关闭证据

2026-10-08，固定 9B 教师在 RTX 4070 Laptop GPU 上完成 v2 的 32 题格式试运行：**32 条格式有效，0 失败、0 拒答**；加上先行 Score/Boolean 单题，共 34 次 HTTP 尝试、32 个不同题目，无重试。完整 32 题采集 manifest 耗时 115.6265689 秒，151 项文件引用的 hash/bytes 审计通过。全部标签仍为 `pending`，`training_admission=false`、`quality_evidence=false`；本结果只证明本轮响应格式及来源绑定通过，不表示答案正确率或正式训练准入。

服务已卸载并主动停止，GPU 后核为 used 324/free 7625 MiB。58 份 runner 清理回执均为空，75 项登记身份及全部 58 个原始 root PID/start 最终无匹配存活。监控最终状态为 `Deadline`，主动停止的 server runner 为 `Failed/exit-1`；这些原始状态保留，下文分别解释。

## 固定身份与执行范围

三份固定输入为 [Score 单题](../../.artifacts/teacher-gpu-prompt-v2-20261008/inputs-score-1/manifest.json)、[Boolean 单题](../../.artifacts/teacher-gpu-prompt-v2-20261008/inputs-boolean-1/manifest.json)、[32 题包](../../.artifacts/teacher-gpu-prompt-v2-20261008/inputs-all-32/manifest.json)。来源为提示包提交 `3ac5ead` 与历史 v1 教师证据 `087bfab`。输入保持 AI drafted/audit/pilot、人工审核与训练准入 pending；输入包没有答案或金标，真实响应另存不可覆盖的结果目录。

教师为 `unsloth/Qwen3.5-9B-GGUF@3885219b6810b007914f3a7950a8d1b469d598a5`，catalog `qwen35-9b-q4km`、API model `qwen3.5-9b-q4-k-m`。Q4_K_M GGUF 为 5,680,522,464 bytes，固定 SHA-256 `03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8`；来源及许可工程核查见[运行准备](teacher-runtime-readiness-2026-10-08.md)和[权重恢复](teacher-weight-restore-2026-10-08.md)，人工训练/再分发审签仍未完成。

复用 task-local Tomur CUDA 13.2 bundle 与 `gpu_layers=999` config。host DLL SHA-256 为 `c1ec451de366e24c48ff524af840fcded2e400e5c49fe938153de23830467833`，TeacherTool DLL 为 `ccf65714e4d3e3ee853fbe806ece3d2e44f12e4c18ec6bc442428d285b7d3557`。Tomur 源码 HEAD 已核为 `b07ae41450a5832e790eaac1bcdad61531a6a0e0`；复用 host 的构建身份仍为 `32675f8`，没有改写成最新源码构建。本阶段未重新构建、下载或重哈希大权重/native 库。教师 native runtime 只在外部离线工具边界，Sezika 核心保持 C#/静态 Driver 推理。

监督者另行明确授权 900 秒实卡窗口，UTC 开始 `2026-10-07T20:19:24.5788344Z`、截止 `20:34:24.5788344Z`，最多 34 次 HTTP 尝试。独占结果根为 `.artifacts/teacher-gpu-prompt-v2-20261008/simple-run-20261007-2019/`。直接使用已有 bounded runner 逐项启动服务、monitor、collector，并复用独立 20 秒 HTTP 卸载小脚本；未运行被禁用的复杂 harness。完整事实以该目录中的原始回执及审计 JSON 为准。

## 实际结果与审计

| 实际采集 | PID | HTTP尝试 / pending / failed / abstained | 完成manifest耗时 | prompt / completion tokens |
| --- | ---: | --- | ---: | --- |
| Score1 | 99968 | 1 / 1 / 0 / 0 | 115.5496554秒，包含冷模型加载 | 533 / 18 |
| Boolean1 | 3620 | 1 / 1 / 0 / 0 | 3.9836969秒 | 497 / 17 |
| full32 | 73872 | 32 / 32 / 0 / 0 | 115.6265689秒 | 16213 / 562 |

**共34次实际HTTP尝试、32个不同题目，无retry**；两个tiny是随后full32内同题的额外尝试。两个tiny严格返回字符串 `"3"` 与 `"true"` 后才启动full32；full32中这两题标签相同，只表示本次一致性。全部34响应通过三字段JSON字符串ID、实际请求、来源与hash/bytes合同，保留pending/human review required，不能当作答案正确率。末题progress为115.608秒，完整manifest为115.6265689秒，两者明确区分。

新server PID72812、start `20:19:27.2958606Z`、parent28088的新日志实际记录RTX4070 Laptop、CUDA0 model buffer与33/33层offload。monitor PID100776共49个样本全部WithinLimits：GPU最大5953MiB、最小free1996MiB，最大观测working set5,696,294,912 bytes、最小系统free RAM16,161,173,504 bytes。monitor最终 `Deadline/538.410秒`、StopRequested=false：预留70秒逻辑提前停止下一轮采样，结束发生在full32全部HTTP完成之后；不称为Done，也不将资源采样当作benchmark。

完成后卸载PID67836/exit0，实际响应session.loaded=false/busy=false；核PID/start/全命令/parent后主动停止单一server。server runner `Failed/exit-1` 是已记录的主动关闭，保留原回执，不称自然成功；其cleanup为空。显存后核PID8280/exit0保存used324/free7625MiB，与本次最早观测一致，随后明确释放GPU给监督者的其他任务，本阶段不再查询GPU。

CPU审计先1题再34条：PID34984/72348均exit0、cleanup为空，完整审计记录0.3770384秒，核9+9+133=151项引用hash/bytes；每个full32包上限133。审计保存原请求、prompt、实际HTTP body、原响应、严格标签与tokens，报告位于 `full-audit.json`。full32 manifest SHA-256为 `5151fa98f0496e76c4a9b6cd64f6230792f078e695aae7311e41300b556093c2`；records54,198 bytes、SHA-256 `deb5d8ec8e2c8f1d3f772c42cde1bde9f6141e975d4b20e942b52e1ab09a08e4`。

收取所有工具会话final，58份runner报告的cleanup均为空（含49次monitor NVIDIA查询）；对75项已登记身份先Get-Process按开始时间筛选，再只对匹配身份做精确CIM，0个匹配本任务存活对象。原回执、短命子进程的argv/开始时间与runner观察限制原样保留；证据在 `process-and-resource-closure.json`。旧日志读取曾因ReadAllText共享模式拒绝，在未发送Boolean前改为共享读取完成门禁，没有重试教师。成功交付物与所有历史失败保留，未清用户文件或共享缓存。真实GPU采集及格式修复已验证；人工正确性审核、正式训练准入、模型质量与训练仍未完成。

原阶段最后身份closure在UTC20:34:04完成、位于20:34:24截止内；文档补记至20:35:15，超过原边界，如实保留。监督者随后另准许纯CPU五分钟窗口20:35:15–20:40:15，未重跑已通过数据审计，补核全部58份runner的原始root PID/start（包括CIM观察前已退出的短命查询）并记录argv；全部root开始时间匹配存活为0。没有新增GPU/HTTP/build；回执见 `cpu-closure-followup.json`，不把后续CPU文档时间并入已完成GPU采集窗口。

## 历史准备及未采用的编排草稿

最初准备阶段没有启动模型、服务、GPU 查询或教师 HTTP。脚本位于 `.artifacts/teacher-gpu-prompt-v2-20261008/harness/`；`Invoke-Job.ps1` 经已有 runner 启动任务，`Unload.ps1` 为限时 loopback 卸载操作，`Invoke-GpuV2Run.ps1` 草稿编排 Score1→Boolean1→full32。Monitor V2 旧文件保留，新运行目录副本仅调整独占写目录，绑定源 SHA-256 `d8615309caa1abdd67c95577f1528ef99641ed5d6fc27dfe079085aa1ad2dbf5`。

独立审查发现五项：进度成功流污染 job 返回值、cleanup 预算未共享、Process.Start 与登记之间的失败窗口、停止后缺完整 runner 子树回执核验，以及首次调用失败时 teacher_called 失真。成功流改为 Write-Host；调用状态改为记录启动次数，启动后但尚无完成记录时 teacher_called=null。其余三项生命周期缺口未验收，因此 `Run` 模式已禁用；实际采集改用上述逐项 bounded runner。没有放宽 parser 或正式准入。

无模型 Prepare tiny 实际通过：PowerShell 7.6.6、PID33764、外层45秒、exit0、内部约0.189秒、runner约6.870秒、cleanup为空；零HTTP/GPU，只证明固定文件和逻辑门禁。禁用 Run 的最终保护随后加入，tiny 不能证明 server 生命周期或停止测试。原8分钟准备阶段到界后停止，由监督者另批 UTC20:13:54–20:18:54 修核窗口，保留草稿并转交简单方式。

Prepare 身份记录位于 `harness/prepare-processes/`、输出位于 `harness/prepare-20261007-201357-191/`。旧输入、旧脚本、失败、全分母与成功交付物均保留；未删除用户文件或共享缓存。历史准备结果不作为教师质量、GPU 性能或训练证据。
