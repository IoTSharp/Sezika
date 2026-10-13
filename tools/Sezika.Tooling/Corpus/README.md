# 语料与教师工具

实现语言为 C# / .NET 10。正式训练、真实教师、对外发布与模型质量不属于本模块的本地编码验收。每个请求都有项目数、行数或字节上限；共享 `Budget` 管理取消、墙钟和进度。原始输入、闭合分片和历史回执保持原字节；新输出使用新目录与新 SHA。

`Sezika.Tooling corpus <命令> --request <typed-json> --output <new-receipt-json>` 提供 prepare、compose、queue、collect、admission、dataset、research、research-check、seed-check、inspect、bind。`--timeout-seconds` 与 `--cancel-file` 属于共同入口。请求与模块回执使用 `CorpusJsonContext`，主入口报告由主智能体接线。

Parquet 与 XZ 由 JIT 的 DatasetTool 解码，依赖方向为 DatasetTool → Tooling。调用 `Sezika.DatasetTool corpus prepare <typed-json>`、`corpus inspect <typed-json>`。`corpus xz-checks <new-owned-directory>` 保存纯 C# XZ 协议夹具与检查回执。没有下载并执行上游 dataset loader、外部 Python/XZ 解码器或脚本旁路。

## 输入与恢复

- `CorpusPrepareRequest` 明确原始根、资产表、权利表与输出根；仅使用显式准入的原训练载体。source / 原 split / revision / license / source-file SHA / source-row / decoded-row SHA 均入记录或冻结元数据。
- NFC、CRLF 规范化、内容去重、冲突排除及 Choice 的确定轮转有明确语义。`exclusions.jsonl` 不可忽略；`records_written` 是物理输出分母，不是正确率或有效训练数。
- 默认每分片最多 50,000 条且最多 512 MiB UTF-8 逻辑字节。派生行最多 4 MiB。去重表是有容量和 probe 上限的磁盘索引，`DeleteOnClose` 仅回收本任务以 CreateNew 创建的索引。
- 失败保存闭合分片、排除、隔离和 partial manifest。最多四阶段恢复；恢复重新核 SHA/字节与原始输入身份，重建去重索引，不修改已闭合分片。未完成的多问题原 row 重放后继续去重，已写物理记录计入 source coverage。
- `RecoverLedger` 和 `RecoverLedgerSha256` 明确绑定 phase2 历史隔离行；不把历史失败回执改写成成功。历史闭合 Python manifest 可通过其原 `corpus-bindings.json` 只读导入；其工具身份保持 `legacy-python-frozen-v1`。
- `CorpusQueueManifest` 分别绑定 questions / private reference 的 SHA 和字节。private reference 不进入 HTTP 请求。原 family / 原派生 split 明确保留，精确共享 state 合并 family，语义跨来源审核仍待完成。
- 教师逐项账本先写 POST 意图，再保留原始响应和 SHA。已完成项独立从原响应与冻结 reference 重验。丢失 annotation 可恢复；没有确定响应的 POST 保持 UNKNOWN，禁止自动重发。localhost / 注入 handler 标为 `SyntheticTransport`，外部 `ActualTeacherCalled=false`，请求分母另列 `HttpRequestsIssued`。
- 参考一致只表示 answerability 与 argmax 一致。分布未校准；human review / formal training admission 不由教师结果自动批准。development / test / calibration / ood 的用途保持 reference-only。

## tools/corpus 逐入口归属

| 旧入口 | 新归属 / 退役范围 |
| --- | --- |
| `prepare_corpus.py` | `Prepare`、21 类来源适配、原始载体读取、隔离/冲突/分片/恢复；Parquet/XZ 由 DatasetTool 注入 |
| `compose_corpora.py` | `Compose`；固定输入 manifest，原元数据和 notice 字节归档，排除和去重，生成新分片身份 |
| `bind_corpus.py` | `Bind`；manifest / exclusions / rights / assets 固定 SHA，明确不准入 |
| `audit_coverage.py` | `CheckResearch` 的来源完整性/原物理行分母；重复镜像和任务适配结论仍来自冻结审核材料，不能由行数推断新题数 |
| `estimate_compute_budget.py` | `Estimate`；三份固定历史回执、相同 run/input/GPU 身份和固定数学场景，不作为当前吞吐/收敛/费用预算 |
| `fetch_question_sources.py` | `BuildResearchPlan` 产生固定 revision/载体计划；实际 HTTP/Range 拉取归 T4 transport，原四来源/日期限定列表退役 |
| `inspect_sources.py`、`inspect_extra.py` | `Inspect`；固定资产 SHA、小样本、私有 raw sample、适配失败保留；没有标签质量或正式训练声明 |
| `build_teacher_queue.py`、`question_queue.py`、`question_templates.py` | `BuildQueue`、`CorpusSyntheticTasks`；七结构、五变体、双语、候选轮转与十二阈值域，或固定 24 Score pilot |
| `question_queue_checks.py`、`test_corpus.py` | `CorpusChecks`：冻结语义、原 SHA、NFC/冲突/轮转、24/21/3、CSV、恢复与资源边界的本地检查 |
| `collect_teacher.py` | `Collect`；明确身份/许可/模型声明，responses 或 chat-completions，原始请求/响应、失败/未知与恢复 |
| `check_teacher_admission.py` | `CheckAdmission`、`Annotate`；split 用途、拒答/分歧、概率维度、pending 审核与非授权语义 |
| `question_teacher_receipt.py` | 固定历史 24/5-batch/路径回执生成器退役，原回执保留；复用原响应 SHA/冻结 reference 重验及 admission |
| `question_phase2_recovery_receipt.py` | 固定 7898/240800 和任务目录回执生成器退役，原独立审计不改；新的恢复由固定 ledger/SHA 与恢复分母检查覆盖，不冒称重跑历史审计 |
| `source-review-choices.py`、`source-review-prepare-arc.py` | `CorpusAdapters` 的 ARC/OpenBook/AFQMC/LCQMC 适配与 `Inspect`；固定日期/固定资产 SHA 的任务配方退役，原证据保留 |
| `source-review-supplement.py` | `Inspect` 的 JSON/columnar/data/CSV/Parquet/TAR/GZip/XZ 小样本能力；fixed29-source 运行定位退役 |
| `source-review-semantics.py`、`source-review-interpret-supplement.py` | 固定目录的审核汇总配方退役；审核规则/结论保持冻结材料，后续 `Inspect` 只报告实际读取结果，不自动批准许可、语义或标签 |
| `bounded_start.py`、`run_guarded.py` | T1b 的 StartupGate / ProcessExecutor；本模块不保留 interpreter 执行器 |

## datasets 与 research 逐入口归属

| 旧入口 | 新归属 / 退役范围 |
| --- | --- |
| `datasets/teacher-general-v1/Build-Pilot.ps1` | `BuildDataset(mode=pilot)`，固定 source manifest/candidates 或原请求，重新构建新版本；原请求、record/family/candidate 顺序保持 |
| `datasets/teacher-general-v2/Build-PromptV2.ps1` | `BuildDataset(mode=prompt-v2)`，明确 candidate JSON string 身份与严格响应格式；精确请求 bytes 不变，新 prompt 与 manifest 新 SHA |
| `datasets/teacher-general-v2-review/Build-Review.ps1` | `BuildDataset(mode=review)`，兼容原 native TeacherTool 与新 corpus collector，固定原始 request/response SHA，pending template 和 evidence 归档；原人工记录不改 |
| `Build-CollectionPlan.ps1`、`Extend-CollectionPlan.ps1` | `BuildResearchPlan`；配置输入 source-seeds + 冻结 HF/GitHub metadata，原 hardcoded additions 改为 seeds 数据 |
| `Check-SourceCompleteness.ps1`、`Summarize-Collection.ps1` | `CheckResearch`；显式 plans/ledgers，独立 SHA/bytes、partial/failed 分母、静态 C# Parquet footer 或 JSONL 物理行数 |
| `Validate-SeedDatasets.ps1` | `CheckSeeds`；固定 Sys1Cal outcomes[0]→True 配对概率和 S1MB 评测载体核查，明确不重算 gold、不校准、不训练准入 |
| `Collect-Sources.ps1`、`Collect-RangedFiles.ps1` | 来源计划由本模块固定；传输/续传归 T4，凭据读取归 T2；本地源码存在不等于真实服务器拉取验收 |
| `Build-UploadBundle.ps1` | 本模块的资产/权利/notice 约束与 T4 publication preview/transport；原固定来源列表是冻结审核输入，不作为新的许可自动批准规则 |
| `Publish-MoarkDataset.py`、`Verify-MoarkDataset.py`、`Finalize-MoarkPublication.py` | T4 的明确 private/public 发布合同及独立读回；原 UI/状态恢复参数和历史回执不改，不宣称本模块实际发布 |
| `Inspect-Moark.ps1`、`Inspect-SavedMoarkSession.ps1`、`moark_dataset_browser.py` | 一次性浏览器/session 探针退役；需要的当前操作由 T2/T4 明确 API/身份合同承担，不读取旧 cookie 作为执行入口 |
| `Resolve-GiteeRepositoryIdentity.ps1`、`Test-MoarkCredential.ps1`、`Use-MoarkRepositoryCredential.ps1` | 一次性固定账户/仓库探针退役；T2 静态凭据 API 与 T4 目标/读回合同保留，跨模块验收由主智能体负责 |

源码测试回执由主智能体在最终树上串行构建与运行。上表跨 T1b/T2/T4 的条目是归属，不是本模块独立完成或实际外部验收的声明。旧脚本原件/历史 fixture 不删除、不原地改写；从当前流程撤销由 T7 的入口分类执行。
