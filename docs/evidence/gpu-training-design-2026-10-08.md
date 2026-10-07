# S4-07 三题型 linear marker head GPU 训练工程

状态：最小工程已实现、构建期 PTX/ABI 已导出、极小张量真实 GPU 合同检查通过；CLI、正式数据训练、encoder 反传、模型质量、训练 AOT 与其他设备/平台没有本次验收。开始只读梳理，监督主线程随后确认工程范围并在教师服务尚未启动时授权串行实卡检查。基线 Sezika HEAD：62369fd2f94930afe4dabf42530eb833f97f5858。共享 ROADMAP/CHANGELOG/HANDOFF 和 Git 由监督主线程维护。

## 现有路径与最小范围

CPU `IndependentMarkerHeadTrainer` 训练无 bias 的 `[3,H]` 权重；Choice/Score/Boolean 各使用一行，候选顺序、Boolean false/true 与 Score 等级索引不改变。当前没有名为 CudaLinearHead 的类型；独立 CUDA 前向位于 IndependentCudaDecisionPipeline，复用 `CudaKernels.Linear`。

新 `IndependentCudaMarkerHeadTrainer.Train` 接受 caller-owned CudaDevice 和已验证 MarkerFeatureSet，复用 CPU Prepare 的完整序列/marker/标签/有限值校验与字节 hash、固定 xorshift 顺序及 MarkerTrainingState。核心只增加对静态引用 Sezika.Cuda 的窄 InternalsVisibleTo，并将 MakeOrder 改为 internal。没有新动态程序集、模型 API、native 数值库、CUDA Runtime、cuBLAS/cuDNN、运行时 ILGPU launcher。

冻结 encoder：现有 CudaModernBertEncoder 实现 IEncoder，因此后续 CLI 可向 IndependentMarkerFeatureExporter 传该 GPU encoder。最小 feature-export 会由 GPU 计算完整序列，再将快照读回供 marker 提取和 hash 绑定；训练时按样本一次上传并常驻 GPU。GPU gather 可以后续减少传输，不将 CPU 地址提取/序列化当作梯度计算。本次检查直接使用 tiny 已知特征，不重新运行真实 encoder。

当前 train-original 仍显式 Scalar encoder + CPU workflow；新 GPU trainer 不自动改变该命令。正式训练接线必须显式 GPU backend，缺设备时失败，CPU 仅用于受限参考/诊断，不得静默 fallback。报告与恢复还须绑定 feature backend、training backend、FP32数学合同、kernel source/manifest/static PTX 身份及设备/驱动；既有 feature_source 不能证明训练设备。这是主线程下一切片。

## 算法与提交语义

每题以候选 marker 特征 X 和对应类型权重 W 求 logits；现有 Linear 在 GPU 产生 `[候选,3]`。三个新 C# kernels：

| kernel | GPU 工作 | 静态参数数 |
| --- | --- | ---: |
| MarkerCrossEntropy | 单 thread 稳定 softmax；loss=`log(sum(exp(z-max)))+(max-z[target])`；delta=`p-onehot` | 6 |
| MarkerGradientSgd | 每参数按固定候选顺序累加梯度，写 next；未选类型行直接复制旧值 | 9 |
| MarkerUpdateStatus | 单 thread 检查 loss、完整梯度与 next 全部有限，输出 loss/status；无 atomic 竞态 | 5 |

梯度和 SGD 均在 GPU 计算。host 每步只下载2个float作健康/进度检查；已通过有限值检查并检查总取消/期限后才交换 current/next。失败没有返回新 state，不更改传入 resume/initial 权重。GPU FP32/FMA 与 CPU FP64 参考不承诺 bit exact；同一GPU、PTX与特征的2+2恢复和连续4步按本次tiny输入验证了相同权重字节及 loss。

复用旧 byte-level feature hash；身份比较接受hex大小写，但 hash序列化保留原始拼写，因此仅改identity中的hex大小写仍可能导致 feature SHA 不同并拒绝恢复。此工程没有暗改旧checkpoint格式或canonicalize hash；后续需要版本化解决，不能把它描述为大小写无关的完整恢复合同。

## ABI、产物与文件归属

ArrayView 使用现有16-byte / alignment8 ABI，float/int scalar均4-byte / alignment4。构建工具固定 ILGPU1.5.3、PTX7.0、sm_70；runtime只使用生成字节与Driver调用。17项单一 manifest 绑定 kernel源SHA及每PTX SHA；与基线比较确认旧14项 entry、block、参数ABI和PTX hash全未漂移。

- Kernel源码：src/Sezika.Kernels/TransformerKernels.cs。
- 编译期边界与固定17项表：tools/Sezika.KernelCompiler/Program.cs。
- 静态运行：src/Sezika.Cuda/CudaKernels.cs、CudaPtx.cs、IndependentCudaMarkerHeadTrainer.cs。
- 生成产物：src/Sezika.Cuda/Generated 下三个新PTX、kernel-manifest.json、GeneratedKernelArtifacts.g.cs。未手写PTX；旧14 PTX bytes不变。
- 核心复用：src/Sezika/AssemblyInfo.cs、IndependentMarkerHeadTraining.cs。
- tiny checker：tests/Sezika.Cuda.Diagnostics.Tests/IndependentCudaTrainingChecks.cs 和 Program.cs 三个显式检查入口。

Kernel source SHA-256：3743158624364474AE920D4863C48D322168954B8FB992B5868D7DB0660E2FBD。

Manifest SHA-256：8CF83A3B5145345D92D50D4695B4DD0200AE690C9E1FD70E910B399241B18D79。

Static generated C# SHA-256：2FA7854F06EC34CFEE2C64F0FA9C15FF05EF4A9A9BE04480FAEA5506822F7D7D。

## 数量、时间与资源边界

训练至多32题、32候选、H<=2048、1024 tokens、10000累计步、600秒；Score<=10候选、Boolean=2。最多32 feature buffers+7 working buffers，每题特征只上传一次；最大feature缓存8MiB，加3个权重/next/gradient缓冲至多72KiB和小logits/delta/status，显式owned buffer总量远低64MiB。本次tiny为H3、2或3候选；溢出case为H2。

每host循环有固定数量上限、deadline与取消，kernel循环由上述host校验尺寸限制。Driver调用不能中途取消，必须用外层bounded process兜底。train调用仅拥有其buffers/modules，finally按逆序释放，保留caller-owned device/context；检查器用using创建并释放自己的device。编译工具最多17 kernel、每PTX1MiB、ABI65536行/16参数，Ctrl+C、墙钟和逐kernel进度；先max-kernels1小输入，再17完整集。

## 本次执行证据

固定 PowerShell7.6.6、.NET SDK10.0.401，所有外部程序由 tools/Invoke-BoundedProcess.ps1 记录PID、创建时间、命令/参数、父链与finally清理。根进程日志目录：`.artifacts/gpu-head-training-20261008-head-checkpoint/logs`。

| 执行 | PID | 外层上限 | 结果 |
| --- | ---: | ---: | --- |
| KernelCompiler 单节点 build | 65596 | 180秒 | exit0，零警告错误 |
| 1 kernel tiny export | 59108 | 30秒 | exit0，内层15秒 |
| 17 kernel full export | 14276 | 180秒 | exit0，内层120秒，全部ABI匹配 |
| CUDA+checker单节点 build | 106320 | 180秒 | exit0，零警告错误 |
| CPU finite-difference reference prepare | 74440 | 30秒 | exit0，27检查；内层3秒，无GPU训练 |
| GPU tiny Choice | 33204 | 90秒 | exit0，21检查 |
| GPU full three types+finite failure | 43672 | 90秒 | exit0，61检查 |
| GPU index0 identity | 93256 | 20秒 | RTX4070 Laptop、596.08、8188MiB、CC8.9 |

参考先以三类型中央差分核CPU解析梯度；GPU检查使用非零初始权重，以CPU FP64 loss/梯度和GPU更新反推梯度作容差对照（abs/relative各2e-5）。覆盖未选类型行保持原值、GPU恢复2→4与连续4步、预取消无launch、提交后取消不更改resume、FP32大特征在第2步logit溢出被status拒绝；每个路径核 owned bytes/allocations/modules/releaseFailureCount 回到初始，不销毁caller device。没有真实模型、数据训练、吞吐或质量结论。

所有程序已结束；process/cleanup核查和产物身份保留于同任务artifact目录。ptx-tiny/full及日志作为可复查证据保留，工程bin/obj为既有共享构建目录，不清空。没有本阶段新增长期服务或MCP进程。

监督者复审后另跑既有CUDA回归PID46224（59项）与独立pipeline回归PID68444（16项），均exit0，外部60秒预算下各约4.2/4.1秒；没有教师服务占用GPU。新增训练检查与旧前向回归分别记录，原独立pipeline的Native AOT历史不充当本次训练AOT证据。
