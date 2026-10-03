# S4-06 独立开发集诊断工具

`Sezika.IndependentModelTool evaluate-original` 为独立 mmBERT 与自有 marker head 提供一个有界的 development 诊断入口。它复用 `train-original` 生成的 head asset 和训练报告，重新加载同一模型、tokenizer、数据 manifest 与记录文件，只评估 `development` split。

## 用法

```powershell
dotnet tools/Sezika.IndependentModelTool/bin/Release/net10.0/Sezika.IndependentModelTool.dll evaluate-original `
  --package .artifacts/models/mmbert-independent/converted `
  --records datasets/independent-v1-original/records.jsonl `
  --head .artifacts/models/mmbert-independent/converted/head-v2.asset `
  --training-report .artifacts/models/mmbert-independent/converted/development-report-v2.json `
  --report .artifacts/models/mmbert-independent/converted/development-evaluation-v1.json
```

命令最多读取 32 条记录，内部总时限为 10 分钟；每条记录都会报告进度。训练报告、记录文件和 manifest 的 SHA-256 必须一致，head asset 的模型、encoder、tokenizer、特征和文件 hash 必须一致。评估输出使用 `FileMode.CreateNew`，避免覆盖已有证据。

## 输出与语义

报告状态为 `development_diagnostic_only`。总览和语言/题型分组均保留总数、answered、rejected、coverage、answered accuracy、Wilson 95% accuracy interval、平均 NLL/Brier、平均 top-two margin、10-bin ECE 和在候选数一致且正负类均存在时的宏 one-vs-rest AUROC。逐题行保存按输入顺序复制的 `candidate_ids`、完整概率分布、目标/预测索引、目标概率、top 概率、margin、NLL、Brier 和结构化拒答错误码；候选顺序只作为输入字段保留，不从文本推断标签。

拒答保留在 coverage 分母；错误、概率集中度和模型正确率分别记录。候选数不同的分组不计算宏 AUROC，无法定义正负类的分组以 `null` 表示。所有指标都只描述当前 development smoke，不表示 calibration、sealed test、代表性语言质量、教师标签效果或发布门槛。

当前记录合同只有语言、题型、split 和题目内容；候选数组本身提供了可复制的候选顺序，但没有角色、否定或词面相似性标签。报告的 `diagnostic_dimensions` 明确列出行中可用的 `language`、`question_type`、`candidate_order`，当前实际分组只有前两项，暂不可用的 `role`、`negation`、`lexical_similarity` 保持未分组。工具不会从文本或候选索引猜测缺失维度；扩展这些切片前必须先在 S4-05 审核后的记录中加入版本化标签及家族/实体隔离字段。无 answered 行时准确率输出为 `null`，避免把“没有可评价样本”误报成零准确率。

当前 12 条项目自有记录仍只有 train/development split，且每个语言/题型分组的 development 分母为 1。要形成 S4-06 或 S4-08 证据，必须先完成 S4-05 的来源许可、人工审核、污染检查和独立封存，再使用新的独立 split；本工具不会绕过这些准入条件。
