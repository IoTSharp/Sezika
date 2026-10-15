# C# 工具调用合同

更新：2026-10-15。当前实现与验收按根 ROADMAP 分项推进；本机、回环服务、适用 AOT 和现场证据分别记录。正式训练、租机、模型/数据发布与部署仍须当轮范围，源码提交授权不包含这些操作。

所有长期实现使用 C# / .NET 10；Windows 终端使用 PowerShell 7。原生目标以准确绝对路径、结构化 argv、固定 SHA 和新任务目录声明。不能通过解释器、内嵌命令或生成脚本恢复旧流程。

项目不固定 SDK 版本，`global.json` 仅排除预览 SDK，目标保持 `net10.0` / C# 14。每次构建记录实际解析的 SDK 与工具 SHA；本任务已核安装目录是工具身份，不是项目版本锁。

## 命令入口

迁移命令使用 `--request <类型化 JSON> --output <新 JSON> --timeout-seconds <秒数> [--cancel-file <路径>]`；字段、必填项和 JSON 校验以各入口实际源码合同为准，不能把一个入口的检查推断到全部命令。原始成功、失败和 UNKNOWN 回执保留，已有输出不覆盖。

| 工作 | C# 入口与请求类型 |
| --- | --- |
| 任务身份和预算 | `task prepare` / `TaskPreparationRequest` |
| 全量输入只读准入 | `task validate-inputs` / `CorpusInputAdmissionRequest`；固定计划及policy/readback SHA，完整corpus物理资产、完整扫描/严格长度与底座身份；实现与实测按ROADMAP分项验收 |
| 小回执只读快照 | `task snapshot` / `ReceiptSnapshotRequest`；准确相对路径，最多32文件、64KiB/文件、总1MiB/20秒，原字节Base64及SHA；失败保留已完成分母 |
| 工程输入准入 | `bundle inputs` / `BundleInputRequest`（`SourceRoot`、`Projects`）；输出完整 `EngineeringSources` bytes/SHA，仅表示工程输入覆盖 |
| 完整工程构建与冻结 | `bundle build` / `BundleBuildRequest`；完整 engineering manifest 覆盖源码、工程配置及 PTX/ABI，实际从新冻结快照编译 |
| 已有工具逐字节冻结 | `bundle freeze` / `BundleRequest` |
| 新目录安装及已有安装核验 | `bundle install` / `BundleInstallationRequest`；完整 TAR/SHA 清单、显式 native 权限与限定 help/version 依赖探针，`verify` 只核已有字节与权限 |
| 归档与独立解包 | `archive pack` / `PackRequest`；`archive extract --archive <TAR> --sha256 <SHA> --destination <新目录> --output <新回执>` |
| 进程生命周期 | `process run` / `ExecutionRequest`；[执行器合同](../tools/Sezika.Tooling/Execution/README.md) |
| 已登记身份补审 | `process audit --log-directory <回执目录> --output <新回执>`，最多12目录；补审不回写旧失败 |
| 原生 Git/LFS | `git run` / `GitRequest`；`preview` 只读预览、`commit-only` 精确所属路径本地提交，`publish` 正常推送；安全凭据、原生 pre-push、准确父提交与独立 fresh 内容读回分别验收 |
| HTTP 下载/恢复 | `transport download` / `DownloadRequest`；Range、partial 与 sidecar 保持原身份 |
| 冻结来源采集 | `source acquire` / `SourceAcquisitionCommandRequest`；固定计划及metadata pins，完整selected/unselected分母、cache重验、分路partial与JSONL ledger |
| 模型资产组合与复核 | `model mirror` / `ModelMirrorCommandRequest`、`model verify` / `ModelMirrorVerifyRequest`；外部manifest SHA、固定revision、许可与config/tokenizer/weights/license四角色，逐资产SHA256及完整bundle读回 |
| 分块清单/复核/重组 | `transport prepare`、`transport verify`、`transport reassemble` |
| 发布准备和预览 | `publication prepare`、`publication preview`；不接触平台写接口 |
| 提供方模型卡校验 | `publication validate-card` / `ModelCardCommandRequest`；固定HF模型卡YAML接口、单POST/50秒，回执只保留状态/计数/SHA；实际平台调用须当轮范围 |
| 已授权平台写入 | `publication upload`、`publication commit`；网络写不确定时先读实际 ref，不自动重试 |
| 数据/教师 | `corpus prepare/compose/queue/collect/admission/dataset`；`DatasetTool corpus`、既有 TeacherTool |
| 研究与冻结样本 | `corpus research/research-check/seed-check/inspect/estimate/bind` |
| 独立验证 | `validation profile/summarize/data-admission/language-audit/batch/near-ties` |
| Oracle 批次 | `oracle batches` / `OracleBatchRequest`；冻结参考、实际捕获、比较三阶段与恢复；[合同](../tools/Sezika.Tooling/Oracle/README.md) |
| 逐项退役审查 | `inventory review` / `RetirementReviewRequest`；固定466行分类、精确 C# 来源与独立验证回执、声明的现行调用范围 |
| 四文件断点 | `checkpoint verify` / `CheckpointVerificationRequest` |
| 宿主只读准入 | `host inspect` / `HostInspectionRequest`；磁盘通过不代替 GPU 准入 |
| 固定 GPU 观察窗口 | `host sample-gpu` / `GpuObservationRequest`；固定 native image SHA、完整 UUID、采样数量/间隔/墙钟、原始流与原生回执；观察结果不能作为吞吐、训练或质量验收 |
| 保存与恢复调度 | `task resident` / `ResidentRequest`；prepared-task、真实同卡 profile、完整 Inputs、唯一 GPU 与保存预留共同绑定 |

`BundleBuildRequest.SourceRepositoryRoot` 为可选的独立 Git 仓库绝对目录，省略时沿用 `SourceRoot`。它只用于构建前后两次 HEAD 核对；源码覆盖、复制、编译和前后 SHA 仍绑定冻结的 `SourceRoot`。`engineering-overlay.json` 记录基线提交与完整输入身份，未提交覆盖不能被描述为该 Git 提交已有的内容。

新增命令的认证来自具名凭据存储或有界stdin，默认 `CredentialSource="none"`；请求和回执不得夹带token。`source acquire` 的CLI包裹字段为 `Acquisition`，计划/ledger本体使用snake_case；明确区分 `sha256` 与含blob header的 `git-blob-sha1`，实际内容另记SHA256。单个凭据只允许一个selected official origin，带认证禁镜像，跨origin重定向不携带认证；公开多来源计划保持支持。取消先保存完整planned ledger再抛出，外层失败不覆盖内层分母。采集完成不等于已准入训练。

采集回执目录另含完整 `research-download-plan.json`。将它作为 `corpus research-check` 的 `Plans`，同目录的 `source-ledger.jsonl` 作为 `Ledgers`，`Root` 指采集的 `OutputRoot`。即使只选择部分资产，两个文件仍保留全部计划分母。检查器明确绑定新ledger的plan SHA、source/path/revision/split和原digest；`VerifyHashes=true`独立重验实际SHA256及原digest。Git blob预期值不填入 `ExpectedSha256`，也不从下载输出生成预期。新partial分别核镜像/official实际文件；旧ledger与旧partial语义继续保留。该接线的实测状态仍见分项证据。

`model mirror` 的CLI包裹字段为 `Mirror`，model manifest和verify请求使用snake_case。它在任务自己的文件系统组合调用者固定的完整资产集合；现有typed worker/SFTP可运输这些结果，远端部署、模型质量、推理和公开Git/LFS发布分别验收。`task snapshot`仅接受具名小型文本/metadata文件并拒脚本与链接，逐文件重开核字节；它不声明跨文件原子保存或恢复历史可信基线。四项新能力的运行状态仍以分项验收证据为准。

`Sezika.RemoteTool upload/download` 使用 `--connection <无秘密 JSON> --request <传输 JSON> --local <文件> --output <新回执>`。`dispatch` 使用 `--connection`、`--request <worker JSON>`、`--logs <新目录>`、`--output`。只允许固定 C# worker 与明确 payload；认证来自有界 stdin 或准确凭据存储目标。控制器固定主机键及 Native AOT worker 全 image SHA，worker 绑定完整代码/runtime 清单和请求 SHA；控制器独立读回回执。JIT 控制器、AOT worker 和 payload 部署能力分别验收。

## 有界进程与验证

`process run` 的 `ExecutionRequest` 使用准确绝对 `Executable`、结构化 `ArgumentList`、已存在的 `WorkingDirectory` 和全新 `OutputDirectory`。选择已冻结的 C# apphost，或准确 dotnet host加已核 DLL 首参数；构建是单独声明的 native SDK 调用。解释器、内嵌命令和生成脚本不作为替代入口。该请求本身没有程序 SHA 字段：源码、程序及 DLL SHA 必须由上层任务/验证准入和独立核验绑定，不能把 host hash 当应用身份。

预算字段为 `TimeoutSeconds`、`CleanupSeconds`、`MaximumProcesses`、`MaximumInputBytes`、`MaximumOutputBytes`；还可显式声明 `Environment`、有界 `StandardInput`、`CancelFile` 和 `StopFile`。Windows 的 `WindowsConsoleMode` 为 `detached` 或 `hidden`。Linux 使用固定 C# `BrokerExecutable` 及 `BrokerArgumentPrefix` / `WatchdogArgumentPrefix`；嵌套 broker另以 `NestedBrokers` 的准确路径/SHA准入。外层 CLI 期限与内部工具期限都须容纳清理，STOP 保存与 CANCEL硬取消分别记录。

执行目录保留 stdout/stderr、登记 PID/出生/argv/父链和 `result.json`，独立核对 `ExecutionStatus`、退出码、`CleanupStatus`、`RemainingProcesses`、输入/输出完整性及错误。WSL 内的 Linux 进程必须有内层回执；Windows 外层退出0或后续成功不能覆盖旧失败/UNKNOWN，也不能证明内部进程已回收。

`validation batch` / `ValidationBatchRequest` 固定每项 `Name`、`Executable`/SHA、`Arguments`、`Inputs`、`Mutations`、`Outputs`、预期退出码/标准错误与期限；dotnet模式另固定 `ManagedAssembly`/SHA。先执行未修改的成功基线，只变异任务副本，再核准确错误及不存在误认成功的输出。回执保留 Planned/Completed/Passed/Failed/Unprocessed及独立输出SHA和清理结果。批次只准入既定验证器和子命令，不执行模型或生成独立参考。

预期非零退出须声明准确的目标 stderr，或必需 JSON 输出中的精确状态/字段约束。`ValidationOutput.JsonEquals` 接受1–32个字段，严格比较 JSON 类型和值；同时声明的 stderr、状态和字段检查必须全部通过。不能以退出码符合预期或任一检查通过掩盖其他失败。

`validation summarize` / `S5SummaryRequest` 只核已完成历史 schema1 的 S5 `requests` 报告；当前画像使用 `validation profile` / `ProfileValidationRequest`。`validation language-audit` / `LanguageAuditRequest` 只适配固定来源及明确 `MaximumRecords` 的既有小型fixture，不读取 calibration。报告一致性、fixture回归、模型实测、质量、校准、AOT和性能分别验收。

## 独立参考与实际捕获

现有独立冻结参考须核原模型/revision、权重/tokenizer、原始源码/程序、输入清单和合同 bytes/SHA及原执行来源。比较器的来源文件参数只读取 bytes/SHA，不运行旧文件。C# `OracleCapture` / 实际数值捕获仍执行自己的真实前向，随后比较/评分；参考 tokens/logits不能复制成实际捕获。

新的 `Sezika.ReferenceTool` 提供 `capture`（固定原始 mmBERT 权重）、`laya`、`tokenizer`、`build-proof`、`proof-checks` 和 `laya-input-checks` 类型化入口。它使用独立移植的 C# 数学与原始资产，输出明确采用 `port-derived.v2`、`upstream_executed=false`；原 PyTorch v1 来源门槛及原件保持。原权重导出与独立消费比较、tokenizer golden、Laya 输入/冻结 logits 后处理、完整神经前向、质量和 AOT 分别验收，准确范围见[参考工具说明](../tools/Sezika.ReferenceTool/README.md)及[证据](evidence/tooling-reference-final-2026-10-14.md)。原始 Laya 五资产已验明，Windows 独立参考与实际 SIMD 已对 `choice-en-short` 完成真实单条前向和比较，覆盖1/46、缺45条，`full_manifest_passed=false`；全清单与跨平台/AOT仍分别验收，见[单条证据](evidence/tooling-oracle-capture-final-2026-10-14.md)。局部46/42 fixture、生产模型自比较和单条通过均不能冒充上游实际执行或全量验收。

## 全量训练与保存

数据的原来源 commit 与实际 PublishedDatasetCommit 分开记录。先将完整准入 corpus 原字节存入私有数据仓库，固定发布 ref 并独立认证 fresh Git/LFS 读回，再让有当轮授权的新服务器直接拉取。数据不经模型公开通道，development/test/calibration/OOD 不回流训练。既有完整分母为14,592,720条训练记录；新分母仅由独立准入资料决定，阶段更新上限不能重写全局计划。

RepositoryTrainer 的 `profile-corpus`、`train-corpus`、`validate-corpus` 使用固定 `CorpusRunPlan`。每次换卡重新核实际 UUID、driver、容量、消费者及最长完整序列前向/反向 profile。`--stage-updates` 只推进同一 AdamW/global cursor。resident 将 STOP 留给 trainer 保存，CANCEL 是执行器硬取消；不把两者混称。

当前全量路线增加独立 `CorpusInputAdmissionRequest`：固定 `PlanPath`/`PlanSha256`、`SourcePolicyPath`/`SourcePolicySha256`、`ModelReadbackReceiptPath`/`ModelReadbackReceiptSha256` 和文件/字节上限。`task validate-inputs` 只读核完整资产及三方绑定，报告只代表实际输入检查，不授予训练、许可或质量批准。真实 `ResidentRequest.InputAdmission` 必须存在并绑定同一个计划路径和SHA；resident在GPU预检/启动前调用同一准入器。CPU内部模拟保持独立，不能借模拟结果绕过此门。旧的无policy长度路线仍可解释历史材料，不能成为当前全量准入；两平台本机正负例及调用接线范围见 [输入准入证据](evidence/tooling-final-v17-2026-10-15/input-admission.md)，真实服务器/GPU仍为NOT_RUN。

断点必须同时包含 `weights.safetensors`、`moments.safetensors`、`checkpoint.json`、`corpus-cursor.json`。核完整 Inputs、SHA、调度结构、全局游标及计划，cursor 是完成标记。新 checkpoint 的自报 Inputs 不能作为唯一预期：使用独立 profile/前驱合同。全局完成与所有来源 EOF 单独验收；CPU controller simulation、进程退出0或四文件 SHA通过均不是 GPU 训练、optimizer 实际加载、模型质量或校准证据。

## 当前旧入口

迁移清单、分项替代和历史原件见 [T0 分类](evidence/tooling-migration-classification-2026-10-13.json)。当前工作使用以上 C# 入口；旧脚本只用于追溯原始证据，不能作为新的执行入口。受保护的未提交旧改动与原失败回执保持原字节；最终退役覆盖仍以 T7/T8-code 回执为准。

`RetirementReviewRequest` 固定 `ClassificationPath`/SHA、准确 `SourceRoot`、`ExpectedEntries=466`、`Gates`、`CurrentCallers`与 `CallerScopeDeclaredComplete`。每个 gate提供唯一 Id、WorkPackage、精确 OriginalPaths、CSharpEntry、C# Sources bytes/SHA、Receipt bytes/SHA、准确 ExpectedReceiptStatus及实际 IndependentlyVerified。来源只接受明确 `.cs`/`.csproj`，每个原条目只能归属一个gate；回执必须是实际独立核验过的顶层通过状态，不能从草案或普通自检推造。

`CurrentCallers` 逐文件固定路径/bytes/SHA和 code/documentation/configuration类别；在最终编辑冻结后重新生成这些pins，明确声明审查范围。动态拼接或歧义的脚本引用保持 REVIEW_REQUIRED；缺原文件保持 NOT_FOUND与UNKNOWN；缺gate、来源或回执不符保持PENDING。READY只表示该条目在所声明范围和准确门槛下已可审查，不执行退役或删除，不代表项目全量DONE。共享外部技能仅审查项目依赖关系，解除项目依赖不授权修改机器技能。

`ReviewedNonExecutionLines` 为可选的独立逐行裁定：绑定完整调用者 SHA、准确行号与 UTF-8 行正文 SHA，记录类别和理由；它不提供自动控制流证明。`HistoricalArchiveDecisions` 则逐原件绑定 bytes/SHA、具体用途、理由及独立批准，仅用于明确撤销具名历史配方的当前执行资格。报告保留原冻结分类，并以 `ReviewBasis` 区分历史配方裁定与能力替代门。归档裁定不接受外部技能或既有未跟踪用户材料，也不能掩盖当前执行引用、不完整范围或字节漂移；同一原件不能同时用两种门。两者都不执行脚本、移动原件或改写历史状态。

`CurrentEvidenceRetentions` 单列已有用户材料的准确当前bytes/SHA、用途、理由、独立复核及明确的 `NoCurrentOperationalResponsibility`。只有撤销当前执行职责的材料可进入 `RETAINED_UNKNOWN_HISTORY`；它不计READY，历史基线未知数仍从冻结分类的完整分母报告，不能用保留决定代替尚需实现的功能。2026-10-08的五份label-audit材料属于当时32题/5请求的一次性审题和报告工作，现有报告自述完成、人工审查仍pending；原max/ultra变体及失败证据只供追溯，当前流程不重放旧授权或调用这些文件。最终无执行caller范围与当前pin须另行验明。

固定29来源saved samples的语义附录和固定75来源硬编码RULES的历史报告配方，现撤销当前重放职责，逐原件进入历史归档决定。原输入、输出、失败与脚本字节保留；这些历史人工结论不作为新来源或新许可的自动判断。当前C#采样、source binding与检查输出供显式语义和许可审阅；`corpus inspect`不宣称复现旧报告或自动完成人工审批。

新增安装、GPU 观察、Git 模式及历史配方裁定的当前源码与运行验收按 ROADMAP 继续绑定；文档中的入口描述不自动将尚未运行的候选标为 PASS。

原始失败及历史源bytes/hash未变需要单独保全证据；本次现状快照不自动证明历史原件未改。完成调用者修订后再冻结完整源清单、验收回执与退役审查，任何随后改动都须重生受影响的pins并重跑门槛。
