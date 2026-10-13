# Sezika

**用 C# 构建的本地多语言决策引擎。**

Sezika 是一个非自回归的 System 1 决策引擎：输入文本或 JSON 状态，输出类型化判断与概率分布，面向意图识别、候选选择和规则语义判断等场景。

`C# / .NET 10` · `CPU / CUDA` · `Native AOT 目标` · `Apache-2.0`

> ⏸️ **项目暂缓推进，等待算力与资金支持。** 截至 2026-10-13，真实全参数训练已启动过，服务器现已关机；完整一轮训练、正式质量与概率校准仍未完成。

## ✨ 能做什么

| 决策原语 | 用途 | 输出 |
| --- | --- | --- |
| Choice | 从有限候选中选择 | 选中项与完整概率分布 |
| Score | 按有序标准评分 | 概率加权得分与等级分布 |
| Boolean | 判断一个命题 | `P(true)` |

模型、CPU/GPU 算子与调度使用 C# 实现。CUDA 路径采用构建期导出的 PTX 与静态 Driver 绑定；核心推理不依赖 PyTorch、ONNX Runtime、cuBLAS/cuDNN 或远程模型 API。Sezika 输出判断，业务动作由调用方负责。

## 🚦 当前进度

| 状态 | 已有成果与边界 |
| --- | --- |
| ✅ 基础引擎 | 模型加载、tokenizer、CPU/CUDA 推理、三种决策原语与 CLI 已实现 |
| ✅ 工程验证 | 指定模型上的数值对照及 Windows / Linux WSL2 Native AOT 验证已有证据，不代表新权重或所有平台均已验收 |
| 🧪 真实训练 | 已在 RTX 5090 上执行 C# 编码器与决策头全参数反向训练、FP32 AdamW 更新和断点恢复 |
| ⏸️ 全量训练 | 目标 **14,592,720 次更新**；最近核对的完整保存点为 **45,056 次，约 0.31%**；停机后的保存状态待重新核验 |
| ⏳ 正式模型 | 多语言质量、校准、封存测试及新权重部署性能仍待验收；现有输出为未校准概率 |

现有[实验模型入口](https://moark.com/IoTSharp/Sezika-Decision-Base)用于研究与验证，不能视为本轮全量训练完成后的正式模型。模型权重独立于源码和 NuGet 管理。

## 💰 剩余训练预算

以下是 **2026-10-13** 的单张 RTX 5090 方案估算：

| 项目 | 估算 |
| --- | --- |
| 剩余更新 | 14,547,664 次，以最近核对的保存点计算 |
| 已跑语料实测速率 | 约 4 次更新 / 秒 |
| 剩余训练时间 | 约 **960–1,014 GPU 小时**，即 **40–43 天连续运行** |
| GPU 公开单价 | **4 元 / 小时** |
| 剩余 GPU 费用 | 约 **人民币 3,800–4,100 元** |

这不是总成本或完成日期承诺：估算未包含最终独立验证、存储等费用，后续文本长度、训练优化和实际计费也会改变结果。目前可用算力余额为 **0 元**。[计算依据与实测范围](docs/training-budget-2026-10-13.md)

## 🚀 开发体验

需要 .NET 10 SDK；先按[独立使用说明](docs/standalone-usage.md)准备并校验模型资产，再执行：

```powershell
dotnet build Sezika.slnx -c Release
dotnet run --project src/Sezika.Cli -c Release --no-build -- inspect --model .artifacts/models/laya-mmbert
dotnet run --project src/Sezika.Cli -c Release --no-build -- predict --model .artifacts/models/laya-mmbert --input samples/requests/decision.zh.json --deadline-seconds 300
```

以上是已有固定资产的开发入口。[独立模型 CLI](docs/independent-cli-s4-15.md)另有显式模型格式；开发期可运行不等于正式质量验收。

## 🧭 文档与路线

- [路线图](ROADMAP.md)：恢复训练、质量评测、性能和发布的下一步。
- [架构设计](docs/architecture.md) · [GPU / AOT](docs/gpu-aot.md) · [已有实测证据](docs/stage-evidence.md)。
- [训练预算与暂停记录](docs/training-budget-2026-10-13.md) · [更新记录](CHANGELOG.md) · [English](README.en.md)。

自有代码采用 [Apache License 2.0](LICENSE)；模型、数据与 tokenizer 的来源和许可分别管理，见[第三方声明](THIRD_PARTY_NOTICES.md)。

<a id="sponsorship"></a>

## ❤️ 我们需要赞助

我们需要赞助来继续完成 Sezika 的全量训练与独立验证。欢迎提供训练经费、可用 GPU 算力，或参与 C# / CUDA 优化与评测。

| 微信赞助 | IoTSharp 企业微信群 |
| :---: | :---: |
| <a href="docs/assets/wechat-sponsor.jpg"><img src="docs/assets/wechat-sponsor.jpg" alt="维护者提供的微信收款码，用于赞助 Sezika" width="230" /></a> | <a href="docs/assets/iotsharp-wecom.jpg"><img src="docs/assets/iotsharp-wecom.jpg" alt="IoTSharp 企业微信交流群二维码" width="230" /></a> |
| 扫码自愿支持训练经费 | 微信扫码加入，一起交流 Sezika 与 IoTSharp |

点击图片可查看原图。交流群二维码来自 [IoTSharp 官方项目](https://github.com/IoTSharp/IoTSharp)；如入口失效，请查看该项目的最新社区信息或提交 Issue。[图片来源](docs/assets/SOURCES.md)

**🙏 Sezika 仍需要你的赞助与参与。每一份算力和支持，都能帮助我们继续完成真实训练与验证。**
