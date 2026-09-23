# CUDA Driver backend 与 smoke

`src/Sezika.Cuda` 是 Sezika 的 CUDA Driver backend。它只使用静态 C# `LibraryImport` 调用 NVIDIA Driver API，不引用 ILGPU runtime、CUDA Runtime、cuBLAS、cuDNN 或其他计算库。PTX 由 `Sezika.KernelCompiler` 在构建期从 C# kernels 生成，并随 ABI manifest、源码 hash 和 PTX hash 固定；它不是模型权重或用户可注入的程序。

原型包含两个公开 API：

- `CudaDevice.Open` 创建一个有所有权的 CUDA context，并提供同步、module、显存和错误诊断。
- `CudaVectorAdd.Execute` 与 `CudaGemm.Execute` 运行构建生成的 FP32 kernels。GEMM 使用 row-major `C[M,N] = A[M,K] × B[K,N]`，覆盖非整齐 block 尺寸。
- `CudaDecisionScorer` 将候选 pooled states 和线性 head 通过 GEMM 一次送入 GPU，并实现 `IDecisionScorer`；`Sezika.Cuda.Smoke` 已通过 `DecisionEngine` 的 typed Choice 请求验证这条路径。
- `CudaModernBertEncoder` 与 `CudaDecisionPipeline` 将真实 mmBERT 的 embedding、RoPE、全局/局部 attention、norm、GELU、gated MLP、决策 head 和 marker scorer 保持在显存中；`Sezika.RealModel.Smoke` 在 RTX 4070 上与 CPU scalar oracle 对齐。

`CudaContextHandle` 与 `CudaModuleHandle` 使用 `SafeHandle`；设备显存由 `CudaDeviceBuffer` 负责释放。kernel 参数通过明确的指针数组传递，module 与 context 在 `CudaDevice.Dispose` 时按依赖顺序回收。CUDA 设备不可用、驱动入口缺失、分配失败、module/JIT/launch/synchronize 失败均抛出带稳定 code 和 `CUresult` 的 `CudaException`，不会生成伪造结果。

## 受限验证

先确认 PowerShell 7 和设备信息：

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion'
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -Command 'nvidia-smi --query-gpu=name,driver_version,compute_cap,memory.total --format=csv,noheader'
```

普通 JIT smoke（需要本机 NVIDIA 驱动）：

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -Command 'dotnet run --project samples/Sezika.Cuda.Smoke/Sezika.Cuda.Smoke.csproj -c Release --no-restore'
```

Native AOT smoke：

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoLogo -NoProfile -Command 'dotnet publish samples/Sezika.Cuda.Smoke/Sezika.Cuda.Smoke.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishAot=true -p:StripSymbols=true'
```

真实模型 smoke 已证明完整 encoder/head、取消、module/buffer 回收和 win-x64 Native AOT 发布物在目标 RTX 4070 上可运行。当前证据不覆盖生产性能、linux-x64 AOT 或逐语言质量；逐算子 trace 的最大差异和 head logits 误差见 [阶段证据](stage-evidence.md)。
