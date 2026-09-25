# 固定题集原始 logits 数值诊断

对固定 SHA-256 的 JSONL 题集，按同一请求依次运行 scalar、SIMD、CUDA 后端，记录每个候选的原始 marker logit、绝对/相对误差、token 数和失败码。只比较实际成功返回 logits 的行；超预算行不会伪造数值。模型加载器核对固定权重及 tokenizer hash。此工具不衡量语义正确率，成功退出仅表示三后端存在可比较的长输入样本，不表示误差已通过发布门槛。

```powershell
dotnet tools/Sezika.NumericParity/bin/Release/net10.0/Sezika.NumericParity.dll <model-dir> <eval.jsonl> <output.json> <records-1..64> <timeout-seconds> <long-input-tokens-1..1024> <dataset-sha256> [skip-records]
```

`skip-records` 从零开始，仅选择完整冻结文件中的指定连续行；工具仍对整个 JSONL 核 SHA-256，报告保留原始 ID 和从一开始的行号。先用 1 条短输入试运行，再选择 257–1024 token 范围内的样本；`long-input-tokens` 是已实际推理的完整序列长度下界，最大 1024。256 是独立 prefix 内容预算，不能继续当作完整序列或 decision head 的长度上限。

工具通过当前生产引擎复用 `PromptSequenceBuilder`，明确使用 `strict`，凡需要 instruction、option 或 state 截断的输入均拒绝；不会为取得长样本悄悄切换兼容截断。报告 schema v2 写入 `RenderingVersion=sezika.prompt.laya-4066d5d5.v2`、长度策略和 256/1024 两种预算，旧输入渲染版本的测量不能替代本次结果。候选 logits 以标签关联比较；本工具比较 Sezika 后端内部一致性，独立 Laya 数值验收仍使用 oracle。

整体期限 1–1800 秒、每次请求 30 秒，最多 64 条、跳过与读取累计最多 10000 条，支持 Ctrl+C 与逐题进度。运行时须通过 `tools/Invoke-BoundedProcess.ps1` 设置外层墙钟超时并记录进程身份；比较结果需与模型、题集、数值容差及参考实现 oracle 一起审核。
