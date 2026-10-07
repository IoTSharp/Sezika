# 独立 CPU/CUDA pipeline 工程验收

北京时间 2026-10-08，监督起点 `3a7435e`。实现范围是 S4-15 的静态独立 pipeline、三行自有 linear marker head CUDA 前向和生命周期；不包含 GPU 训练、正式默认迁移、实际模型质量或发布。设计见 [独立 CUDA 合同](../independent-cuda-s4-15.md)。

`IndependentDecisionV1Runtime` 通过完整模型/数据/特征/head 身份核对注入 pipeline，共用 decision-v1 输入和 typed 结果。CPU 保留原有标量参考；CUDA 使用既有 C# 构建期 PTX、Driver resident encoder 和静态 Linear，将 `[tokens,hidden]` 与独立 `[3,hidden]` head 相乘，不加载 legacy head 或运行动态 ILGPU launcher。当前 token 上限仍为 1024。

独立只读复核发现并关闭公开 CPU/CUDA pipeline 的关闭竞态，以及 runtime 同线程重入导致提前释放的问题。gate 内复查关闭状态并保证幂等；重入返回 `decision_session_busy`；同线程请求 dispose 延期至当前 evaluation finally，pipeline 清理异常仍清理 model。新增有界 fixture 覆盖这些路径。

## 实际检查

固定 PowerShell 7.6.6、.NET SDK 10.0.401。构建经 `Invoke-BoundedProcess.ps1`，关闭 build servers/共享编译/节点复用，`-m:1`。执行记录见 [索引](supervision-2026-10-08/pipeline-execution-index.json)。

| 检查 | PID | 上限 | 实际结果 |
| --- | ---: | ---: | --- |
| Release solution | 37516 | 240 秒 | 103.309 秒，0 警告/错误 |
| 独立 CPU 极小检查 | 5192 | 45 秒 | 17 项通过 |
| 修正后的独立 CUDA 实卡极小检查 | 80836 | 45 秒 | 16 项通过 |
| 核心完整回归 | 96840 | 90 秒 | 190 项通过；缺 legacy tokenizer oracle 明确 SKIP |
| 修正接线后的 CUDA 完整诊断 | 61220 | 90 秒 | 59 项通过 |
| CLI 回归 | 75568 | 90 秒 | 41 项通过 |
| 独立预测导出回归 | 36732 | 60 秒 | 11 项通过，fixture 保持 not_eligible |
| 核心 win-x64 Native AOT 发布 | 57408 | 300 秒 | 52.020 秒，退出 0，无编译/trim/AOT 警告 |
| 核心原生独立检查 | 26736 | 60 秒 | 18 项通过 |
| CUDA win-x64 Native AOT 发布 | 67440 | 300 秒 | 22.202 秒，退出 0，无编译/trim/AOT 警告 |
| CUDA 原生独立实卡检查 | 43896 | 60 秒 | 17 项通过 |

两份原生检查都核对 `IsDynamicCodeSupported=false`、`IsDynamicCodeCompiled=false` 和 TPA 为 null，随后执行相同 tiny CPU/CUDA 路径。实卡 fixture 为六 token、八 hidden、单 encoder layer，三题型使用非零且互异的自有 head 行；冻结误差为 `abs <= 1e-4 + 1e-4*abs(reference)`。D2H 实测 72 字节，仅三题型 logits 回到主机；请求 workspace 和卸载后的 allocations/modules 回到基线，caller 设备保留。

## 首次失败与修复

CUDA 首次 pilot PID103616 因 fixture 的 `byte_fallback:false` 违反现有 tokenizer 合同退出 1；只把 fixture 改为受支持的 BPE byte-fallback，没有放宽产品 parser。该检查直接供应 token IDs，不执行 tokenizer Encode。修正项目重建 PID106168 无警告/错误，随后 pilot 通过。

完整 CUDA 首次 PID104788 在既有 context 释放后执行新增检查而失败。这是监督者的共享入口接线错误；新增调用已移到显式设备释放之前。重建 PID90936 无警告/错误，完整 59 项通过。两次失败及清理结果原样保留，未用旧 PASS 替代修复后的结果。

## 归属与边界

[资源核查](supervision-2026-10-08/pipeline-resource-audit.json)覆盖 15 份 runner 记录、69 次登记进程身份观测：cleanup errors 全空，没有 PID、创建时间、完整命令行和父 PID 同时匹配的存活管线验证进程。Windows 采样可能漏过极短中间进程。独立资产恢复使用另一个有界 runner 通道，不属于这份资源快照。

检查的唯一临时 tokenizer 通过 finally 按绝对路径和任务归属删除；日志、原生产物和回归输出保留在 `.artifacts/supervision-20261008`。没有新增独占 MCP 服务，没有 Graphify、广域工具扫描或按名称终止。已有 `docs/wechat-introduction.*` 未修改、暂存。

本次 tiny 合同、数值和 Windows AOT 工程子任务已验收。真实原始 encoder 的上游 oracle、真实自有 head 的 CPU/CUDA 数值、linux-x64 AOT、语言质量、校准与性能仍需分别验收；产品 CLI/预测工具当前默认独立 CPU，CUDA 工厂的产品命令接线另行推进。
