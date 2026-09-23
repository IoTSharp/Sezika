# Sezika

**Multilingual, non-autoregressive System 1 decision engine.**

Sezika 面向本地软件中的语义判断，目标是使用 **C#、.NET 10 与 Native AOT**，把文本或结构化状态转换为类型化决策与概率分布。模型、CPU/GPU 算子及调度以 C# 实现，GPU 运行时允许调用系统显卡驱动。

```text
文本 / JSON 状态 + 类型化问题 + 候选标准
                ↓
      多语言编码器 → 决策头 → 校准
                ↓
       Choice / Score / Boolean
                ↓
      调用方判断、拒答或转入生成模型
```

## 项目状态

当前仓库包含可运行的纯 C# FP32 encoder、固定真实 Laya/mmBERT 开发资产的安全加载、真实 marker-head 的 Choice/Score/Boolean typed smoke、tokenizer oracle、校准评估器，以及 ILGPU 构建期 PTX/ABI、CUDA Driver 完整 encoder/head 和 win-x64 Native AOT smoke。**模型权重仍不随仓库发布；逐语言质量报告与跨平台性能矩阵仍未完成。** 验收边界与命令见 [阶段证据](docs/stage-evidence.md)。

## 独立使用

Sezika 可以独立加载固定模型包并执行决策。先按[独立使用说明](docs/standalone-usage.md)准备并验证模型资产；在仓库根目录执行：

```powershell
dotnet build Sezika.slnx -c Release
dotnet run --project src/Sezika.Cli -c Release --no-build -- inspect --model .artifacts/models/laya-mmbert
dotnet run --project src/Sezika.Cli -c Release --no-build -- predict --model .artifacts/models/laya-mmbert --input samples/requests/decision.zh.json --deadline-seconds 300
```

`predict` 使用 CPU 返回 Choice、Score、Boolean 的 JSON 答案；[中文](samples/requests/decision.zh.json)与[英文](samples/requests/decision.en.json)请求均可直接修改。`inspect` 是固定资产预检查，完整张量配置仍在加载时验证。C# 宿主可以使用 `DecisionModelRuntime.Load(...)` 和 `Evaluate(...)` 复用同一 session；API 示例和输出语义见[独立使用说明](docs/standalone-usage.md)。当前结果为 `uncalibrated`；开发期 CLI 尚未正式发布。

## 决策原语

| 原语 | 语义 | 输出 |
| --- | --- | --- |
| Choice | 从请求给定的有限候选中选择 | 选中项、完整概率分布、集中度 |
| Score | 按有序 rubric 评分 | 概率加权期望、各级概率、等级说明 |
| Boolean | 判断命题成立的可能性 | `P(true)`；外部兼容层可映射为 `noul` |

适用方向包括意图识别、模型/工具路由、RAG 候选重排、内容判断和结果核验。决策引擎返回建议，由调用方控制动作与权限；开放式回复由生成模型承担。

## 技术边界

- 首个候选架构采用 mmBERT-base 类多语言 encoder 加类型化决策头。权重、tokenizer 和训练数据逐项审核许可，独立于引擎代码发布。
- 基础推理使用 C# 实现算子，不依赖 Python、PyTorch、ONNX Runtime、cuBLAS/cuDNN 或另一服务进程。开发阶段可使用固定参考实现生成数值对照数据。
- GPU 已通过 NVIDIA CUDA Driver 路径：C# kernels 在构建期由 ILGPU 1.5.3 导出 PTX/ABI，Native AOT 通过静态 Driver 绑定执行完整 encoder/head；不使用 cuBLAS/cuDNN。
- 类库静态纳入宿主；使用 source-generated JSON，保持 Native AOT/trimming 可分析性。
- 模型资产、context、问题/候选数量、并发、工作空间和取消均有明确边界。
- 多语言能力逐语言评测；概率、分布集中度、校准有效范围与拒答分别表达。

## 文档与工程

- [独立使用](docs/standalone-usage.md)：固定模型准备、CLI、C# 调用和决策输入输出。
- [参考项目分析](docs/research.md)：Laya 的可审计模型实现，以及 TypeSafe Jev 的公开协议与边界。
- [架构设计](docs/architecture.md)：推理路径、模型资产、契约、校准与资源约束。
- [纯 C# GPU 与 Native AOT](docs/gpu-aot.md)：ILGPU 编译期边界、CUDA Driver 路径及最小验证关口。
- [阶段路线图](ROADMAP.md)：实现顺序、验收条件与工作量判断。
- [闭环审计](docs/closure-audit-2026-09-23.md)：逐阶段证据、状态与剩余条件。
- `src/Sezika`：.NET 10 核心类库、模型加载、tokenizer、CPU encoder 与 typed decision engine。
- [English](README.en.md)

真实模型、CUDA 与 AOT 命令及实测结果见 [阶段证据](docs/stage-evidence.md) 和 [GPU/AOT 设计](docs/gpu-aot.md)；多语言质量和跨平台性能矩阵仍待执行。

## 开源许可

自有代码使用 [Apache License 2.0](LICENSE)。研究参考与第三方资产边界见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。本项目不声称复现 Jev 的闭源模型，也不继承上游的速度或质量结论。
