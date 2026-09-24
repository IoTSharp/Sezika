# 阶段 1–6 实现证据

状态（2026-09-24）：固定真实模型资产、tokenizer oracle、CPU/CUDA encoder 与真实 marker-head typed decision 已落地。S5-04/S5-05 完成 Windows 四后端 Native AOT 性能矩阵及 Ubuntu WSL2 四后端真实模型 AOT smoke；多语言质量与裸机 Linux 性能仍需独立验收。完整口径见 [S5 性能与 AOT 证据](s5-performance-aot.md)及[原始报告](evidence/s5-2026-09-24/)。

路线图任务编号见 [ROADMAP.md 的编号任务板](../ROADMAP.md#编号任务板)。本表只记录已经存在的证据，任务状态不会因为设计文档或构建成功自动升级。

| 阶段 | 已实现 | 当前证据边界 |
| --- | --- | --- |
| 1 模型资产/tokenizer | 固定 `convaiinnovations/laya-multilingual@052592a15d198d9ad47da779604259b10b47b7aa`，Apache-2.0；仓库提交 `model.json` v1 与 `source_lock.json`，包含 170 tensor mapping、SafeTensors F16/F32、文件及逐张量 SHA-256；C# BPE/Metaspace tokenizer | `.artifacts` 中的权重和 tokenizer 不进入仓库分发；8 个中英、CJK、RTL、组合字符、emoji、混合脚本样本与 `tokenizers 0.22.2` oracle 逐项通过；SafeTensors 重叠、越界和不支持 dtype 的负向测试通过，真实加载器校验 170 个张量 hash。S1-04 已提供有界 `.part` Range 下载、loopback 断点/Range/hash 测试、代理重试、staging/原子提交、`installation.json`、嵌套 model id、lease 卸载和损坏/路径校验；隔离检查返回 4/4（[发布目录设计](model-packaging.md)）；不下载真实模型。 |
| 2 C# encoder | 真实 22 层 mmBERT FP32 scalar encoder：embedding、global/local attention、split-half RoPE、LayerNorm、gated GELU MLP、取消与形状检查；`EncoderExecutionOptions`、`EncoderWorkspacePool`、`System.Numerics.Vector<float>` SIMD Linear/Norm/Add/GELU 与 W8A32 线性层 | tiny ModernBERT fixture 的逐 trace scalar/SIMD 最大绝对差 `0`，尾部向量 Linear、取消、deadline、并发租约、workspace 归还和 unload 后无活动租约均通过 [S2-03 记录](s2-03-resources.md)；早期真实包 CPU/GPU Trace 最大差异为 `1.7578125e-2`。Windows scalar/SIMD/int8 性能及当前数值误差见 [S5 记录](s5-performance-aot.md)；W8A32 慢于 SIMD，额外量化缓存不降低总内存。 |
| 3 决策闭环 | Choice/Score/Boolean、稳定 softmax、温度 profile、concentration/abstention、session/deadline/token budget、source-generated JSON；typed response 现在保留 raw logits，`PrimitiveAlignment` 对齐 logits/probabilities/legend/abstention；session 增加 micro-batch/workspace/resident memory 预算与 unload fail-closed | 4 个中英文固定输入 fixture 通过逐键数值/legend/abstention 对齐，22 项核心测试覆盖 typed output、S3 对齐契约、模型安装/lease 及资源边界；真实模型质量仍单独验收 |
| 4 多语言/校准 | 冻结 encoder 的可复现二分类 head trainer、温度拟合、NLL/Brier/ECE/accuracy/macro-F1/coverage/selective-risk 计算；新增许可明确原创中英×领域×primitive fixture、数据卡、manifest hash 与 12 个绑定 profile | `tools/Validate-S4Data.ps1` 通过 `24 records, 8 files, fixture_only`；S4-03 profile 全部 `pending_measurement`，门槛已冻结但未运行真实模型，不能视为多语言质量达标（[数据卡](data-card-s4-02.md)、[校准报告](calibration-report-s4-03.md)） |
| 5 CUDA/AOT | `Sezika.Kernels` C# → ILGPU 1.5.3 构建期导出 14 个 PTX、ABI manifest、源码/PTX SHA-256 与静态 launch descriptors；CUDA Driver `LibraryImport`/SafeHandle；完整 resident GPU encoder/head；W8A32 encoder/head、统一后端基准与资源遥测 | S5-04/S5-05 完成：Windows `win-x64` Native AOT scalar/SIMD/int8/CUDA 四后端各覆盖 1/8/32 问、每项 5 次采样；Ubuntu WSL2 `linux-x64` 四后端真实模型 AOT smoke 各为 3 问、1 次采样。两 RID 发布无警告，取消、卸载、对象回收哨兵与 CUDA 资源归零通过；[完整证据](s5-performance-aot.md)保留数值误差、冷首请求、热采样与内存口径。WSL smoke 不代表裸机 Linux 性能，当前量化无速度/总内存收益。 |
| 6 开源发布 | 核心类库已可构建开发 NuGet 包，包内带 README、Apache-2.0 LICENSE、NOTICE 与第三方声明 | `.artifacts/packages/Sezika.0.1.0-dev.nupkg` 已生成；正式发布版本、CLI 发行流程、模型/数据卡、签名、跨平台发行流程与 NuGet 上传仍未验收；S5 的 Windows/Ubuntu WSL2 AOT 构建和运行验证不等同正式分发。 |

## 已执行命令

2026-09-24 的 S5 最终验证：Release solution build 为 0 警告/0 错误，核心/资源/CUDA 测试分别为 37/35/43 项通过，`win-x64` 与 `linux-x64` Native AOT 发布均无警告。Windows 四后端完整基准与 Ubuntu WSL2 四后端真实模型 AOT smoke 共八份报告汇总验证通过，`matrix_verified=true`；实际命令、环境、模型/输入 hash 和独立报告见 [S5 证据](s5-performance-aot.md)及[证据目录](evidence/s5-2026-09-24/)。正式采样与额外 instrumented CUDA pass 分开记录；未进行多语言质量验收。

此前独立使用入口（S3-05）验证：`DecisionModelRuntime` 拥有模型和 CPU session；CLI 通过文件/stdin 读取请求并返回结构化结果。当轮 Release solution build 为 0 警告/0 错误，核心测试为 23/23（包含并发 unload 回归）。固定真实模型的英文文件请求和中文 stdin 请求均返回 Choice/Score/Boolean，分别使用 95/119 tokens；运行记录见 [独立 CLI smoke](standalone-cli-smoke.md)，原始结果及输入 hash 见 [JSON 证据](evidence/standalone-cli-2026-09-23.json)。该历史验证轮未执行 CUDA、AOT 或语言质量评测。

以下保留之前阶段验证的命令与结果：

在 PowerShell 7.6.6、.NET SDK 10.0.401 下：

```text
dotnet build Sezika.slnx -c Release --nologo                 # 0 warnings, 0 errors
dotnet run --project tests/Sezika.Tests/Sezika.Tests.csproj -c Release --no-build
dotnet run --project samples/Sezika.Cuda.Smoke/Sezika.Cuda.Smoke.csproj -c Release --no-build
dotnet run --project tools/Sezika.ModelTool/Sezika.ModelTool.csproj -c Release --no-build -- verify --timeout-seconds 1200
dotnet run --project tools/Sezika.KernelCompiler/Sezika.KernelCompiler.csproj -c Release --no-build -- D:\source\Sezika\src\Sezika.Cuda\Generated
dotnet run --project samples/Sezika.RealModel.Smoke/Sezika.RealModel.Smoke.csproj -c Release --no-build
dotnet publish samples/Sezika.Cuda.Smoke/Sezika.Cuda.Smoke.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:StripSymbols=true
dotnet publish samples/Sezika.Cpu.Smoke/Sezika.Cpu.Smoke.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:StripSymbols=true
# Ubuntu WSL: same command with -r linux-x64, then run the published binary
```

早期验证在 PowerShell 7.6.6、.NET SDK 10.0.401 下执行，`dotnet build Sezika.slnx --no-restore` 通过且为 0 警告/0 错误；当轮核心测试项目 22 项通过，独立 `Sezika.Resources.Tests` 12 项通过并报告 scalar/SIMD trace `max_abs_error=0`；`tools/Validate-S4Data.ps1` 报告 `24 records, 8 files, fixture_only`。模型工具输出 `Verified 170 tensors, model and tokenizer SHA-256`；构建期工具输出 14 个带 ABI/hash 的 PTX，并在临时目录重生成后与提交目录逐文件 SHA-256 一致。真实模型 smoke 输出 CPU/CUDA logits、`real_typed_decisions passed`、`max_abs_error=9.536743E-07` 和 `cancel smoke passed`；CUDA smoke 覆盖非整齐 vector-add/GEMM 与决策头。另有 tiny model 的 CPU win-x64 与 Ubuntu WSL linux-x64 Native AOT smoke，及 CUDA/tiny win-x64 AOT smoke；这些早期发布物未加载 644 MB 真实权重，不能代替真实模型 AOT 推理证据，后者现由上述 S5 实测补充。发布项目不引用 ILGPU 项目，ILGPU 只存在于构建工具。S3-03 的 fixture 对齐数值报告见 [s3-03-primitive-alignment-report.md](s3-03-primitive-alignment-report.md)，真实模型参考实现和质量数字仍单独验收。
