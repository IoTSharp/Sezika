# 固定题集原始 logits 数值诊断

对固定 SHA-256 的 JSONL 题集，按同一请求依次运行 scalar、SIMD、CUDA 后端，记录每个候选的原始 marker logit、绝对/相对误差、token 数和失败码。只比较实际成功返回 logits 的行；超预算行不会伪造数值。模型加载器核对固定权重及 tokenizer hash。此工具不衡量语义正确率，成功退出仅表示三后端存在可比较的长输入样本，不表示误差已通过发布门槛。

```powershell
dotnet tools/Sezika.NumericParity/bin/Release/net10.0/Sezika.NumericParity.dll <model-dir> <eval.jsonl> <output.json> <records-1..64> <timeout-seconds> <long-input-tokens-1..1024> <dataset-sha256> [skip-records]
```

`skip-records` 从零开始，仅选择完整冻结文件中的指定连续行；工具仍对整个 JSONL 核 SHA-256，报告保留原始 ID 和从一开始的行号。先用 1 条短输入试运行，再选择 257–1024 token 范围内的样本；`long-input-tokens` 是已实际推理的完整序列长度下界，最大 1024。256 是独立 prefix 内容预算，不能继续当作完整序列或 decision head 的长度上限。

工具通过当前生产引擎复用 `PromptSequenceBuilder`，明确使用 `strict`，凡需要 instruction、option 或 state 截断的输入均拒绝；不会为取得长样本悄悄切换兼容截断。报告 schema v2 写入 `RenderingVersion=sezika.prompt.laya-4066d5d5.v2`、长度策略和 256/1024 两种预算，旧输入渲染版本的测量不能替代本次结果。候选 logits 以标签关联比较；本工具比较 Sezika 后端内部一致性，独立 Laya 数值验收仍使用 oracle。

整体期限 1–1800 秒、每次请求 30 秒，最多 64 条、跳过与读取累计最多 10000 条，支持 Ctrl+C 与逐题进度。运行时须通过 `tools/Invoke-BoundedProcess.ps1` 设置外层墙钟超时并记录进程身份；比较结果需与模型、题集、数值容差及参考实现 oracle 一起审核。

## 固定 oracle 的逐层诊断

新增 `--trace-oracle` 模式按明确的 1–3 个 case ID，顺序运行 scalar、SIMD、CUDA，使用生产输入构造器及类型化引擎。参考与合同必须分别提供完整 SHA-256；真正进入 backend 的 token IDs、markers、候选标签和顺序必须与参考一致。logits、概率及预测使用原冻结容差，近并列也必须保持离散预测一致。

```powershell
dotnet <isolated-build>/Sezika.NumericParity.dll --trace-oracle `
  .artifacts/models/laya-mmbert `
  tests/fixtures/laya-oracle/reference.cpu-fp32.v1.json `
  tests/fixtures/laya-oracle/contract.v1.json `
  <new-report.json> choice-en-short scalar,simd,cuda 1800 `
  bf0d537305149672f07d34dc6b3e2d3614aeb2c26aa6d11aa7902578100c53fa `
  771ca781c52f1d6f9cb22859bed007ef483a54a2543c644de33f88d6e0687eee
```

模型执行须有相应授权，并通过有界进程 runner。先验证单条短输入，再选择最多 3 条；不能把这个命令示例当成已执行证据。每 case 的三个后端串行，整体 1–1800 秒、单次请求最多 5 分钟，Ctrl+C 取消；已有输出文件拒绝覆盖。

诊断保存 embedding norm、每个 encoder layer hidden、encoder final、两个 head layer hidden 及 scorer logits 的全张量误差摘要与哈希。只保留当前 case 的 scalar 快照，预检最多 128 MiB；后端回调产生的临时张量与模型工作区另计。每个预期 checkpoint 必须出现且只出现一次，形状、有限性或缺失检查失败会阻止本次诊断通过。逐层差异是 C# 后端内部定位信息，尚无独立上游逐层参考和冻结的逐层验收阈值，不能宣称逐层 oracle 对齐。

报告 `selected_diagnostics_passed` 只表示所选三后端输入、输出数值与 trace 完整性通过。`full_s306_gate_passed` 保持 false；真实近并列为 0 时 `near_tie_acceptance=not_measured`。已有 46 条参考中近并列为 0，不通过合成 logits、同文本候选或自比较补造真实近并列证据。更多诊断边界见 [S3-06 数值诊断](../../docs/s3-06-numerical-diagnostics.md)。

`--trace-self-test` 仅运行有界诊断逻辑自检，覆盖近并列预测翻转、阈值超限、非有限值、概率归一化、形状错误、trace 缺失/重复、内存上限及取消。合成值只验证比较器，不计入模型推理或近并列验收。
