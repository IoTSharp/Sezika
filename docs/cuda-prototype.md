# CUDA Driver 原型

`src/Sezika.Cuda` 是 Sezika 的第一个 CUDA Driver 闭环探针。它只使用静态 C# `LibraryImport` 调用 NVIDIA Driver API，不引用 ILGPU runtime、CUDA Runtime、cuBLAS、cuDNN 或其他计算库。固定 PTX 仅用于验证 ABI、加载、launch、显存复制和释放；它不是模型权重或用户可注入的程序。

原型包含两个公开 API：

- `CudaDevice.Open` 创建一个有所有权的 CUDA context，并提供同步、module、显存和错误诊断。
- `CudaVectorAdd.Execute` 与 `CudaGemm.Execute` 运行固定 FP32 PTX probe。GEMM 使用 row-major `C[M,N] = A[M,K] × B[K,N]`，覆盖非整齐 block 尺寸。
- `CudaDecisionScorer` 将候选 pooled states 和线性 head 通过 GEMM 一次送入 GPU，并实现 `IDecisionScorer`；`Sezika.Cuda.Smoke` 已通过 `DecisionEngine` 的 typed Choice 请求验证这条路径。

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

当前原型尚未证明完整 Sezika encoder、真实模型资产或生产性能。通过 vector add/GEMM 和 tiny DecisionEngine smoke 表示 Driver ABI、固定 head GEMM、typed request 和资源回收在目标设备上可用；完整模型路径仍需在同一显存、取消、AOT 和 CPU oracle 约束下逐算子对齐。
