# 固定 Laya 输入修复与 C# 数值对照

本次使用固定 Laya 0.3.20、原有多语言权重以及测量前冻结的 `contract.v1.json`，完成独立参考捕获、输入合同修复和 C# 真实推理对照。SIMD 与 CUDA 各捕获完整 46 条：38 条 answered、8 条 validation failed。其中 38 条数值回答与上游的 token、marker、标签、logits、概率及预测通过合同，4 条非法输入的失败阶段/类型一致，另有 4 条明确的候选数量合同差异。**整套上游合同不标记通过；明确选择的 42/46 子集通过。**

参考是独立执行固定上游 Python/PyTorch 得到的 [46 条真实 capture](../../tests/fixtures/laya-oracle/reference.cpu-fp32.v1.json)，不是从 C# 输出倒推的期待值。准备及执行详情见 [oracle 证据](laya-oracle-2026-09-25.md)。C# `OracleCapture` 的参考 DTO 不包含参考 tokens、markers、logits 或 prediction 字段；它只取展开输入及身份信息，独立调用生产 `ModernBertDecisionEngine` 和 scalar/SIMD/CUDA 后端。`RecordingPipeline` 验证真正交给后端的序列与共享构造器相同。

## 固定身份与容差

- Laya commit：`4066d5d5fbf08b66c6757ddeedbd797bd7655bc0`。
- 模型：`convaiinnovations/laya-multilingual@052592a15d198d9ad47da779604259b10b47b7aa`。
- 权重 SHA-256：`9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204`。
- tokenizer SHA-256：`609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f`。
- cases SHA-256：`d0fb41807322f27e60fe5eabd2a146d4dc8ce31b326eb4f292bcc20ab85b4a7a`。
- contract SHA-256：`771ca781c52f1d6f9cb22859bed007ef483a54a2543c644de33f88d6e0687eee`。
- reference SHA-256：`bf0d537305149672f07d34dc6b3e2d3614aeb2c26aa6d11aa7902578100c53fa`。

数值判定为 `abs(actual-reference) <= absolute + relative*abs(reference)`。logits 为 `0.0005 + 0.0001*abs(reference)`，概率为 `0.0001 + 0.0001*abs(reference)`，Score 为 `0.001 + 0.0001*abs(reference)`；预测必须一致。没有变更合同或放宽门槛。近并列定义仍为 top-two logit margin 不超过 0.001，本批实际近并列记录为 0，不能宣称验收了近并列输入。

## 实测结果

| 后端 | 完整捕获 | 支持范围比较 | 最大 logits 绝对误差 | 最大概率绝对误差 | 总长 |
| --- | --- | --- | ---: | ---: | --- |
| Scalar FP32 | 明确选择的核心 18/18 | 18 answered 对照通过 | 0.000038387253880500793 | 0.000008816924735144394 | 最高 960 token |
| SIMD FP32 | 46/46 | 38 answered + 4 failed 对照通过 | 0.000030517578125 | 0.00000352787509105834 | 最高 1024 token |
| CUDA FP32 | 46/46 | 38 answered + 4 failed 对照通过 | 0.0000874996185302734 | 0.0000137659511832422 | 最高 1024 token |

18 条核心矩阵覆盖 Choice/Score/Boolean × 中文/英文 × 短/384-token 中输入/960-token 长输入。其余支持范围覆盖候选顺序、Unicode 与对象、结构化说明、左右截断、mask 文本、默认 Boolean、255/256/257、1023/1024/1025 原始长度，以及候选和说明前缀裁剪。**所有真实推理对照使用显式 `laya_compatible`**；strict 的拒绝语义通过单独的真实 tokenizer 和注入 pipeline 测试验收。

[独立 token 覆盖率重测](laya-coverage-2026-09-25.md)不执行 encoder/head：PAWS strict/compatible 均为 250/250；Nimble strict 为 306/324，compatible 为 324/324。Nimble 的 18 条严格拒绝均来自说明或候选前缀裁剪，state 没有丢弃。这是输入可构造性，不能计入本表的真实数值对照或用作答案正确率。

完整比較报告均 exit 1、16 个差异，全部来自以下四个 case；每条分别记录 status、token IDs、marker positions、candidate labels 的差异，没有其它数值或预测差异：

| case ID | Laya | Sezika 默认生产合同 |
| --- | --- | --- |
| `choice-one-candidate` | answered | Choice 至少 2 个候选，拒绝 |
| `choice-33-candidates` | answered | Choice 默认最多 32 个候选，拒绝 |
| `score-one-level` | answered | Score 至少 2 个等级，拒绝 |
| `score-eleven-levels` | answered | Score 默认最多 10 个等级，拒绝 |

显式子集比较只排除上述四项，使用原始完整参考和捕获文件，不删除或重写其中的行。报告保留原文件哈希、46 行身份、所选/排除 ID、`input_coverage=42/46`、`full_manifest_passed=false`。4 条原始非法输入为 `validate/invalid_question`，不带虚构的 logits 或预测。

原始交付物逐字节复制并由仓库 `.gitattributes` 保持原字节：

- [Scalar 核心 capture](laya-parity-2026-09-25/scalar-capture.json)、[18 条比较报告](laya-parity-2026-09-25/scalar-core-comparison.json)与[独立 scalar 执行证据](laya-scalar-parity-2026-09-25.md)；只覆盖核心18条，不外推其余28条边界或拒绝语义。
- [SIMD capture](laya-parity-2026-09-25/simd-capture.json)，SHA-256 `310907c99693372f6848cd1bba477b3b065c6624376e686b5410f7a2c3901dad`。
- [SIMD 完整差异](laya-parity-2026-09-25/simd-full-comparison.json)与[支持范围通过报告](laya-parity-2026-09-25/simd-supported-comparison.json)。
- [CUDA capture](laya-parity-2026-09-25/cuda-capture.json)，SHA-256 `a5b6fd6fc753c0c0b43aaa7847aede0bf16a52a2d1e983ab899289212be63951`。
- [CUDA 完整差异](laya-parity-2026-09-25/cuda-full-comparison.json)与[支持范围通过报告](laya-parity-2026-09-25/cuda-supported-comparison.json)。
- [比较器负向回归](oracle-comparator-2026-09-25.md)独立证明 token 变更与不合法选择会被拒绝；reference 自比较不能当作 C# 推理证据。

## 构建与运行记录

Windows PowerShell 7.6.6，.NET SDK 10.0.400，运行时 .NET 10.0.11，Windows 10.0.26200 / X64。每次通过 `tools/Invoke-BoundedProcess.ps1`，保存 PID、启动时间、完整参数、父链、stdout/stderr、超时和任务子树清理。表中耗时取 `.result.json` 的 `ElapsedSeconds`，不是稍后打印的墙钟数值。原始进程记录已逐字节归档至 [processes](laya-parity-2026-09-25/processes/)，[索引](laya-parity-2026-09-25/execution-index.json)保存退出码、耗时与记录哈希；其中的原工作目录与日志绝对路径仍保留测量时原值。

| 操作 | PID | 上限 | 结果 | runner 证据前缀（位于 `.artifacts/processes/`） |
| --- | ---: | ---: | --- | --- |
| SIMD 1 条 smoke | 34896 | 1800 秒 | exit 0；6.780 秒 | `20260925-141101-509-s3-csharp-simd-smoke` |
| SIMD 完整 46 条 | 25916 | 1800 秒 | exit 0；741.475 秒 | `20260925-141641-039-s3-csharp-simd-full` |
| CUDA 1 条 smoke | 71448 | 1800 秒 | exit 0 | `20260925-142140-355-s3-csharp-cuda-smoke` |
| CUDA 完整 46 条 | 22404 | 1800 秒 | exit 0；33.217 秒 | `20260925-142225-058-s3-csharp-cuda-full` |
| SIMD 完整/支持范围比较 | 20324 / 38468 | 各 60 秒 | exit 1 / 0，预期合同差异 | `20260925-143026-796-s3-compare-simd-full` / `20260925-143044-182-s3-compare-simd-supported` |
| CUDA 完整/支持范围比较 | 22276 / 40692 | 各 60 秒 | exit 1 / 0，预期合同差异 | `20260925-142324-032-s3-compare-cuda-full` / `20260925-142346-249-s3-compare-cuda-supported` |
| 最终 solution 构建 | 7804 | 300 秒 | 0 warning / 0 error | `20260925-142400-538-s3-solution-final-build` |
| 最终 prompt/runtime 检查 | 71236 | 180 秒 | 137 项通过 | `20260925-142525-803-s3-final-prompt-tests` |
| 资源回归 | 78452 | 180 秒 | 35 项通过 | `20260925-142611-766-s3-resource-tests` |
| CUDA 诊断回归 | 69028 | 90 秒 | 43 项通过 | `20260925-142611-864-s3-cuda-diagnostics-tests` |

这些运行没有超时或清理错误。短命比较进程可能在 CIM 查询前已退出；后续 runner 因此额外保存 `Process.Start` 调用方的 PID、启动身份、父 PID 和原始 argv，观测不到的子进程 CIM 字段仍留空，不补造系统观测值。完整 capture 中保存所执行的 `Sezika.OracleCapture.dll`、`Sezika.dll`、`Sezika.Cuda.dll` 哈希。最终整合构建使用独立输出目录，避免覆盖正在运行的数值捕获程序集；之后增加的示例引擎 Boolean 默认值和预编码非法标签/字符预算防护不改变本批合法输入序列，最终 137 项检查再次逐项验证了该序列。普通 .NET 构建不构成 Native AOT 证据。

完整捕获的实际命令形状（`<backend>` 为 `simd` 或 `cuda`，output 为对应新目录）：

```powershell
& tools/Invoke-BoundedProcess.ps1 -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @(
  'tools/Sezika.OracleCapture/bin/Release/net10.0/Sezika.OracleCapture.dll',
  '--reference','tests/fixtures/laya-oracle/reference.cpu-fp32.v1.json',
  '--model','.artifacts/models/laya-mmbert',
  '--cases','tests/fixtures/laya-oracle/cases.v1.json',
  '--contract','tests/fixtures/laya-oracle/contract.v1.json',
  '--output','<new-directory>','--backend','<backend>',
  '--length-policy','laya_compatible','--max-cases','46','--timeout-seconds','1800'
) -TimeoutSeconds 1800 -LogName '<unique-label>'
```

完整比较使用 `OracleCompare reference actual contract cases new-report 30`；支持范围比较额外传入完整 42 个 ID 的 CSV，精确 argv 见 runner JSON 与报告 `requested_case_ids`。最终构建为 `dotnet build Sezika.slnx -c Release --artifacts-path .artifacts/build/s3-contract-final --nologo -v minimal -p:UseSharedCompilation=false -nodeReuse:false -m:2`；测试运行该隔离目录下的 DLL，核心测试使用 `--prompt-contract`，没有调用默认套件中的示例训练器。

本次 CPU/CUDA 任务部分并行，耗时包含加载、验证、记录和资源竞争，只证明在预算内完成，不能据此声称后端速度比。修正后的 PAWS/Nimble 质量、校准、近并列候选、两 RID Native AOT、真实长输入性能与多题矩阵仍分别验收。没有训练、模型权重下载或发布；旧质量和性能记录保持其原输入版本的范围。

2026-09-26 后续证据：[修正路径两 RID Native AOT 长输入回归](input-aot-2026-09-26.md)已完成，SIMD/CUDA支持范围和scalar明确长输入分别通过，两RID输入检查各138项通过。本文以上内容继续保留2026-09-25的JIT捕获范围，AOT结论以新增报告为准。
