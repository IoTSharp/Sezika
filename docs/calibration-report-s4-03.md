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

每个语言/领域/primitive profile 的测试集至少 100 条独立记录。相对随机或多数类基线，accuracy 提升至少 0.05；相对未校准结果，NLL 与 Brier 不得恶化超过 0.02；ECE ≤ 0.10；拒答 coverage ≥ 0.95；selective risk ≤ 0.20；score 的 MAE ≤ 0.75。置信区间的目标置信度为 95%。

这些数值是发布前冻结的验收门槛，不是当前测量结果。门槛校验会拒绝缺少 `ObservedMetrics`、测试样本不足、未拟合或超限的 `verified` profile。`pending_measurement` profile 可用于审计和后续拟合，但不能被 UI/API 当作已校准能力。

## 质量报告格式

真实模型运行后，应在此文档的版本化附件中记录每个 profile 的：运行版本、模型/资产 hash、数据清单 hash、样本数、accuracy、macro-F1、NLL、Brier、ECE、coverage、selective risk、score MAE、95% 置信区间、拒答阈值、失败样本数和原始输出 artifact 路径。报告必须分别给出校准前后指标，并把 OOD、高置信错误和拒答样本单独统计。

当前仓库没有真实模型质量数字。S4-02 的 24 条原创 JSONL 只是协议与 provenance fixture，每个 test split 仅三条记录，不能满足上述门槛；因此本轮不生成伪造 logits、概率、准确率或语言质量结论。

核心测试还验证了 fail-closed 行为：`pending_measurement` profile 改为 `verified` 但没有拟合结果时被拒绝；即使填入拟合标记，测试样本数不足的 `ObservedMetrics` 仍被质量门槛拒绝。只有后续真实模型运行产出的完整指标快照才能创建 `verified` profile。
