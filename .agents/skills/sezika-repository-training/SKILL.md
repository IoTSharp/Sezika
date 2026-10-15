---
name: sezika-repository-training
description: 在 Sezika 项目内准备完整准入语料的私有 Git/LFS 交付，使用 C#/.NET 10 固定输入、调度全量 GPU 训练、保存恢复与独立验证；适用于离线准备或已授权的新实例任务。
---

# 仓库驱动的 Sezika 训练

使用 [C# 调用合同](../../../docs/tooling-current-workflow.md) 和根 ROADMAP 的当前验收状态。所有长期工具、数据/教师处理、传输和调度使用 C#/.NET 10；训练、离线及构建工具可 JIT，自包含通过不能宣称 Native AOT 推理部署已验收。旧技能原文按 SHA 保存在 [历史快照](../../../docs/history/tooling-skills-20261014/manifest.json)，只用于追溯。

先读 AGENTS、ROADMAP、HANDOFF、Git/index/dirty 和该任务真实证据。保护其他改动与旧模型、数据、失败回执。本技能不授予租机、开机、购买、正式训练、发布、可见性变更或历史改写的权限；使用当轮已有授权，离线准备不自动连接旧实例。

默认全量流程是完整准入 corpus 原字节先入私有数据仓库，固定 PublishedDatasetCommit 并独立认证 fresh Git/LFS 读回，随后服务器直接拉取明确派生入口。本机不默认中转大语料或权重；源 DatasetCommit 与发布提交分开记录，不能重写 provenance。保留原文、source/split/family、许可、排除账本和完整分母；development/test/calibration/OOD 不回流训练。

1. 按 [仓库与许可](references/repositories.md) 核准确 source/data/model ref、实际权限、逐文件 byte/SHA 与来源许可；既有 ref 是历史基线，不跟随默认分支漂移。
2. 用 Tooling `task prepare` 创建唯一任务及配置预算；用 `corpus bind`/既有 RepositoryTrainer 固定完整 CorpusRunPlan。实际 PublishedDatasetCommit、清单 SHA 与独立读回另绑定，不覆盖原来源提交。
3. 用 `bundle build` 的完整工程输入 manifest 和冻结快照构建；包内含准确 PTX/ABI、工具及许可。用 `archive pack/extract` 独立核完整清单。实现与命令接线见 [执行顺序](references/execution.md)。
4. 取得有当轮授权的新实例与准确截止后，使用固定主机键的 RemoteTool 和 Native AOT worker；只执行明确 C# payload，不传任意远端命令。先完整输入读回、宿主/磁盘检查、真实 GPU/driver/UUID/使用者及同卡最长完整序列 profile，再准入训练。
5. `task resident` 绑定 prepared-task、真实 profile、完整 Inputs 和前驱四文件断点。STOP 安全保存、CANCEL 硬取消分别处理。阶段上限只推进同一全局计划与 AdamW/cursor；不足预算先保存并报告剩余，不能自行缩全局分母或续租。
6. 保存后按 [本机镜像与读回](references/local-mirror.md) 核新产物。模型/数据发布仅执行已批准的准确 payload；private 原始及派生训练记录不进入公开模型，公开报告只保留经审核的汇总。

[训练方法](references/methods.md) 区分全参数、独立 head 与未实现方法。全量完成需固定 TrainRecords×Epochs、所有来源 EOF、完整四文件 checkpoint 与独立读回；本机 controller simulation、短程阶段、SHA通过、教师一致率均不代表真实 GPU 全量训练、质量、校准、AOT 或性能通过。

GPU 并发1。每个原生调用和批次有项目数/墙钟、取消、进度及任务 PID/出生身份/完整 argv/父链；先 tiny 再完整范围。只回收核身份的任务子树和准确临时路径。工具发现限 PATH/配置/已知路径，不调用 Graphify。互联网直连失败仅该调用通过控制机本地代理重试；网络写不确定先读实际 ref。历史服务器、租期和发布授权不能沿用。
