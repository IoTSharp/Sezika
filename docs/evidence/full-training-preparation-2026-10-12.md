# 全量训练准备与源码交付（2026-10-12）

本轮按用户要求提交、推送源码，并准备完整可训练语料的固定实验。没有购买或连接新 GPU 实例，没有启动正式训练、批量教师调用或发布数据/模型。

源码首批提交 `275d064f37145a7a31962bae4d30af2aa1cf09aa` 已普通推送到 `origin/main`，独立远端 ref 相同。它包含来源转换、组合语料、流式 C# epoch/恢复调度与教师题库采集入口。历史失败、未验收质量和无关未提交文件保留。

最终训练工具代码固定为 `d925f1c51dfe172e6f5b4ccb97408a5281440bf6`，已经普通推送并独立核对远端。中间提交 `56e3fd4b` 增加完整准入工程；`d925f1c5` 修复交付准备器对真实 C# 扫描回执和排除账本的接线，19 项真实格式与原夹具回归通过，包括序列化省略零计数字段。准备器 SHA 为 `730aa214c7b3b58bf6c1a2fd7e537584bf772d6b2187c741f14bae08782c2da2`。

## 输入与来源范围

- 原组合语料 manifest：`8421a5adb63e79f752865edecff49db653a51eb82f37fac4aa0ffa4c062dcabe`；bindings：`257f87fbeb3ae009b46a72baa780029dc95fc541cea760e39cdc3412edeaeb75`。
- 共 18 个已适配来源、353 个 gzip shards、16,276,841 条物理规范记录。原排除 15,337；记录数包含同一原问题的属性展开，不等于独立题目数。
- 本轮私有来源政策固定 17 来源、34 个原始资产，SHA：`57dfc53a758ad32d7ebcc0cb3b426a80e7da40e6f24be01d1bf7bb13b31655ed`。扣除原排除及来源隔离后、超长排除前 train 14,592,758、development 1,619,175。
- `samatv256/jev-decisions-v1` 的逐 repository 组件许可未验，整个来源暂隔离；Open-Jev 仅允许已核对的十个生成配置。具体合同见[来源报告](../research/full-training-data-admission-2026-10-12.md)。
- 固定数据仓库 commit：`d19515802d31310b4050226b51ab85c60f130d42`；模型仓库 commit：`e2e5bc97d62dfbd4e2bbd070542324bb544e6b32`。使用原始 encoder/tokenizer，新决策头从零初始化。

原 test/development/calibration/OOD 不回流为监督。全部 75 个下载来源不自动变成可训练来源；尚未适配或无训练/组件权利的来源不纳入本轮。提供的 exact family 隔离与跨来源语义泄漏是不同门槛，后者仍待全面审查。

## 新增工程与离线验证

长度扫描每 4,096 行保存完整分段回执，固定 corpus/tokenizer/policy/分段身份；最多 32,768 分段、50M 行、1M 排除，逐阶段有项目数、墙钟、取消和进度限制。中断保留已完成分段，恢复不重新分词已完成前缀。未完成、损坏、身份不一致或非长度渲染错误不得成为完整 PASS。

只有明确的 token budget 错误可成为超长排除。原排除、来源排除和长度排除分开记账；完整 EOF、所有分母和 family 隔离通过后，另建不可变派生语料，原 shards 字节保持。政策、扫描完成回执、父合同和第三方 notices 都绑定 SHA。

分词器新增可选有界 BPE 缓存，默认运行时仍使用原行为。核心构建零警告/错误；微型 merge/byte fallback/added-token、命中/淘汰、严格长度、取消和有限并发检查通过。固定真实 tokenizer `197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356` 的 267 个多语言/added-token 文本，两轮含有/无 special tokens 的缓存 ID 与原路径完全一致。核心共 284 项通过；原 Laya tokenizer oracle 因其原资产缺失仍明确 SKIP，不能借用这次缓存一致性当上游 oracle。

来源策略 24 项 tiny 检查通过，包括 exact asset、原 split/revision/SHA、未知配置、来源隔离、损坏策略及 policy/scan 绑定防篡改；fixture 已精确清理。

扫描恢复 21 项端到端 tiny 检查和调度/来源包 46 项检查通过，覆盖中断分段、禁止重复分词已完成前缀、最终账本中断恢复、SHA/family/分片损坏、排除上限、非长度错误与派生父身份。原 fixture 失败保留；具体回执见[长度准入](../training-length-admission-2026-10-12.md)。交付准备器 10 项 fixture 通过；真实原输入 421 文件、3,854,272,286 bytes 全 SHA 校验通过，输出明确列出四个离线 Pending 和新实例门槛。

真实按来源有限前缀 pilot 共 576 物理行，544 接受、32 来源隔离、无新长度排除；所有 reader 早停并明确 EOF 未验证。该诊断不接受完整覆盖，不包含题目、答案或逐题 ID。

真实首段扫描 4,096 条，原排除 4 条、接受 4,092 条、来源/长度新增排除均为 0；分段耗时 7.4 秒。全量共 4,243 分段。该结果仅为首段预处理实测，不是完整长度、GPU 训练速度或质量证据。全量长度和最终派生身份以私有完整回执为准。

## 完整扫描与固定派生语料

完整扫描随后通过全部 4,243 个分段、353 个分片的 EOF、排除分母及 2,362,071 个供应 family 的训练／开发隔离检查。16,276,841 条物理记录中，原排除 15,337、来源隔离 49,571、严格超长 45（train 38、development 7），最终接受 16,211,888，train **14,592,720**、development **1,619,168**。这是规范记录数，包含同一问题的属性展开，不是独立题目数；跨来源语义泄漏与金标正确性仍需另验。

最长接受序列及最长训练序列均为 7,773 tokens，来源 ToolACE。七次有界扫描／恢复尝试累计工作 3,691.073 秒，包含最终 family 汇总；这个 CPU 预处理耗时不能用于估算 GPU 更新速度。完成回执状态为 `PASS_complete_strict_scan_explicit_exclusions_private_only`，SHA 为 `2c29156eabe1c651473172f5cd03adf8d1d4437cf1bedbbeaaf1d3b10d199e8f`。

新语料位于私有任务根的 `admitted-corpus-v1`，同卷硬链接复用 353 个原始 gzip 分片，原始数据及失败记录保留；父 manifest/bindings/原排除清单与第三方 notices 一起归档。新 manifest SHA 为 `9fdc4e15499c9b08f42d4afea3800e53af1f0ade22b4458600e323f196ab7269`，bindings 为 `6312798f19081dd29f01d627a446cdf500a4136a55f6750d46aa85d68554ac16`，完整 `lengths.json` 为 `b79247245feb6644f179dd248fcf321d4719e010d3004fcc73c575b0258a4a73`。政策、完整扫描、父身份与排除分类均在派生合同中绑定。

另由独立只读程序检查 4,243 个分段引用、353 个 EOF、所有分母、64,953 个排除身份、分类/split、父账本前缀、49 个 notice 绑定及政策与派生合同；`final-admission-independent-audit.json` 为 `PASS_independent_complete_sidecar_admission_audit`，耗时 73.719 秒，SHA 为 `2ace4f1a4e05d760ca3e33cffc51545c930de2fa13e2b54d64879c2807cf8d2d`。此审计不重读题目或重算 Family，供应 Family 隔离证据来自完整 C# 扫描；gzip 完整 SHA 由交付器另核。审计 guardian 的进程终检 live/unknown=0，专属 gate 精确清理。

最终 Linux 包为 `.artifacts/repository-training-full-bundle-20261012-d925f1c5/tool-bundle-final`，398 个清单条目、399 个实际文件，50 个工程源文件与准确代码提交匹配；清单 SHA 为 `ca927c84407729a876a5bf1b6fa9f05a304353f479862a666c5835c5ce730637`。该准确包在 Ubuntu WSL 经三次真实运行通过启动监管、24 项来源策略与 21 项扫描恢复检查，运行前后全部 SHA 不变。v3 回执为私有 `wsl-tool-bundle-smoke-v3.json`，SHA 为 `1bbab8b68374a96d0536ef93878c86d89b47f0e95bb48bd91619b9e73c88e584`；Windows/Linux 终检 live/unknown 均 0。v2 快退 WSL 根进程未及时观测的 FAIL/UNKNOWN 原样保留，v3 使用已有协作启动登记入口；没有改写旧失败。该证据只覆盖 Linux 合成 CLI 检查，不是 GPU、真实模型或 AOT 硬件验收。

## 交付与新实例门槛

[训练包准备器](../../tools/repository-training/Prepare-FullCorpusDelivery.ps1)按准确输入逐文件核大小/SHA，生成私有运输清单与 pending 计划；[交付合同](../training-full-corpus-delivery-2026-10-12.md)说明完整长度、来源政策、源码工具包、容量、profile 与租期检查。Linux 工具包另固定真实分词/序列构造、trainer、CUDA/PTX/ABI 源码身份。

最终实际交付版本为 `question-full-delivery-final-private-v2`。唯一一次真实最终准备调用通过 877 个运输文件、4,039,014,600 bytes 的完整大小/SHA 核验，累计读取含配置 4,039,055,287 bytes；helper 用时 14.089 秒，guardian 17.153 秒。`LocalDeliveryReady=true`、离线 Pending 为空；`TrainingReady=false`、`FormalTrainingAdmitted=false`。严格来源与长度筛查后的 17 个来源全量 train 14,592,720，Epochs=1、GlobalUpdates=14,592,720；development 1,619,168 独立保留。最终工具包、helper、manifest/bindings/lengths 输入在执行前后 SHA 不变。

`readiness.json` SHA 为 `9cf95fe0af4315c92caf116b5bd2d142558728fd9e55d8f12fb61bc61ac2b2af`，`transport-manifest.json` 为 `4ae82fd17b804f7fed7d053941a51344d363f9a252b860e3997288d57c3fcafc`，待绑定计划为 `0fcb82b2b253c1d5f6dda02db2dc5847c08a30e868cea137189a6ea42d5c71ea`。新实例六项 Pending 为实际 UUID、固定 Linux 路径、只读 preflight、新租期、同计划真实 profile 和远端 C# 实际准入；没有复用旧实例。

本机仅生成清单，无输入复制或归档；当时输出卷剩余 26,269,200,384 bytes，所需本机余量 1 GiB。新服务器数据盘最低估算 23,079,835,396 bytes，真实容量和峰值仍需预检/profile；完整耗时保持 null。最终 helper 外层回执 SHA 为 `a38d7de0fd4b81c2ff61a857cbc716678193828e5da008bba89290f16d0cdb09`，Succeeded、FinalCheckComplete=true、live/unknown=0；退出 gate 已精确清理。执行证据为 `question-full-delivery-final-private-v2.execution.json`，SHA `4b5dc78f5ae5096ec95a5a04c21b4a035cd32be0185e706952207ec00a5974f2`；早期 pending-v1、历史失败及模板原件保留。

没有新实例的真实 UUID、Linux 路径、GPU/driver/磁盘预检和租期时，计划保留 null 与 Pending；不得沿用过期服务器。完整训练从全量 `TrainRecords × Epochs` 计算全局更新数，可按 stage 保存完整 encoder/head、AdamW、cursor 后恢复。当前仍是一记录一更新，需要新实例真实 profile 才能估算租期，不能按旧 48 步结果声称全量训练完成或准确率提高。

私有输入和详细回执根为 `C:\Users\mysti\.codex\artifacts\sezika-full-training-20261012-01a12b4c`。正式 GPU 全量训练、独立质量、校准、新权重 AOT/性能分别保持未执行或未验收。

## 准备任务进程与临时目录收口

最终独立只读审计覆盖 12 组冻结日志、88 份原字节回执、330 条进程身份与 293 个不同 PID，10 次有界 CIM 查询在 7.598 秒内确认当前匹配存活与未知数均为 0。88 份原件及审计快照前后 SHA 一致；历史 6 份 Failed、1 份 CleanupFailed 及其中 1 份未及时观测根进程的 UNKNOWN 原样保留，当前进程不存在不改写历史状态。三个本次审计 gate 已按 PID/创建时间/argv/父链及精确绝对路径清理，39 个前序已收口 gate 均不存在；没有终止进程。

合并回执 `process-total-final-audit-v1.json` SHA 为 `58219569ff044d81e6b75c0fee0fea3071b16af0669f6fcc7f8aca8726b3a6fc`；原始 `final-process-audit-v1/full-process-audit.json` 为 `3e4d03812718e0bd13e827b9267a26dd20a50b67a2d5cb70b254a02cba9c553a`；范围及原件 SHA 清单为 `fa7de28ce635dc746a128e4412633bb0bf902d4fdc71842c46299573b123959e`。这份审计固定于最终文档提交前；后续源码文档 commit/push 的自有 Git 进程与回执由主调度另行核对，不纳入该冻结范围。
