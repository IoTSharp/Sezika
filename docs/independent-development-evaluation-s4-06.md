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

## 审核标签侧车与离线分组

`group-development-report` 只读取已经生成的 development 诊断报告及独立标签侧车，不打开题文、原始数据、模型资产或训练器。输入各自以 SHA-256 固定，输出保留原报告的模型、encoder、tokenizer、manifest、records、训练报告、特征和 head 身份。

```powershell
dotnet tools/Sezika.IndependentModelTool/bin/Release/net10.0/Sezika.IndependentModelTool.dll group-development-report `
  --evaluation <development-report.json> --evaluation-sha256 <报告SHA256> `
  --labels <reviewed-labels.json> --labels-sha256 <侧车SHA256> `
  --output <已存在目录中的新报告.json>
```

侧车采用 schema 1、`contract=reviewed-development-slices-v1`、`review_origin=external_declared_human_review` 和 `split=development`，绑定报告原始字节及 data manifest、records hash。`vocabularies` 定义 `role`、`negation`、`lexical_similarity`、`candidate_order` 中的 1–4 个维度、词表版本及每维 1–16 个值；`unknown` 为保留语义。`rows` 按唯一 `record_id` 连接，并逐项核对 `candidate_ids` 的实际顺序。工具拒绝重复/未知记录、重复 JSON 字段、未知 DTO 字段、身份不符和候选映射不符。

每条 `reviews` 声明维度和 `pending`、`approved` 或 `rejected`。只有 `approved` 可以赋予词表中的 `value`；完成的 approved/rejected 声明须带 reviewer、明确 UTC reviewed_at、非空内嵌 evidence 及其 UTF-8 SHA-256。pending 不得带完成审核证据，pending/rejected 不得赋标签。工具只校验外部声明的合同，不证明审核人身份或许可，不生成审核标签。未提供、pending、rejected 各自计数并进入 unknown，四维均保留 source_total，具名分组不会丢掉未审核行的分母。

命令从逐题概率重算预测、correct、NLL、Brier 和 margin，核对原报告分母及有限值，再复用既有指标函数；不信任输入的 aggregate metrics。输出状态为 `development_reviewed_slice_diagnostic_only`，正式质量、校准、封存、训练准入、许可审批和性能接受始终为 false。

范围为最多 32 行、每行 32 个候选、单输入 1 MiB、审核证据总计 64 KiB、JSON 深度 32、输出 2 MiB。工作采用 30 秒协作取消；失败发布另有独立 5 秒恢复预算。输出目录须已存在，使用 GUID partial、无覆盖原子 move 及 move 后取消核验；恢复先核任务文件创建时间、长度和内容 hash，身份已变的文件保留并报错。同步文件 I/O 无法由 CancellationToken 硬中断，验证 lease 关闭后按路径删除仍存在并发替换窗口，不宣称抵御恶意并发文件替换。

有界合成自检先 `check-reviewed-slices --max-checks 1`，再 `--max-checks 12`，均指定已有 `--artifact-directory`。工程构建和十二项合成检查已通过，真实审核侧车、真实诊断报告及质量尚未验收；详见[本轮工程证据](evidence/reviewed-slices-2026-10-08.md)。
