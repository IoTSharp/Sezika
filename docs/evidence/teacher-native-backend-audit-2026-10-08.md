# 教师 CPU native backend 注册核查（2026-10-08）

本次只读核查针对教师 Tomur 的 llama.cpp CPU bundle，固定源码 revision 为 `1bc7a5af0d14`。未修改 Tomur/native 源码，未构建、启动、加载 DLL 或请求模型。结论仅覆盖源码注册方式、实际 CMake 生成配置与 PE 依赖/导出，不能代表真实推理或模型/context ABI 已通过。

## 结论

现有 `GGML_BACKEND_DL=OFF` 不会禁用 CPU。该配置使用编译期链接和 registry 注册 CPU；后端库仍可随 `BUILD_SHARED_LIBS=ON` 生成为 DLL。实际 `ggml.dll` 依赖 `ggml-cpu.dll`，生成的 Release 工程定义 `GGML_USE_CPU` 并链接 `ggml-cpu.lib`，与源码路径一致。

Tomur 的 `LlamaBackendInitializer.TryLoadCpuBackend` 会调用 `ggml_backend_load(ggml-cpu.dll)`，它按插件方式寻找 `ggml_backend_init`。当前 CPU 库提供 `ggml_backend_cpu_reg`，没有插件入口 `ggml_backend_init`；因此该动态探测日志不能证明 CPU 未注册，也不能作为 `vocab_only`、`no_alloc` 异常或模型/context ABI 崩溃的根因。

CPU bundle 的最小配置建议是保持 `-DGGML_CPU=ON -DGGML_BACKEND_DL=OFF -DGGML_CPU_ALL_VARIANTS=OFF -DBUILD_SHARED_LIBS=ON`。无需为 CPU 可用切换为 DL。若以后有明确的插件或多 CPU variant 需求，才按该模式独立构建与验证。

## 源码证据

以下路径均位于 `D:\source\Tomur`：

| 文件与行号 | 实际行为 |
| --- | --- |
| `native/llama.cpp/ggml/CMakeLists.txt:86` | `GGML_BACKEND_DL` 默认 `OFF`。 |
| `native/llama.cpp/ggml/CMakeLists.txt:189` | `GGML_CPU` 默认 `ON`。 |
| `native/llama.cpp/ggml/src/CMakeLists.txt:381` | OFF 分支使用普通 `add_library` 创建后端。 |
| `native/llama.cpp/ggml/src/CMakeLists.txt:395` | 将后端链接到 `ggml`。 |
| `native/llama.cpp/ggml/src/CMakeLists.txt:434` | 非 DL 分支为启用的 CPU 后端定义 `GGML_USE_CPU`。 |
| `native/llama.cpp/ggml/src/ggml-backend-reg.cpp:171` | registry 构造器在 `GGML_USE_CPU` 下调用 `register_backend(ggml_backend_cpu_reg())`。 |
| `native/llama.cpp/ggml/src/ggml-backend-impl.h:256` | DL 模式导出插件入口 `ggml_backend_init`。 |
| `app/Inference/LlamaBackendInitializer.cs:46` | 初始化先探测动态后端，再调用 `llama_backend_init`。 |
| `app/Inference/LlamaBackendInitializer.cs:217` | CPU 候选库以 `ggml_backend_load` 方式探测。 |
| `app/Inference/LlamaNativeMethods.cs:12` | `llama_backend_init` 绑定到 `llama` 库。 |
| `app/Inference/LlamaNativeMethods.cs:16` | 动态注册/设备枚举绑定到 `ggml` 库。 |

开启 DL 后，CMake 将后端改为 `MODULE`，不定义上述静态注册宏。它要求 `BUILD_SHARED_LIBS=ON`；当前 x86 CPU 路径还要求 `GGML_NATIVE=OFF`。`GGML_CPU_ALL_VARIANTS=ON` 也要求 DL。插件加载还须通过入口、CPU score 与 API version 检查。

## 实际构建缓存

缓存为 `.artifacts/supervision-20261008/teacher-runtime/native-build/CMakeCache.txt`；这是观察到的配置，没有重新配置或构建。

| 配置 | 缓存值 |
| --- | --- |
| `BUILD_SHARED_LIBS` | `ON` |
| `GGML_BACKEND_DL` | `OFF` |
| `GGML_CPU` / `GGML_CPU_ALL_VARIANTS` | `ON` / `OFF` |
| `GGML_NATIVE` | `OFF` |
| `GGML_AVX` / `GGML_AVX2` / `GGML_BMI2` | `ON` / `ON` / `ON` |
| `GGML_AVX512` / `GGML_AVX_VNNI` | `OFF` / `OFF` |
| `GGML_CPU_REPACK` / `GGML_LLAMAFILE` | `ON` / `ON` |
| `GGML_OPENMP` / `GGML_BLAS` / `GGML_CUDA` | `OFF` / `OFF` / `OFF` |
| `CMAKE_GENERATOR` / `CMAKE_GENERATOR_PLATFORM` | `Visual Studio 18 2026` / `x64` |
| `CMAKE_BUILD_TYPE` | `Release`；实际构建命令另指定 `--config Release`。 |

生成工程 `native-build/llama.cpp/ggml/src/ggml.vcxproj:168` 包含 `GGML_USE_CPU`，第 187 行链接 `Release\ggml-cpu.lib`。CPU Release 工程还定义 `GGML_AVX2`、`GGML_FMA`、`GGML_F16C` 与 `GGML_BMI2`。本次没有执行 CPU 指令集或性能验证。

## DLL 检查与资源闭环

使用已知工具 `C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.51.36231\bin\Hostx64\x64\dumpbin.exe`，报告版本 `14.51.36260.0`。每个命令为 `/dependents /exports <DLL>`，通过仓库 `Invoke-BoundedProcess.ps1` 执行，每个上限 15 秒；先检查一份 CPU 库，随后最多三份、批次上限 180 秒。四份库均来自教师 host 的 `native/runtimes/win-x64/native`，全部成功。

| DLL | 导出名称数 | 配套依赖与所需导出 |
| --- | ---: | --- |
| `ggml-cpu.dll` | 64 | 依赖 `ggml-base.dll`；有 `ggml_backend_cpu_reg`，无 `ggml_backend_init`。 |
| `ggml.dll` | 16 | 依赖 `ggml-cpu.dll`、`ggml-base.dll`；有加载、注册及设备枚举入口。 |
| `ggml-base.dll` | 685 | 有设备类型、属性、所属 registry 与 registry 名称入口。 |
| `llama.dll` | 262 | 依赖 `ggml.dll`、`ggml-base.dll`；有 backend init 与模型/context 默认参数入口。 |

依赖表还列出系统 Windows API、MSVC 与 UCRT DLL。本次未实际加载这些依赖，因此没有把静态 PE 检查表述为 runtime 初始化验证。

主核查的四个 dumpbin 任务与委派源码核查的九个短进程均为 `Succeeded`，`CleanupErrors` 为空。最终通过有界 CIM 查询复核全部 13 个已记录 root PID 及以它们为父 PID 的进程，均不存在。短进程部分在 CIM 快照前已退出；执行器保留 Process.Start 的 PID、启动时间、argv、launcher 与 parent 记录。没有按名称终止进程，也未删除临时文件或用户文件。

保留证据：

- `.artifacts/supervision-20261008/teacher-native-backend-audit/dll-audit-summary.json`：四份 DLL 的依赖/导出、执行记录与最终退出核对。
- `.artifacts/supervision-20261008/teacher-native-backend-audit/processes/`：四份 dumpbin stdout、stderr、identity 与 result。
- `.artifacts/supervision-20261008/backend-registry-audit/`：九次只读源码核查的 stdout、stderr、identity 与 result；主核查已复核每份结果。

本次不对教师服务 `0xC0000409` 失败退出作新增根因断言。模型/context ABI 修复与下一次真实教师试运行由独立任务负责。
