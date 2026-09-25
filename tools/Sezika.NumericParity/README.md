# 固定题集原始 logits 数值诊断

对固定 SHA-256 的 JSONL 题集，按同一请求依次运行 scalar、SIMD、CUDA 后端，记录每个候选的原始 marker logit、绝对/相对误差、token 数和失败码。只比较实际成功返回 logits 的行；超预算行不会伪造数值。模型加载器核对固定权重及 tokenizer hash。此工具不衡量语义正确率，成功退出仅表示三后端存在可比较的长输入样本，不表示误差已通过发布门槛。

```powershell
dotnet tools/Sezika.NumericParity/bin/Release/net10.0/Sezika.NumericParity.dll <model-dir> <eval.jsonl> <output.json> <records-1..64> <timeout-seconds> <long-input-tokens-1..256> <dataset-sha256> [skip-records]
```

`skip-records` 从零开始，仅选择完整冻结文件中的指定连续行；工具仍对整个 JSONL 核 SHA-256，报告保留原始 ID 和从一开始的行号。先用 1 条短输入试运行，再选接近 256-token head 上限的样本。运行时须通过 `tools/Invoke-BoundedProcess.ps1` 设置外层墙钟超时并记录进程身份；比较结果需与模型、题集、数值容差及参考实现 oracle 一起审核。
