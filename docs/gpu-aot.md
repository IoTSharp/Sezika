# 纯 C# GPU 与 Native AOT

决策日期：2026-09-23。状态：构建期 C# kernel compiler、可信 PTX/ABI 产物、完整 GPU encoder/head 与 win-x64 Native AOT smoke 已通过；跨平台性能矩阵仍待执行。

## 结论

Sezika 可以以纯 C# 编写模型和 GPU 算子，并使用操作系统/显卡驱动运行。推荐把 ILGPU 放到构建工具中，由它将固定 C# kernels 编译为 PTX；发布的 Native AOT 程序通过 C# CUDA Driver 绑定加载和执行这些产物。

**原版 ILGPU 的常规运行时编译/launcher 路径不能直接当作 Native AOT 兼容实现。** 本仓库已将 ILGPU 1.5.3 限定在 `tools/Sezika.KernelCompiler`，构建后提交 PTX、ABI manifest、源码/PTX SHA-256 和静态 C# launch descriptors；发布的 AOT 项目只引用生成的字节数组与 CUDA Driver 绑定。

## 来源与兼容性证据

| 来源 | 核实内容 |
| --- | --- |
| [ILGPU v1.5.3](https://github.com/m4rs-mt/ILGPU/releases/tag/v1.5.3) | 本次检索的最新稳定发布，发布日期 2025-07-12 |
| [研究源码提交](https://github.com/m4rs-mt/ILGPU/tree/ea51bcbdc3695554b8b9899a225d37c12cd0babe) | 本次读取的 master 快照；不与 v1.5.3 标签源码混称 |
| [Disassembler.cs](https://github.com/m4rs-mt/ILGPU/blob/ea51bcbdc3695554b8b9899a225d37c12cd0babe/Src/ILGPU/Frontend/Disassembler.cs) | 使用 GetMethodBody/GetILAsByteArray 与运行时成员解析 |
| [RuntimeSystem.cs](https://github.com/m4rs-mt/ILGPU/blob/ea51bcbdc3695554b8b9899a225d37c12cd0babe/Src/ILGPU/RuntimeSystem.cs) | 使用 DefineDynamicAssembly、DynamicMethod、GetILGenerator 构建动态代码与 launcher |
| [PTXCompiledKernel.cs](https://github.com/m4rs-mt/ILGPU/blob/ea51bcbdc3695554b8b9899a225d37c12cd0babe/Src/ILGPU/Backends/PTX/PTXCompiledKernel.cs) | 公开 PTXAssembly 字符串，可获取编译产物 |
| [.NET Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) | 不支持运行时代码生成，如 Reflection.Emit |
| [CUDA Driver Module API（12.9.1 固定文档）](https://docs.nvidia.com/cuda/archive/12.9.1/cuda-driver-api/group__CUDA__MODULE.html) | cuModuleLoadDataEx 可从 PTX/cubin 等数据加载 module |

最终编译器依赖必须在原型时锁定精确包版本/提交；读取 master 不能证明稳定包行为完全一致。不要把历史 wiki 示例 API 直接复制为当前可编译代码。

## 源码与依赖规则

| 组件 | 允许情况 |
| --- | --- |
| C# tokenizer、张量、调度、校准与 CPU 算子 | 核心实现 |
| C# ILGPU kernels | GPU 算子源代码 |
| ILGPU 编译器 | 仅开发/构建阶段；最终 AOT runtime 不使用动态编译和 launcher |
| 由 C# 自动生成的 PTX / 后续 cubin | 构建产物，有版本、hash 和 ABI 描述 |
| NVIDIA CUDA Driver | 运行时允许的驱动接口；Windows nvcuda.dll、Linux libcuda.so.1 |
| CUDA Runtime、NVRTC、cuBLAS、cuDNN、TensorRT、ONNX Runtime、LibTorch、C++ bridge | 不作为推理路径依赖；它们不属于本设计的 driver-only 例外 |
| 用户模型包携带的任意 PTX / 动态程序集 | 不加载 |

不要求 NVIDIA 用户安装完整 CUDA Toolkit 才能运行 driver-only 发行物，但最低驱动版本、PTX ISA 与 GPU compute capability 必须明确匹配。驱动自己的 JIT 组件属于显卡驱动环境；这与禁止运行时 .NET JIT 的约束不同。

## 构建与运行流程

1. `Sezika.Kernels` 用 C# 表达有限 kernel 集；避免任意模型 shape 生成无界 specialization。
2. `Sezika.KernelCompiler` 在常规 .NET 进程中运行 ILGPU，将每个 kernel 的 PTX 和 ABI manifest 导出。编译工具不是用户部署的一部分。
3. Manifest 至少包括编译器/源代码 hash、kernel entry、参数顺序/类型/字节宽度/对齐、ArrayView 展开方式、index 参数、block/grid 约束、动态 shared memory、PTX ISA、目标 SM 与功能要求。
4. 由同一构建描述生成静态 C# launch stubs；禁止运行时用 Reflection.Emit 构造代理，也禁止猜测 ILGPU ArrayView 的内部布局。
5. Native AOT 宿主加载可信、hash 匹配的产物，通过 LibraryImport 或静态声明的 unmanaged function pointer 调用 Driver API。参数 ABI 与调用约定逐项对照实际 CUDA header。
6. 模型权重和工作空间驻留 GPU；输入/输出按需要传输，使用有限 stream/event 管理依赖与完成时点。

驱动调用范围预计包括设备/context、显存、异步 copy、module/function、launch、stream/event 与错误查询。具体 API 列表以最小原型为准，不在没有调用代码前声明驱动兼容已验证。

## 第一个实现关口：GPU / AOT 最小原型（已通过）

此关口优先于完整 Transformer 的 GPU 开发。

1. 用 C# 实现 vector add 与包含边界检查的小型 GEMM，构建期导出 PTX/ABI；矩阵尺寸覆盖非整齐 tile，不能只测固定方阵。
2. 构建一个不引用 ILGPU runtime 的 .NET 10 Native AOT 测试宿主，调用 driver 分配、复制、launch、同步、读取并释放资源。
3. 检查最终依赖：没有 Reflection.Emit、运行时 IL 读取、动态托管程序集加载，且不加载 cuBLAS/cuDNN 等 native 计算库。
4. 与纯 C# CPU 标量结果对比，覆盖错误尺寸、越界防护、显存不足、设备缺失、取消/超时、重复加载/卸载与 module/stream/buffer 回收。
5. 先验证 win-x64 + 一张明确型号的 NVIDIA GPU，再验证 linux-x64；记录 SDK、ILGPU commit、driver、PTX、SM、数值误差、kernel 时间、端到端时间与冷加载时间。

实测设备为 NVIDIA GeForce RTX 4070 Laptop GPU，driver 596.08，compute capability 8.9，显存 8188 MiB。ILGPU 1.5.3 构建工具生成 14 个 kernel；ABI manifest 记录 `sourceSha256=35A25F3B52B2C359FC929524A55A6D9CF489BB930441DF03016630DBA00FD00D` 和逐 kernel SHA-256。非整齐 vector-add/GEMM、真实 mmBERT encoder/head、取消、module/buffer 回收与 win-x64 Native AOT 发布物均通过。真实 head logits 的 CPU/CUDA 最大绝对差为 `9.536743e-7`；逐算子 Trace 在同一输入上最大绝对差 `1.7578125e-2`，该差异来自 FP32 标量与 GPU FMA/数学指令顺序，当前作为数值证据记录而不宣称 bit-exact。

## 完整推理的性能工作

GPU 速度来自正确的并行策略、数据复用和精度，而不仅是换执行设备。已实现并验证：GEMM、layer norm、softmax/reduction、RoPE、embedding/gather、激活、gated GELU 与局部/全局 attention。后续仍需评估 shared memory、tiling、fusion、量化和 Tensor Core 路线，并补齐 linux-x64、冷启动/热推理、峰值显存和 p50/p95/p99 矩阵。

初始不承诺达到高度优化的 native 数学库速度。小 batch、短序列、逐算子 host-device 往返和未预热的 driver 编译可能让 GPU 更慢。CPU/GPU 对比必须用同一模型、dtype、输入和时间边界。

AMD/Intel GPU 的 OpenCL、Vulkan 路线及 macOS Metal 属于后续独立 backend；每条路线需要 C# kernel 编译产物、driver 绑定、AOT 生命周期和目标设备验证，不能以 ILGPU 支持某后端就宣称 Sezika 已支持所有 GPU。
