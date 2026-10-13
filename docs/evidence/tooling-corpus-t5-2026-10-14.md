# T5：C# 语料、教师队列与研究资料处理

日期：2026-10-14（Asia/Shanghai）。本项按独立索引候选构建并验证，未提交 T2/T3/T4/T6 尚未验收的实现。当前旧入口撤销仍由 T7 分项关闭，正式教师、GPU训练、模型/数据发布及质量均未执行。

候选 `.artifacts/tooling-migration-20261013-01/t5-candidate/source-v2` 来自准确索引导出。23个候选文件共277,896 bytes，实际编译字节/SHA256与索引blob逐项相同，清单在 `compiled-source-manifest.json`。共享Program、MigrationCommands、检查入口和项目文件只暂存本项12个corpus命令及DatasetTool接线；工作区保留后续模块。历史预算计算依赖的纯JSON数值校验Helper随本项进入源码，未混入完整验证调度器。

## 实现范围

- Corpus的类型化准入、格式适配、规范化、去重、恢复、组合、冻结队列、HTTP教师协议、准入账本、数据版本及研究元数据处理。
- 分片同时约束行数、压缩/未压缩字节；逻辑上限512MiB，去重索引使用任务专属DeleteOnClose临时文件。源/split/family/许可、原始题文、候选与标签身份、原始行/展开题目和未处理分母分别保留。
- 多题行部分成功/失败保持原始行分母；恢复不覆盖闭合分片、旧账本和失败。教师POST结果未知时不自动重复，只有明确闭合回执才进入完成集合。
- DatasetTool复用既有DuckDB读取Parquet，新增纯C# SharpCompress XZ读取，固定包版本及长度/字节/取消上限。未执行dataset loader或Python/XZ脚本。
- research/seed/completeness/inspect/estimate/bind均有明确输入身份与范围；静态footer计数不是实际特征计算。estimate只做冻结历史时序的情景算术，不宣称当前吞吐、收敛或费用。

调用与旧入口归属见 [模块说明](../../tools/Sezika.Tooling/Corpus/README.md)。协议 fixture 的响应和显式合成记录不能成为正式模型输出、人审标签、训练准入或质量证据。

## 独立候选实测

| 检查 | 实际结果 | 回执（相对候选目录） |
| --- | --- | --- |
| Tooling/检查项目Release构建 | 零warning/error | `tests-v2-build-result.json` |
| DatasetTool Release构建 | 零warning/error | `dataset-v2-build-result.json` |
| Corpus tiny / 完整 | 1 / 38 PASS，完整0.478秒 | `corpus-v2-{1,38}-result.json` |
| 纯C# XZ | 5 PASS | `xz-v2-result.json` |
| 实际corpus CLI及generated JSON | PASS，历史4次更新/2.5秒均值，10次情景保守公式1440秒，当前吞吐/GPU/rental标志false | `cli-estimate-outer-result.json`、`cli-estimate-command-result.json` |

38项包含来源适配、split/family隔离、严格许可与SHA、分片与去重、截断/损坏/超限拒绝、取消/恢复、教师格式失败与unknown POST不重复、真实本机HTTP socket、prompt原始字节、冻结研究身份、partial物理行分母、Parquet footer与加密格式拒绝、Sys1Cal/S1MB outcome映射与分歧保留。原始payload/子树回收均由独立原生执行器确认RemainingProcesses=0。

第一版独立候选遗漏CorpusEstimate使用的ValidationJson纯Helper，构建CS0234失败保留于 `tests-build-result.json`。补齐该实际依赖后导出新source-v2，再构建和完整验证；未覆盖第一版回执或把后续通过倒填旧失败。

这些证明覆盖本项源码、Windows JIT、纯文件/HTTP协议与输入合同。完整真实外部语料的新版本准备、真实教师调用、正式全量GPU训练、语言质量/校准、新权重推理AOT/性能及平台完整发布仍按各自门槛，不能由38项本机fixture替代。
