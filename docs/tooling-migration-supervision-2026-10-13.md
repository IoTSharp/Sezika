# 工具迁移：新会话的全量编码监督合同

日期：2026-10-13（Asia/Shanghai）。用户已明确授权：提交推送当前改动，创建新会话，由主智能体监督多个子智能体并行，把路线图中全部工具迁移编码逐项完成；每个任务单独提交，定时推送，全部编码范围完成后才结束。实际训练与租用服务器工作另行执行。

**2026-10-15 最新执行范围覆盖下文的持续推进/停止条件：** 用户要求“这一批完成后提交代码并推送，然后剩余工作放到路线图中”。本批实际验收为 T2 SSH 上传预检重构的 Windows10应用/35检查，以及当前工具 Windows11应用/102检查，分别见[SSH](evidence/tooling-batch-close-2026-10-15/ssh-upload-preflight.md)和[补验](evidence/tooling-batch-close-2026-10-15/windows-supplement.md)。Ubuntu重启后当前临时运行材料和部分原件丢失；当前native9、production capture4、crossOS4、Linux协议10和工具Linux10/98明确未验，原native9 FAIL和资源审计UNKNOWN保留，恢复及补验列ROADMAP。Root 对通过的Windows限定任务分别提交，连同既有已验收提交最多执行一次普通 `origin/main` 推送，再独立核远端 SHA。102能力门终签、最终 caller 冻结和真实466项 checker 等剩余工作也列入 ROADMAP，不扩展下一批；本批交付及远端验证完成后暂停 `sezika-4`。不能宣布整个工具迁移或 T8-code 已完成；服务器实测仍为 NOT_RUN。

唯一阶段计划为 [ROADMAP](../ROADMAP.md) 的 **T0、T1a 调用者切换、T1b、T2–T7、T8-code**；本文只规定执行、归属与完成口径。T1a 基线源码提交为 `4d02942`。新会话先核实际本地/远端 HEAD，不把本文的短 SHA 当作最新远端状态。

## 编码范围及完成定义

- 长期工具、服务器程序、训练调度、数据/教师处理、模型下载、上传、发布准备与独立读回、相关技能所需实现全部迁为 C# / .NET 10。复用现有实现，功能与合同不靠脚本包装、动态释放脚本、空实现或固定成功回执代替。
- 训练、离线准备和构建工具允许 JIT/自包含 .NET 10；PTX 继续允许构建期 ILGPU 生成。推理部署保持独立 Native AOT 门槛。ILGPU v2 升级属于可选研究，不是本次迁移编码的前置任务。
- 每项须完成实际实现、调用者接线、必要的失败/取消/恢复检查、文档/技能切换及受控提交。全部当前入口需要有迁移或退役证据；`IMPLEMENTED_PENDING_CALLER_SWITCH` 不能算最终 DONE。
- 当前代码可验证的 Windows/Linux、本机模拟、回环服务、fixture、AOT 适用路径全部做完，不能仅写接口、TODO、NotImplementedException 或“等待租机以后再写”。真实目标访问受限时，先完成协议实现和本地集成；明确保留现场验收的 NOT_RUN。
- T8-live 的租机、开机/续费、正式训练、真实GPU吞吐/长稳、批量教师、模型/数据发布、外部平台可见性修改、生产部署不在本次授权内。源码向已配置 GitHub origin 的普通推送已授权。不得把代码上传授权扩大为模型发布授权。
- 历史一次性脚本与原始证据不能改写成新成功；先提取长期能力，撤销当前调用。必要历史原件仅作为明确的不可执行证据保留。不能删除训练数据、冻结包或检查点，也不能以“都在 artifacts”作为无需迁移的理由。

## 主智能体和并行工作

采用 **1 个主智能体 + 最多 3 个同时工作的子智能体**，遵守实际并发槽上限。主智能体负责拆分、接口合同、依赖调度、共享入口接线、独立复核、最终测试和全部 Git 写操作。子智能体只实现其明确拥有的文件，不能自行 stage/commit/push/merge。

建议首批在依赖允许时并行：

| 子工作方向 | 可分配的独占范围 | 先决条件与共享边界 |
| --- | --- | --- |
| 执行器/远程传输 | T1b 的进程生命周期模块及其专属检查；完成后承接 T2 | 主智能体先确定执行合同；不能同时改 Program.cs、共享 JSON context、方案文件或根文档 |
| 资产传输/发布 | T4 的 HTTP Range、分块/重组、manifest、发布预览及本地传输 fixture | 先实现不依赖未完成执行器的纯文件/HTTP层；真实 Git 写入由主智能体接线 |
| 语料/教师 | T5 的格式适配、准入、规范化、队列和账本；随后承接 T6 的检查迁移 | 保持原数据合同和分母；不启动真实教师/训练；共享 DatasetTool/TeacherTool 入口归属先固定 |

这只是工作方向，主智能体必须根据实时任务状态重新划定准确文件列表。共享 `Budget`、`StartupGate`、`Program.cs`、`ToolingJsonContext`、项目/解决方案、ROADMAP、HANDOFF、CHANGELOG、技能索引由主智能体串行修改。接口变化先通知相关子任务，不能让多个智能体同时重写同一文件。

每个子任务提示必须重复：PowerShell7、数量与墙钟双界、可取消、进展报告、受限工具发现、PID/启动时间/完整命令行/父链登记、仅回收本任务进程树和准确临时路径、禁止 Graphify。每轮子任务最多45分钟，先交付一个可审查切片；达到轮次期限只触发汇报/拆分/重新分派，不代表放弃整个任务。用户未指定总截止时间，不虚构完成日期。

主智能体至少每10分钟复核各工作者的实际进度与依赖。编译、AOT和共享输出目录由主智能体调度，避免多个构建同时写同一 obj/bin。默认单个重构建、`-m:1`、关闭共享编译与构建服务器；测试若并行必须使用独占输出目录和明确资源上限。GPU/远程实训不是本次并行槽的工作。

## 提交、推送和持续接续

- 每个完成并验收的任务独立 Conventional Commit，标题绑定对应 T 编号或明确子交付。主智能体只暂存准确拥有路径/文档段落，不使用 `git add -A`，不夹带其他 dirty。
- 每个 commit 前核当前 index、完整候选 diff、必要测试与秘密/缓存检查；完成后核提交文件列表。多个子任务的未完成代码不能一起进一个“全部完成”提交。
- **每30分钟检查已验收提交与源码状态**；2026-10-14/15 heartbeat 的最新执行范围规定：本轮最多一次普通 fast-forward push 到既有 `origin/main`，随后独立读回准确远端 SHA。当前最终调用者与能力门尚未完成，已验收的独立提交先保留本地，收口后统一推送；网络不确定先读回，不重复变更。没有实质变化或新提交时安静继续，不为定时提交或推送未通过检查的修改。
- 普通 Git/LFS 运输和现有 hook 在替代实现通过前保持，不用关闭 hook/force push 绕过检查。直连失败按机器规则仅该次调用使用本机代理；网络写入超时先读回远端实际 ref，避免重复变更。远端分叉时只整合已核实变更，保留工作树，不自动 reset/clean/stash。
- 定时接续使用绑定新会话的独立 heartbeat；旧 `sezika`、`sezika-2`、`sezika-3` 是已暂停的历史训练/巡检，保持暂停。每次唤醒先确认同一主任务/工作者状态，避免重复启动、重复提交或多个推送者。
- 主智能体在仍有本次编码工作时继续推进，不在完成某个切片后宣布整个目标结束。上下文切换或定时接续从已写入的 HANDOFF/ROADMAP/回执继续。只有编码范围全部 DONE、最终源码推送验证完成，才结束并停用本目标 heartbeat；现场 NOT_RUN 清单单独交付。
- 无变化/正常推进的 heartbeat 保持安静；有实质交付、完成、失败或需要用户处理的事项才通知。

## 现有工作树与起点

规范工作区 `D:\source\Sezika`，分支 `main`，远端 `https://github.com/IoTSharp/Sezika.git`。新会话使用同一 local 项目，不自行新建第二个源工作区。

已有训练优化 dirty 分布在 `src/Sezika.Cuda/`、`src/Sezika.Kernels/`、`tests/Sezika.Cuda.Diagnostics.Tests/`、`tools/Sezika.BackwardKernelCompiler/`、`tools/Sezika.RepositoryTrainer/`、`tools/repository-training/New-ToolBundle.ps1`，以及 CHANGELOG/HANDOFF 内对应旧段落；包含未跟踪的新调度/停止/迁移检查源码与 PTX。这些没有混入 T1a 提交。若它们是本次迁移必需的依赖，主智能体须先单列审查和验证任务，再做独立提交；不能宣称已发布，也不能覆盖或丢弃。

`.playwright-cli/`、`docs/blogs/`、旧 label-audit 材料、WeChat 草稿及与本次无关的文章不是本次代码任务，不纳入提交。训练现场证据可用于理解接口，但不据此把新流程的现场状态标 PASS。

第一步读取 AGENTS、ROADMAP、HANDOFF、本文件、[466条迁移清单](evidence/tooling-native-aot-inventory-2026-10-13.csv)、[T1a说明](../tools/Sezika.Tooling/README.md)和[首批证据](evidence/tooling-csharp-migration-2026-10-13.md)。T1a 已有 Windows/Ubuntu WSL 各32项和 Windows AOT 两命令证明；旧短进程 CIM 观测失败被保留，不能变成未来监护器的容错后门。

本轮交接准备的本机回执位于 `.artifacts/repository-training-tooling-submit-20261013-01/`，含提交前32项检查、远端预检、候选路径与 Git 操作身份。新会话接管后，旧会话停止源码和 Git 写入，只保留交接观察。
