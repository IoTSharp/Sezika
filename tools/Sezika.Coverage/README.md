# 固定题集 token 覆盖率诊断

该工具只加载固定版本 `model.json` 与 `tokenizer.json`，校验 tokenizer 和题集 SHA-256；不加载权重，也不运行模型。它与生产引擎共用 marker 序列构造方法，分别尝试实际 head 预算和显式的 32768-token 诊断上界。`RequiredTokens` 是完整 BOS、EOS、候选 marker、说明、候选和状态的精确 tokenizer 长度；超出诊断上界时为 `null`，并标注 `diagnostic_token_cap_exceeded`。

`TokenEligible` 仅表示请求结构与生产 token 预算通过，不表示推理成功或答案正确。`HeadFits`/`EncoderFits` 单独表示精确长度是否可放入相应上限。`FailureCode` 是按生产验证、序列构造顺序取得的首个拒绝码。逐题结果与 Choice、Score、Boolean 分组一起写入 JSON。

```powershell
dotnet tools/Sezika.Coverage/bin/Release/net10.0/Sezika.Coverage.dll <model-dir> <eval.jsonl> <output.json> <dataset-sha256> <records> <timeout-seconds>
```

运行必须经 `tools/Invoke-BoundedProcess.ps1` 设置外层墙钟超时并记录进程身份。先用 `records=1` 核对输出，再在相同固定题集上运行完整记录数。题集和模型文件来源、版本由调用者独立记录；此工具不会自动下载文件。
