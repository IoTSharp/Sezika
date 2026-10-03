# S4-03 校准 profile 与冻结门槛

状态：`pending_measurement`。`data/s4-03/calibration-profiles.json` 是绑定清单和门槛定义，不是已校准质量报告。

## 绑定范围

清单为中文/英文 × general/support × choice/score/boolean 建立 12 个 profile。每个 profile 固定以下身份：

- Laya/mmBERT 模型 ID、revision、权重 SHA-256；
- tokenizer revision 与 SHA-256；
- `sezika.prompt.v1` 及其 SHA-256；
- primitive、语言、领域和 `calibration` split；
- S4-02 dataset manifest SHA-256；
- 温度参数（初始 fixture 值为 `1.0`，未拟合）。

运行时只能把 profile 加载为 `verified`，前提是 `CalibrationProfileManifest.Validate` 通过，且 profile 已有拟合参数和独立测试指标。模型、tokenizer、prompt、数据清单任一 hash 变化都必须产生新 profile ID；不能覆盖旧 profile 或悄悄复用温度。

## 冻结门槛

每个语言/领域/primitive profile 的测试集至少 100 条独立记录。相对随机和多数类两个基线，accuracy 提升均至少 0.05；相对未校准结果，NLL 与 Brier 不得恶化超过 0.02；ECE ≤ 0.10；拒答 coverage ≥ 0.95；selective risk ≤ 0.20；score 的 MAE ≤ 0.75。置信区间的目标置信度为 95%。

这些数值是发布前冻结的验收门槛，不是当前测量结果。门槛校验会拒绝缺少 `ObservedMetrics`、测试样本不足、未拟合或超限的 `verified` profile。`pending_measurement` profile 可用于审计和后续拟合，但不能被 UI/API 当作已校准能力。

## 质量报告格式

真实模型运行后，应在此文档的版本化附件中记录每个 profile 的：运行版本、模型/资产 hash、数据清单 hash、样本数、accuracy、macro-F1、NLL、Brier、ECE、coverage、selective risk、score MAE、95% 置信区间、拒答阈值、失败样本数和原始输出 artifact 路径。报告必须分别给出校准前后指标，并把 OOD、高置信错误和拒答样本单独统计。

本清单没有通过门槛的真实模型独立测试指标。仓库其他报告中的历史 Laya audit 和独立模型小样本开发数字不能转移为本清单的校准证据。S4-02 的 24 条原创 JSONL 只是协议与 provenance fixture，每个 test split 仅三条记录，不能满足上述门槛；因此本轮不生成伪造 logits、概率、准确率或语言质量结论。

核心测试还验证了 fail-closed 行为：`pending_measurement` profile 改为 `verified` 但没有拟合结果时被拒绝；即使填入拟合标记，测试样本数不足的 `ObservedMetrics` 仍被质量门槛拒绝。只有后续真实模型运行产出的完整指标快照才能创建 `verified` profile。

`CalibrationConfidenceIntervals` 提供确定性的 Wilson 二项区间与有限样本均值区间，供后续报告填入 95% 置信区间；它只计算抽样区间，不改变 profile 状态，也不能把当前 fixture 或 pending profile 解释为质量通过。`CalibrationEvaluator.FitTemperature` 同样限制样本/步数并响应取消，拟合结果仍须绑定独立 calibration split 后经过上述门槛。

## 2026-10-03 合同续审

`verified` 的快照现在必须明确 `EvaluationSplit = sealed_test`，提供随机/多数类 accuracy 差值及未校准对照的 NLL/Brier 差值，Score 还必须提供 MAE。所有可选数值只要存在就必须有限；缺字段或 NaN 不再能绕过门槛。`verified` manifest 不允许包含 pending/rejected profile。pending/rejected 快照允许保留有限但不达门槛的观测，以便审计失败；它们不会因此成为 verified。

`CalibrationEvaluator.Evaluate` 的 accuracy、macro-F1、ECE、NLL/Brier 始终采用完整输入分母，拒答阈值仅影响 coverage 与 selective 指标。新增 `CoveredCount`、可空 `SelectiveAccuracy`、Boolean 的 `NegativeClassRecall`（位置 0 为 false，位置 1 为 true）和 Score 的 `ScoreMae`（按等级期望值）。没有负类真值或没有回答时，相应可空指标为 null。为兼容旧调用，零回答的旧 `SelectiveRisk` 标量仍为 0，报告必须同时检查 `CoveredCount`，不能将其解释为零风险通过。

评估/拟合默认总墙钟 60 秒，可显式设置为最多 10 分钟，支持取消和进度回调。输入最多 1,000,000 条、每条 2..32 类，拟合最多 10,000 grid steps 且最多 20,000,000 样本×候选温度评估。拟合在搜索范围包含 1 时显式加入 identity temperature，避免粗网格漏掉未校准候选；计算保留精确零支持，使用双精度稳定归一化。NLL 仍以 `1e-15` 下限计分，必须在报告中保持该数值合同。置信区间工具现在拒绝有限输入造成的聚合溢出。

这些是工程合同验证，不是 S4-08 质量、数据准入、实际校准或独立模型 AOT 验收。split 字段与差值仍是输入声明；正式流水线必须另行证明资产身份、家族隔离、独立封存、成对基线输出、报告 artifact 和置信区间，不能仅填字段取得发布授权。
