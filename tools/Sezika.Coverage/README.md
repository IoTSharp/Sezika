# 固定题集 token 覆盖率诊断

该工具只加载固定版本 `model.json` 与 `tokenizer.json`，校验 tokenizer 和题集 SHA-256；不加载权重，也不运行模型。schema v2 与生产引擎共用 `PromptSequenceBuilder`，把 **256-token prefix 内容预算**与 **1024-token 完整序列预算**分开。prefix 预算不包含 BOS/EOS 等固定开销，不能将完整序列与 256 比较。每条记录分别检查 strict 与 laya_compatible 两种策略；Choice 保留输入属性顺序，Score 保留数组顺序，Boolean 语义顺序固定为 `false → true`。

`TokenEligible` 仅表示所选策略下的请求结构和 token 构造通过，不表示推理成功或答案正确。默认 strict 拒绝 instruction、option、state 或最终序列的任何 token 丢失；laya_compatible 按固定上游规则截断并保存诊断。两种策略仍应用公开请求验证器的候选范围等限制；不会因为 oracle 支持更宽范围而扩大产品请求边界。

报告字段：

- `StrictEligible` / `CompatibleEligible` 及对应逐题失败码保存两种口径；`FailureCode` 为所选策略的首个生产结构/构造拒绝码。
- `RequiredTokens` 是 instruction、option、state 均未截断时的总长度。`RetainedTokens` 是兼容构造实际保留的总长度，不代表模型执行过该行。
- `PrefixUnclipped` 表示 instruction 和 option 不需要裁剪；`TotalFitsAfterPrefixClipping` 表示按上游 prefix 规则构造后，state 无需超出完整序列预算。两者取代旧版混用整序列与 head 256 上限的 `HeadFits` / `EncoderFits`。
- `Diagnostics` 保留原始/保留 instruction、option、state token 数、截断方向与起始偏移、mask 清理；`CandidateLabels` 保留兼容构造的实际候选顺序。
- tokenizer 单个文本片段采用 32768-token 诊断保护上界；超过该上界或不能构造时长度为 `null`，`MeasurementFailure` 保留实际构造错误码。这是诊断工具保护，不能视为模型的完整序列预算。

schema v2 的 `RenderingVersion=sezika.prompt.laya-4066d5d5.v2` 与旧结果不同，旧版覆盖率不能直接复用。

```powershell
dotnet tools/Sezika.Coverage/bin/Release/net10.0/Sezika.Coverage.dll <model-dir> <eval.jsonl> <output.json> <dataset-sha256> <records> <timeout-seconds> [strict|laya_compatible]
```

末参默认 `strict`，显式覆盖该次诊断的策略口径，不读取题集中的策略字段。最多 10000 条，整体期限 1–1800 秒，Ctrl+C 取消，逐条检查取消并每 10 条报告进度；每次 builder 还有独立 10 秒上限。运行必须经 `tools/Invoke-BoundedProcess.ps1` 设置外层墙钟超时并记录进程身份。先用 `records=1` 核对输出，再在相同固定题集上运行完整记录数。题集和模型文件来源、版本由调用者独立记录；此工具不会自动下载文件。
