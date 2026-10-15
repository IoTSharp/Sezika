# 2026-10-12 离线交付历史证据与配置合同

本页保留2026-10-12准备器的历史证据与旧配置语义，下列脚本和旧Specification不能作为当前执行入口。现行流程见[C#工作流](tooling-current-workflow.md)：先独立核完整corpus/政策/严格长度和资产身份，再用corpus bind、bundle build/freeze、task prepare及具名运输操作组合固定输入，最后由resident绑定独立profile与四文件断点。各入口使用自己的类型化请求；task prepare只生成配置，不能独自代替资产、严格长度或GPU准入。原JSON骨架与数值只说明旧合同，不作为现行请求或新验收。

`tools/repository-training/Prepare-FullCorpusDelivery.ps1` 只读核验声明的资产，生成私有训练输入、运输清单、待绑定计划和准备状态。它不连接服务器、不租机、不训练、不调用教师、不发布或复制数据，也不修改现有输入。全部声明文件的大小与 SHA 都通过才生成完整运输清单；失败输出和已检查的文件保留为部分证据。

本轮使用 `CorpusRunPlan`，不用旧 `New-TrainingTask.ps1` 的最大一百万条、48/17615 更新、旧 commit 或旧 GPU 合同。全量指最终固定派生 corpus 中没有被排除且 `Split=train` 的全部记录。development、test、calibration、OOD 不作监督，拒绝和排除账本保留。`FormalTrainingAdmitted=false` 是实验状态，不自动表示所有来源禁止训练；来源政策、严格长度、GPU 工程及模型质量分别验收。

## 输入配置

配置为 JSON 对象，`SchemaVersion=1`，以下字段显式固定：

| 字段 | 内容 |
| --- | --- |
| `RunId` | 独立的新任务 ID，小写字母、数字和连字符，3–64 字符 |
| `SourceCommit` | 最终代码提交；尚未提交时为 null，保持 Pending，不能填旧提交冒充新工具 |
| `DatasetCommit`、`ModelCommit` | 本轮固定的来源仓库提交；当前已验收来源分别为 `d19515802d31310b4050226b51ab85c60f130d42`、`e2e5bc97d62dfbd4e2bbd070542324bb544e6b32` |
| `CorpusRoot`、`CorpusManifestSha256`、`CorpusBindingsSha256` | 最终关闭的完整派生语料及 manifest/bindings SHA；不能使用正在写入的 shard 或 pilot |
| `SourcePolicyPath`、`SourcePolicySha256` | 本轮来源政策原件；最终 corpus 明确命名的 `source-scope-policy.json` 与 `source_notice_bindings` 或 bindings 的 `SourcePolicySha256` 必须绑定同一 SHA，其他路径碰巧同 SHA 不作为政策绑定 |
| `LengthReportPath`、`LengthReportSha256` | 完整 `CorpusLengthReceipt`；缺失、部分扫描、未解决错误均不能成为长度通过 |
| `BaseModelRoot` | 已验底座 model checkout，独立于旧训练产物 |
| `ModelReadbackReceiptPath`、`ModelReadbackReceiptSha256` | 固定的 `source-assets-readback.json`；复核其准确模型 commit 和 root，以及前 `base_files` 项实际底座字节 |
| `ToolBundleRoot`、`ToolBundleManifestSha256` | 由既有 `New-ToolBundle.ps1` 冻结的 Linux 自包含工具包及 `files.sha256` SHA |
| `SourceRoot` | 本机准确源码路径；对 `engineering-sources.json` 声明的全部文件复算大小与 SHA |
| `BackwardManifestSha256` | 与本次冻结 C# / PTX/ABI 对应的反向 kernel 身份；绑定 profile 时必须提供 |

所有 SHA 必须为 64 位小写 hex，commit 为 40 位。代码、模型、tokenizer 和来源政策各有独立身份。模型仅使用固定底座 encoder，head 从零初始化；不把历史 run 的 head、checkpoint 或 optimizer 误当新初始化。底座 notices 与许可证原件随底座完整资产保留，不能用 encoder 的许可替代整体许可审查。

若只能提供初始已提交快照，可填准确基线 `SourceCommit` 并设置 `SourceCommitIsFinal=false`；输入报告将它单独列为 `SourceBaselineCommit`，固定训练草案的 SourceCommit 保持 null，直到最终工具、代码和门槛验收后由主任务给出准确最终提交。准备器自己的源文件 SHA 与配置 SHA 单独记录，不能借基线提交宣称新工具已包含在远端代码中。

配置骨架如下。尖括号字段必须替换为主任务已经复算的真实固定身份，示例本身不能作为已验收配置执行；缺失长度和实例信息明确留空：

```json
{
  "SchemaVersion": 1,
  "RunId": "full-corpus-new-instance",
  "SourceCommit": null,
  "SourceCommitIsFinal": false,
  "DatasetCommit": "d19515802d31310b4050226b51ab85c60f130d42",
  "ModelCommit": "e2e5bc97d62dfbd4e2bbd070542324bb544e6b32",
  "CorpusRoot": "<fixed closed corpus root>",
  "CorpusManifestSha256": "<actual manifest SHA256>",
  "CorpusBindingsSha256": "<actual bindings SHA256>",
  "SourcePolicyPath": "<fixed original source policy file>",
  "SourcePolicySha256": "<actual source policy SHA256>",
  "BaseModelRoot": "<verified local model checkout>",
  "ModelReadbackReceiptPath": "<fixed source-assets-readback.json>",
  "ModelReadbackReceiptSha256": "<actual readback receipt SHA256>",
  "LengthReportPath": null,
  "LengthReportSha256": null,
  "ToolBundleRoot": null,
  "ToolBundleManifestSha256": null,
  "SourceRoot": "D:/source/Sezika",
  "Epochs": 1,
  "MaximumTokens": 8192,
  "LocalArchiveCopies": 0,
  "NewInstance": {}
}
```

`Epochs` 默认 1；`Seed=17`、`MaximumTokens=8192`、`CheckpointEvery=128`、`ValidationPerSource=32`、`EncoderRate=0.00001`、`HeadRate=0.001`、`WeightDecay=0.01`、`MaximumOwnedBytes=25769803776` 可显式指定并按 C# 工程合同检查。`TrainRecords` 只从完整严格长度回执取得，`GlobalUpdates=TrainRecords×Epochs`，没有用样本上限替代全量分母。分阶段 `StageUpdates` 不改变全部 epoch 的固定计划。

工具要求 `PASS_full_corpus_strict_lengths_only`，manifest、bindings、tokenizer、最大 tokens 和全部 physical/excluded/accepted/train/development 分母一致，Rejected=0；完整 TrainingBySource、最大训练题身份与排除账本 SHA 同时核对。真实 C# 派生流程将 `FailureLedgerSha256` 绑定完整 `exclusions.jsonl`，不要求另外生成空的 `length-failures.jsonl`。全量扫描后的显式排除应另建派生版本，绑定新 manifest/bindings 与新完整长度回执；原数据和原 FAIL 不覆盖。

新长度报告带 `CompleteScan`、`SourcePolicySha256`、`ScanCompletionSha256` 时，三项必须齐备；CompleteScan=true，政策与 corpus bindings 一致，扫描 SHA 与 bindings 的 `LengthScanSha256` 一致。准确命名的 `length-scan-complete.json` 原 bytes/SHA 一起进入运输清单，其真实 C# 状态必须恰为 `PASS_complete_strict_scan_explicit_exclusions_private_only`；扫描身份位于 `Inputs`，不能要求扫描另有顶层 `CompleteScan` 字段。准备器核父 manifest/bindings/原排除账本、完整分母、family 隔离、连续分段引用、父分片身份和派生 `length_admission`。长度报告三个排除分类为零时，C# 序列化可能省略对应字段，按零核对。旧 synthetic 回执仅在 `-LocalFixture` 下接受，生产入口不接受 PASS 前缀或夹具替代真实完成。

## 执行和空间

先以独立、明确标为 `fixture-` 的微型资产使用 `-LocalFixture` 检查。fixture 永不成为真实训练准入。真实调用使用 PowerShell 7，经 `tools/rental-training/Invoke-BoundedProcess.ps1` 和协作 gate 登记进程身份；gate 必须在工作区 `.artifacts/repository-training-*`，日志和生成的私有交付物可放本轮 artifact root。`SEZIKA_START_GATE` 由 guardian 写入 go 后才开始文件核验。

```powershell
& tools/repository-training/Prepare-FullCorpusDelivery.ps1 `
  -SpecificationPath '<fixed-delivery-spec.json>' `
  -OutputDirectory '<fresh-private-delivery-directory>' `
  -MaximumFiles 8192 -MaximumBytes 137438953472 -MaximumSeconds 1800 `
  -CancelFile '<current-task/cancel.request>'
```

上面只是入口格式，实际调用必须放入 guardian，不应直接运行无进程归属的长任务。最大 4,096 shards、128 个训练来源、2,048 个来源元数据绑定、600 个工具包文件；总文件预算 8,192，累计读取字节 128 GiB，单文件 8 GiB。逐 MiB 核 SHA，同时检查取消、EOF、文件大小及写入时间；两个秒数进度间隔不额外启动进程。路径不允许绝对/越界/链接，也不扫描盘符、用户主目录或共享缓存。任何超限、缺文件或 SHA 不一致均保留 FAIL，不能以已完成文件数报告全部 PASS。

工具生成清单而不复制完整输入。`LocalArchiveCopies` 默认 0，`LocalReserveBytes` 默认 1 GiB；将来需要本机归档与解包共存时显式设 2，并按全部输入实际 bytes 检查当前输出卷剩余空间。`ServerReserveBytes` 默认 4 GiB；新实例数据盘最低估算为输入、两份完整 checkpoint、最终完整 package、输出归档和余量，checkpoint 按 encoder 加小型 head 的保守 FP32 权重估算三倍（权重及两份 AdamW moment）。这只是准入估算，仍须新实例只读预检确认实际剩余容量与租期。

所有输出为私有训练资料。`transport-manifest.json` 的 PASS 仅表示全部声明字节/SHA；它不代表许可、长度、GPU、质量或公开发布通过。

## 新实例与 profile

尚无新 GPU/SSH/期限时，`NewInstance` 可省略或为空，草案 `ExpectedGpuUuid=null`。不使用旧实例默认值，不在草案中填虚假 UUID，也不购买或恢复旧实例。

实际新实例取得后显式提供 `NewInstance.GpuUuid`（C# 已观测的 32hex）、Linux 上 `CorpusRoot`、`BaseModelRoot`、`LengthReportPath` 与 `LeaseEndUtc`（UTC Z 格式）。运输不得修改 manifest、bindings、shard、政策、tokenizer 或长度回执字节；远端另外逐文件核 SHA 后，按已确定 Linux 路径冻结计划。由草案得到的计划字节必须与 `profile-corpus` 使用的计划完全一致；只要路径或计划改变，profile SHA 就不能沿用。

`PreflightReceiptPath/Sha256` 指向新实例只读回执，包含实际 `GpuUuid`、`GpuName`、`DriverVersion`、`TotalMemoryBytes`、`FreeDataDiskBytes`、`ComputeUsers`、`BootId`。要求当前 UUID、RTX 5090、Driver≥12080、显存≥30 GiB、没有 compute 使用者，且真实 data disk 容量足够。回执身份由主调度核新实例和新租期；本工具不收集 SSH 凭据或执行连接。

新实例先经 Linux guardian 探针、实际工具依赖、逐文件 SHA 和 tiny 验收，再执行 `profile-corpus`。之后提供 `ProfileReceiptPath/Sha256` 与同目录 `resources-reclaimed.txt`。准备器核本轮 run、完整七项 InputIdentity、当前 UUID、最大完整序列、非零实测耗时和资源释放标记；不复用旧 48 更新 profile。跨租赁实例恢复仍由 C# 显式 stage UUID / 新 profile / 完整 checkpoint+cursor 合同控制。

有 profile 后给出全 epoch 与当前 stage 的估算：`4×MaximumStepSeconds×updates + 600 + 120×预计保存数`。`RemainingPhaseReserveSeconds` 默认 1,800，独立验证、保存运输等必需阶段另留实际预算；总期限不足保持 Pending，不降低全局分母或自动续租。

准备器始终将实际 C# 远端运行准入标为 NOT_RUN，并保持 `TrainingReady=false`。`LocalDeliveryReady` 仅表示离线资产、完整长度、源码工具包与本机容量门槛齐备；不代表新实例已启动、完整训练已完成或准确率已提高。实际 GPU 与最终固定全量训练由主调度唯一启动，独立验证和完整模型保存各保留自己的回执。

## 输出与失败

新目录保存 `training-inputs.json`、`transport-manifest.json`、`corpus-run-plan.pending.json`、`readiness.json`。记录完整训练分母、初始化方式、各输入 SHA、具体 Pending、空间和耗时估算。发生失败时保存 `failure.json` 与已验证文件列表，保留原 guardian stdout/stderr/result；失败目录不删除重建。只精确清理本任务已核实退出的进程和临时 gate，交付物、旧模型、旧回执和用户文件均保留。

## 早期离线证据（保留历史）

2026-10-12 的 `question-delivery-fixtures-v3/receipt.json` 为 `PASS_ten_synthetic_offline_delivery_fixtures`：缺失长度、完整长度、部分长度、分母错误、encoder 改动、越界 notice、政策未绑定、扫描政策不一致、同 SHA 政策别名、基线提交十项均通过预期结果检查。夹具声明为 synthetic，未运行 GPU、教师或网络。准备器源文件 SHA 为 `eda7d006322cc2aab2fbe06da652a26080d7504dccaecdc2aea6ea97c4665a35`。原 v1 失败、v2 通过及其输入和 guardian 回执均保留。

真实原始语料准备版本位于 `C:/Users/mysti/.codex/artifacts/sezika-full-training-20261012-01a12b4c/question-full-delivery-pending-v1`。此次完整复核原始 `combined-corpus-v1` 的 353 个 shard 与完整 18 个底座文件，连同声明的来源 notices、政策原件与身份元数据，共 421 个运输文件、3,854,272,286 bytes，状态为 `PASS_all_declared_transport_bytes_SHA_only`。原始语料 manifest 的 physical 记录数为 16,276,841；它不表示严格长度后可训练记录数，`TrainRecords` 和 `GlobalUpdates` 仍为 null。

输入配置 SHA 为 `fc0944e72e05fcbc30c62d039eae844522521c5a6c3bfabacca6f4172ad40479`。模型来源 readback SHA 为 `febe2486c85bbc247842cb0ed78391a7efa1ecdf23312dde8fce16e065c963cd`；底座 encoder SHA 为 `6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528`，tokenizer SHA 为 `197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356`。初始源码基线 `275d064f37145a7a31962bae4d30af2aa1cf09aa` 单独保留，最终 SourceCommit=null。

这版离线 Pending 为最终源码提交、原始 corpus 尚未绑定本轮政策、完整严格长度回执和冻结 Linux 工具包。实例 Pending 为新 GPU UUID、固定 Linux 输入路径、只读 preflight、新租期、同计划实测 profile 与远端 C# 准入；ExpectedGpuUuid=null，LocalDeliveryReady=false，TrainingReady=false。完整派生 corpus 后续另建版本，不能覆盖这版原始证据。

本次只生成清单，无输入复制与归档，输出卷当时剩余 37,054,619,648 bytes，所需本机余量 1 GiB。服务器最低容量估算为 22,895,093,082 bytes，须由真实新实例复核，当前没有全量耗时估算。guardian 回执 `process-logs/20261011-193425-542-question-full-delivery-pending-v1.result.json` 记录 PID 4588、启动时间 `2026-10-11T19:34:26.329017Z`、父 PID 77256、Succeeded、14.897 秒；最终观察完成、已验证存活进程 0、未知进程 0，专属 gate 已清理。日期为 UTC，对应本地 2026-10-12 凌晨。

## 最终离线交付结果

最终版本为同一私有任务根的 `question-full-delivery-final-private-v2`，保持早期 pending-v1 原件不变。来源与完整长度准入后，固定 17 个来源的 train 14,592,720、development 1,619,168；Epochs=1、GlobalUpdates=14,592,720。最终代码提交为 `d925f1c51dfe172e6f5b4ccb97408a5281440bf6`；准备器 SHA 为 `730aa214c7b3b58bf6c1a2fd7e537584bf772d6b2187c741f14bae08782c2da2`，19 项夹具涵盖真实 C# 扫描 schema、账本绑定和省略的零字段。

唯一一次最终真实 helper 调用核对 877 个运输文件、4,039,014,600 bytes，包含派生 corpus、父合同、固定底座及 Linux 工具包；含配置等累计读取 4,039,055,287 bytes。helper 墙钟上限 900 秒，guardian 上限 930 秒；实际 helper 14.089 秒、外层 17.153 秒。最终 `LocalDeliveryReady=true`、`OfflinePending=[]`，`TrainingReady=false`、`FormalTrainingAdmitted=false`，GPU/完整时长字段保持 null。仅剩六项实例门槛：新 UUID、固定 Linux 输入路径、只读预检、新租期、同计划真实 profile、远端 C# 实际运行准入。

最终配置 SHA 为 `2bd681365e9ba5e13b6bb70f3bc2c0f5ebcdf158f22e81dc82bbfc51110b2439`；`training-inputs.json` 为 `bcf746e859502c5c0a50551ea155bf5eadc3624eaee29cdae70ddd2eb9c7ef0c`；`transport-manifest.json` 为 `4ae82fd17b804f7fed7d053941a51344d363f9a252b860e3997288d57c3fcafc`；`corpus-run-plan.pending.json` 为 `0fcb82b2b253c1d5f6dda02db2dc5847c08a30e868cea137189a6ea42d5c71ea`；`readiness.json` 为 `9cf95fe0af4315c92caf116b5bd2d142558728fd9e55d8f12fb61bc61ac2b2af`。工具包 `files.sha256` 身份为 `ca927c84407729a876a5bf1b6fa9f05a304353f479862a666c5835c5ce730637`，50 个工程源文件与准确代码提交一致；文档后续提交不改变固定训练工具身份。

本机只生成清单，未复制或归档完整输入；当时输出卷剩余 26,269,200,384 bytes，所需余量 1 GiB。新服务器数据盘最低容量估算为 23,079,835,396 bytes，仍须真实预检；这个估算不是实际峰值或租期承诺。当前每条 train 记录执行一次更新，完整一轮 14,592,720 更新，必须新实例实测后评估可承担的阶段与总预算。

最终执行回执 `question-full-delivery-final-private-v2.execution.json` SHA 为 `4b5dc78f5ae5096ec95a5a04c21b4a035cd32be0185e706952207ec00a5974f2`；外层回执 SHA 为 `a38d7de0fd4b81c2ff61a857cbc716678193828e5da008bba89290f16d0cdb09`，Succeeded、FinalCheckComplete=true、live/unknown=0，已退出的专属 gate 精确清理。此次仅离线准备通过，没有新实例连接、GPU 训练、教师批量采集或模型/数据发布；准确率提升尚未获得证据。
