# 全量训练准备与源码交付（2026-10-12）

本轮按用户要求提交、推送源码，并准备完整可训练语料的固定实验。没有购买或连接新 GPU 实例，没有启动正式训练、批量教师调用或发布数据/模型。

源码首批提交 `275d064f37145a7a31962bae4d30af2aa1cf09aa` 已普通推送到 `origin/main`，独立远端 ref 相同。它包含来源转换、组合语料、流式 C# epoch/恢复调度与教师题库采集入口。历史失败、未验收质量和无关未提交文件保留。

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

## 交付与新实例门槛

[训练包准备器](../../tools/repository-training/Prepare-FullCorpusDelivery.ps1)按准确输入逐文件核大小/SHA，生成私有运输清单与 pending 计划；[交付合同](../training-full-corpus-delivery-2026-10-12.md)说明完整长度、来源政策、源码工具包、容量、profile 与租期检查。Linux 工具包另固定真实分词/序列构造、trainer、CUDA/PTX/ABI 源码身份。

没有新实例的真实 UUID、Linux 路径、GPU/driver/磁盘预检和租期时，计划保留 null 与 Pending；不得沿用过期服务器。完整训练从全量 `TrainRecords × Epochs` 计算全局更新数，可按 stage 保存完整 encoder/head、AdamW、cursor 后恢复。当前仍是一记录一更新，需要新实例真实 profile 才能估算租期，不能按旧 48 步结果声称全量训练完成或准确率提高。

私有输入和详细回执根为 `C:\Users\mysti\.codex\artifacts\sezika-full-training-20261012-01a12b4c`。正式 GPU 全量训练、独立质量、校准、新权重 AOT/性能分别保持未执行或未验收。
