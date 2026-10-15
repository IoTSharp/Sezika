# 🧭 Sezika 路线图

> **工具链 C# 迁移进行中；GPU 全量训练暂停，等待算力与资金支持。** 更新于 2026-10-15。本文是唯一的当前阶段计划。

状态：✅ 已有对应范围证据 · ⏸️ 部分完成、当前暂停 · ⏳ 待完成。实现、数值、真实训练、质量、校准、AOT 与性能分别验收。

## 🔧 长期工具与技能统一为 C# / .NET 10

2026-10-13 用户更新边界：**训练、离线准备和构建工具允许 JIT/自包含 .NET 10；PTX 允许构建期由 ILGPU 生成。推理部署的 Native AOT 目标保持。** 去除长期脚本依赖是当前主线，不以训练工具全部 AOT 或 ILGPU 升级作为前置门槛。范围基线见[评估与清单](docs/tooling-native-aot-assessment-2026-10-13.md)。

2026-10-14 用户补充：项目不固定 .NET SDK 版本，`global.json` 仅排除预览SDK；目标仍为 `net10.0` / C# 14。每次构建记录实际工具版本及SHA，已有冻结候选与历史构建证明保持原字节。

2026-10-13 原全量监督授权由2026-10-15最新要求收窄为**当前批次验收、提交与推送，剩余工作留路线图**。主智能体复用最多3个不冲突工作者，每项独立提交，本轮最多一次普通推送并独立核远端SHA；交付后暂停定时接续，不展开下一批。[监督与交接合同](docs/tooling-migration-supervision-2026-10-13.md)保留归属及历史边界；正式训练及租用服务器实测另行进行。

| 顺序 / 编号 | 状态 | 如何修改 / 对应旧入口 | 完成标准 |
| --- | --- | --- | --- |
| T0 | ✅ 范围评估与分类冻结 | 466条基线全部分配工作包及迁移/证据保留处置；C# 校验严格 CSV、唯一路径、身份和分母 | [冻结分类](docs/evidence/tooling-migration-classification-2026-10-13.json)、[检查证据](docs/evidence/tooling-migration-t0-2026-10-13.md)。分类通过不代表实现或旧调用者已退役 |
| T1a | ✅ 首批实现与本机检查；⏳ 旧调用者切换 | `process audit` / `archive extract` 使用source-generated JSON、静态系统 API、预算/取消/新回执；`0e6b635`补原生回执改名降级保护 | Windows/Ubuntu各32项、Windows AOT两命令，以及两平台新增AOT审计17项通过。[首批证据](docs/evidence/tooling-csharp-migration-2026-10-13.md)、[降级保护](docs/evidence/tooling-audit-guard-2026-10-14.md)；最终调用者与全量集成另验 |
| T1b | ✅ 当前两平台执行器与新 Linux 观测回归；⏳ 最终调用者 | c5dc19f 补原生观察、父预算、即时登记与实际 exec 证明；Windows核心8clients/70检查、Linux execution tiny1/full22及Git8通过 | [v17进程证据](docs/evidence/tooling-final-v17-2026-10-15/process-lifecycle.md)。Root独核500份canonical/201个birth；原PID914484 UNKNOWN、旧失败和2个无final观察目录限制保留，不能以新通过覆盖历史缺口 |
| T2 | ✅ Git/LFS最终回环与Windows上传预检重构；⏳ native9/Linux/crossOS及最终调用者 | e741f92 完成有界preview/commit-only、160调用预算及stdin认证；Windows/Ubuntu各13clients覆盖11类；fe65e1f共享一次上传父链/目录预检，Windows10应用/35检查及case1/3/6实际负控通过 | [Git最终证据](docs/evidence/tooling-final-v17-2026-10-15/native-git.md)、[预检重构](docs/evidence/tooling-batch-close-2026-10-15/ssh-upload-preflight.md)。32MiB/512帧保持；原native9端到端FAIL不改，当前native9/crossOS未重验。原64身份UNKNOWN/失败保留，真实托管平台NOT_RUN |
| T3 | ✅ bundle/resident/安装/观察及输入准入切片；⏳ 最终调用者 | e602522只读task snapshot；c89b5bc全量input admission并强制真实resident启动前同计划/独立profile身份匹配；CPU12两平台36callbacks通过 | [输入准入](docs/evidence/tooling-final-v17-2026-10-15/input-admission.md)、[快照](docs/evidence/tooling-final-v17-2026-10-15/receipt-snapshot.md)。resident tiny1/full8两平台18项；真实GPU、吞吐与训练NOT_RUN，固定PTX/ABI与checkpoint合同保持 |
| T4 | ✅ HTTP/Range/发布准备、模型卡和完整模型运输；⏳ 最终调用者 | c501df1类型化官方wire模型卡校验、37411ba完整manifest/cache/partial/readback模型运输；既有块运输与发布预览保持 | [模型卡](docs/evidence/tooling-final-v17-2026-10-15/model-card.md)、[模型运输](docs/evidence/tooling-final-v17-2026-10-15/model-mirror.md)。两平台本机fixture通过；外部托管平台与模型/数据发布NOT_RUN，物理块与逻辑SHA、private/public分别验 |
| T5 | ✅ 语料能力、来源获取、研究身份与实际Dataset调用者；⏳ 最终caller冻结 | 314e601固定canonical/RequestedId；f83e47a多来源acquisition与research ledger；c1b03b8修复实际Parquet reader，独立两行synthetic载体及两平台14项CLI正负回归通过 | [来源获取](docs/evidence/tooling-final-v17-2026-10-15/source-acquisition.md)、[研究身份](docs/evidence/tooling-final-v17-2026-10-15/research-identity.md)、[实际Dataset](docs/evidence/tooling-final-v18-2026-10-15/dataset-reader.md)。native20/internal1两平台产品证据已完成；原FAIL/UNKNOWN保留，教师、人审、许可和训练另验 |
| T6 | ✅ 独立参考/捕获/比较及Linux Numeric26；⏳ 消费者/协调器最终接线 | `2b3ff83` ReferenceTool、`7857428` Linux exec来源门、`2407c24` Capture、`a8d1b0b` Compare；`b6fcc2a` 独立收口Linux真实26/26、884736元素及原权重/来源证明 | [Linux数值证据](docs/evidence/tooling-linux-real-numeric26-2026-10-15.md)、[单条实际捕获](docs/evidence/tooling-oracle-capture-final-2026-10-14.md)。新26结果为port-derived=true、independent=false、upstream=false；Laya仍1/46、missing45、full=false。原迟交/FAIL/UNKNOWN保留，消费者最终绑定另验 |
| T7 | ✅ 检查器、逐行/历史配方裁定与UNKNOWN保留机制；⏳ 最终能力门/caller冻结 | 621dc97单列RETAINED_UNKNOWN_HISTORY并拒绝掩盖当前调用；准确分区270 archive、191 replacement paths/102 gates、5历史UNKNOWN，共466 | [保留机制](docs/evidence/tooling-final-v17-2026-10-15/current-evidence-retention.md)。Root49+corpus30+transport23门仍待当前运行及caller逐项绑定；600条既有语义复核、18文档/8技能须最终字节重pin，不套旧192/656行，不称466已退役 |
| T8-code | ⏳ 后续整体收口 | Windows/Linux 编译、fixture/回环服务、协议适配、调度接线与完整模拟流程；补齐适用 AOT 检查 | 本批另有[Windows11应用/102检查](docs/evidence/tooling-batch-close-2026-10-15/windows-supplement.md)，不代表整体完成；仍须全部迁移编码、调用者和失败/取消/恢复验收，以及最终源码远端SHA |
| T8-live | ⏸️ 本次范围外 | 租用服务器后验证真实输入拉取、正式训练恢复/停止、GPU性能/长稳、真实模型上传与独立读回 | 另有当轮服务器/训练/发布授权与实际证据；当前保持 NOT_RUN，不阻塞编码收口，也不由本地模拟冒充通过 |

### 当前批次收口与后续工作（2026-10-15用户最新范围）

本批已分项交付 fe65e1f 的 SSH 上传预检四源重构和 Windows10应用/35检查，以及23d00cc的当前Native AOT Windows11应用/102检查。原FAIL/UNKNOWN保持；Windows补验的一次资源审计UNKNOWN1由独立出生身份证明PID复用，原审计仍为FAIL。Ubuntu重启后当前 `/tmp` 运行包/请求和部分原始回执丢失；因此本批只接受实际完成的Windows范围，不将原21套件或29项SSH计划标为全部完成。连同既有已验收提交，最多一次普通推送既有 `origin/main` 并独立核远端SHA，完成后暂停 `sezika-4`。

剩余工作保留如下，下一批从实际源码、调用者及回执重新接续：

1. **恢复当前Linux材料并完成受影响回归。** 先核Ubuntu新boot与持久资产，建立新运行根、源码/工具/预算身份并保存持久原件；不得复建旧路径冒充旧证据。补当前SSH的Linux10应用、native9、四份生产bundle捕获和四项crossOS（原29项中剩19项），以及工具补验Linux10应用/98检查。32MiB/512帧/64KiB不变，native9原FAIL保留，只有新端到端读回通过才闭合对应门。
2. **T7 的102个能力门终签。** 逐门裁定 Root49/corpus30/transport23 的当前职责、准确 C# 来源和实际运行证据；准确对应191条 replacement paths，另有270条历史归档与5条历史基线未知保留，共466。transport最终为53路径，旧60路径中的7条不得重复计入替代门。既有 PREPARE、源码交集和汇总检查数不替代逐门语义验收。
3. **T1a/T1b及T2–T6最终调用者冻结。** 文档、技能和工具 README 已分项切换到 C#；最终仍须冻结实际完整 caller 范围、重pin逐文件及逐行正文、复核历史/禁止/负控/metadata类别和当前执行引用。588候选/65命中文件/894行仅为当前预扫描快照，不套用旧600行标签或旧位置；任何后续编辑重新生成受影响的pins。
4. **运行真实466项 C# 退役检查器。** 在上述来源、门和 caller 准备完毕后实际执行 `inventory review`，目标为461 READY与5 RETAINED_UNKNOWN_HISTORY；历史UNKNOWN分母及原FAIL/partial保持，READY只代表声明范围内的审查，不是删除原件或取得发布/训练授权。最终结果、资源和源码身份独立保存后才闭合T7。
5. **T8-code整体收口。** 将最后门与caller结果绑定到相应提交及远端源码，更新整项迁移状态；有实际源码变化时只补受影响的构建/回归，不把本批通过或旧整包回执扩大为新整树验收。受保护的CUDA/Kernel/RepositoryTrainer等foreign dirty继续独立管理。
6. **T8-live另行授权和验收。** 服务器安装/真实输入拉取、真实GPU前后向与吞吐/长稳、正式训练保存恢复、批量教师、实际模型/数据发布及独立完整远端读回保持 NOT_RUN；不因本批源码推送自动启动。

本批已实现并分项验收只读`task snapshot`、官方wire模型卡校验、完整manifest模型运输、多来源acquisition/research ledger和`CurrentEvidenceRetention`，对应提交与有限本机证据见上表。后者仅保留无当前运行职责的历史材料，单列`RETAINED_UNKNOWN_HISTORY`；不能替代仍需迁移的功能，也不将历史未知计为READY。剩余为准确来源、实际门和最终调用者的绑定，不重复列为未实现功能。

T3的`task prepare`仍仅生成配置。c89b5bc已实现只读`task validate-inputs`及真实`task resident`启动前同计划准入，核完整corpus、来源policy/complete-scan/严格长度与底座readback；本机synthetic正负例、取消和完整分母证据见上表。CPU模拟保持独立，服务器/GPU仍NOT_RUN；对应T7门须另外绑定当前来源和调用者。

2026-10-15实际调用者修复状态：c1b03b8已删除DatasetTool的冗余SET，双平台JIT及14项实际CLI回归通过。fe65e1f的SSH上传预检四源重构已通过Windows合同与回环35项，原native9端到端FAIL不改，当前native9/Linux/crossOS待验。两平台ProcessFixture NativeAOT仅服务64MiB安装合同，既有构建/help通过按原日期保留；本次Windows安装10项已实际通过，当前Linux材料持久性与98项补验仍待恢复，不将全部21套件标为完成。

可选研究 **T-ILGPU**：官方 v2 编译管线/AOT 支持已在 2026-10-08 合入主线，但 GitHub 当前稳定 Release 仍为 v1.5.3。待稳定包/API/许可与实际 PTX、数值、恢复身份验证后再评估升级；当前继续使用固定 1.5.3 的构建期 PTX 流程，不阻塞 T1–T8。来源及限制见评估文档更新。

用户重新指定下一批后，主智能体从上述剩余项按依赖接续；未就绪依赖先固定接口，不能假接线。新实现必须有独立检查和回执，旧入口仅在对应替代验证通过后撤销；不一次性删除全部旧工具，不改写原始失败/历史证据，也不把 C# 包装器继续运行脚本算作迁移完成。T8-live 单独保留现场门槛。

## ✅ 已有基础

| 阶段 | 已有成果 | 证据范围 |
| --- | --- | --- |
| S0–S2：契约与资产 | 类型化请求、模型 / tokenizer 加载、CPU scalar / SIMD 编码器 | [架构](docs/architecture.md)、[阶段证据](docs/stage-evidence.md) |
| S3：推理闭环 | Choice / Score / Boolean、CLI、输入预算、取消和资源回收 | [独立使用](docs/standalone-usage.md)；完整数值诊断仍列入后续任务 |
| S4：训练基础 | 数据准入、真实 C# GPU 前反向、FP32 AdamW、保存与恢复 | [本轮训练与预算](docs/training-budget-2026-10-13.md)；不代表全量或质量完成 |
| S5：GPU / AOT 基础 | 构建期 PTX、静态 CUDA Driver、指定模型的 Windows / WSL2 AOT 验证 | [实测范围](docs/s5-performance-aot.md)；新权重与裸机 Linux 另验 |
| S6：开发交付 | 开发 CLI、模型格式与实验资产已有入口 | [独立模型 CLI](docs/independent-cli-s4-15.md)；正式产品发布待完成 |

## 📍 接下来做什么

| 顺序 / 原任务编号 | 状态 | 工作 | 完成标准 |
| --- | --- | --- | --- |
| 1 · S4-16 | ⏸️ | 获得算力支持，核验实际磁盘保存点及新运行窗口 | 权重、optimizer、元数据与游标完整且身份一致，恢复后真实推进 |
| 2 · S5-06 / S5-07、S3-06 | ⏸️ | 做代表性吞吐测量，继续优化 C# CPU / CUDA 计算与训练开销 | 数值和恢复验证通过；报告有效更新 / 秒及成本，不以 GPU 利用率替代提速证据 |
| 3 · S4-16 | ⏸️ | 完成全部准入语料的一轮训练 | **14,592,720 次更新**、所有来源 EOF、完整保存及独立读回通过 |
| 4 · S4-05 / S4-06 / S4-08 | ⏳ | 补齐数据许可审查、隔离、人工金标、多语言评测及概率校准 | 独立评测正确率、拒答、校准与语言切片；保留失败和完整分母 |
| 5 · S4-12 / S4-15、S6-02 | ⏳ | 正式模型包、CLI / NuGet 与新权重部署验证 | Windows / Linux 目标上的独立加载、AOT、性能和资源回收通过，模型卡及许可齐全 |
| 6 · S6-03 | ⏳ | IoTSharp 等项目试点 | 实际业务评测、边界和失败回退通过；核心仍不内置业务动作执行器 |

可选后续：其他模型对照（S4-09）、教师数据及人工审核（S4-10 / S4-11）、LoRA 等适配研究（S4-13）。这些研究不替代当前全量训练目标。

## 💰 恢复所需预算

- 最近可核完整保存点 **45,056 / 14,592,720，约 0.31%**；停机后是否存在更新保存点待核实。
- 按当前单卡约 4 更新 / 秒，剩余约 **960–1,014 GPU 小时 / 40–43 天连续运行**。
- 按公开 **4 元 / 小时**计，剩余 GPU 费用约 **3,800–4,100 元**；当前可用算力余额 **0 元**。
- 未包含最终独立验证、存储及其他费用；语料长度、优化和计费变化均需重估。详见[预算依据](docs/training-budget-2026-10-13.md)。

## 📏 保持的边界

- 完整训练保持准入语料、来源与 split 隔离；不把抽样或短跑称为完整一轮。
- 当前采用一条记录一次更新；批处理、并行训练或优化器语义变化须单独设计与验证，不能直接假设多卡等比例提速。
- 旧模型的数值 / AOT 证据不自动覆盖新权重；概率不是正确率，未经测量不称已校准。
- 服务器已关机，自动巡检已暂停。获得新的开机与运行窗口确认后再接续，不自动购机或续费。

原任务编号及详细历史证据保留在[历史快照](docs/history/roadmap-2026-10-13.md)与[更新记录](CHANGELOG.md)中；历史快照不作为当前执行计划。

**❤️ 我们需要赞助。欢迎支持训练经费、GPU 算力或参与实现与评测，入口见 [README 的赞助与交流群](README.md#sponsorship)。**
