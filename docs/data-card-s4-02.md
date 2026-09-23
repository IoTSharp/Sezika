# S4-02 中英决策数据卡

状态：`fixture_only`。本数据卡和 JSONL 文件建立数据契约、许可、来源与切分边界，不代表 Sezika 模型已经完成多语言质量验收。

## 资产与许可

- 数据集标识：`sezika-s4-02-en-zh-decision-fixture`。
- 记录由 Sezika 贡献者为本仓库原创，随项目 Apache-2.0 许可发布；没有从网页、用户日志、第三方数据集或模型输出复制文本。
- 没有 teacher 生成、翻译或改写数据，也没有个人可识别信息。`source_entity` 是审计用的来源实体标签，不是现实个人或组织。
- 清单、逐文件 SHA-256 和 prompt schema 版本记录在 [dataset-manifest.json](../data/s4-02/dataset-manifest.json)。

## 内容与任务

数据覆盖 `en`、`zh` 两种语言，`general`、`support` 两个领域，以及 `choice`、`score`、`boolean` 三种 primitive。每条记录包含有界 `state`、`instructions`、`criteria` 和人工标签；它可以作为冻结 encoder head trainer、温度拟合和评估器的输入适配层。

记录是短小的协议夹具。它们不模拟真实用户分布，不包含模型 logits、概率、预测、准确率或拒答结论。未下载或运行真实模型时，不能用这些记录计算并发布质量分数。

## Split 与泄漏约束

每个语言/领域组独立保存 `calibration.jsonl` 与 `test.jsonl`。校准记录使用 `sezika-authoring/<language>-<domain>-calibration` 来源实体，测试记录使用不同的 `sezika-review/<language>-<domain>-test` 来源实体；两个 split 不共享 `id`、`source_entity` 或记录文本。校准日期早于测试日期，日期仅作为夹具的切分元数据。

profile 只能绑定 `calibration` split；`test` split 只用于最终质量报告。任何跨语言、跨领域合并必须新建 manifest 并重新冻结 profile，不能把合并后的分数回填到既有 profile。

## 已知限制与后续门槛

当前每个文件只有三个记录，低于质量报告要求的最小测试规模；这是有意保留的 schema fixture。S4-03 的门槛要求每个 profile 至少 100 条独立测试记录，并要求在固定模型、tokenizer、prompt、primitive、语言、领域和 split 上测量 accuracy、macro-F1、NLL、Brier、ECE、coverage、selective risk，以及 score 的 MAE。质量报告必须提供原始输入 hash、运行版本和失败原因。

本数据卡不授权把模型预测作为执行许可，也不改变模型、tokenizer 或第三方资产的许可。新增真实数据前必须补充来源实体、授权文本、去标识化说明、时间窗和重叠检查结果。
