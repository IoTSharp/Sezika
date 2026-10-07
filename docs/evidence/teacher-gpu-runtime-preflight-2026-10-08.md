# 教师 GPU runtime 准备核查（2026-10-08）

本次只读检查为固定 llama.cpp `1bc7a5af0d14` 的 Tomur 离线教师 GPU 试运行准备工具、DLL 与资源证据。未构建、启动 native runtime、请求模型、安装工具、修改 Tomur 源码或执行 Git 操作。教师使用 Tomur 既有 native 路径；这些 native 库不进入 Sezika core。

## 已确认的本机条件

| 项目 | 观察值 |
| --- | --- |
| GPU | NVIDIA GeForce RTX 4070 Laptop GPU，index 0，compute capability 8.9。 |
| 显存 | 总计 8188 MiB；第一次查询空闲 7625 MiB，利用率 0%。这是瞬时快照。 |
| 驱动 | 596.08；`nvidia-smi` 报告 CUDA Version 13.2。 |
| nvcc | `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.2\bin\nvcc.exe`，13.2.51。 |
| CUDA Toolkit | 已安装 NVIDIA CUDA 13.2.0；`CUDA_PATH` 与 `CUDA_PATH_V13_2` 指向同一目录。 |
| MSVC | 已有 CPU 构建记录为 19.51.36260.0；固定目录 `VC\Tools\MSVC\14.51.36231\bin\Hostx64\x64`。 |
| host compiler 支持 | CUDA 13.2 `include/crt/host_config.h:162` 允许 `1920 <= _MSC_VER < 1960`，当前 1951 在范围内。无需从该检查推导 `allow-unsupported-compiler`。 |
| VS 集成 | VS 2026 `MSBuild\Microsoft\VC\v180\BuildCustomizations` 内已登记 CUDA 13.2 props、targets、Version.props 与 xml。 |
| 系统内存 | 总计 66540240 KiB，检查时空闲 25253004 KiB，约 24.08 GiB。 |

现有教师 cache 为 `GGML_CUDA=OFF`，host 中不存在 `ggml-cuda.dll`。Tomur source bundle 宣告 CUDA 13，但 `native/runtimes/win-x64/native` 物理目录不存在；三个明确 preset build cache 路径也不存在。因此不能把已有 manifest 当成 GPU runtime 产物。

## 最小固定版本构建方案

精确结构化参数已保存于 `.artifacts/supervision-20261008/teacher-gpu-readiness/gpu-build-preparation.json`，供监督者通过 `Invoke-BoundedProcess.ps1` 使用；本次未执行方案。

- 源码仍为 `D:/source/Tomur/native/llama.native`，其 llama.cpp submodule 固定为上述 revision。
- 使用新的任务构建目录 `.artifacts/supervision-20261008/teacher-runtime/native-build-cuda132`。
- CMake 固定为已知 VS 2026 bundled executable，generator `Visual Studio 18 2026`，architecture `x64`，toolset `cuda=13.2`，`CUDAToolkit_ROOT` 明确指向已登记 Toolkit。
- 固定 `CMAKE_CUDA_ARCHITECTURES=89-real`，只编译本次 4070 的 device code。
- 设置 `GGML_CUDA=ON`、`GGML_CPU=ON`、`BUILD_SHARED_LIBS=ON`；保持 `GGML_BACKEND_DL=OFF`、`GGML_CPU_ALL_VARIANTS=OFF`、`GGML_NATIVE=OFF`、`GGML_OPENMP=OFF`、`GGML_CUDA_NCCL=OFF`。
- CUDA graphs 与 FlashAttention 保持 `ON`，不编译所有 FlashAttention quant variants。
- 只构建 `--config Release --target llama --parallel 2`，避免执行 wrapper 的其他视觉目标。配置上限 120 秒，构建上限 1200 秒。

现有 `windows-x64-cuda13` preset 继承 `gpu-base`，会开启 DL 和所有 CPU variants。针对单块固定 GPU 的本任务可使用上述较小构建范围；静态 registry 模式同样会注册 CUDA 后端。

## 同版本配套 DLL

五个核心 DLL 必须一起取自新构建的 `bin/Release`：`llama.dll`、`ggml.dll`、`ggml-base.dll`、`ggml-cpu.dll`、`ggml-cuda.dll`。不能将新 CUDA backend 和此前 CPU cache 的核心库混配。

下列三个实际 DLL 已在 `C:/Program Files/NVIDIA GPU Computing Toolkit/CUDA/v13.2/bin/x64` 核实，来源为本机已安装的 NVIDIA CUDA Toolkit：

| DLL | 字节数 | dumpbin 静态依赖 |
| --- | ---: | --- |
| `cudart64_13.dll` | 555120 | Windows API-set DLL。 |
| `cublas64_13.dll` | 51571824 | `cublasLt64_13.dll` 与 `KERNEL32.dll`。 |
| `cublasLt64_13.dll` | 453003888 | `KERNEL32.dll`。 |

`C:\Windows\System32\nvcuda.dll` 存在，版本为 32.0.15.9608，属于系统驱动。实际 GPU bundle 的依赖仍需在构建后检查，PE 检查不能代替真实加载。host manifest 也需要如实纳入 CUDA variant 和实际配套资产身份。

## GPU 试运行边界

8 GiB 显存不等于已经证实能容纳 9B Q4 的约 5.29 GiB 权重、2048-token context、KV/recurrent state 与 scratch。建议先做一项有界真实加载/请求，再扩至最多 32 项、并发 1、批次墙钟上限 1200 秒；冷加载/首项上限 120 秒。128 output tokens 为初始资源建议，最终应与所需结构化输出长度匹配。

Tomur 的配置 `gpu_layers` 会规范化到 0..999（`app/Config/LocalConfiguration.cs:101`）。**配置 -1 会变为 0，因此该 host 必须用 999 请求 full offload，或使用明确的正数请求 partial offload。** 当前 native session 对 2048 context 使用 batch 2048、micro-batch 512。

试运行应配置 `runtime.accelerator.preference=cuda`，读取真实设备选择与 GPU layer offload 日志，确认选中 4070 和非零 GPU 层，然后再收集 32 条。若回退 CPU、加载失败或 GPU 不可见，停止该批次。配置和设备枚举不构成 GPU 推理通过证据，仍须保留真实 token usage、实际 offload 与资源记录。

初始资源预算为 GPU resident 目标不超过 7168 MiB、显存空闲下限 512 MiB，host resident 上限 16 GiB、系统空闲下限 8 GiB；这些是本次试运行保护边界，不是已测峰值。是否需要降低正数 offload 或调整 context，由首项真实结果决定。

## 检查与清理记录

两个 `nvidia-smi` 查询、一次 `nvcc --version` 与三次 Toolkit DLL `dumpbin /dependents` 均通过仓库 bounded runner 执行，每项上限 15 秒。DLL 批次最多三项、90 秒。六项全部成功，`CleanupErrors` 为空；最终按六个记录 root PID 与 parent PID 查询，均不存在。本次没有创建持续监控、终止进程或删除文件。

精确 argv、PID、启动时间、launcher/parent、结果与 stdout/stderr 保留在 `.artifacts/supervision-20261008/teacher-gpu-readiness/processes/`。GPU plan JSON 也保留依赖表及最终清理核对。持续监控脚本另行准备，只有收到新服务唯一身份后才启动。
