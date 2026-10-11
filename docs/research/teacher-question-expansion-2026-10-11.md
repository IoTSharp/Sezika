# 2026-10-11 教师题库扩充与 Score 小批量验证

本轮生成的题库是用于离线教师标注的私有候选队列，不是已经准入正式训练的数据。参数、业务名称和翻译增加题量，但不能把这些改写称为独立的通用推理覆盖。教师标注与学生模型准确率、校准和业务效果分别验收。

当前最新队列为独立的 **v3：16,347 条、1,027 个家族、18 个上游来源的 267 条样本**。v1/v2 及对应教师请求/响应均保留。正式训练准入为 false。

## 最新题库 v3 与上游来源覆盖

资产目录：`C:\Users\mysti\.codex\artifacts\sezika-corpus-20261011-01a12b4c\teacher-queue-v3`。合成题仍为 16,080 条、760 个家族；上游样本为 267 条、267 个家族。Quartz、ARC、恢复后的 phase2 各从已关闭的完整转换结果选 64 条未排除的派生 train 家族；`pilot-all-v3` 的 16 个来源仅选流程样本，共 75 条，每来源最多 32 条，且明确 `SourcePreparationPilotOnly=true`。这 75 条不能称为已完成这些来源的完整转换或正式训练准入。

| 上游来源 | 实际样本数 | 转换范围 |
| --- | ---: | --- |
| allenai/quartz | 64 | 已完成的原 train 转换 |
| allenai/ai2_arc | 64 | 已完成的原 train 转换 |
| dwidlee/systemone-lite-phase2 | 65 | 64 条完整恢复样本加 1 条旧流程样本 |
| AmazonScience/massive | 3 | 流程样本 |
| Anthropic/hh-rlhf | 11 | 流程样本 |
| LocalLLaMA/typed-decisions | 10 | 流程样本 |
| PolyAI/banking77 | 3 | 流程样本 |
| Team-ACE/ToolACE | 3 | 原 unsplit 的派生 train 流程样本 |
| ZefanCai/Open-Jev | 12 | 流程样本 |
| clinc/clinc_oos | 2 | 流程样本 |
| google-research-datasets/go_emotions | 3 | 流程样本 |
| google/boolq | 3 | 流程样本 |
| google/civil_comments | 6 | 流程样本 |
| kaivoss/system-one-270m-data | 1 | 流程样本 |
| nvidia/HelpSteer | 1 | 流程样本 |
| nvidia/HelpSteer2 | 2 | 流程样本 |
| samatv256/jev-decisions-v1 | 13 | 流程样本 |
| stanfordnlp/snli | 1 | 流程样本 |

v3 的 Choice/Boolean/Score 为 7,521 / 6,619 / 2,207，派生 train/development 为 15,083 / 1,264。英文 8,290、中文 8,040、来源声明 mn-MN 3、语言未知 und 14。只有中文与英文合成部分是对应翻译，不能把原来源语言未知样本说成双语。

`source-sampling-coverage.json` 保留每个 corpus manifest SHA、原转换的 pilot 状态、上限和实际题量。`SourceProvenance` 保留原 Dataset、Revision、Path、FileSha256、OriginalSplit 和 Row。原家族及派生 split 放入 `OriginalFamily`、`OriginalDerivedSplit`，`source-family-map.jsonl` 连接完全相同 state 与原来源家族，保留同一来源/家族在不同 corpus 的统一 train split。语义近似和跨来源污染仍需另行审核。新增来源没有产生参考标签冲突；完全重复题仍删除并保留账本。没有读还在写入的主全量转换分片。

`question-full-check-v3.json` 为 PASS：8,400 次独立规则标签检查、16,347 条队列与参考对应、全部资产 SHA、同源家族映射、pilot 范围和 hidden gold 均核实。首次检查沿用了比导入器更严格的 `1e-6` 分布和误差阈值，遇到 1 条上游序列化分布的浮点误差 `1.0000000000287557e-6` 而失败。失败回执 `process-logs/20261011-155239-201-question-full-check-v3.result.json` 保留；检查改为原有 canonical probability 合同的 `2e-5` 后通过，原标签未改、未归一化。

v3 没有新增教师调用。24 条 Score pilot 的题目身份保持与 v2 一致，真实教师证据仍固定引用 v2 请求前冻结的 manifest。

## 已生成的题库 v2（保留历史）

私有资产目录：`C:\Users\mysti\.codex\artifacts\sezika-corpus-20261011-01a12b4c\teacher-queue-v2`。

共 **16,144** 条唯一题目、**824** 个家族，删除 **2,800** 条完全重复的可见题目。合成题有 16,080 条、760 个家族；另从已关闭且 byte/SHA 核实的 Quartz 转换结果中选取原始 train 部分的 64 条、64 个不同 exact family。没有从原始评测集补充训练监督。原始 train 内派生 family holdout 的边界继续保留。

| 题型结构 | 题目数 | 家族数 | 所需事实与规则 |
| --- | ---: | ---: | --- |
| 显式阈值规则 | 6,720 | 480 | 原 12 个领域词汇共用一个阈值判断结构 |
| 日期先后与领先天数 | 1,320 | 40 | Gregorian、ISO 日期、明确的天数等级 |
| 关系传递与路径 | 1,280 | 40 | 有向边、完整性标记、最长路径的 0 至 4 等级 |
| 同等权威证据冲突 | 1,360 | 40 | 同时刻同对象、冲突保留、支持来源计数 |
| 工具路由与字段完整度 | 1,360 | 40 | 显式有限路由表、四个必填字段，不执行工具 |
| 预算余量算术 | 1,320 | 40 | 同单位整数、明确余量等级，不授权支付 |
| 时间区间重叠 | 1,360 | 40 | UTC 同一天、左闭右开区间、重叠时长等级 |
| 虚构文档证据 | 1,360 | 40 | 全部事实写在题面、四项必需命题的支持数量 |
| Quartz 原训练题样本 | 64 | 64 | 保留原来源、原标签和冲突排除身份 |

Choice **7,344** 条，Boolean **6,600** 条，Score **2,200** 条。英文 8,104 条、中文 8,040 条，差额为英文 Quartz 样本。派生 train 14,880 条、development 1,264 条；同一家族不会跨越这两个 split。证据不足的 Boolean/Score 明确 `answerable=false`、参考 `Target=null`，合计 2,296 条，不以均匀概率伪造答案。Choice 提供明确的“证据不足”或“证据冲突”选项。

`questions.jsonl` 不含 `Target`。参考监督保存在独立的 `reference-gold.jsonl` 和 `pilot-reference-private.json` 中；这里的文件名 gold 表示参考账本，并不把合成规则答案或上游标注升级为人工金标准。教师请求仅包含 RecordId、State、Instruction、Type、Candidates。

中文/英文、反事实、否定、事实顺序变换和候选换序使用同一个 family。Choice 换序同步移动参考答案；Boolean 始终保持 false/true 顺序；Score 固定有序等级 0、1、2、3、4，不排列成另一套 ordinal。完全重复的题面通过 content-derived RecordId 排除并写入 `exact-duplicates.jsonl`。

## 本轮实际检查

生成器：`tools/corpus/build_teacher_queue.py`、`question_queue.py`、`question_templates.py`。极小输入先生成独立 24 条 Score 后再生成完整 v2；每次使用独占的 cooperative startup gate 和现有 PowerShell 7 进程 guardian。离线数据处理没有调用模型算子、GPU 或训练程序。

`tools/corpus/question_queue_checks.py` 独立按可见事实重新计算 **8,400** 个参考标签，检查两个语言的结论一致、Choice 位置变换标签映射、Score 的有序等级、Boolean 顺序、family/split、队列和参考账本一一对应以及所有资产 SHA。额外核验日期同日、区间端点相接不重叠、等权威冲突不能作 Boolean 真值。极小队列与完整队列均 PASS。

证据：`question-score-tiny-check-v2.json`、`question-full-check-v2.json` 和 `process-logs/*question*.result.json`，均位于本轮私有资产根。完整题库准备进程 PID 59608、检查 PID 47296 已由 guardian 验证结束，无存活或身份未知的任务子进程。

## 小批量教师调用

用户选择沿用已有教师配置。新调用核实并复用 CC Switch 当前选择的 `sonnet_vip`，离线教师为 `gpt-6.1-sol`、reasoning `max`。网关声明只能证明该网关报告的模型身份，不能独立确认上游实际模型。

Score 试验固定 **24** 条：7 个新增结构各 3 条，另 3 条证据不足样例；参考可答 21、应拒答 3。原 24 条 Choice/Boolean 教师试验由主任务保留，当前 Score 调用是独立证据。最多 5 个逻辑 batch、10 个 HTTP 请求、1,000 秒教师阶段；正式训练准入仍为 false。请求不带私有参考标签，凭据仅在内存，直连失败按该请求使用本机 7890 代理；不变更全局配置、不并行教师调用。

请求前冻结 `teacher-score-call-input-identity-v2.json`，记录队列 manifest SHA 与教师脚本 SHA。HTTP Idempotency-Key 使用请求 SHA，以免原 Choice/Boolean 试验与新 Score 试验批次编号相同而复用另一请求。原始请求、原始响应、失败尝试、逐题拒答/分布、网关报告模型、请求/响应 SHA 和参考一致性保存在 `teacher-score-pilot-v2`。

实际结果：**24/24 校验完成，21 条可答、3 条拒答，与参考规则的可答性及可答题 ordinal argmax 全部一致**。5 次 HTTP 均直连成功，教师阶段 115.641 秒，21 条仅为待人工审核的实验监督候选；没有正式训练准入。教师进程 PID 46752 及其记录的子进程已结束，guardian 闭合 PASS。完整 v2 队列没有进行批量标注，学生准确率没有测量，概率没有校准。

`question-score-teacher-audit-v2.json` 是独立读回检查：5 批可见请求字段、24 个准确题号、私有参考未进入请求、请求/响应 SHA 与逐题记录一致、固定 0 至 4 等级及 3 个 null 拒答均 PASS。网关响应中的模型字段为 `gpt-6.1-sol`；仍只属于网关声明。教师脚本实际 SHA256 为 `82b27a8259460e102c0138445ffac4f200ff0bfe351068c85df0d53bb4ad4ab4`，调用前与独立读回时相同。该检查证明采集证据对应，不能将 24/24 参考一致称为学生准确率。

## 继续训练前的边界

这批题库可以用于审核、分批教师标注和困难题发现。应保留拒答、冲突和教师/原标签分歧，人工检查后再决定监督用途。扩大题量应继续引入独立来源、真实需求与多语言问题；合成模板数量和同源改写数不能替代独立测试题的覆盖。

正式训练仍需逐源权利与目标语义准入、token 长度和资源检查、训练器流式消费与排除账本验收、固定 family split、真实 GPU profile 和独立学生评测。本轮没有训练新模型、租赁服务器或发布数据。

## phase2 历史隔离行的独立恢复

主任务修正已审查的 `staged_v1` Choice 适配后，本子任务仅调用现有转换器，不修改它，并以历史 `full-reviewed-v1\stage-01\quarantine.jsonl` 的准确来源/行号筛选恢复。极小输入先扫描原 Parquet 前 3 行，恢复 1 条，之后完整扫描 240,800 个原 train 行。私有完整恢复目录为 `phase2-recovered-v1`。

历史匹配的隔离行有 75,866 个，其中 32,400 行声明 Stockfish、`staged_v1`；43,466 行仍属于当前此恢复合同不支持的 noul 模式。实际写入 **7,898** 条 Choice，全部保留为 `source_engine_reference`，不是人工金标准。去掉排除账本的 6 条冲突早期记录后，结构上有效 **7,892** 条（train 6,992、development 900），共 6,473 个 exact family。

另外保留 22,656 次完全重复跳过；新隔离账本含 43,466 条非 Choice/标签不适配、1,592 条候选数量或类型不合规、254 条冲突来源行。冲突事件为 6 个，254 是相关新隔离来源行数，二者不是同一分母。未用改标签方式消除原冲突。

`question-phase2-recovery-audit-v1\receipt.json` 独立再读真实原 Parquet，核实 7,898 条记录的 source row SHA、原 state/instruction、Stockfish 声明、参考候选 alias、确定性候选换序后的 Target 和 family。原文件 SHA 为 `3eee02ea543d8d8a5df361d831dc3939dd7225848e483440976b83f6a565c701`。原隔离/恢复/审计证据全部保留。

现有 binder 固定 source rights/assets、manifest 和 exclusions 后，C# `corpus-check` 真实流式读取通过，回执 `phase2-csharp-check.json` 为 `PASS_structural_streaming_only`。Manifest SHA 为 `c6b50184b09fc893780f8548230d9ec7c7d178a6cb144bc3ad7bfede9b76b6cb`，bindings SHA 为 `46e6eab92544c913de6f668c96f19cea24bc78eb9c8044fc7a29a7b7863f01bb`。正式训练、token 长度、GPU 训练仍均未准入，未增加教师批次。
