# 全来源语料的流式训练与阶段恢复（2026-10-11）

本次新增的是可审查的 C# 训练调度路径。编译、离线 gzip 调度和负面检查不等于真实 GPU 前向、反向、收敛、准确率、校准、AOT 或性能验收。本机没有运行本轮正式训练；完整语料转换、逐行长度检查和新实例 GPU profile 必须分别产生自己的回执。

## “全部训练”的分母

“全部”指固定最终语料中，来源条款已复核、格式可表达、未被冲突排除账本移除且 `Split=train` 的记录。原始测试集、development、校准与 OOD 不进入任何 reverse/update 路径。各源原始物理行存在重复、多个标签或语言版本；不能把下载行数当作独立题目数，也不能把教师分布直接称为人工答案。

Choice、Boolean 和 Score 分别保留候选 ID、`false/true` 语义顺序和升序等级。77/150 类意图数据若派生成不超过 32 个候选的有限选择题，其任务已变为候选集合内选择；这个结果不能报告成原始 77/150 类任务准确率。工具参数或自由文本生成也不能伪装成有限决策的监督答案。

## 新路径与旧实验

旧 `profile/train/validate` 路径与历史 48-update 计划保持原行为。新路径使用 `CorpusRunPlan`、`corpus-package.json` 与独立的 source-generated JSON context，避免套用旧 `Acceptance` 的固定 run ID 和 checkpoint。

| 命令 | 输入与结果 | 可确认的范围 |
| --- | --- | --- |
| `corpus-check` | 固定 manifest、bindings，检查全部 gzip 分片 | 原始字节 SHA、记录形状、排除分母、所给 Family 的隔离 |
| `lengths-corpus` | 固定 corpus 与 tokenizer，输出 `lengths.json` 和失败账本 | 每条未排除记录的真实严格 token 长度，含 Choice 旋转；不算模型特征 |
| `corpus-schedule-check` | 小型合成 gzip fixture 与 CPU 负面检查 | 不均衡来源覆盖、跨 epoch 游标、SHA 变更拒绝、heldout/排除过滤、取消与 reader 释放 |
| `profile-corpus` | 全局固定 plan、新实例 GPU UUID | 三次真实训练步与最大长度形状探针，第二步检查 encoder 非零梯度；不接受质量 |
| `train-corpus` | 同 plan、同次 GPU profile、阶段更新数、可选完整 checkpoint | 当前阶段完整保存 encoder/head、两个 AdamW moment 和 epoch 游标 |
| `validate-corpus` | 最终 package SHA、同 plan、独立进程 | 新保存权重的真实 GPU 前向、按源及 split 固定抽样、Choice 旋转；仍不接受准确率或校准 |

## 固定全局计划

最终 corpus 关闭全部 gzip 并生成身份回执后再写计划，不能用还在变化的 shard、部分转换输出或 pilot 替代最终语料。计划至少固定以下字段：

| 字段 | 要求 |
| --- | --- |
| `SchemaVersion`、`RunId` | 版本 1；本轮独立、可追溯的实验身份 |
| `CorpusRoot`、`CorpusManifestSha256`、`CorpusBindingsSha256` | 固定最终语料、排除与来源条款身份 |
| `LengthReportPath`、`LengthReportSha256` | 全语料严格长度全部通过的回执；拒绝记录不能静默截断 |
| `BaseModelRoot`、`SourceCommit`、`DatasetCommit`、`ModelCommit` | 固定代码、来源仓库与底座身份 |
| `TrainRecords`、`Epochs` | TrainRecords 必须等于长度回执的 train 分母；每个 epoch 每条训练记录恰好一次 |
| `Seed`、`MaximumTokens` | 来源顺序与构造序列的固定参数 |
| `EncoderRate`、`HeadRate`、`WeightDecay` | 有界、有限的训练超参 |
| `CheckpointEvery`、`ValidationPerSource` | 保存间隔和按来源/split 验证配额 |
| `MaximumOwnedBytes`、`ExpectedGpuUuid` | GPU 内存预算和初始实例身份；不得套用旧实例信息 |
| `ExperimentalOnly` | 必须为 true；不能将结构准备直接升级为正式质量准入 |

显式工程上限为 5,000 万条物理记录、最多 128 个训练来源、4,096 个分片、16 个 epoch、8 亿全局更新和 8,192 tokens。SHA/Family 检查的 Family 索引上限为 400 万。当前每条题目对应一个 AdamW update；还没有以有效 batch 聚合多条题目。超过上限应进行明确的设计修订，不能把超限部分静默丢掉。

## 流式来源混合

每个 epoch 对来源名称按 `SHA256(seed, epoch, source)` 排序，然后在仍有记录的来源之间轮转。每个来源内部保持固定 manifest 分片和行顺序，耗尽后退出轮转。训练不将全量 State 放进数组；每个活动来源至多持有一个当前记录和 gzip reader。每个分片打开前验证文件大小与 SHA。

这种安排保证完整覆盖，不会把小源重复到大源同样大小，也不会改变大源的最终样本权重。来源很不均衡时，大源在 epoch 后段仍可能占主导。需要更强的任务平衡、比例随机混合或 loss 权重时，应另立固定实验计划并做独立比较，不能宣称当前来源轮转已经改善质量。

全局更新游标以 Int64 计算。`Epoch = CompletedUpdates / TrainRecords`，`WithinEpoch = CompletedUpdates % TrainRecords`。恢复时跳过已完成 epoch，仅按固定顺序流式扫描当前 epoch 的已完成前缀。每次完整 epoch 的最后 yield 都必须继续推进 iterator，验证尾部 EOF 和记录分母后才确认覆盖。

## 有界阶段与恢复

`--stage-updates` 是当前阶段的边界，不进入全局 plan 的 SHA。全局计划仍固定全部 epoch；分阶段运行不会重置 AdamW 或修改超参。阶段预检只估算当前阶段步数和保存开销：

```text
保守阶段秒数 = 4 × profile.MaximumStepSeconds × stageUpdates
              + 600 + 120 × 本阶段预计保存数
```

预算不足时程序拒绝启动该阶段，保留所有既有输出。每个输出目录必须全新。完整 checkpoint 包含 `weights.safetensors`、`moments.safetensors`、`checkpoint.json` 与 `corpus-cursor.json`；只有四项身份一致才可恢复。中断期间尚未保存的更新不构成完成回执，应从最后完整 checkpoint 重跑。当前阶段目录只保留最近两个已核实归属的 checkpoint，之前的阶段目录由外层资产迁移流程管理。

示例命令中的路径须替换成当前已固定、已核实的真实文件，以下不是已经执行的 GPU 训练：

```powershell
# 在 PowerShell 7 内，配合现有有界进程 runner 执行。
dotnet Sezika.RepositoryTrainer.dll profile-corpus `
  --plan '<fixed-corpus-plan.json>' --output '<fresh-profile-directory>' `
  --gpu-uuid '<current-32hex-uuid>' --timeout-seconds 1800

dotnet Sezika.RepositoryTrainer.dll train-corpus `
  --plan '<same-fixed-corpus-plan.json>' --output '<fresh-phase-directory>' `
  --profile '<same-instance-profile/profile.json>' --stage-updates 100 `
  --gpu-uuid '<current-32hex-uuid>' --timeout-seconds 3600

dotnet Sezika.RepositoryTrainer.dll train-corpus `
  --plan '<same-fixed-corpus-plan.json>' --output '<next-fresh-phase-directory>' `
  --profile '<current-instance-profile/profile.json>' --stage-updates 100 `
  --resume '<last-complete-checkpoint-directory>' `
  --gpu-uuid '<current-32hex-uuid>' --timeout-seconds 3600
```

跨租赁实例恢复可用 `--gpu-uuid` 明确绑定本次新设备，而不改变全局训练计划；必须先在新设备重新 `profile-corpus`。profile 的数据、plan、最大长度、GPU UUID 与资源释放 marker 均要匹配。模型/优化器持久化身份仍来自全局固定输入，旧 48-update checkpoint 或修改过的 plan 无法借此恢复。

## 硬件与成本

本机 RTX 4070 Laptop 约 8 GiB 的显存无法通过现有完整长序列路径的资源门槛。当前实现明确限定新实例 RTX 5090 / 32 GiB、CUDA Driver 12.8 或更新版本，并保留 1 GiB 驱动开销。没有新增 GPU 混合精度、activation checkpointing、多卡、梯度累计或其他尚未验收的省显存能力。

租用总时长需要依据最终语料长度分布、真实 profile 和完整更新数计算。当前一题一 update 的实现，百万到千万级题目会产生同等数量的 encoder 反向更新；更多数据不自动意味着便宜或更准。固定总计划可分阶段推进，但阶段化不减少总计算量。

全部计算完仅形成未校准的实验模型包。准确率必须另用未泄漏的测试题、原始人工 gold 与教师参照分别计算，并保留随机/多数类/旧模型/相同底座的对照。业务策略、拒答阈值和实际执行授权由核心之外的业务层决定；Sezika 的有限候选 System 1 决策并不等同于自由文本通用生成模型。

## 验证证据

本分支本地构建与小型离线调度回执保存于：

`C:\Users\mysti\.codex\artifacts\sezika-corpus-20261011-01a12b4c\stream-training-agent`

每次构建/测试使用现有 `Invoke-BoundedProcess.ps1`，记录 PID、创建时间、argv、父链、超时、输出和最终资源闭环。失败回执保留，不用后一次成功覆盖。最终整棵修改树的编译、全语料严格长度、新实例 profile、真实完整训练恢复以及独立 GPU 验证仍分别需要相应回执。

2026-10-11 的本子任务构建 `corpus-workflow-build-v4` 零警告/错误，`scheduler-v2.json` 通过 31 项小型 CPU/gzip/负面检查。检查包括全部 12 个跨 epoch 恢复位置、heldout 与冲突排除、文件 SHA 和计划身份变更拒绝、缺失/仅 pending cursor 拒绝、AdamW moment 文件篡改拒绝，以及 deadline 后释放所有活动来源 reader；没有运行真实 GPU optimizer 恢复。

## 私有模型包的来源映射

完整训练模型包另存 `input-manifest.json`、`input-corpus-bindings.json`、`input-source-rights.json` 和 `input-source-assets.json`，保持来源原字节。原 manifest 的全部 `source_notice_bindings` 所声明的组件元数据、逐记录排除账本和 README/LICENSE/NOTICE/COPYING/CITATION 文件复制到 `corpus-provenance/<原相对路径>`。每个输入和副本核对大小、SHA，复制前后再次核对原文件身份；不读取或复制原训练 shards、题目和教师 gold。

`corpus-provenance.json` 保留原 `SourceAssetsPath`、`SourceRoot` 的历史值，同时提供可携带的有效映射 `SourceAssetsPath=input-source-assets.json`、`SourceRoot=null`。它不会改写原 `input-corpus-bindings.json`，也不改变 tensor、优化器、checkpoint 和全局训练身份。独立来源核验只读取包内映射及其 SHA，无需旧 source root；完整 `validate-corpus` 的题目评估仍需要固定原语料。

来源 manifest 可能由 Windows 整理，声明路径使用反斜杠。打包访问和 basename 判别将相对路径的分隔符规范成 `/`，便于 Linux 租赁实例读取，同时原 manifest 字节与回执 `OriginalRelativePath` 保留原始反斜杠形式。确切的 `SOURCE_LICENSES.md` 是已绑定来源许可证据，允许按声明 SHA 打包；没有放开其他 `SOURCE_*` 题目载体。

复制和核验上限为 512 文件、单文件 16 MiB、累计 128 MiB、路径深度 16、120 秒，另受当前 RunBudget 的取消文件和总墙钟限制。只有声明的元数据或 notice 名称通过白名单；`shards` 路径和未支持的题目载体拒绝复制。来源回执最后经 pending、flush/fsync 和同目录 rename 提交；任何缺文件、原字节变更或超限均保留失败目录，不能作为完整包消费。独立验证还要求来源回执的 manifest/bindings SHA 与固定全局输入一致，并逐项核对原 manifest 与副本 notice 映射，不能用自洽但属于另一语料的回执替换。

此包明确声明 `PrivateSourceMetadata=true`、`PublicPublicationAccepted=false`；组件 exclusions 中可能含逐题 RecordId，属于私有来源元数据。公开模型发布必须另制去逐题 ID 的来源摘要及映射，并单独核验许可与发布范围，不能直接发布当前私有包。`LICENSE-COMPONENTS.md` 对应实际文件名 `input-source-rights.json`，整体训练输出许可证仍为 unknown；这些整理步骤没有接受正式训练质量或重新授权任何来源。

2026-10-12 本地补充构建 `corpus-workflow-build-v5-provenance` 零警告/错误，`scheduler-v3.json` 通过 39 项小型 CPU/gzip/负面检查。新增八项覆盖无 SourceRoot 时的本地绑定 assets、组件与排除身份映射、中英文 notice 字节保真、私有/公开标志、原 notice 和包内 notice 篡改拒绝、缺失 assets 拒绝、固定全局语料身份不符拒绝、遗漏声明 notice 映射拒绝。两份 guardian 回执均 Succeeded、live=0、unknown=0；未执行真实 GPU 模型打包或 optimizer 恢复，旧失败回执保留。

最终合并语料 `combined-corpus-v1` 的只读来源复核发现 `SOURCE_LICENSES.md` 白名单缺口；原失败 `source-provenance-compatibility-v1.json` 保留。补充确切名称及访问路径规范后，`source-provenance-compatibility-v2.json` 完整核对 43 项 notice bindings 与四项核心文件的 SHA、大小和路径：共 47 文件、4,022,423 bytes，最大文件 2,177,854 bytes，最大目标路径深度 10，全部低于打包上限。复核没有读取 raw shards，不能替代完整模型打包或 GPU 训练验收。

补充的 `source-provenance-compatibility-v3.json` 检查原 manifest 的全部 353 个分片路径字符串，反斜杠路径数为零；未读取分片内容。`corpus-workflow-build-v8-platform-contract` 零警告/错误，`scheduler-v4.json` 保留原 39 项检查并追加三项，总计 42 项：Windows 反斜杠转换为主机无关 `/` 的明确合同、`SOURCE_LICENSES.md` 的原字节/SHA/历史反斜杠路径映射，以及许可原字节篡改拒绝。basename 判别不依赖主机的 `Path.GetFileName` 规则。这个合同检查避免仅依靠 Windows 原生同时兼容两种分隔符而得到假通过；它仍不是 Linux 进程或真实 GPU 打包验收。

## 已闭合语料的组合

`tools/corpus/compose_corpora.py` 仅整理离线数据，最多组合四个 `COMPLETE_PREPARATION_ONLY` 输入 root。配置文件是 JSON 数组，每项包含 `root`、`manifest_sha256`、`bindings_sha256`，第一项可额外固定 `dedup_sha256`。第一个 root 的 `dedup-index.sqlite` 必须已关闭、无 WAL/journal，且索引物理分母与 manifest 一致；主转换仍在写入时不得组合。

```powershell
python tools/corpus/compose_corpora.py --inputs '<fixed-inputs.json>' `
  --output '<fresh-composed-corpus>' --seconds 600
```

主语料分片同卷硬链接，核对 inode、字节数与 SHA，避免重写大文件；原输入和输出都应按不可变资产管理，因为硬链接共享内容。程序拒绝跨卷硬链接错误，未自动回退到大文件复制。补充物理记录最多 20,000 条，先在 256 MiB 缓冲预算内还原候选旋转并比较 canonical key/原始 target：相同题目只保留一份；与主记录冲突时，将主 RecordId 加入唯一 mandatory exclusions；补充之间冲突时均不写新分片。原补充 root 已排除的行直接跳过，不制造指向不存在行的排除分母。

各组件原 manifest、bindings、rights、assets、exclusions 与本地 notices 保留原始字节并固定 SHA。组件绑定了 `SourceRoot` 时，还会按来源已绑定的 asset 读取 README、LICENSE、NOTICE、COPYING、CITATION.cff 及 license_evidence；最多 128 文件/16 MiB，核对原 bytes/SHA 后归档并写入输出 notice bindings。缺失或未绑定的证据记录在 `composition.source_notice_missing_evidence`；不升级来源许可、unsplit 或语义 Family 的复核状态。

最后 bindings 经 pending 文件、fsync 与同目录 rename 提交。失败保留全部输出而不能按未完成绑定消费。输出是 C# `Corpus` 可读取的单一 root；它没有重建全量合并 dedup index，因此不能直接作为下一次组合的主 root。后续若要再次组合，必须先重建并绑定完整索引。

`composition-selftest-v3/self-test.json` 通过九项小型合成检查，包含硬链接、重复与冲突、唯一 RecordId/排除分母、候选反旋转、输入 SHA 变更拒绝、中英文来源 README 与所有组件元数据字节保真、待审语义状态保留。`composition-csharp-check-v3.json` 是实际 C# 消费结果：三条物理行、一条排除、两条训练记录；结构检查通过，formal/token/GPU/语义通用质量均未接受。最后一次检查通过启动 gate 和 `run_guarded.py` 完成进程闭环；前一次极速 CLI 的 `CleanupFailed` 回执及原 PID 后续已退出的独立证据均保留。
