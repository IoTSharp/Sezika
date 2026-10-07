# 通用中英教师 pilot 候选包

按用户选择的通用文本方向，本轮准备 32 条 AI 起草候选：中英 Choice 各 6 条、Score 各 5 条、Boolean 各 5 条。交付入口为 [候选包说明](../datasets/teacher-general-v1/README.md) 和 [hash manifest](../datasets/teacher-general-v1/manifest.json)。每条包含语言、唯一 family、来源、`origin: AI drafted`、pilot/audit 用途和人工审核待定状态。

Choice 根据短文本做主题或意图分类；Score 给出具体有序等级并限定判断依据；Boolean 只判断文本内明确支持或冲突的陈述。不同语言的基础题使用不同场景，没有把翻译副本分配到新的独立 family。题目与 prompt 已曝光，任何后续翻译、改写、反事实及实体变体须保留原 family、保持 split 隔离，不能充当封存测试数据。

本包提供 32 个单题 `decision-v1` 请求与 32 个 UTF-8 教师 prompt，索引逐项绑定路径与 SHA-256。学生请求身份固定为 `sezika/mmbert-base-independent-s4`；教师 catalog 与真实服务身份独立管理，本轮没有填写未知教师 revision 或编造响应。回答采集格式与实施方对齐为 `question_id/status/selected_candidate_id`，只接受 `answered/abstained`；Choice ID、Score 零起始等级索引字符串、Boolean `false/true` 分别保持固定映射，拒答须为 null。实际响应解析与质量验收属于采集任务。

现有 `Sezika.TeacherTool validate-records` 校验调用后具有教师身份和 request/prompt hash 的记录，并非本包的采集输入。采集工具可读取 manifest 的 `candidates.path/sha256/bytes` 与 64 项 `assets`，先核验原始字节/hash，再读取题文和 prompt。索引没有教师 model/revision、response hash、目标答案或人工审核 ID。

验证范围明确为结构与文件完整性，不是人工金标或模型质量：

| 检查 | 实际结果 |
| --- | --- |
| PowerShell 版本 | 7.6.6 |
| 循环前人工核对 | `-lt/-eq` 比较退出；最多 32 题，候选数及文件数均有限；共享取消与墙钟界限 |
| 极小试运行 | `Build-Pilot.ps1 -MaximumRecords 1 -TimeoutSeconds 10 -ValidateOnly`，exit 0，约 0.111 秒 |
| 完整组装 | `Build-Pilot.ps1 -MaximumRecords 32 -TimeoutSeconds 10`，exit 0，约 0.410 秒；生成 64 个 request/prompt、32 行索引、manifest 与结构报告 |
| 完整结构报告 | [assembly-validation.json](../datasets/teacher-general-v1/assembly-validation.json)，统计与 32 个唯一 family 已核对 |
| 文件复验 | [file-verification.json](../datasets/teacher-general-v1/file-verification.json)，10 秒上限内约 0.312 秒完成 64 个 request/prompt 的 UTF-8 SHA-256、索引对应 hash、请求在 prompt 中的逐字节一致性、32 个唯一 family 及分配核对；起草源与生成器 hash 同时匹配 |

manifest SHA-256 为 `b2ab11e5cabf1967b87e5a06fa9d0e270a1aec0a6df8b8f55e3c670f767fa8fa`，候选索引 SHA-256 为 `0e9852639587417bfabf2ac04f43555cf82dbb03ba57e03b27febbafcd50b0d8`。监督者另从原始文件独立复验67项绑定（起草源、生成器、索引、64项request/prompt）、32个唯一family与六分组统计，并审读每个语言/题型的样例；见[独立核验](evidence/supervision-2026-10-08/teacher-candidates-verification.json)。`.gitattributes`对该精确候选目录关闭换行转换以保留跨平台hash。这些检查不代表教师已回答或答案正确。

未构建或运行 C# parser、未联网、未调用教师、未下载模型、未训练、未读取 sealed_test。`training_admission: pending`、`human_review: pending`、`teacher_called: false`、`quality_evidence: false` 均保留。AI 的结构检查及题文审读不等于人工审核批准。

本轮仅写 `datasets/teacher-general-v1/` 和本文，不改共享文档或 Git。短时 PowerShell 命令同步结束，无后台进程、下载或临时文件；失败回滚只删除生成脚本记录并核实绝对路径归属的本次文件，成功交付物保留。监督者在单独提交前仍须验收 hash 与采集器实际兼容性。
