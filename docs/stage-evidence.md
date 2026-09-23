# 阶段 1–6 实现证据

状态：实现骨架与可执行 smoke 已落地。真实发布模型、多语言质量报告和 Tomur 主仓库 provider 仍需外部资产与宿主变更后分别验收。

| 阶段 | 已实现 | 当前证据边界 |
| --- | --- | --- |
| 1 模型资产/tokenizer | `ModelManifest`、路径/哈希/形状预算校验、安全 SafeTensors F32/F16/BF16 读取、确定性 Unicode/byte fallback tokenizer | 没有下载或发布第三方权重；需要固定模型 revision 后生成 manifest 与 token oracle |
| 2 C# encoder | FP32 embedding、RoPE、双向 attention、LayerNorm、GELU MLP、取消与形状检查 | tiny deterministic model 与 CPU tests 通过；尚未对齐真实 mmBERT checkpoint |
| 3 决策闭环 | Choice/Score/Boolean、稳定 softmax、温度 profile、concentration/abstention、session/deadline/token budget、source-generated JSON | tiny model typed response 通过；未声称真实模型质量 |
| 4 多语言/校准 | 冻结 encoder 的可复现二分类 head trainer、温度拟合、NLL/Brier/ECE/accuracy/macro-F1/coverage/selective-risk 计算 | 评估器具备可复现输入；尚无许可明确的中英数据集与质量门槛报告 |
| 5 CUDA/AOT | C# 维护的固定 PTX vector add/GEMM、CUDA Driver `LibraryImport`/SafeHandle、GPU scorer、win-x64 Native AOT smoke | RTX 4070 Laptop / driver 596.08 / CC 8.9 实测；证明固定 probe 与决策头 GPU GEMM，不等于完整 Transformer GPU encoder |
| 6 Tomur R22 | `DecisionStatus` 与 `managed-decision` 状态契约、DecisionEngine 可被宿主静态引用 | Tomur provider/API/Catalog 必须在 `D:\source\Tomur` 由宿主变更完成；本仓库不伪造跨仓库验收 |

## 已执行命令

在 PowerShell 7.6.6、.NET SDK 10.0.401 下：

```text
dotnet build Sezika.slnx -c Release --nologo                 # 0 warnings, 0 errors
dotnet run --project tests/Sezika.Tests/Sezika.Tests.csproj -c Release --no-build
dotnet run --project samples/Sezika.Cuda.Smoke/Sezika.Cuda.Smoke.csproj -c Release --no-build
dotnet publish samples/Sezika.Cuda.Smoke/Sezika.Cuda.Smoke.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:StripSymbols=true
```

GPU smoke 输出固定 vector-add/GEMM 数值并通过 `DecisionEngine` + `CudaDecisionScorer`，Native AOT 发布物同样通过。发布物扫描未发现 ILGPU、cuBLAS/cuDNN、ONNX Runtime、LibTorch、Reflection.Emit 或动态程序集加载字符串。
