# 阶段 1–6 实现证据

状态：固定真实模型资产、tokenizer oracle、CPU/CUDA encoder 与 Native AOT smoke 已落地；多语言质量报告和 Tomur 主仓库 provider 仍需分别验收。

| 阶段 | 已实现 | 当前证据边界 |
| --- | --- | --- |
| 1 模型资产/tokenizer | 固定 `convaiinnovations/laya-multilingual@052592a15d198d9ad47da779604259b10b47b7aa`，Apache-2.0；仓库提交 `model.json` v1 与 `source_lock.json`，包含 170 tensor mapping、SafeTensors F16/F32、SHA-256；C# BPE/Metaspace tokenizer | `.artifacts` 中的权重和 tokenizer 不进入仓库分发；8 个中英、CJK、RTL、组合字符、emoji、混合脚本样本与 `tokenizers 0.22.2` oracle 逐项通过（本机资产缺失时测试明确 skipped）；模型来源与底座 MIT/Gemma 2 来源见 [model-source](model-source.md) |
| 2 C# encoder | 真实 22 层 mmBERT FP32 scalar encoder：embedding、global/local attention、split-half RoPE、LayerNorm、gated GELU MLP、取消与形状检查；Trace 覆盖 267 个 encoder 算子节点 | 真实包 `Hello world`（4 tokens）CPU encoder 0.52–0.65 s，输出有限；CPU/GPU Trace 同输入逐节点最大绝对差 `1.7578125e-2`（FP32 标量与 GPU FMA/数学实现差异），head logits 误差 `9.54e-7`；尚无独立语言质量报告 |
| 3 决策闭环 | Choice/Score/Boolean、稳定 softmax、温度 profile、concentration/abstention、session/deadline/token budget、source-generated JSON | tiny model typed response 通过；未声称真实模型质量 |
| 4 多语言/校准 | 冻结 encoder 的可复现二分类 head trainer、温度拟合、NLL/Brier/ECE/accuracy/macro-F1/coverage/selective-risk 计算 | 评估器具备可复现输入；尚无许可明确的中英数据集与质量门槛报告 |
| 5 CUDA/AOT | `Sezika.Kernels` C# → ILGPU 1.5.3 构建期导出 14 个 PTX、ABI manifest、源码/PTX SHA-256 与静态 launch descriptors；CUDA Driver `LibraryImport`/SafeHandle；完整 resident GPU encoder/head；win-x64 Native AOT smoke | RTX 4070 Laptop GPU / driver 596.08 / CC 8.9 实测；真实 mmBERT CPU/CUDA head logits `[1.0985773,0.55617267]` vs `[1.0985773,0.5561736]`，最大绝对误差 `9.536743e-7`；非整齐 vector/GEMM、取消、module/buffer 回收通过；性能矩阵与 linux-x64 AOT 仍未验收 |
| 6 Tomur R22 | `DecisionStatus` 与 `managed-decision` 状态契约、DecisionEngine 可被宿主静态引用 | Tomur provider/API/Catalog 必须在 `D:\source\Tomur` 由宿主变更完成；本仓库不伪造跨仓库验收 |

## 已执行命令

在 PowerShell 7.6.6、.NET SDK 10.0.401 下：

```text
dotnet build Sezika.slnx -c Release --nologo                 # 0 warnings, 0 errors
dotnet run --project tests/Sezika.Tests/Sezika.Tests.csproj -c Release --no-build
dotnet run --project samples/Sezika.Cuda.Smoke/Sezika.Cuda.Smoke.csproj -c Release --no-build
dotnet run --project tools/Sezika.ModelTool/Sezika.ModelTool.csproj -c Release --no-build -- verify --timeout-seconds 1200
dotnet run --project tools/Sezika.KernelCompiler/Sezika.KernelCompiler.csproj -c Release --no-build -- D:\source\Sezika\src\Sezika.Cuda\Generated
dotnet run --project samples/Sezika.RealModel.Smoke/Sezika.RealModel.Smoke.csproj -c Release --no-build
dotnet publish samples/Sezika.Cuda.Smoke/Sezika.Cuda.Smoke.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:StripSymbols=true
```

PowerShell 7.6.6、.NET SDK 10.0.401 下，模型工具输出 `Verified 170 tensors, model and tokenizer SHA-256`；测试项目 15 项通过。构建期工具输出 14 个带 ABI/hash 的 PTX。真实模型 smoke 输出 CPU/CUDA logits、`max_abs_error=9.536743E-07` 和 `cancel smoke passed`；CUDA smoke 覆盖非整齐 vector-add/GEMM 与决策头。Native AOT 发布物在同一 RTX 4070 上运行通过；发布项目不引用 ILGPU 项目，ILGPU 只存在于构建工具。
