# 独立运行 Sezika

本文说明如何准备固定的 Laya/mmBERT 模型包，并在本地进程中加载模型执行 `Choice`、`Score` 和 `Boolean` 决策。

当前仓库提供可复用的 .NET 类库、模型资产工具、开发期 CLI 和真实模型 smoke。`src/Sezika.Cli` 和 `DecisionModelRuntime` 的 typed 决策入口当前使用 CPU；CUDA encoder/head 有单独的 smoke 路径，尚未接入这些入口的 backend 选择。正式发布、跨平台发布和模型卡仍属于 S6-02。`Sezika.ModelTool` 只负责准备和验证模型资产，不执行推理。

## 环境与固定资产

需要 .NET 10 SDK。以下命令从仓库根目录执行；Windows 终端按仓库约定使用 PowerShell 7。先构建 Release，后续命令使用同一构建结果：

```powershell
dotnet build Sezika.slnx -c Release
```

首个真实模型固定为：

- 模型：`convaiinnovations/laya-multilingual`
- revision：`052592a15d198d9ad47da779604259b10b47b7aa`
- 权重 SHA-256：`9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204`
- tokenizer SHA-256：`609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f`

来源、许可证和底座 revision 见[固定模型来源](model-source.md)。模型权重不在 Git 仓库或 NuGet 包中，下载前应确认上游许可和本机磁盘预算。

## 准备并验证模型包

`Sezika.ModelTool` 的默认目录是 `.artifacts/models/laya-mmbert`。下载过程使用有界的 Range 请求、断点文件和 SHA-256 检查；直连失败时最多进行一次 `http://127.0.0.1:7890` 代理重试。也可以用 `--package-dir` 指定其他绝对或相对目录。

```powershell
dotnet run --project tools/Sezika.ModelTool -c Release --no-build -- download --package-dir .artifacts/models/laya-mmbert --timeout-seconds 1200
dotnet run --project tools/Sezika.ModelTool -c Release --no-build -- manifest --package-dir .artifacts/models/laya-mmbert --timeout-seconds 1200
dotnet run --project tools/Sezika.ModelTool -c Release --no-build -- verify --package-dir .artifacts/models/laya-mmbert --timeout-seconds 1200
```

`manifest` 离线读取 SafeTensors header，写出 `model.json` 和 `source_lock.json`；`verify` 再次检查固定权重、tokenizer 的 hash 和 tensor 范围。可加载的目录至少包含：

```text
.artifacts/models/laya-mmbert/
├── model.json
├── model.safetensors
├── encoder/config.json
├── rl_agent_config.json
└── tokenizer/tokenizer.json
```

运行时还会校验 manifest 身份、每个 tensor 的 hash、形状和 dtype。缺少文件、hash 不符或 revision 不符时，`ModernBertModelLoader.Load` 会失败并返回结构化 `DecisionException`，不会回退到关键词、远端 API 或其他模型。

## 使用开发期 CLI

模型包准备完成后，可以直接从仓库运行开发期 CLI。`inspect` 只做固定资产预检查，读取最多 1 MiB 的 manifest、文件存在性和固定资产 hash，不分配模型张量。`assets_verified: true` 表示该预检查通过，不保证完整 tensor 配置可加载；实际加载仍会继续验证 tensor、形状和预算。未通过预检查时返回退出码 2 和 JSON 诊断。

```powershell
dotnet run --project src/Sezika.Cli -c Release --no-build -- inspect --model .artifacts/models/laya-mmbert
```

`predict` 从 `--input` 文件读取请求，或用 `--input -` 从 stdin 读取，并把 `DecisionResponse` JSON 写到 stdout。仓库已提供[英文请求](../samples/requests/decision.en.json)和[中文请求](../samples/requests/decision.zh.json)，每份都包含 Choice、Score 和 Boolean 三种问题。以下命令分别使用这些文件执行 CPU typed 决策：

```powershell
dotnet run --project src/Sezika.Cli -c Release --no-build -- predict --model .artifacts/models/laya-mmbert --input samples/requests/decision.en.json --deadline-seconds 300
dotnet run --project src/Sezika.Cli -c Release --no-build -- predict --model .artifacts/models/laya-mmbert --input samples/requests/decision.zh.json --deadline-seconds 300
Get-Content samples/requests/decision.en.json -Raw | dotnet run --project src/Sezika.Cli -c Release --no-build -- predict --model .artifacts/models/laya-mmbert --input - --deadline-seconds 300
```

可选的资源边界是 `--minimum-concentration`（`0` 到 `1`，默认 `0`）、`--deadline-seconds`（`1` 到 `300`，默认 `30`）和 `--max-tokens`（`1` 到 `4096`，默认 `4096`）。上面的命令为 CPU 推理设置 300 秒上限；模型加载另受 CLI 的总命令时限约束。成功退出码为 0；输入、资产、取消或预算错误退出码为 2，并在 stderr 输出 `{ "code": "...", "message": "..." }`。CLI 使用 source-generated JSON；一次成功运行不代表质量、校准或性能验收。

## 使用类库执行决策

引用 `Sezika` 类库后，可以通过 `DecisionModelRuntime.Load` 加载固定模型包，再把 UTF-8 JSON 交给 `Evaluate`。同一个 runtime 可以顺序复用多次请求；它拥有模型和 session，释放时会卸载模型及工作空间。下面的代码从仓库根目录读取已有的英文请求：

```csharp
using System.Text.Json;
using Sezika;

using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
using var runtime = DecisionModelRuntime.Load(
    ".artifacts/models/laya-mmbert",
    budget: new DecisionResourceBudget
    {
        MaxQuestions = 3,
        MaxTokens = 4096,
        Deadline = TimeSpan.FromSeconds(300),
    },
    cancellationToken: cancellation.Token);

var response = runtime.Evaluate(
    File.ReadAllBytes("samples/requests/decision.en.json"), cancellation.Token);
Console.WriteLine(JsonSerializer.Serialize(
    response, DecisionJsonContext.Default.DecisionResponse));
```

`Evaluate` 的 JSON 重载内部使用 `DecisionRequestParser`，在 DTO 反序列化前拒绝重复属性，并检查 JSON 深度、请求大小、问题数量、候选数量和 token 预算。输入结构如下；可直接修改仓库内的请求文件：

```json
{
  "model": "convaiinnovations/laya-multilingual",
  "state": { "message": "duplicate invoice charge" },
  "questions": {
    "intent": {
      "type": "choice",
      "instructions": "choose the request type",
      "criteria": {
        "billing": "the request is about billing",
        "technical": "the request is about a software problem"
      }
    },
    "severity": {
      "type": "score",
      "instructions": "score the impact from low to high",
      "criteria": [ "minor impact", "critical impact" ]
    },
    "is_billing": {
      "type": "boolean",
      "instructions": "is this a billing issue?",
      "criteria": {
        "true": "the request is a billing issue",
        "false": "the request is unrelated to billing"
      }
    }
  }
}
```

三个问题可以放在同一个请求中；每个问题仍按固定 marker 序列单独编码，并由 response 的 `usage.micro_batch_count` 报告实际批次数。`state` 和 criterion 可以是字符串、数字、数组或对象；运行时使用其 JSON 文本作为提示内容。

请求默认使用 `"length_policy": "strict"`：说明或候选前缀需要裁剪，或整条输入超过模型的 1024-token 总上限时，在推理前拒绝。256 是说明与候选前缀预算，不是总长上限。要复现固定 Laya 的裁剪语义，必须显式指定 `"length_policy": "laya_compatible"`；成功 answer 的 `input_diagnostics` 会报告原始/保留 token 数、丢弃的 state token 数及截断方向。数组 state 保留末尾，其他 state 保留开头。Choice 维持输入候选顺序；Boolean 的语义 marker 顺序固定为 false、true，criteria 可省略以使用默认描述。详见[输入合同](input-contract-s3-09.md)。

## 输出语义

响应包含模型 revision、backend、每个问题的 typed answer，以及 token、问题数、micro-batch 和 workspace 用量。

| 原语 | 关键字段 | 语义 |
| --- | --- | --- |
| `choice` | `choice`, `probabilities`, `concentration` | `choice` 是候选 ID 的 argmax；`probabilities` 包含所有候选；`concentration` 是分布集中度，不是正确率。 |
| `score` | `score`, `legend`, `probabilities` | 第 `i` 个等级使用数值 `i`（从 0 开始），`score = Σ i × pᵢ`；`legend` 保存等级原文。 |
| `boolean` | `probability_true` | true 候选的概率；`logits.true` 与 `logits.false` 仅用于对齐和诊断。 |

每个 answer 的 `status` 为 `answered` 或 `abstained`。设置 `minimumConcentration` 后，低于门槛的结果仍保留 argmax/概率，但必须按拒答处理，不能直接作为动作授权。执行失败会抛出结构化错误；不会填充示例结果。

固定 Laya head 的当前温度为 `[1, 1, 1]`，响应中的 calibration 状态是 `uncalibrated`。S4 的 profile 仍是 `pending_measurement`，因此当前概率不能解释为已经完成的多语言质量或可靠性保证。

## 运行真实模型 smoke

仓库中的真实模型 smoke 会加载指定目录，运行 CPU encoder、marker head 以及三种 typed primitive；如果检测到 CUDA driver，还会额外运行 CUDA 路径。它使用固定请求，不是通用 CLI：

```powershell
dotnet run --project samples/Sezika.RealModel.Smoke/Sezika.RealModel.Smoke.csproj -c Release --no-build -- .artifacts/models/laya-mmbert
```

没有 CUDA driver 时，CUDA 行会明确显示为 skipped；这不影响 CPU smoke。命令需要实际模型权重，不能在没有模型包时用 tiny demo 结果代替真实模型结论。

## 当前边界

- 只接受代码中固定的 Laya/mmBERT revision；通用模型发现、任意 checkpoint 导入和 Python pickle 不在运行时路径中。
- `model.safetensors` 和 tokenizer 必须单独准备，模型权重不会被打包进程序或 NuGet。
- 当前 head 未完成真实语言质量、校准、跨平台性能和延迟矩阵验收；不要把集中度当作准确率，也不要把 fixture smoke 当作质量报告。
- Sezika 只返回决策建议，不生成开放式解释、不执行工具或业务动作。调用方必须自行实施授权、人工确认和失败处理。
- 独立 `sezika doctor`、正式安装包、签名和跨平台发布仍属于 S6-02；当前 `src/Sezika.Cli` 的 `inspect`/`predict` 只应作为开发验证路径。

相关设计和证据见[模型发布目录](model-packaging.md)、[请求校验](request-validation.md)、[阶段证据](stage-evidence.md)和[路线图](../ROADMAP.md)。

本机中英文文件/stdin 真实推理的命令、结果和边界见[独立 CLI 运行记录](standalone-cli-smoke.md)。
