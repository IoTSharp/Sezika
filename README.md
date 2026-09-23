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

当前仓库包含参考项目研究、架构与阶段规划、.NET 10 类库骨架和类型化契约草案。**尚未实现模型加载、tokenizer、encoder 或真实决策推理；没有发布模型或 NuGet 包。** Native AOT 兼容是工程目标，尚未执行构建、测试或发布验证。

## 决策原语

| 原语 | 语义 | 计划输出 |
| --- | --- | --- |
| Choice | 从请求给定的有限候选中选择 | 选中项、完整概率分布、集中度 |
| Score | 按有序 rubric 评分 | 概率加权期望、各级概率、等级说明 |
| Boolean | 判断命题成立的可能性 | `P(true)`；外部兼容层可映射为 `noul` |

适用方向包括意图识别、模型/工具路由、RAG 候选重排、内容判断和结果核验。决策引擎返回建议，由调用方控制动作与权限；开放式回复由生成模型承担。

## 技术边界

- 首个候选架构采用 mmBERT-base 类多语言 encoder 加类型化决策头。权重、tokenizer 和训练数据逐项审核许可，独立于引擎代码发布。
- 基础推理使用 C# 实现算子，不依赖 Python、PyTorch、ONNX Runtime、cuBLAS/cuDNN 或另一服务进程。开发阶段可使用固定参考实现生成数值对照数据。
- GPU 首先规划 NVIDIA CUDA：构建期用 ILGPU 将 C# kernels 编译为 PTX，Native AOT 运行时通过 C# CUDA Driver 绑定执行。ILGPU 的常规运行时 JIT 路径不作为 AOT 兼容方案；完整路径待原型验证。
- 类库静态纳入宿主；使用 source-generated JSON，保持 Native AOT/trimming 可分析性。
- 模型资产、context、问题/候选数量、并发、工作空间和取消均有明确边界。
- 多语言能力逐语言评测；概率、分布集中度、校准有效范围与拒答分别表达。

## 与 Tomur 集成

Sezika 保持独立开源仓库。Tomur 计划通过同进程 C# provider 调用固定版本的库，统一管理模型下载、加载、诊断与决策 API。接入计划见 [Tomur 集成设计](docs/tomur-integration.md)，当前尚未对接。

## 文档与工程

- [参考项目分析](docs/research.md)：Laya 的可审计模型实现，以及 TypeSafe Jev 的公开协议与边界。
- [架构设计](docs/architecture.md)：推理路径、模型资产、契约、校准与资源约束。
- [纯 C# GPU 与 Native AOT](docs/gpu-aot.md)：ILGPU 编译期边界、CUDA Driver 路径及最小验证关口。
- [阶段路线图](ROADMAP.md)：实现顺序、验收条件与工作量判断。
- `src/Sezika`：.NET 10 核心类库与契约草案；当前不包含 engine 实现。
- [English](README.en.md)

验证命令与矩阵将在相应阶段执行；按当前协作约定，构建、测试、启动与模型下载需要用户明确要求。

## 开源许可

自有代码使用 [Apache License 2.0](LICENSE)。研究参考与第三方资产边界见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。本项目不声称复现 Jev 的闭源模型，也不继承上游的速度或质量结论。
