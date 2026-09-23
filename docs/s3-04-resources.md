# S3-04 决策 session 与资源生命周期

状态：`✅ 已实现`。`ModernBertDecisionEngine` 以单 session gate 拒绝重入，使用 linked cancellation/deadline token，在每个 question/micro-batch 边界检查 token，并在 `finally` 中释放 gate。当前 cross-encoder 每个 question 是一个 bounded micro-batch；`DecisionUsage.MicroBatchCount` 报告实际 forward 次数，不能把多个问题宣传成固定成本。

请求在执行前校验 question/token/candidate 预算，并为每个编码问题估算 transient workspace；超过 `MaxWorkspaceBytes` 或模型常驻估算超过 `MaxResidentBytes` 时返回结构化错误。`ModernBertModelPackage.Dispose` 清零已加载张量、释放 encoder workspace pool，后续 engine 调用返回 `decision_model_unloaded`。session busy、取消、deadline、workspace 归还、模型 lease 卸载和安装目录卸载边界分别有测试覆盖。

当前证据不代表真实模型吞吐、RSS/显存峰值或 GPU kernel 取消延迟。CUDA 已提交 kernel 仍遵守 driver fence；S5-04/S5-05 负责跨硬件性能和真实模型资源矩阵。
