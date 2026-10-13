# Sezika 工具与技能代码统一为 C# / .NET 10 Native AOT：范围评估

> **后续用户修订（2026-10-13）：训练、离线准备和构建工具允许 JIT/自包含 .NET 10，PTX 可以构建期生成。以下“所有工具强制 AOT”“PTX 编译器为迁移前置阻塞”等内容是最初严格假设，已被本条及 ROADMAP 的 T0–T8 替代；文件数量与脚本调用链调查仍有效。当前实施主线是 C# / .NET 10 与去脚本依赖，推理部署继续保持 AOT。**

首批实现及实际检查见 [T1a 迁移证据](evidence/tooling-csharp-migration-2026-10-13.md)。下方“本次未实施”是最初评估时的状态，文件数量是迁移前基线，不能作为后续实现进度。

官方 ILGPU v2 编译管线 PR #1577 在 2026-10-08 合入 `master`，明确引入独立 kernel compilation pipeline 与 AOT 支持；同日合入的 #1578 对接 NVCC 等厂商原生编译工具，并提供可选远程编译服务。当前项目固定的是 1.5.3，v2 新管线不能当作原 API 的直接替换。因此先前只依据本项目固定 1.5.3 给出的阻塞判断不能推广为“ILGPU 没有 AOT 路线”。未来升级须另核稳定包、构建依赖及数值/ABI/恢复身份；不引入运行时远程编译或未经验证的 SDK 依赖。

核查来源：[官方 #1577](https://github.com/m4rs-mt/ILGPU/pull/1577)、[官方 #1578](https://github.com/m4rs-mt/ILGPU/pull/1578)、[官方 Releases](https://github.com/m4rs-mt/ILGPU/releases)。这些是项目主线/发布信息，尚不构成本项目 v2 兼容性实测。

评估日期：2026-10-13（Asia/Shanghai）。源码基线：`e544815529affa1ef1f271f76ccd47f2f3febdaf`，同时检查当前未提交工作树。本文记录现状、迁移对象和验收要求；阶段顺序以 [ROADMAP](../ROADMAP.md) 为准。

**结论：当前不满足“项目长期代码全部 C# / .NET 10 Native AOT、无任何脚本执行路径”。改动属于较大规模工具链重构，主要涉及数据准备、进程监护、SSH、上传下载、发布读回、技能入口和构建验收。训练计算主体可以复用；PTX 编译工具需要单独处理架构兼容问题。**

本次状态为 `STATIC_ASSESSMENT_COMPLETE`；代码迁移、完整 AOT 构建矩阵、无脚本端到端验收均为 `NOT_RUN`。未连接服务器、运行训练、上传下载模型、读取凭据、提交或推送。工作树已有训练优化修改，不属于本次实现成果。

## 1. 要落实的范围

- 项目自己维护、以后还要使用的工具、检查程序、数据转换、教师采集、训练控制、模型下载、上传、仓库发布和服务器运行代码，统一使用 C# / .NET 10，并按实际目标平台发布 Native AOT。
- 不允许把脚本改扩展名、嵌入 C# 字符串、打进资源后释放执行，或由 C# 包装器继续调用 `pwsh`、`powershell`、`bash`、`sh`、`python`、`cmd /c`。`Add-Type`、here-doc、动态生成凭据脚本和 Git shell helper 同样计入迁移。
- `SKILL.md`、JSON、YAML、模型清单、PTX 和已有数值参考数据属于说明或资产，可以保留；技能中的可执行流程必须调用正式原生工具，不能继续教智能体临时拼脚本。
- 历史一次性修补、旧诊断和重复版本不逐个翻译。先提取仍需复用的能力，保留原证据、原 SHA 和历史定位，撤销其当前执行入口。不能原地修改旧脚本后继续使用旧证据清单。
- SDK、系统、驱动和 Git 等外部工具不是“我们维护的代码”；是否允许某个外部二进制仍按实际用途审查。它们产生的 shell hook 或调用的解释器不能成为绕过本要求的隐藏路径。数据/模型仓库自带的第三方 loader 仍不得执行。
- 本次审查所用 PowerShell 7 只用于读取工作树及生成评估资料，没有新增 `.ps1/.sh/.py` 工具文件。执行环境的终端与项目交付工具分别统计。

## 2. 核查数量与统计边界

用 `git ls-files` 完整枚举受版本控制的脚本、项目文件和技能入口；行数按当前工作树文本计算，包含注释与空行，不代表去重后的业务逻辑量。随后追踪核心调用链、嵌入脚本及当前技能明确引用的任务目录。没有对整个磁盘或全部历史任务目录递归扫描。

| 区域 | 脚本数 | 当前行数 | 处理判断 |
| --- | ---: | ---: | --- |
| `tools/repository-training/` | 18 | 2,674 | 仓库训练准备、输入拉取、发布、验证主链 |
| `tools/rental-training/` | 16 | 2,410 | 租机流程、SSH、进程监护、模型回收；含旧合同及重复 guardian |
| `tools/corpus/` | 24 | 3,157 | 语料转换、准入、合并、教师队列及采集 |
| 公共进程监护与对应测试 | 4 | 903 | 三个根级 runner 和 `tools/tests/Test-BoundedLinuxProcess.ps1` |
| `tools/model-publishing/` | 1 | 353 | 模型发布包准备 |
| 工具目录中的校验、评测脚本 | 11 | 1,824 | 数据、性能、教师、基线、数值和 preflight 检查 |
| 两组 Oracle 工具 | 4 | 1,246 | 三个 Python 导出器及一个 PowerShell 入口 |
| **`tools/` 小计** | **78** | **12,567** | 迁移或明确退役，不等于全部仍活跃 |
| `datasets/` 的构建脚本 | 3 | 684 | 判断哪些仍用于复现数据，提取通用构建能力 |
| `docs/research/public-decision-data-2026-10-10/` | 17 | 1,621 | 采集、完整性、上传、凭据及浏览器历史代码混放 |
| **上述非 evidence 候选小计** | **98** | **14,872** | 需要分类的主体范围，尚未去重 |
| `docs/evidence/` 已跟踪脚本 | 184 | 12,580 | 历史原件和快照；先核复用关系，再退役入口 |
| **已跟踪脚本总计** | **282** | **27,452** | `.ps1` 138、`.sh` 106、`.py` 37、`.cmd` 1 |

另有三类补充范围，不能混入“282 个已跟踪文件”的分母：

1. **10 个明确命名的 ignored 任务目录中，枚举到 174 个脚本、13,928 行。** 其中有旧版本和一次性修补，只能视为能力候选；不是 174 个都要重写，也不是整个 `.artifacts` 的总量。
2. **共享技能依赖 5 个脚本、1,049 行。** 包括 MoArk 技能 4 个脚本与 SSH stdin wrapper 1 个。
3. `docs/evidence/label-audit-20261008/` 另有 5 个未跟踪 PowerShell 文件，属于已有用户材料，需参与最终归档审查，本次没有改动。

逐文件条目见 [脚本清单](evidence/tooling-native-aot-inventory-2026-10-13.csv)，共 466 行：282 tracked、174 ignored 定点枚举、5 共享依赖、5 已有 untracked。初始迁移建议为 `PROPOSED_NOT_IMPLEMENTED`；后续 T1a 对应两行已更新为 `IMPLEMENTED_PENDING_CALLER_SWITCH`。实现替代不等于旧入口已经退役，不能仅凭路径名称认定无需迁移。

## 3. 具体需要改哪些代码

### 3.1 训练控制、服务器运行与进程监护

主要对象是 [仓库训练工具](../tools/repository-training/)、[租机训练工具](../tools/rental-training/)、三个根级 runner，以及当前任务目录中的控制器。

需要保留的能力包括：任务/租期/预算合同、冻结工具包、预检、唯一 GPU 使用锁、执行阶段、日志与进度、`STOP` 安全保存、`CANCEL` 中止、检查点恢复、子进程身份登记、超时和完整子树回收。迁移不能只实现 `Process.Start` 加超时，把已有身份、回收和失败语义丢掉。

当前额外发现：

- `.artifacts/repository-training-restart-20261013-01/launch-resident.sh` 会生成 `resident-controller.sh`，内部还运行 Python，并通过 Bash guardian 启动 C# 训练器。
- `.artifacts/repository-training-cadence-20261013-01/install-cadence.sh`、`switch-cadence.sh` 等承载后续保存节奏控制；最近使用的流程不能只按较早的 restart 目录重建。
- 两个目录中的 `ssh_bridge*.py` 使用 Paramiko/psutil，负责凭据读取、SFTP、远程执行及身份记录，不能因处于 ignored 目录就漏掉。
- 公共和租机目录已有多份 441/442 行 Bash guardian，另有任务内派生版本。应合并规则与参数，保留原 SHA 的追溯关系，不平移出多套 C# 实现。

建议以一个共享 C# 工具库和一个跨平台原生 CLI 提供 `process`、`remote`、`task`、`bundle` 等命令组；同一 CLI 可以在服务器以受控任务模式运行。无需新增长期开放网络端口或另建一套训练引擎。Windows 的 Job Object、Linux 的进程身份与子树回收路径要分别验证。

### 3.2 输入拉取、上传、模型下载与发布验收

主要对象：

- `Prepare-FullCorpusDelivery.ps1`、`New-ToolBundle.ps1`、`Download-RepositoryRun.ps1`、`Expand-RepositoryArchive.ps1`、`Publish-RepositoryRun.ps1`。
- `remote/clone-inputs.sh`、`pull-full-corpus.sh`、`publish-run.sh`、`mirror-public-model.sh`、`pack-result.sh`、`verify-publication.py`、`reassemble-optimizer.py`。
- `tools/model-publishing/Prepare-Package.ps1` 和租机目录的 `Prepare-Package.ps1`、`Download-Model.ps1`、`Verify-Experiment.ps1`。
- 研究目录内仍需复用的数据采集、Range 下载和平台发布能力。

**新的全语料发布适配器已经存在于 ignored 任务目录，不能当作“还没有实现”而另写一套。** `.artifacts/repository-training-moark-full-20261013-001/publisher/full_corpus_publication.py` 读取 `corpus-package.json`，检查完整 epoch、全局更新、来源身份与四文件断点；相邻 `transport/full_corpus_transport.py` 处理物理分块、重组和 Git/LFS 运输。它们应连同现有 fixture 迁入正式 C# 源码；本次只核查实现，没有重跑其旧验收。

技能还明确引用 `.artifacts/repository-training-encoder-full-20261011-121827/local-mirror-contract/` 和 `local-mirror-preparation/`，其中的模型/数据独立读回、分块重组和公开模型汇总也要提取。

Hugging Face 发布能力散落在 `.artifacts/model-publishing-20261009-01a12099/`，例如 `hf_publish.py`、`hf_multipart_bounded.py`、`windows_hf_credential.py`。应迁移仍需使用的 API、分块恢复和认证能力；旧浏览器探针、失败替代路径及一次性文档修补不整体重写。

现有 [Sezika.ModelTool](../tools/Sezika.ModelTool/Program.cs) 已用 C# 完成固定模型下载、Range 续传、SHA 和代理回退，可优先复用；需要补齐通用输入身份、认证及 AOT 兼容性。底座下载、训练结果回收、发布后的独立读回是三个不同操作，不能合成一个含糊的“下载成功”。

### 3.3 语料与教师工具

`tools/corpus/` 的 24 个 Python 文件需按能力合并到现有 `Sezika.DatasetTool` / `Sezika.TeacherTool` 或其共享 C# 组件：

- 来源格式读取、字段适配、准入/隔离、source/split/family 保持、规范化、去重和分片。
- `prepare_corpus.py`、`compose_corpora.py`、`bind_corpus.py`、覆盖审计与计算预算。
- 问题模板、教师队列、请求/响应账本、恢复、准入检查和收口回执。
- `bounded_start.py` / `run_guarded.py` 的启动握手与取消并入统一进程组件。

迁移需关注 Python JSON 排序/数字/Unicode 与 C# 输出的差异、gzip 头字段及 SHA、SQLite 去重键、Parquet 数据类型、归档安全和部分输出恢复。已有语料及其原始 manifest 保持不可变；新工具若产生新字节，应生成新版本与新 SHA，不能继续沿用旧身份。

`Sezika.DatasetTool` 目前引用 `DuckDB.NET.Data.Full 1.5.3`，`ExportJsonCommand.cs` 使用 DuckDB 读取 Parquet。它用于离线数据解码，不是模型推理算子；但它含外部原生依赖，C# 文件存在并不证明整个工具已经满足严格依赖边界。若将“只准系统/驱动 API”统一覆盖工具层，应将其替换为经过 AOT 实测的托管读取方案，并验证实际语料的类型和压缩格式。本次未认定某个替代包已经兼容。

### 3.4 技能与凭据

项目内需要同步修改的说明文件共 8 份：两个 `SKILL.md`、租机技能的两个 reference、仓库训练技能的四个 reference。保留技能说明形式，将入口、参数、故障恢复和验收示例全部指向正式原生 CLI；删除对历史 `.artifacts` 脚本作为可复用入口的依赖。

项目实际使用的共享代码：

| 所属技能 | 当前代码 | 项目迁移边界 |
| --- | --- | --- |
| `moark-platform-api` | `Invoke-MoarkApi.ps1`、`Invoke-BoundedProcess.ps1`、`moark_api.py`、`test_moark_api.py` | 平台查询、schema/目录、凭据读取、HTTP 和检查迁入 C#；核共享调用者再替换入口 |
| `usm-gateshell-ssh` | `scripts/invoke-ssh-bash.ps1` | Sezika 的 SSH 流程解除此脚本依赖；不因此改写整个通用堡垒机技能 |

`Install-RemoteGitCredentials.ps1` 还通过 `Add-Type` 编译 C# 片段，再生成 Bash helper。需要把其本已有的 Windows Credential Manager 调用变为编译期静态绑定，Git helper / askpass 变为原生可执行入口。继续保持凭据不进入 argv、URL、仓库、回执和日志，并保留 host/path 精确约束、stdin 协议和清理规则。

Git LFS 的 pre-push 也应纳入执行链检查：不能一边宣称无脚本，一边继续安装 shell hook；也不能通过禁用 LFS hook 来省略上传检查。需验证原生 hook/等价受控调用的实际行为。

## 4. 已有 C# 工具仍要整改的部分

主源码、工具、示例和测试共 31 个项目文件；另有 6 个历史 evidence 项目。31 个中 `tools/` 占 20 个。

**20 个工具项目中，10 个显式声明 `IsAotCompatible=true`，没有一个在自身 `.csproj` 声明 `PublishAot=true`。** 这只说明默认配置，不能据此断言它们从未使用命令行参数做过 AOT；历史证据确实记录了部分 Windows AOT 执行。

| 分组 | 工具 | 本次判断 |
| --- | --- | --- |
| 已声明 AOT 兼容分析，仍需发行矩阵 | Benchmarks、CalibrationTool、Coverage、Evaluation、IndependentNumericCapture、IndependentNumericCompare、IndependentPredict、NumericParity、OracleCapture、OracleCompare | 复用实现，统一原生发布、功能验收及依赖检查 |
| 未声明 AOT 兼容分析 | CliChecks、DatasetTool、ExperimentTrainer、FullTrainingChecks、IndependentModelTool、ModelTool、RepositoryTrainer、TeacherTool | 按工具用途补分析与原生构建；不能仅补 XML 就标 PASS |
| PTX 编译入口 | KernelCompiler、BackwardKernelCompiler | 需要独立架构验证，见下文 |

另外，`Sezika.ExperimentTrainer` 不在当前 `Sezika.slnx` 中；如果保留它，必须加入统一检查清单，或明确退役，避免解决方案全通过却漏掉长期工具。

### 4.1 具体 AOT 缺口

- [New-ToolBundle.ps1](../tools/repository-training/New-ToolBundle.ps1) 第 26 行使用 `--self-contained true` / `UseAppHost=true`，没有启用 `PublishAot`。两个被打包项目也未配置它。因此当前打包入口不能充当“服务器工具包已 Native AOT”的证明。
- [ModelTool 项目](../tools/Sezika.ModelTool/Sezika.ModelTool.csproj) 显式开启反射 JSON；其 `Program.cs` 第 276–279 行从 `object` 调用 `JsonSerializer.Serialize`，manifest 又使用嵌套 `Dictionary<string, object?>`。需要 typed DTO + source-generated JSON，或明确的 `Utf8JsonWriter` 路径。
- 两个 kernel compiler 项目同样显式开启反射 JSON；BackwardKernelCompiler 还把匿名对象存进 `List<object>`。这部分可以重构序列化，但不能解决下面的编译前端问题。
- 当前共享 `Directory.Build.props` 已是 `net10.0` / C# 14，并默认关闭 JSON 反射。此次主要工作不是升级 .NET 版本，而是消除例外、补原生交付和运行证明。

### 4.2 PTX 编译工具是严格“全部 AOT”的最大结构性问题

`KernelCompiler` 和 `BackwardKernelCompiler` 通过 `typeof(...).GetMethod(...)` 把方法交给 ILGPU `PTXBackend.Compile`。本地固定包 `ILGPU 1.5.3` 的 XML API 文档也显示 IL frontend / .NET 方法反汇编和 Reflection.Emit 相关运行时代码。

静态 CUDA Driver 执行预生成 PTX 的训练/推理路径可继续复用；但**不能因此认定用于生成 PTX 的两个编译工具本身可 Native AOT**。本次没有运行发布试验，严格状态是 `ARCHITECTURAL_BLOCKER_REQUIRES_PROTOTYPE`。

按本次要求，不给这两个自有工具自动豁免为 JIT。需要研究重构/替换其编译入口，例如直接读取受控的源文件或 PE 元数据、采用静态编译前端，消除依赖运行时方法 IL/Emit 的路径；同时验证所用编译后端能否进入 AOT。该项属于独立编译工具工程，不能估作“添加 PublishAot 的小改动”。完成前只能报告其余工具的已验收范围，不能报告全项目工具全部 AOT。

### 4.3 Python Oracle 的能力边界

`Sezika.LayaOracle/export_oracle.py` 与 `Sezika.IndependentOracle/export_oracle.py` 调用固定上游 PyTorch/Transformers；`export_tokenizer_boundaries.py` 使用 Python tokenizers。它们是参考输出生成器，不能直接改成调用 Sezika 同一路径后仍称“独立上游参考”。

按零 Python 要求，应停止这些执行入口，保留已有真实参考输出及来源/hash，再以独立 C# 数值参考、冻结 fixture 和差分检查承接以后需要的验证。若没有新的上游参考生成机制，要明确标注该能力未替代；不得伪称 C# 自比较完成了上游数值对齐。

`Sezika.Evaluation/BaselineVerification.cs` 第 492 行附近还校验 `python`、`torch_version` 等来源字段；切换后需要区分历史上游参考与新 C# 参考的元数据，不能删掉旧合同检查或给新数据伪填旧来源。

## 5. 建议保留的结构与合同

结构建议是**新增一个小型共享工具库和一个原生运维 CLI**，复用已有四类专业工具，避免每个旧脚本都对应一个新项目：

| 能力 | 建议归属 | 关键验收 |
| --- | --- | --- |
| 进程、SSH/SFTP、HTTP、hash、归档、凭据、Git/LFS | `Sezika.Tooling` 共享库 + `Sezika.Tooling.Cli`（建议名称，尚未创建） | Windows/Linux 本机失败与取消检查、完整进程回收 |
| 语料构建与核验 | 扩展 `Sezika.DatasetTool` | 固定输入的记录语义、隔离、数量、恢复与新旧身份 |
| 教师采集与账本 | 扩展 `Sezika.TeacherTool` | 已有请求不重复发送，响应身份与费用/失败语义不丢失 |
| 模型资产下载/转换/验证 | 复用 `ModelTool` / `IndependentModelTool` | 中断续传、Range 拒绝/变更、最终完整 SHA、AOT 实际执行 |
| 训练与检查点 | 复用 `RepositoryTrainer` / `FullTrainingChecks` | 全量合同、STOP/CANCEL、四文件恢复和独立验证 |
| 发布与独立读回 | 原生运维 CLI 调用受控发布组件 | 预览、精确目标/ref、LFS 实物、认证及匿名读回分开报告 |

必须保留或显式版本化的合同：

1. 数据、源码、模型的 commit 与 manifest 身份；source/split/family/许可及泄漏隔离。
2. `CorpusRunPlan`、`corpus-package.json`、四文件 checkpoint/cursor、全局更新/EOF/完整 epoch 语义。
3. 传输分块清单与逻辑文件 SHA；物理上传成功不能替代重组验证。
4. 进程身份、启动握手、deadline、取消、payload exit 和 cleanup 结果；历史 `UNKNOWN` 不能自动升级为成功。
5. Git credential stdin/get/store/erase 协议、host/path 白名单、可见性、发布预览和远端读回。
6. 旧 JSON/TSV 回执需要只读兼容或明确转换旁车。新输出字段用显式 DTO；不覆盖历史原件及其 SHA。

标准 SSH/SFTP、HTTP 和 Git/LFS 协议无需重发明。改的是本项目的客户端与调度实现、受控命令入口以及输出合同；不能把远端 Bash 字符串当作新的“协议”继续保留。

## 6. 改动量与完成判据

粗估以**98 个非 evidence 脚本候选 / 14,872 行**为主体，再加任务目录中被确认仍需使用的能力、20 个 C# 工具项目及 8 份项目技能说明。174 个 ignored 文件含复制品、旧版本和修补脚本，不能与主体行数直接相加当作精确开发工作量。

工作量风险：进程监护与跨平台回收、SSH/认证、Git/LFS 发布恢复、语料格式兼容为高；普通 hash/归档/检查命令和技能引用替换为中低；严格 AOT 的 PTX 编译链为最高且需原型定界。未完成去重和编译前端原型前，不给精确工期承诺。

完成时至少要有以下证据：

- 维护中的源码/技能/任务模板/分发包没有脚本文件、内嵌脚本或生成解释器入口；旧证据按明确归档范围报告，不能声称整个 Git 历史无脚本。
- 每个保留的工具列出目标 RID、源码与产物 SHA、AOT 构建结果及实际 smoke。不得用自包含/JIT 或只检查项目属性代替原生可执行文件。
- Windows/Linux 分别验证；Linux 目标在相应构建环境产生原生包，并在实际受支持 Linux 环境运行，不以 Windows 构建或 WSL smoke 代替服务器验收。
- 从准备、获取输入、训练启动/恢复/安全停止、打包、上传到独立读回，实测过程中没有脚本解释器参与项目流程，包括 helper/hook/askpass。
- 用冻结小样本覆盖损坏文件、LFS pointer 未物化、路径逃逸、断网、超时、取消、过期租期、PID 复用和失败后清理。真实模型/数据/GPU流程另列结果。
- 不通过忽略 IL 告警、开启反射后门或隐藏脚本调用来满足构建；不把训练成功、AOT 成功、模型质量及发布成功混称。

## 7. 证据与复查方式

| 证据 | 观察 | 导出的改动 |
| --- | --- | --- |
| `git ls-files` 与逐文件行数；随附 CSV | 282 个 tracked 脚本；另有 ignored 和共享技能依赖 | 分类迁移与退役；扫描不能只看 `src/` |
| `New-ToolBundle.ps1` + 两个项目文件 | 当前默认打包为自包含应用，没有 AOT 开关 | 原生发布矩阵和包身份重建 |
| `ModelTool/Program.cs:276` 及项目 JSON 开关 | 反射序列化例外 | 显式类型与 source-generated JSON |
| 两个 compiler 的 `PTXBackend.Compile` 入口；本地 ILGPU 1.5.3 API 文档 | 依赖 ILGPU 编译前端 | 严格全 AOT 的独立架构工作包 |
| 两个项目技能及 `local-mirror.md` | 直接引用脚本和 ignored 路径 | 技能切换到正式原生入口 |
| 当前 restart/cadence/publisher/transport 目录源码 | 常驻控制、SSH、全语料发布适配器在版本控制之外 | 将仍需复用的能力正式收编 |

可在仓库根使用以下只读命令复查已跟踪文件和主要配置；统计不包含之后新增的评估文档：

```text
git rev-parse HEAD
git status --short
git ls-files -- '*.ps1' '*.psm1' '*.psd1' '*.sh' '*.bash' '*.py' '*.pyw' '*.cmd' '*.bat'
rg -n --glob '*.csproj' 'PublishAot|IsAotCompatible|JsonSerializerIsReflectionEnabledByDefault|PackageReference' tools src samples tests
rg -n --hidden --glob '*.md' '\.ps1|\.sh|\.py|\.cmd' .agents/skills
```

本次为静态范围审查和关键调用链核查，不是逐行审完全部历史脚本，也不提供新的运行验收。旧文档中的实例在线状态、性能或 AOT 成功只作为历史证据，未在此次重新验证。
