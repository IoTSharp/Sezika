# 固定 Laya 参考归档

2026-10-15 当前补充：Linux 真实 Numeric26 已独立收口为26/26、884736元素、零失败，仍为 port-derived、upstream=false。见 [Linux Numeric26 证据](../../docs/evidence/tooling-linux-real-numeric26-2026-10-15.md)。下方2026-10-14 T8追加中的 Linux Numeric NOT_RUN 是该阶段原状态，保留供追溯；它不覆盖后续新证据，也不把 Laya 的1/46提高为全量通过。

当前通过 [OracleCapture](../Sezika.OracleCapture/README.md) 执行相同输入的真实 C# 捕获，再由 [OracleCompare](../Sezika.OracleCompare/README.md) 对既有冻结参考作只读比较。使用 [C# 调用合同](../../docs/tooling-current-workflow.md) 的固定目标和有界进程；实际 tokens、markers、logits、概率及失败须来自实际实现，不能复制参考数值。

当前 C# 参考生成入口为 [ReferenceTool](../Sezika.ReferenceTool/README.md) 的 `laya` 和 `tokenizer` 命令。tokenizer 已在固定资产上完成267项边界检查；Laya 输入46项和后处理42项已验。2026-10-14 后续取得并核实原始五个模型资产，由原 ModelTool 生成170个tensor的清单；独立 C# reference 与 Windows typed SIMD 各完成 `choice-en-short` 单条真实前向，并通过显式该ID的数值比较。新结果使用 `port-derived.v2` 合同，覆盖1/46、缺45条，`full_manifest_passed=false`、`upstream_executed=false`；不属于新一轮上游Python执行。七类深层证明负控及资源闭环见 [当前单条正控证据](../../docs/evidence/tooling-oracle-capture-final-2026-10-14.md)，早先输入/tokenizer等阶段记录见 [参考证据](../../docs/evidence/tooling-reference-final-2026-10-14.md)。后续Linux单条与Native AOT范围见下方T8追加，全46条新C#前向仍未验收。

## 历史证据

原 S3-08 开发期 Python/PyTorch oracle 直接调用固定 Laya 0.3.20 的输入、模型和解码实现。2026-09-25 的原始证据先完成1条试运行，再完成 **46/46 条真实参考捕获：42 条 answered、4 条非法输入 failed**。冻结的六个直接依赖版本已在该次加载和推理中验证；独立参考捕获不等于 C# 数值对齐、语言质量或校准验收。

已版本化的真实输出见 [reference.cpu-fp32.v1.json](../../tests/fixtures/laya-oracle/reference.cpu-fp32.v1.json)，命令、进程、完整哈希和边界观察见 [执行证据](../../docs/evidence/laya-oracle-2026-09-25.md)。

详细字段、容差与限制见 [设计说明](../../docs/laya-oracle.md)。输入与合同分别位于 [cases.v1.json](../../tests/fixtures/laya-oracle/cases.v1.json) 和 [contract.v1.json](../../tests/fixtures/laya-oracle/contract.v1.json)。这些文件只有原创输入和事先冻结的工程门槛，没有模型输出或正确答案标签。

原 [tokenizer 微型导出器](export_tokenizer_boundaries.py) 不加载权重，使用原 `tokenizers` 与固定 tokenizer hash，覆盖全部249个 added tokens和原创边界文本，共267条，保存有/无 BOS/EOS 的结果。原范围上限300条、内部30秒与外层40秒，保留1条试运行及完整输出证据；这些是历史条件，不能作为现行复现入口。已冻结结果见 [边界fixture](../../tests/fixtures/tokenizer-mmbert-boundaries.v1.json)，它用于验证 C# 分词实现，不属于语言质量数据。

### 原固定条件

- [source-lock.json](source-lock.json) 固定 Laya 提交 `4066d5d5fbf08b66c6757ddeedbd797bd7655bc0`、已锁定模型 revision 和五个资产的 SHA-256。源代码必须来自调用方提供的干净本地 Git checkout，不自动 clone、安装或下载。
- 使用独立 CPython `3.12.10` 环境；[requirements.txt](requirements.txt) 固定六个直接数值依赖。这个组合已完成本次真实捕获，但尚不是带 wheel hash 的完整传递依赖 lock。导出会验证版本并保存整个环境的包版本、PyTorch 构建信息和平台。`source-lock.json` 保留测前字节及其 `prepared_not_executed` 声明以维持本次 provenance 哈希；当前执行状态以证据页为准。
- CPU FP32、单线程、单题 forward、seed 0、确定性算法；禁用 AMP、compile、FastLaya、语言温度覆盖和联网。输入总长 1024、前缀预算 256，checkpoint 温度固定为 1。
- 上游加载会修补 tokenizer config。工具先核对原始资产，在全新的任务输出目录中复制配置，权重/tokenizer 内容可 hardlink，跨卷则复制。只让上游修改工作副本的 config，不修改来源目录。工作副本保留，工具不执行删除。

### 原执行与输出合同

原始命令、准确路径、进程及完整 SHA 见上方执行证据；保留原文，不作为新执行说明。原调用上限64条、总期限1800秒，要求新输出目录与源模型、上游 checkout 分离。超时或取消只保留实际完成的 partial，不写成完整捕获；原流程没有断点续推、重试、并发导出或后台任务。

原协作取消在循环、hash、逐题和输出边界检查；原生依赖调用中的取消及任务子树回收另由原始 runner 回执证明。新 C# 替代必须独立验收这一生命周期合同，旧日志不能证明新进程已回收。

成功导出目录包含 `capture.json`、逐题刷新的 `capture.partial.json`、`run.json` 与 `model-workspace`。未完成或超时时只把 partial 当作已完成 case 的证据；外层强制终止可能使 `run.json` 仍显示 running，以 runner 结果为最终退出事实。模型加载/环境/配方生成失败是整次导出失败；上游单题校验、构造或推理异常则记录为该题 failure，不填示例 logits。

导出后的 `capture.json` 可以交给 [Sezika.OracleCompare](../Sezika.OracleCompare/README.md) 与同 schema 的 C# 导出做离线比较。C# 输入修复、C# 真实输出和数值验收须由各自的对照证据证明；语言质量另由带人工标签的数据集评估。


2026-10-14 T8 追加：Windows/Linux 三个工具的 Native AOT 发布均已完成。Linux Capture 真实单条 AOT SIMD 与独立 C# port 参考及原生 Compare 通过，覆盖仍1/46；Windows Capture 仅模型前 guard。Windows Numeric真实26项通过，Linux Numeric仅tiny1 fixture、真实26项NOT_RUN。资源审计通过范围为61份完整回执，原MP32 UNKNOWN和严格审计FAIL单列保留。准确范围见[最终证据追加](../../docs/evidence/tooling-oracle-capture-final-2026-10-14.md)。
