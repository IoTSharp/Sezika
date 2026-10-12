---
name: sezika-repository-training
description: 在 Sezika 项目内先发布完整准入语料到私有 Git/LFS 数据仓库，再按固定提交执行全量流式 GPU 训练、断点恢复与独立验证；用于准备全量训练或更换训练实例。
---

# 仓库驱动的 Sezika 训练

当前项目工作区为 `D:\source\Sezika`。数据、模型和源码是三个独立仓库。使用 [仓库合同](references/repositories.md) 中的已验收身份，每次再核实际 ref；使用 [训练方法](references/methods.md) 判断代码已经支持什么，禁止把旧 215 题合同套在新原始数据上。

## 当前默认：全量语料先入私有仓库

用户要求全量时，默认消费已经转换、完成严格扫描并准入的整个 corpus，不重新转换 raw，不设置抽样上限。先将完整派生目录原字节保存到既有 private 数据仓库的 `derived/<run-id>/corpus/`，正常 Git LFS push 后，以独立新 checkout 的认证完整 byte/SHA 读回验收准确发布提交。服务器随后按该真实提交克隆，只拉取该派生入口所需 LFS；本机 tar/SFTP 全量直传不作为默认数据交付路径。数据源提交与新发布提交分别记录，不为发布改写语料内原来源身份。

2026-10-12 固定输入：仓库拉取接续工具源码 `3461fc0dae689d4d471654af5d85b9850d0908a4`（51项工程源码、399个工具包文件已独立核SHA及Ubuntu68调度检查）；旧离线准备工具 `d925f1c51dfe172e6f5b4ccb97408a5281440bf6`仅保留历史身份；原数据来源 `d19515802d31310b4050226b51ab85c60f130d42`；模型来源 `e2e5bc97d62dfbd4e2bbd070542324bb544e6b32`。准入 corpus 位于 `C:\Users\mysti\.codex\artifacts\sezika-full-training-20261012-01a12b4c\admitted-corpus-v1`，407原文件含353分片、17来源；完整发布413文件/2,623,103,071bytes，实际PublishedDatasetCommit `5e180c4136f8dd72cb5f37cb8673bddcbe9bc7f5`已正常push/fresh及福海服务器完整413文件SHA通过，临时认证已清理。train **14,592,720** / development **1,619,168**。这是规范记录数，属性展开不等于独立题目数；隔离及未适配来源仍保留账本，development/test/calibration/OOD 不回流训练。

准备入口为 `Prepare-FullCorpusDelivery.ps1` 与真实 `CorpusRunPlan`，执行入口为 `profile-corpus` / `train-corpus` / `validate-corpus`，见[当前执行合同](references/execution.md)。`Epochs=1`，全局分母为 `TrainRecords × Epochs`；`--stage-updates` 只推进同一不可变计划、AdamW 与 global cursor，不减少分母。仅全局更新完成、各来源 EOF、完整四文件 checkpoint/cursor 与独立验收通过，才能报告全量 epoch 完成；48步或单阶段完成不能替代。

先完成私有数据发布，再接续新 GPU 实例。用户已暂停租用服务器；旧北京时间2026-10-12 14:30截止已过，不能沿用。旧 d925f1c5 冻结工具只准 RTX5090/32GB/CUDA12.8。本轮新增 RTX4090 候选准入：精确设备名、CC8.9、至少30GiB、Driver>=12080及固定UUID；必须重新冻结工具，在同卡执行完整8192形状前向/反向 profile，并保存设备及profile SHA绑定的 gpu-admission.json。普通24GiB 4090不准入。福海同版本closed驱动与GSP关闭已生效，但新启动仍出现Xid62/45/158，GPU稳定性失败；不宣称4090实机支持或启动正式训练。正式训练只用项目 C# GPU 路径。当前一记录一更新，按新 profile 估算整轮和保存/读回预算；不足时提前报告需续租，未经用户新授权不购买、不缩全局数据、不把短跑称全量。限时保存最后完整四文件断点与实际覆盖/未完成状态。

本轮仅授权 private 数据提交与推送，模型及公开发布未授权。下面2026-10-11的模型发布、服务器直传和历史改写授权只描述历史，不能用于本轮或未来授权。

## 准备与租机

1. 读取根 `AGENTS.md`、当前 `ROADMAP.md`、dirty/index、最新数据与模型证据。保护旧实验、失败回执、用户文件和不属于本任务的改动。新计划先记 ROADMAP。
2. 用户要求“准备好再租”时，先完成全部离线准备。旧聊天 `01a12099-57a1-7bf0-bd69-7d87e8576d61` 的服务器是历史实例，其租期已在北京时间 2026-10-10 00:22 截止；不得将旧连接信息当作新服务器默认值。本轮未取得新实例和新期限前不连接、购买或恢复旧 GPU。
3. 创建新 task，使用 `tools/repository-training/Prepare-FullCorpusDelivery.ps1` 固定完整派生语料、三个输入 commit、原始 split、许可、预算和待验收状态；另记录实际 PublishedDatasetCommit。任务目录不能复用或覆盖。读取[执行合同](references/execution.md)，核既有全量扫描/准入回执与全部文件 SHA。旧 `New-TrainingTask.ps1` / `prepare` 的最大百万条入口只用于历史单来源流程，不用于当前全量 corpus。
4. 当前完整 encoder 反向、AdamW 和保存/恢复已有 C# 工程及 tiny GPU 数值证据；2026-10-11 RTX 5090 已通过真实 mmBERT 三步 profile，详见[本轮实机证据](../../../docs/evidence/repository-training-real-2026-10-11.md)。profile 证明完整输入形状、反向和更新可运行，不能替代固定训练、质量或发布验收。用户选择完整微调；LoRA 尚未实现。每个新计划仍需真实底座 profile，取得显存、最长序列和耗时后再准入预算。不要安装 PyTorch、ONNX Runtime、LibTorch 或训练桥接替代项目实现。

## 历史2026-10-11实例、短跑与发布合同

本节至“有界执行与收口”之前均为历史合同及回执；其中“本轮”“当前”和已授权发布只指2026-10-11，实际新训练以本文当前全量入口和当轮授权为准。

### 历史新实例执行

- 取得本轮 SSH host/user/port/auth、可信指纹（如有）、创建时间和租期、用户目标与下载/回推预留。生成无凭据 `connection.json` / `deadline.json`，每个阶段准入覆盖全部剩余必需阶段的最坏预算。服务器只做系统、磁盘、GPU/driver/UUID/boot/现有使用者的只读预检；不结束他人进程、不 reset/reboot/更新驱动。
- Linux 在新独立任务根克隆 **源码、数据集、模型**；先按[仓库合同](references/repositories.md)核当前visibility，本轮private数据集与maikebing private模型需授权认证；IoTSharp public模型是发布目标，训练输入仍按当轮固定合同选择。固定 checkout commit，下载真实 LFS 内容，核 `ASSET_MANIFEST.json` / 模型清单与逐文件 SHA。clone 和 fsck 不能替代实际内容 SHA。不执行数据仓库带来的 `.py` loader 或任意代码。
- 先 guardian `/usr/bin/true` 探针，再合同/长度、tiny GPU、唯一训练、完整保存和独立新进程读档验证；按当轮选择执行本机下载或服务器直接发布。所有模型/算子/调度使用 C#；编码器 GPU 前向、决策头反向、编码器反向是独立门槛。新训练只从当前派生账本读数据，保持原始 train/dev/test/calibration/OOD 和 family 边界；评测标签不能调参或成为训练监督。
- 固定底座训练从零 head 开始，旧 head 是比较对象；继续旧 head 是另一种显式训练合同。完整保存 head/checkpoint/report 并核 SHA；本机保存路径优先下载新字节，服务器直接发布路径先验新完整包再上传仓库。本轮用户后来选择服务器直传，本机仅保存小报告/日志/验收证据，不等待本机大文件中转。完整模型包括实际 encoder、tokenizer、配置、head、checkpoint、报告、来源/许可及全部清单；单独 head 包不能称完整模型。

## 提交与回推

本次用户已经授权“训练完成把模型提交并推送；数据有改动也推送”。本次执行不重复询问；未来加载本技能时仍以当轮请求判断发布授权，技能本身不授予无限期自动发布权限。

- 先确定本轮运输路径：本机保存完整包，或服务器直接发布。两种都核保存卷容量、完整模型SHA、独立验证分母、源 commit和资源回收。本机路径另预算归档/解包共存及发布checkout；服务器直传路径在服务器预算发布/LFS缓存/匿名读回共存空间，本机只下载小报告和回执。模型可保存到另一个有容量的本轮专属目录，仍保持任务和run-id身份。形成可审查模型卡，分别记录实验指标、正式质量、校准、AOT/性能和发布状态。训练进程退出 0 不能自行置为正式质量通过。
- 模型新产物放在 `runs/<run-id>/`，保留历史包与根底座原字节，写版本与可消费的包入口；更新本次清单和模型卡。数据只有新增派生数据、纠错或账本时才提交到 `derived/<run-id>/`，原始镜像保持不可变；普通未跟踪缓存不算数据变更。
- 使用普通 Git LFS basic、单并发和有界请求，逐项 stage 本任务路径，提交后核准确 commit。数据若有变更先推送并读回数据 ref，再让模型报告绑定该真实数据 commit，最后推送模型。没有数据变更时模型绑定原固定数据 commit，跳过数据提交/推送。
- 本机路径使用[发布助手](../../../tools/repository-training/Publish-RepositoryRun.ps1)的 `-PreviewOnly`；服务器直传路径生成同等可审查的owned-path清单、逐文件SHA、属性前后身份及远端旧ref，再正常commit/push。两种都仅追加本次路径的LFS / `-text`规则，push后再查真实ref。超时/断线可能已经改变远端，先只读核commit/LFS状态再决定后续，禁止盲目重复写、force、关闭pre-push或悬空LFS pointer。
- 原来源文件的CRLF通过单次 `core.whitespace` 的 `cr-at-eol`识别，保持原字节；上游notices自带尾空格时，固定准确来源路径集合并逐byte/SHA保真，其余生成文件仍严格做whitespace检查。凭据helper先做真实Git dummy的空行/EOF与范围拒绝检查。提交后push失败时核真实ref、父commit、空index、owned路径及SHA，再推既有commit；已推成功则继续读回，不重复commit。完整命令与本轮故障边界见[执行合同](references/execution.md)。
- 模型和变更数据均需独立新checkout完整LFS/byte/SHA读回，按当前visibility明确认证状态：本轮private数据和maikebing private内部预发布模型授权认证读回，IoTSharp public模型匿名读回。上传、平台登记、Git ref、授权下载和公开下载分别记录；保留切换可见性前的历史snapshot，只按用户当前明确合同改变visibility。凭据从安全存储或本轮内存取得，不放URL、argv、仓库配置、清单或日志。

服务器直传时，凭据只经受保护stdin或任务内存传递；如需要RAM临时文件，先核实际tmpfs及任务专属路径、限制权限，finally精确清理。远端Git调度/hash/资产复制可使用Bash或系统工具，不能改用它们实现模型/训练算子。独立读回与写入分开运行，private读回只使用当轮授权凭据、public读回不带凭据；最终记录可见性、认证状态、服务器完整模型路径及输出commit，没有完整本机下载就明确写未下载。

组织发布是当轮显式授权的可选步骤。本轮用户最新角色是maikebing **private内部预发布**、IoTSharp **public正式发布目标**，正式名/slug `Sezika-Decision-Base`，首个48步产物为 `v0.1-preview`，仍保持实验质量状态；private dataset不镜像。先核实际组织仓库URL、当前读写/LFS权限和public可见性，从授权认证完整验证的个人模型准确commit同步完整base和本run，保持权重、来源/许可、包入口、报告与清单字节身份；组织普通push后另做独立匿名完整LFS/byte/SHA验收，分别报告两个模型仓库commit。只复制已审核模型资产，不复制private数据checkout/derived、缓存、凭据或服务器任务根。历史组织权限失败不代表本次失败，也不代表本次已成功；当前未执行时写PREPARING_NOT_PUSHED。组织预算也计入当前明确租期，不自行续费。

push前核平台单对象上限；optimizer moments超限时，仅传输层分块并记录准确原文件/每块bytes与SHA，独立读回重组后验原完整SHA才能恢复，原package/checkpoint身份和失败提交均保留。超限未发布commit不能作为新待推分支祖先继续触发旧LFS对象上传。模型介绍使用[中文模板](../../../docs/templates/model-card.zh.md)，独立写本轮实测与C#消费边界；组织新README展示介绍时精确保留和映射原base README/清单，不能覆盖原件后冒称全路径原SHA不变。

private源完整验证后再审核public白名单的实际内容。公开模型保留权重、checkpoint、optimizer、tokenizer与包身份；详细验证记录的逐题编号、候选、教师标签、预测分布和loss不得随报告复制到公开仓库，改发按Split/Type/Rotated的汇总。记录私有原报告SHA、公开summary新SHA及准确转换路径，更新所有引用/清单；private原报告保持不可变。文件名没有raw/derived不能替代字段审核。已公开细目时，先正常fast-forward修复当前树；旧Git历史和平台对象清理另核当轮明确授权与读回，详见[本机接续合同](references/local-mirror.md)。

## 本轮最终结果及继续门槛

用户后来明确授权IoTSharp单一master历史改写；最新公开提交 `9c522c4e227c52c86f493a90595a894a41e1935a` 已由一次精确lease推送完成，独立匿名fresh全历史证明旧详细报告提交与路径不再可达。当前55个owned+1个保留文件、6,174,855,372bytes全SHA PASS，tree与下列c68049汇总版本相同、文件差异0，private仓库未改，普通LFS hook保留。此授权只限本轮该准确分支/旧ref/候选，不使未来常规发布自动具有force权限。平台旧对象/缓存物理清理未执行、仍NOT_VERIFIED；正式质量/校准/新权重AOT和性能仍分别验收。完整回执见[已授权历史清理证据](../../../docs/evidence/repository-training-history-cleanup-2026-10-11.md)。

### 历史清理前的发布与提案快照（保留）

当前本机已经验收 private 模型底座18文件及本run33文件、private 数据原275资产及新8派生文件，全部实际bytes/SHA通过，原optimizer实际恢复通过。IoTSharp 当前公开提交 `c68049c735c24da5f1c611c2b229619bb4b391ac` 已正常Git/LFS推送，55个owned文件及1个保留文件、6,174,855,372bytes在原fresh匿名checkout及独立LFS缓存完成准确ref/HEAD、clean、objects/pointers和全部实际bytes/SHA验收，无private缓存复用。当前 `validation-summary.json` 仅按Split/Type/Rotated汇总，128原题加68换序共196前向；权重、tokenizer、package、训练报告、checkpoint和optimizer原SHA保持。旧公开提交 `dfef170bc9e26dff7e4317fc08686d0abd013438` 的完整运输PASS保留历史，其 `validation.json` 详细标签仍在可达Git历史；整体历史隐私收尾未完成，不能称所有任务已完成。

已准备本地历史清理候选 `9c522c4e227c52c86f493a90595a894a41e1935a`：以安全介绍提交 `2a410d64a70955364439514cba60207b8b9ee796` 为父，tree与当前验收版本完全相同，文件差异0；未推送、未授权。只有用户明确授权本次IoTSharp master历史改写后，才能以准确当前ref为lease执行；不碰private库，不改变大LFS对象。平台旧对象/缓存物理清理仍需平台另行确认，不因历史改写自动置PASS。提案回执见[本机接续证据](../../../docs/evidence/repository-training-local-mirror-2026-10-11.md)。

租期结束后，补齐模型下载、检查点恢复和仓库镜像可在本机进行，无需GPU或新租期。先从private源准确commit做授权认证的新clone和完整LFS下载，完成全部实际bytes/SHA及optimizer实际重组，再从组织当前ref正常Git/LFS推送已审核模型资产，最后用不带凭据的新clone和空LFS缓存验收。具体步骤见[本机读回与模型镜像](references/local-mirror.md)。新训练和新权重GPU/AOT验收才另行申请有效计算资源。下列PARTIAL是服务器租期结束时的历史快照；后续真实结果以[本机接续证据](../../../docs/evidence/repository-training-local-mirror-2026-10-11.md)为准。

2026-10-11本轮为 `PARTIAL_MODEL_PUSHED_READBACK_PENDING`，发布验收 `NOT_ACCEPTED`。48步完整encoder/head训练、196次独立GPU前向已完成；正式质量/校准/新权重AOT和性能未验收，完整模型未下载本机。maikebing private模型正常Git/LFS push/ref为 `e2e5bc97d62dfbd4e2bbd070542324bb544e6b32`，独立fresh HEAD一致；源完整pre-push SHA及optimizer逻辑重组通过，但fresh认证完整LFS读回480秒超时，不能升级发布通过。private数据 `d19515802d31310b4050226b51ab85c60f130d42` 已push且切private前完整匿名snapshot通过，当前fresh认证完整读回NOT_RUN。

IoTSharp public `Sezika-Decision-Base v0.1-preview` 当前为 `INTRODUCTION_PUBLISHED_MODEL_WEIGHTS_NOT_PUSHED`，仅README单次native PUT及认证/独立匿名7585bytes/SHA读回通过，实际master `2a410d64a70955364439514cba60207b8b9ee796`；完整权重NOT_PUSHED、完整匿名模型读回NOT_RUN。文档commit不是model artifact commit。未来镜像须重新核ref并以该2a410d64值为expected master，原初始c23c3c42仅历史；先完成准确个人源commit完整认证读回/分块重组，再普通组织push和独立匿名完整模型读回，private数据不镜像。

最终382文件小证据本机SHA及服务器332身份退出/凭据RAM清理通过，原FAIL保留；它们不替代三仓库完整读回。HTTP206 Range诊断仅证明运输片段和带宽，加速助手仍draft、实机NOT_RUN；本机fixture或修复不能写成支持。用户已确认本轮租期结束，最后已确认截止为北京时间2026-10-11 15:00；15:30续租建议已收口，不再等待确认或连接旧实例。当时的新实例接续建议保留为历史；当前已改走本机完整读回、optimizer恢复与组织镜像流程，无需为补齐发布租GPU或重复训练。旧服务器磁盘保存物的存续未复查。

## 有界执行与收口

Windows 只用 PowerShell 7。Bash 用单引号 here-string 经既有 stdin SSH wrapper；所有远端 shell 内容不进入 PowerShell 可展开字符串。复用 `tools/rental-training/Invoke-BoundedProcess.ps1` 和 `remote/Invoke-BoundedLinux-v2.sh`，每个循环/请求/命令有项目数、墙钟、取消、进度和 backoff；先人工检查退出比较并以极小输入试跑。GPU 并发 1，按实际资产和剩余期限决定资源预算。

登记任务进程 PID/创建或 starttick/argv/父链，只回收核身份的本任务完整子树。临时路径先解析绝对路径并核归属；保留交付物、失败目录、原始回执和用户文件。工具发现限 PATH/配置/已知路径，不扫描盘根或大目录，不调用 Graphify。互联网直连失败仅该调用尝试本机 `http://127.0.0.1:7890`；远端的 localhost 不是控制端代理，不能将这个地址直接写成远端默认代理。

用户要求多个智能体时，可以并行处理工具修复、回推准备和证据文档，先划定不重叠的文件归属。GPU 训练仍由唯一主调度执行；各智能体不另启 GPU、重复下载或写同一仓库。需要续租时报告当前绝对截止、剩余阶段的预算和需要的延长期限，不能自行购买或续费。用户明确缩小实验步数时另建计划并重新 profile，报告实际使用的数据条数，不称完整一轮。

最终报告新实例真实状态、三个输入commit、派生数据commit、模型输出commit、完整模型保存位置和所选运输路径、实际指标、每个验收门槛及仍未运行事项。服务器直传时另列本机小证据路径，不写成完整模型已本机保存。准备阶段必须写“未进行正式GPU训练/回推”，不要借用旧模型或旧服务器的成功证据。

本轮工程、数据和租机门槛见 [交接](../../../docs/training-repository-handoff-2026-10-11.md)。技能供当前项目使用；保留旧 `sezika-rental-training` 的历史固定实验合同。
