# S2-03 CPU encoder resource evidence

状态（2026-09-23）：已实现并在 tiny deterministic ModernBERT fixture 上验证 CPU scalar/SIMD 对齐与有界资源生命周期。

## 实现边界

- `ModernBertEncoder` 接受 `EncoderExecutionOptions`，显式选择 `Scalar` 或 `Simd`，并为每次请求创建 linked cancellation/deadline token。
- deadline 同时由 `CancellationTokenSource.CancelAfter` 和 `Stopwatch` 边界检查驱动，避免系统 timer 调度延迟让已过期请求继续进入下一层。
- `EncoderWorkspacePool` 以 token 数、hidden/intermediate 宽度和 attention score 上限估算单请求 FP32 工作空间；并以 `SemaphoreSlim` 限制活动租约数和总字节数。模型权重保持只读共享；租约在 `finally`/`using` 路径释放。
- SIMD 路径使用 `System.Numerics.Vector<float>` 实现 Linear、LayerNorm、Residual Add 和 gated GELU 的向量化；attention、RoPE 和 trace 结构继续复用标量实现，保证逐算子对照边界清晰。Scalar 仍是正确性参考。
- `CudaModernBertEncoder` 的纯构造校验使用 `using` 立即释放 CPU encoder workspace，不留下未拥有的资源。

## 有界验证

命令（PowerShell 7.6.6，.NET SDK 10）：

```text
dotnet build tests/Sezika.Resources.Tests/Sezika.Resources.Tests.csproj -c Release --nologo
dotnet run --project tests/Sezika.Resources.Tests/Sezika.Resources.Tests.csproj -c Release --no-build
```

结果：12 项通过，`max_trace_abs_error=0`。覆盖逐 trace 拓扑、scalar/SIMD 最大误差、输出形状、非整齐 SIMD linear 尾段、正常释放、第三并发租约拒绝、租约字节归还、卸载与活动租约交叉、预取消、取消后无活动租约、deadline 边界和 deadline 后无活动租约。

该夹具证明实现和资源边界；它不代表真实 mmBERT 的语言质量、吞吐、p95/p99、量化收益或跨平台 SIMD 性能已验收。完整性能矩阵仍属于 S5-04。
