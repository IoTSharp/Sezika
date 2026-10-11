# 专用语料与完整训练准备（2026-10-12）

本轮按用户授权使用三个子智能体并行处理来源与格式复核、教师题库及训练程序，主任务独占数据整合与验收。准备于北京时间 2026-10-11 开始，接续处理于 2026-10-12 完成。没有购买或连接租赁服务器，没有开始本轮正式 GPU 训练，没有提交、推送或公开原始及派生数据。

## 来源覆盖与答案语义

母集合 75 来源中 74 下载完整，3,492 个资产、37,870,718 物理行；原行数含载体副本、语言版本和跨配置重叠，不是独立题目数。119 组完全相同文件含 147,818 个额外物理行；语义和跨源近重复仍未全部复核。

已审查镜像固定源 commit `d19515802d31310b4050226b51ab85c60f130d42`，18 来源中 16 个有当前适配路径，另两个是评测/校准参考。原始 train 完整读取，只导出可明确表示为 Choice、Boolean 或 Score 的问题；原 test、development、calibration、OOD 不当训练监督。ToolACE 原始 unsplit 的派生 family holdout 单独声明，不冒称原生 train/test 隔离。

原始人工类别、ordinal 等级、偏好对、标注比例、教师分布及 Stockfish 引擎参考均保留不同 LabelKind/LabelSemantics。MASSIVE/Banking/CLINC 的 gold 加最多 31 个负候选是训练派生选择任务，不能当原始全类分类基准；工具选择不代表参数或自由文本生成。许可证未知、评测用途、索引或处理收据、未知适配与不合规候选均有明确待审/隔离理由。

另外补查 29 个仅有 catalog 的来源：17 个提供带任务/标签的样本，两个只读到索引或处理收据，六个无确认的原 train 载体，一个归档前缀不属于 train，三种格式失败保留。小样本字段检查不能升级为完整来源准入，见 [逐来源覆盖与真实字段证据](../research/corpus-source-coverage-2026-10-11.md)。

## 完整转换与组合身份

私有任务根：`C:\Users\mysti\.codex\artifacts\sezika-corpus-20261011-01a12b4c`。

| 产物 | 物理规范记录 | 当前说明 |
| --- | ---: | --- |
| `full-reviewed-v1` | 16,262,908 | 16 个镜像来源的适配部分全部读取，源不适配行另入 quarantine |
| `quartz-full-v1` | 2,684 | 2,696 个原 train 行，扣除冲突账本后可结构消费 2,672 条 |
| `arc-full-v1` | 3,369 | 3,370 个原 train 行，1 重复、0 冲突；声明 CC BY-SA 4.0 与原 notices 保留 |
| `phase2-recovered-v1` | 7,898 | 仅恢复历史隔离中的明确 Stockfish Choice；扣除 6 冲突后 7,892 条 |
| `combined-corpus-v1` | 16,276,841 | 18 个实际来源，扣除 15,337 条唯一排除后 16,261,504 条；尚需全量严格长度及质量准入 |

主转换三阶段：第一次在 6,209,709 条处达到边界，第二次在 14,670,091 条处触及单文件解码期限，第三次恢复完成 16,262,908 条。两次原失败、闭合分片及阶段 manifest/exclusions/quarantine 全部保留。第一次没有活动原行游标，第二次重放依靠 SQLite 去重，因此累计 duplicate 统计包含恢复重放，不能把它解释为独立来源重复量。后续恢复有原行游标。

四根组合输入 manifest/bindings SHA 在 `composition-inputs-v1.json` 固定，主 SQLite 只读并核 SHA/分母。主 347 分片使用经过原 inode/bytes/SHA 检查的硬链接，补充 13,951 条物理记录经过排除和跨根 exact key/未旋转目标比较后保留 13,933 条，新增跨根 exact duplicate/conflict 均为 0。补充已排除的 18 条不再次写入。原源文件保持不可变。

最终 combined manifest SHA：`8421a5adb63e79f752865edecff49db653a51eb82f37fac4aa0ffa4c062dcabe`。

最终 bindings SHA：`257f87fbeb3ae009b46a72baa780029dc95fc541cea760e39cdc3412edeaeb75`。

来源原 manifest/bindings/rights/assets 与 README/LICENSE/CITATION/NOTICE 按原 bytes/SHA 归档于 components；来源资产清单在本地复算并绑定，rights 与 SourceAssets 的用途分开。组合没有重建完整 merged SQLite index，不能直接把该输出作为下一轮组合 primary；C# reader 可消费固定分片。

完整 C# 流式检查 `combined-csharp-check-v1.json` 已通过，实际遍历 353 个分片、16,276,841 条物理记录，排除 15,337 条；有效 train 为 **14,637,199**、development 为 **1,624,305**，共 **2,411,660** 个声明家族。已检查声明家族的 split 隔离，但跨来源语义家族、近重复及原基准泄漏仍需独立复核。guardian 回执实耗 124.713 秒、退出 0、登记进程全部退出；状态明确为 `PASS_structural_streaming_only`，formal/token/GPU 均未接受。

有效记录按题型为 Boolean 13,789,884、Choice 2,194,913、Score 276,707。civil_comments 与 go_emotions 两个来源合计占 train 的 **84.01%**，前者单独占 76.56%。它们把原评论展开为多个属性判断，并非每条都是独立通用问题；Boolean 固定候选为 false/true，训练目标 false 的平均质量分别为 96.56% 和 95.80%。这里统计的是监督分布，包含软目标，不能称为正确率。逐源轮转保证完整覆盖，小源耗尽后仍由大源主导；正式实验前还需明确任务/来源/类别权重，避免把全部记录等权混合当作通用能力方案。

## 题库与教师

`teacher-queue-v3` 共 16,347 题、1,027 家族：16,080 条合成题和 18 个上游来源的 267 条样本。Choice 7,521、Boolean 6,619、Score 2,207；英文 8,290、中文 8,040、mn-MN 3、语言未确定 14。队列按家族分为 train 15,083 / development 1,264，尚不是代表性封存测试。八类合成结构覆盖规则阈值、预算算术、日期、文档证据、证据冲突、关系链、区间重叠及工具路由；语言改写和换序不当作新的独立家族。

独立规则重算 8,400 个标签、候选映射、两语言等价、family split、源 provenance、隐藏 gold 和资产 SHA 检查通过。上游样本只取未排除派生 train；部分来自 pilot，明确保留 pilotOnly。私有参考标签与教师可见输入分别存放，未生成代表性封存测试已验收的声明。

沿用既有 `sonnet_vip / gpt-6.1-sol / max` 配置。该身份是网关声明，未独立验证实际上游模型。两批教师各 24 题，总 48 题校验完成，43 条可答、5 条按合同拒答，与规则参考一致 48/48。43 条可答中 39 条来自 train、4 条来自 development；旧试采集均写为 `experimental_candidate` 的原回执保留，后四条仍只作开发验证，不能成为训练监督。Score 一致性指可答性与等级 argmax 一致，未检验教师分布校准。以上仅为采集流程验证，人工审核/正式训练准入仍待完成。10 次真实 HTTP 请求均 200，完整请求/响应/哈希保留；凭据仅在内存，未执行整个题库的大批量教师调用。

后续 collector 已修复 split 准入：只有可答且参考一致的 train 标记训练候选；development/test/calibration/OOD 单列参考用途，非法 split 在 HTTP 前拒绝，保留原 Split；教师解释明确不作 gold/执行授权。`question-teacher-split-admission-check-v1.json` 的 12 个离线场景通过，旧两轮四份记录/报告前后 SHA 全部一致，正式训练准入仍为 false，没有新增教师调用。

## 程序与训练门槛

新增 C# 离线 Parquet 解码、规范 gzip 流式 reader、manifest/bindings/SourceAssets/排除 SHA 检查、有界兼容导出、逐源完整 epoch 调度、64 位全局游标、阶段预算、完整 encoder/head 与 AdamW 双 moment checkpoint、最终 cursor 原子 marker、独立验证及私有来源元数据包。训练算子、调度和模型仍是 C# / 静态 CUDA Driver/PTX，Python 与 DuckDB 仅用于离线数据处理和外部教师采集。

构建、调度恢复、组合和语义检查分别保留本轮回执。最终整树 `final-corpus-trainer-build-v4-portable` 零警告/错误，`integrated-corpus-scheduler-final-v4.json` **42 项** CPU/gzip/负面检查通过，包含跨 epoch 恢复位置、heldout/冲突排除、SHA/计划/moment/cursor 篡改拒绝、deadline 释放 reader 及来源便携映射。语义检查 11 项、组合 9 项亦通过。两份最终 guardian 均 Succeeded、live=0、unknown=0。小型调度测试的 `RealCorpusValidated=false` 表示该测试未运行真实全 epoch 调度；真实语料的全量结构遍历由独立 `combined-csharp-check-v1.json` 证明。历史和当前构建不能替代完整 GPU 训练/恢复、模型质量、校准、AOT 与性能验收。

最后交叉复核保留了原来源包失败，并补修精确的 `SOURCE_LICENSES.md` 及 Windows 反斜杠路径在跨平台取名/访问的问题；原 manifest、历史相对路径和全局 SHA 不变。实际 43 项 source_notice_bindings 加 4 个核心描述文件，共 47 文件/4,022,423 bytes，逐项 SHA/尺寸通过；最大文件 2,177,854 bytes、最大目标深度 10，均在 512 项/16 MiB 单文件/128 MiB 总量/depth16 上界内。353 分片路径使用 `/`。组件排除账本含逐题身份，整个来源元数据包保持私有；不存在公开发布准入。Linux 真实运行和 GPU 完整模型打包仍未运行，路径合同检查不能冒称已经通过。

严格长度已实测：ARC 3,369/3,369、Quartz 未排除 2,672/2,672、phase2 未排除 7,892/7,892 通过。16 来源 pilot 的 605 条中 594 通过、11 条超过 8,192-token 合同，失败全来自 Jev clean50k，并保留记录。完整 1,600 多万条语料的 token 准入尚未完成，不能将这批规范数据称为立即可正式训练；超长或无法表示记录需要单独的派生/排除合同，不能静默截断。

本机 RTX 4070 Laptop 约 8 GiB 可以完成当前数据整理；旧真实最长输入全参数 profile 曾使用约 11.19 GiB，当前训练代码明确限定 RTX 5090 / 32 GiB / CUDA Driver 12.8 或更新版本。新实例仍需重新 profile。单题单 update 的百万/千万级完整训练还受到吞吐限制，详见 [历史计算预算及其适用范围](../research/full-corpus-compute-budget-2026-10-11.md)。没有编造租价、训练工期或自行租机。

新模型同题集正确率 119/256（46.48%），旧模型 126/256（49.22%）；当前数据准备没有新增学生准确率结果。完整训练是否改善质量必须由未泄漏的独立测试、题型和语言切片、少数类召回、换序一致性、拒答和校准分开验证。概念与后续实验见 [准确率与数据策略](../research/model-quality-and-data-strategy-2026-10-11.md)及 [运行说明](../training-corpus-runbook-2026-10-11.md)。

## 资源与保存

任务日志记录 guardian/root/子进程 PID、创建时间、完整命令和父链、超时、取消及最终回收。最终 `resource-audit-final-v2.json` 核 103 份回执、234 个登记身份，当前存活 0；以准确来源回执 PID 核 `go` marker 后清理 40 个专属 gate，保留交付物和失败。最初审计把 marker 内容误认作字符串 `go`，因此 v1 保留了目录；已按实际 guardian 写入 PID 修正并先跑 3 回执/4 身份 preview，再执行 v2，不覆盖原回执。5 份历史观测不完整的回执继续保留，当前登记身份不活跃不能补写历史漏观测为通过。只查询本任务登记身份，不按名称杀进程，不扫描整机；既有 dirty、旧模型和旧发布不改。最终检查 owned diff/新增文件无 whitespace 或冲突 marker，未提交或推送。
