# 仓库训练与完整编码器反向工程实证

日期：2026-10-11；状态：离线工程通过，可以租用新 RTX5090 做真实底座 profile。真实 mmBERT 完整训练、正式质量、新权重 AOT/性能、真实模型/data回推均 NOT_RUN。用户目标和固定输入见[交接](../training-repository-handoff-2026-10-11.md)。

## 数值、保存与回归

本机 RTX4070 Laptop GPU，UUID `134BF256A4B04FD4E3073C3D34F79CB4`。全部GPU训练检查为极小合成模型，不能外推真实mmBERT/5090显存或吞吐。

- Choice/Boolean/Score全图中央差分共1008参数坐标，各336，epsilon0.0001；阈值0.0003+0.5%relative，最大绝对误差0.00013484732818777904。包含重复embedding token、global/local attention。首步AdamW另核独立公式，最大差0。4检查通过。
- tiny完整checkpoint/恢复7检查，完成4updates，1008权重与moments状态坐标同连续训练逐位一致，最大差0。峰值owned GPU8064bytes；缓冲、module与context回收检查通过。
- 249项既有核心回归通过。固定tokenizer oracle因既定fixture路径缺少指定资产SKIP，不能写成无skip全通过；本次真实数据长度检查使用已固定的实际tokenizer。
- 20个反向PTX/静态ABI编译完成；Linux自包含RepositoryTrainer/FullTrainingChecks构建零warning/error。纯C#代码、构建期ILGPU、运行时CUDA Driver；未使用训练框架/bridge。

原数值与恢复JSON分别在 `.artifacts/repository-training-engineering-20261011/gradient-tightened.json`、`storage-tiny/storage-checks.json`；小型永久副本见同名证据目录。

## 数据与Linux

25002原行，最终21913accepted（train17615/development4298）、3089隔离、duplicate0；最终21913/21913strict8192通过，最长7740token。第一次39个超长的完整FAIL保留。分母、类型、来源revision、ledger和SHA见交接及证据JSON。其余17来源未适配，formal_training_admitted=false，teacher概率不是实际正确率。

WSL Ubuntu真实Linux执行：

- v3冻结工具的完整SHA、help、4项tiny梯度、7项storage/resume通过；guardian真实TSV为completed/payload_exit0/final_check_complete1/cleanup_incomplete0/live0/unknown0/unchecked0。
- 许可证修正后的v5在Linux实际完成2行prepare→lengths→admit，附README/rights保留字节；给旧full/admitted附来源文件，旧manifest/records/ledger SHA未变。重复覆盖拒绝。旧CLI在拒绝时产生SIGABRT诊断，结果不是重复覆盖成功；未发现工具包根生成core文件。错误处理已改为打印完整异常并返回1，最终v6独立Linux执行已核返回1和拒绝原因。
- 最终工具包v6清单398文件，manifest SHA `2b40e72cee968dba9b4b7da8355ae432f8412c7cd0d3c97ea77e26b18dab0e96`。最终全部SHA、LinuxCLI/help、3个Bash脚本语法、skill quick_validate、11个文档链接、10个PowerShell解析与37个冻结源文件SHA均通过；实际回执见[证据JSON](repository-training-engineering-2026-10-11.json)，任务根也有 `preparation-summary.json`。
- 模型包附实际底座LICENSE-COMPONENTS/license-provenance、7份notices、当前数据来源与代码LICENSE/NOTICE；tokenizer精确Gemma来源未知，整体许可unknown。底座旧许可说明保留为历史，不充当本轮训练报告。

这些检查不是新租用5090上的真实底座profile。Toolkit12.8也不代表driver已核验。

## 迁移与发布模拟

file://专用本地bare fixture5检查通过：Preview、SHA篡改拒绝、普通commit/push与准确ref读回。fixture commit为 `5e591c4686378493a7e3b50088811561dfd107b0`。10MiB nested tokenizer的精确新增LFS规则Preview通过。fixture没有实际GPU模型质量或外网发布。

安全tar模拟9文件通过：唯一regular文件、路径/链接/重复/总字节/文件数边界、归档SHA和全部文件SHA。不是实际训练模型下载。发布助手只stage本任务清单；数据先/模型后、独立kind validation、属性前后SHA、普通LFS hook与远端漂移拒绝合同已具备。真实授权push及匿名全量读回仍待真实训练结果。

## 失败与收口边界

初期C#命名/保护/JSON生成器构建失败、fixture v1/v2失败、快速PID采样造成CleanupFailed均保留原回执；成功修复另存，不改写失败。原日志根为 `.artifacts/repository-training-engineering-20261011/process-logs`，各bundle/fixture也保留独立日志。

最终补审131份回执、266条登记身份、245个唯一PID，matching_live0/unknown0，265条已退出、1条PID已复用。它只证明记录身份在查询时已退出或PID被其他创建时间复用，不能补造早期未观察的历史argv/子链；历史4个root身份未及时观察的局限保留。按登记PID、创建时间、命令行、父链查询，未按名字杀进程、未停止用户进程。

源码未commit/push。原dirty的诊断文档、Benchmarks/IndependentModelTool、研究/文章等未暂存或改写；旧ROADMAP诊断hunk保留，HANDOFF.md字节未改。旧服务器未连接，模型/数据真实仓库未推送。
