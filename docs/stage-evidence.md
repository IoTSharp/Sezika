# 阶段 1–6 实现证据

状态（2026-09-23）：固定真实模型资产、tokenizer oracle、CPU/CUDA encoder、真实 marker-head typed smoke 与 Native AOT smoke 已落地；多语言质量报告和跨平台性能矩阵仍需分别验收。

路线图任务编号见 [ROADMAP.md 的编号任务板](../ROADMAP.md#编号任务板)。本表只记录已经存在的证据，任务状态不会因为设计文档或构建成功自动升级。

| 阶段 | 已实现 | 当前证据边界 |
| --- | --- | --- |
| 1 模型资产/tokenizer | 固定 `convaiinnovations/laya-multilingual@052592a15d198d9ad47da779604259b10b47b7aa`，Apache-2.0；仓库提交 `model.json` v1 与 `source_lock.json`，包含 170 tensor mapping、SafeTensors F16/F32、文件及逐张量 SHA-256；C# BPE/Metaspace tokenizer | `.artifacts` 中的权重和 tokenizer 不进入仓库分发；8 个中英、CJK、RTL、组合字符、emoji、混合脚本样本与 `tokenizers 0.22.2` oracle 逐项通过；SafeTensors 重叠、越界和不支持 dtype 的负向测试通过，真实加载器校验 170 个张量 hash。S1-04 已提供有界 `.part` Range 下载、loopback 断点/Range/hash 测试、代理重试、staging/原子提交、`installation.json`、嵌套 model id、lease 卸载和损坏/路径校验；隔离检查返回 4/4（[发布目录设计](model-packaging.md)）；不下载真实模型。 |
| 2 C# encoder | 真实 22 层 mmBERT FP32 scalar encoder：embedding、global/local attention、split-half RoPE、LayerNorm、gated GELU MLP、取消与形状检查；新增 `EncoderExecutionOptions`、`EncoderWorkspacePool` 与 `System.Numerics.Vector<float>` SIMD Linear/Norm/Add/GELU | tiny ModernBERT fixture 的逐 trace scalar/SIMD 最大绝对差 `0`，尾部向量 Linear、取消、deadline、并发租约、workspace 归还和 unload 后无活动租约均通过 [S2-03 记录](s2-03-resources.md)；真实包 CPU/GPU Trace 最大差异仍为 `1.7578125e-2`，性能矩阵尚未验收 |
| 3 决策闭环 | Choice/Score/Boolean、稳定 softmax、温度 profile、concentration/abstention、session/deadline/token budget、source-generated JSON；typed response 现在保留 raw logits，`PrimitiveAlignment` 对齐 logits/probabilities/legend/abstention；session 增加 micro-batch/workspace/resident memory 预算与 unload fail-closed | 4 个中英文固定输入 fixture 通过逐键数值/legend/abstention 对齐，22 项核心测试覆盖 typed output、S3 对齐契约、模型安装/lease 及资源边界；真实模型质量仍单独验收 |
| 4 多语言/校准 | 冻结 encoder 的可复现二分类 head trainer、温度拟合、NLL/Brier/ECE/accuracy/macro-F1/coverage/selective-risk 计算；新增许可明确原创中英×领域×primitive fixture、数据卡、manifest hash 与 12 个绑定 profile | `tools/Validate-S4Data.ps1` 通过 `24 records, 8 files, fixture_only`；S4-03 profile 全部 `pending_measurement`，门槛已冻结但未运行真实模型，不能视为多语言质量达标（[数据卡](data-card-s4-02.md)、[校准报告](calibration-report-s4-03.md)） |
| 5 CUDA/AOT | `Sezika.Kernels` C# → ILGPU 1.5.3 构建期导出 14 个 PTX、ABI manifest、源码/PTX SHA-256 与静态 launch descriptors；CUDA Driver `LibraryImport`/SafeHandle；完整 resident GPU encoder/head；CPU win-x64/linux-x64 Native AOT smoke | RTX 4070 Laptop GPU / driver 596.08 / CC 8.9 实测；真实 mmBERT CPU/CUDA head logits `[1.0985773,0.55617267]` vs `[1.0985773,0.5561736]`，最大绝对误差 `9.536743e-7`；非整齐 vector/GEMM、取消、module/buffer 回收通过；生成器已固定 UTF-8 无 BOM/Unix 换行并与提交 PTX/manifest 字节一致；性能矩阵、SIMD/量化与 GPU 多 RID 仍未验收 |
| 6 开源发布 | 核心类库已可构建开发 NuGet 包，包内带 README、Apache-2.0 LICENSE、NOTICE 与第三方声明 | `.artifacts/packages/Sezika.0.1.0-dev.nupkg` 已生成；正式发布版本、CLI、模型/数据卡、签名、真实模型跨平台发布与 NuGet 上传仍未验收 |

## 已执行命令

独立使用入口（S3-05）已完成：`DecisionModelRuntime` 拥有模型和 CPU session；CLI 通过文件/stdin 读取请求并返回结构化结果。最终 Release solution build 为 0 警告/0 错误，核心测试为 23/23（包含并发 unload 回归）。固定真实模型的英文文件请求和中文 stdin 请求均返回 Choice/Score/Boolean，分别使用 95/119 tokens；运行记录见 [独立 CLI smoke](standalone-cli-smoke.md)，原始结果及输入 hash 见 [JSON 证据](evidence/standalone-cli-2026-09-23.json)。本轮未执行 CUDA、AOT 或语言质量评测。

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

PowerShell 7.6.6、.NET SDK 10.0.401 下，`dotnet build Sezika.slnx --no-restore` 通过且为 0 警告/0 错误；核心测试项目 22 项通过，独立 `Sezika.Resources.Tests` 12 项通过并报告 scalar/SIMD trace `max_abs_error=0`；`tools/Validate-S4Data.ps1` 报告 `24 records, 8 files, fixture_only`。模型工具输出 `Verified 170 tensors, model and tokenizer SHA-256`；构建期工具输出 14 个带 ABI/hash 的 PTX，并在临时目录重生成后与提交目录逐文件 SHA-256 一致。真实模型 smoke 输出 CPU/CUDA logits、`real_typed_decisions passed`、`max_abs_error=9.536743E-07` 和 `cancel smoke passed`；CUDA smoke 覆盖非整齐 vector-add/GEMM 与决策头。另有 tiny model 的 CPU win-x64 与 Ubuntu WSL linux-x64 Native AOT smoke，及 CUDA/tiny win-x64 AOT smoke；这些发布物未加载 644 MB 真实权重，不能代替真实模型 AOT 推理证据。发布项目不引用 ILGPU 项目，ILGPU 只存在于构建工具。性能矩阵和量化收益仍属于 S5-04；S3-03 的 fixture 对齐数值报告见 [s3-03-primitive-alignment-report.md](s3-03-primitive-alignment-report.md)，真实模型参考实现和质量数字仍单独验收。
