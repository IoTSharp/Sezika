# 固定 Laya 离线参考导出器

这是 S3-08 的开发期 Python/PyTorch oracle，直接调用固定 Laya 0.3.20 的输入、模型和解码实现。它不被 .NET solution、NuGet、CLI 或 Native AOT runtime 引用。2026-09-25 已在获准的隔离环境中先完成 1 条试运行，再完成 **46/46 条真实参考捕获：42 条 answered、4 条非法输入 failed**。冻结的六个直接依赖版本均通过实际加载和推理验证；独立参考捕获不等于 C# 数值对齐、语言质量或校准验收。

已版本化的真实输出见 [reference.cpu-fp32.v1.json](../../tests/fixtures/laya-oracle/reference.cpu-fp32.v1.json)，命令、进程、完整哈希和边界观察见 [执行证据](../../docs/evidence/laya-oracle-2026-09-25.md)。

详细字段、容差与限制见 [设计说明](../../docs/laya-oracle.md)。输入与合同分别位于 [cases.v1.json](../../tests/fixtures/laya-oracle/cases.v1.json) 和 [contract.v1.json](../../tests/fixtures/laya-oracle/contract.v1.json)。这些文件只有原创输入和事先冻结的工程门槛，没有模型输出或正确答案标签。

## 固定条件

- [source-lock.json](source-lock.json) 固定 Laya 提交 `4066d5d5fbf08b66c6757ddeedbd797bd7655bc0`、已锁定模型 revision 和五个资产的 SHA-256。源代码必须来自调用方提供的干净本地 Git checkout，不自动 clone、安装或下载。
- 使用独立 CPython `3.12.10` 环境；[requirements.txt](requirements.txt) 固定六个直接数值依赖。这个组合已完成本次真实捕获，但尚不是带 wheel hash 的完整传递依赖 lock。导出会验证版本并保存整个环境的包版本、PyTorch 构建信息和平台。`source-lock.json` 保留测前字节及其 `prepared_not_executed` 声明以维持本次 provenance 哈希；当前执行状态以证据页为准。
- CPU FP32、单线程、单题 forward、seed 0、确定性算法；禁用 AMP、compile、FastLaya、语言温度覆盖和联网。输入总长 1024、前缀预算 256，checkpoint 温度固定为 1。
- 上游加载会修补 tokenizer config。工具先核对原始资产，在全新的任务输出目录中复制配置，权重/tokenizer 内容可 hardlink，跨卷则复制。只让上游修改工作副本的 config，不修改来源目录。工作副本保留，工具不执行删除。

## 获准执行后的命令

以下为后续复现模板；本次实际使用的路径和命令见执行证据。依赖安装、源码 checkout 准备和真实推理需要各自已授权的范围。不要在 Sezika 产品 Python 环境中安装这些包。

先用第一条短 Choice 输入试运行并检查进程日志、fixture 内容与循环边界，再增加至完整清单。输出必须是父目录已存在的全新目录；不要指向源模型或上游 checkout 内部。

```powershell
pwsh -NoProfile -File tools/Sezika.LayaOracle/Invoke-LayaOracle.ps1 `
  -PythonPath 'D:\explicit-oracle-venv\Scripts\python.exe' `
  -UpstreamDirectory 'D:\explicit-checkout\laya' `
  -ModelDirectory 'D:\source\Sezika\.artifacts\models\laya-mmbert' `
  -OutputDirectory 'D:\source\Sezika\.artifacts\laya-oracle-smoke' `
  -MaxCases 1 -TimeoutSeconds 180
```

通过极小输入的真实检查后，使用另一个新目录设置 `-MaxCases 64`、`-TimeoutSeconds 1800`，按清单实际数量导出；64 是硬上限，当前清单少于该数。如果全量 CPU 导出超时，只保留已经完成的 partial 证据，不能写成全套通过。当前工具没有断点续推、重试、并发导出或后台任务。

`-CancelFile 'D:\explicit-path\oracle.cancel'` 允许外部通过创建该文件取消；Ctrl+C 也会取消。循环、hash、生成探测和逐题操作检查墙钟期限与取消标记，并报告进度。原生 Python/PyTorch 调用可能暂时不响应协作取消，因此必须使用此 PowerShell 入口的仓库有界 runner，它记录进程身份和父链并在外层超时/取消后清理任务子树。runner 的短命中间进程采样限制仍见它自己的报告。

成功导出目录包含 `capture.json`、逐题刷新的 `capture.partial.json`、`run.json` 与 `model-workspace`。未完成或超时时只把 partial 当作已完成 case 的证据；外层强制终止可能使 `run.json` 仍显示 running，以 runner 结果为最终退出事实。模型加载/环境/配方生成失败是整次导出失败；上游单题校验、构造或推理异常则记录为该题 failure，不填示例 logits。

导出后的 `capture.json` 可以交给 [Sezika.OracleCompare](../Sezika.OracleCompare/README.md) 与同 schema 的 C# 导出做离线比较。C# 输入修复、C# 真实输出和数值验收须由各自的对照证据证明；语言质量另由带人工标签的数据集评估。
