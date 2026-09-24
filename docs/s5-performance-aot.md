# S5-04 / S5-05：性能、真实模型 AOT 与资源验证

本文件记录 2026-09-24 的实现和实测证据。性能与模型语言质量分开验收，所有 typed response 仍为 `uncalibrated`。

## 实现与复现入口

- `tools/Sezika.Benchmarks`：可编译为 Native AOT 的真实模型基准/生命周期程序；固定请求、模型与 tokenizer hash、硬件/运行时/精度、原始耗时、最近秩 p50/p95/p99、分配、RSS、数值对齐及结构化失败报告。
- `EncoderKernelMode.QuantizedInt8`：encoder/head/scorer 的 W8A32 逐输出行对称权重量化；读取 int8 权重执行点积，FP32 activation/accumulation，非线性及 embedding/norm 不量化。原始 FP32 张量仍保留供 oracle/CUDA 使用，额外量化张量内存单独记账。
- `IMarkerDecisionPipeline`：CPU/CUDA 共用 `ModernBertDecisionEngine` 的提示词、token/问题预算、温度、概率、legend 与拒答语义。session 接管注入 pipeline 的所有权，清理异常也继续卸载 model/gate；CUDA encoder/device 由调用方按顺序管理。
- `CudaDevice`：设备/Driver 身份、owned/current/peak 显存、module 计数和释放错误；默认关闭 profiling，独立开启时记录 H2D/D2H、module load 墙钟和 CUDA event kernel 时间。没有 runtime ILGPU、cuBLAS/cuDNN 或其他 native 数值库。
- `tools/Invoke-BoundedProcess.ps1`：任务进程身份、外部期限、异常输出和已核实任务子树清理；WSL 另用 Linux `timeout`。`tools/Summarize-S5.ps1` 验证并汇总原始报告。

参数、完整运行示例及计时口径见[基准工具说明](../tools/Sezika.Benchmarks/README.md)。基准不下载模型、不训练、不拟合校准、不发布到外部服务。

## 固定条件

模型：`convaiinnovations/laya-multilingual@052592a15d198d9ad47da779604259b10b47b7aa`，Apache-2.0。权重 SHA-256 `9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204`，tokenizer SHA-256 `609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f`。加载器逐文件及张量核验，没有下载新权重。

硬件为 Intel Core Ultra 9 185H（16 cores / 22 logical processors），约 64 GiB RAM，NVIDIA GeForce RTX 4070 Laptop GPU（driver 596.08、CC 8.9、8188 MiB）。Windows 11 build 26200；Linux 为 Ubuntu WSL2、kernel `6.6.87.2-microsoft-standard-WSL2`，不能外推为裸机 Linux 性能。

PowerShell 7.6.6；Windows SDK 10.0.401 + VS 2026 MSVC 14.51.36231；Ubuntu SDK 10.0.112 + clang 18.1.3 / gcc 13.3.0。AOT 编译并行度限制为 2，推理顺序执行、CPU 推理线程为 1，不固定 P/E 核 affinity；测量期间不同后端顺序运行。

固定短请求：state 为 `"help"`，instructions 为 `"type"`，两个候选 `"yes"` / `"no"`，choice/score/boolean 循环排列，1/8/32 问。完整 JSON、输入 hash、token 数、raw logits 和 typed response 保存在原始报告中。此输入用于固定计算负载，不是语言质量数据集。

## 计时与验收边界

热 E2E 从 JSON parse 开始，包含 tokenization、逐题 encoder/head、响应 JSON 序列化；不包含模型加载、报告写盘和结果验证。1/8/32 问分别执行 1/8/32 次 forward，不是同时处理多题的 GPU batch。每个正式配置先预热，再采样；小样本 p95/p99 不代表稳定尾延迟。

冷启动记录第 0 轮加载、后端初始化、三题首响应；OS 文件缓存和 Driver 缓存没有清空。第 1 轮为同一进程重载/卸载验证，单独列出，不混充独立 cold-start 样本。

CUDA profiling 是另外一次三题请求，每个 kernel 用 event 同步，不能与无 profiling 的热 E2E 混算。module load 包含 Driver 加载和可能的 JIT/cache 工作，不能隔离为纯 driver JIT。RSS 是整个进程高水位，包含加载和 oracle；显存 owned peak 不含 Driver/context 全部开销，free/total 是设备范围读数。完整 GC 后验证 model/encoder/head/pipeline/embedding 弱引用哨兵已回收，不能据此声称所有 RSS 立即还给 OS。

数值烟测在固定四 token 上比较 scalar reference、encoder 输出、三种 primitive logits/概率；CPU 还在相同 FP32 hidden 输入上比较 head-only。阈值在运行前固定：FP32 encoder/logits/probability 为 `0.05 / 0.005 / 0.002`，W8A32 为 `2 / 0.5 / 0.1`。通过数值门槛不等于量化语言准确率已验收。

## Windows Native AOT 正式基准

同一个最终 win-x64 可执行文件运行全部四后端。每配置预热 1 次、正式采样 5 次；下表为热 E2E 的 **p50 / p95，单位 ms**，按最近秩计算，5 样本时 p99 与 p95 相同。原始数据、每次分配量及吞吐见[汇总](evidence/s5-2026-09-24/summary.json)。这是固定短输入的小样本基线，未冻结长期性能 SLA。

| 后端 | 1 问 / 13 tokens | 8 问 / 104 tokens | 32 问 / 416 tokens |
| --- | ---: | ---: | ---: |
| scalar FP32 | 1,239.075 / 1,501.720 | 9,728.982 / 10,357.255 | 41,743.809 / 65,340.558 |
| SIMD FP32 | 1,289.871 / 1,395.542 | 6,913.985 / 7,789.679 | 21,038.749 / 24,921.720 |
| W8A32 | 1,578.179 / 1,630.017 | 14,098.108 / 14,739.406 | 66,723.845 / 82,011.972 |
| CUDA FP32 | 40.057 / 41.002 | 311.137 / 316.516 | 1,228.334 / 1,236.029 |

冷启动与重载各记录一次三题响应；加载与首响应的细分区间并未覆盖 total 中所有初始化操作，因此不能要求各列简单相加。RSS 包含 scalar oracle 等检查，不能解释为纯热推理工作集。

| 后端 | 首轮模型加载 ms | 后端初始化 ms | 首次三题响应 ms | 首轮 total-to-first ms | 同进程重载 total-to-first ms | 峰值 RSS MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| scalar | 3,016.364 | 0.012 | 4,795.469 | 7,812.862 | 6,822.815 | 3,487.50 |
| SIMD | 5,229.584 | 0.023 | 3,226.535 | 8,458.365 | 5,054.676 | 4,066.06 |
| W8A32 | 4,023.629 | 124.371 | 5,366.040 | 9,514.899 | 8,927.357 | 3,726.11 |
| CUDA | 2,956.232 | 319.673 | 131.428 | 3,408.227 | 3,877.196 | 3,335.64 |

最终 AOT 的 `Vector<float>.Count` 为 4；此前普通 .NET 试跑的宽度 8 与旧可执行文件数据没有混入本表。量化未带来本轮性能提升，32 问的 SIMD/CUDA p50 分别约 21.04 s / 1.23 s；该比较只适用于这里的硬件、短请求与单线程配置。

## 两个 RID 的真实模型 AOT 矩阵

| RID / 运行环境 | scalar | SIMD | W8A32 | CUDA | 验证范围 |
| --- | --- | --- | --- | --- | --- |
| win-x64 / Windows 11 | [通过](evidence/s5-2026-09-24/win-scalar.json) | [通过](evidence/s5-2026-09-24/win-simd.json) | [通过](evidence/s5-2026-09-24/win-int8.json) | [通过](evidence/s5-2026-09-24/win-cuda.json) | 1/8/32 问，各 5 样本、1 次预热、2 轮加载/卸载 |
| linux-x64 / Ubuntu WSL2 | [通过](evidence/s5-2026-09-24/linux-scalar.json) | [通过](evidence/s5-2026-09-24/linux-simd.json) | [通过](evidence/s5-2026-09-24/linux-int8.json) | [通过](evidence/s5-2026-09-24/linux-cuda.json) | 三题、1 样本、无预热、2 轮加载/卸载；运行 smoke，不作为性能分位数结论 |

两个 RID 分别 publish 并运行，最终 runtime/ILCompiler 为 .NET 10.0.12。8 份报告均验证 Native AOT、模型/input hash、数值门槛、取消后恢复、无效输入诊断及两轮资源卸载。Windows 的最终可执行文件 SHA-256 为 `3fb16c92838bdc730818d46bece7402eea04554ebf3b622020c9721badb2d1b8`；两个文件的完整身份、构建命令与 timeout 见[执行证据](evidence/s5-2026-09-24/execution.json)。汇总脚本已校验全部 8 份报告，`matrix_verified: true`。

两个 RID 的原生依赖检查只有系统库；CUDA Driver 在 GPU 路径动态绑定。运行项目依赖图没有 ILGPU；ILGPU 仅用于独立构建期 kernel 导出。全部模型/session 弱引用哨兵在完整 GC 后回收，Windows managed memory 约 64.47 MiB；不能据此声称进程 RSS 为零。最终进程与子进程身份核验见[进程审计](evidence/s5-2026-09-24/process-audit.json)，测量源码身份见[source hash 清单](evidence/s5-2026-09-24/source-hashes.json)。

## 自动检查与 CUDA 分项

最终 Release solution build 为 0 警告、0 错误。核心检查 37/37，CPU 资源/量化检查 35/35，CUDA 诊断检查 43/43。CUDA 检查包括非整齐 vector/GEMM 的精确传输字节和分配峰值、预取消、部分上传后构造失败、首算子后取消与恢复、跨 context 误用拒绝、两个 context 交替执行，以及 device 优先释放仍持有的子资源。异常清理使用无快照分配的有界集合遍历，finally 保证 events/context 继续释放。

Windows 正式报告在首次 encoder trace 回调处触发取消，到观察到异常的耗时分别为 scalar `0.1495 ms`、SIMD `0.0081 ms`、W8A32 `0.0082 ms`、CUDA `0.0306 ms`。这是固定算子边界上的单次取消检查，不是任意运行中 kernel 的抢占或最坏取消延迟承诺；随后三题请求均恢复成功。

Windows Native AOT CUDA 正式报告中：首次权重 H2D 为 `1,286,842,372 bytes / 180.474 ms`，module load 为 `14.620 ms`。独立三题 profiling forward 的 H2D/D2H 分别为 `156 / 156 bytes`，时间 `0.0555 / 0.2072 ms`；1,053 次 kernel launch 的 event 时间合计 `112.196 ms`，该次带 instrumentation 的墙钟时间 `127.350 ms`。这些阶段时间按各自计时范围记录，不能相加冒充另一种端到端统计。

该运行峰值 owned 显存为 `1,288,116,684 bytes`。两轮卸载均验证 owned bytes、allocation count、loaded module count、release failure count 为零；device Dispose 继续释放 context 和 profiling events。固定四 token 的 CPU/CUDA logits 最大绝对差为 `5.2154064e-7`，不是整个语言数据集的误差结论。

Windows W8A32 同一数值输入的 encoder、head-only、端到端 logits、概率最大绝对差分别为 `1.5139353 / 0.017830715 / 0.038885668 / 0.00075342547`，均处于运行前固定的烟测门槛内。encoder/head 量化缓存分别增加 `110,837,760 / 14,804,740 bytes`（合计约 119.8 MiB，不含对象头）。本轮 W8A32 热延迟高于 SIMD；这是一条可复现的权重量化基准路径，尚无量化语言准确率或压缩整个 session 的证据。

## 本机产物与执行限制

本轮最初在 D 盘发布时出现磁盘空间不足。后续 Native AOT 产物和过程日志保存在 `C:\Users\mysti\AppData\Local\Temp\sezika-s5-20260924`，模型仍在仓库既有 `.artifacts/models/laya-mmbert`，未复制为开发源码基线。

删除本轮失败发布中间目录 `.artifacts/s5/build-win`、`.artifacts/s5/build-linux` 的操作被自动审批返回 `blocked by policy`，目录保留。失败不计为通过证据；对应 Windows 根进程以及 WSL 发布命令退出后均作了检查。原有用户 `debug.log` 未修改。

S5-04 与 S5-05 已完成上述固定硬件、Windows 基准及 Ubuntu WSL2 真实模型 AOT 范围。最终结果、原始报告和命令索引保存在 `docs/evidence/s5-2026-09-24`；没有独立报告的 RID/后端不计为通过。更广硬件、裸机 Linux、长输入、量化语言质量和正式发行仍需独立验收。
