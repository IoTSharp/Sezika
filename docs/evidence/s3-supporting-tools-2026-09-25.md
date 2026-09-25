# S3 输入合同迁移后的支持工具极小验证

2026-09-25 使用主线程统一构建出的 Release / `net10.0` 程序完成三次最小工具运行：Benchmarks 自检通过；DatasetTool 正确阻止未复核、未封存的五条原创示例准入；Coverage 对一条格式适配输入输出 61-token 长度及原始候选顺序。没有运行真实性能矩阵、PAWS/Nimble 题集、质量评分或模型推理，也没有再次构建、下载权重、训练、发布。

PowerShell **7.6.6**。执行器为 `C:\Program Files\dotnet\dotnet.exe`，被执行程序的 runtimeconfig 目标为 `Microsoft.NETCore.App 10.0.0` / `net10.0`，反射 JSON 序列化关闭；这里记录目标 runtimeconfig，不把它当成进程实际解析到的 runtime patch 版本。程序来自 `.artifacts/build/s3-contract-final/bin/<项目>/release/<项目>.dll`，构建结果另由主线程证据记录。

## 程序与输入身份

| 程序 | SHA-256 |
| --- | --- |
| Sezika.Benchmarks.dll | `4847193fd0be270e26714a3f6b4a23254bac439cec67d57e4bb62a221a47ca15` |
| Sezika.DatasetTool.dll | `e897fbae51db377f73c72440019811292beb112b30e34f825a10c55e30bd79df` |
| Sezika.Coverage.dll | `ad3c998687c2db06bb39783875cf46449ea1d58b9862c7fe4776f947ede034c1` |

Coverage 输入为本任务新建的 `synthetic-oracle-choice-en-short-format-adapter.jsonl`，**仅作合成格式适配器的工具自检输入**。其 `state` 和 `question` 来自已冻结独立 oracle 的 `choice-en-short`；没有添加质量标签、预测或新模型输出。JSONL 显式保存用途、原 case ID 和参考文件 hash，记录 ID 为 `synthetic-oracle-choice-en-short-format-adapter`。候选顺序保持原始 `z_delivered / a_transit / m_unknown`。

- 参考来源：[reference.cpu-fp32.v1.json](../../tests/fixtures/laya-oracle/reference.cpu-fp32.v1.json)，SHA-256 `bf0d537305149672f07d34dc6b3e2d3614aeb2c26aa6d11aa7902578100c53fa`。
- 合成格式输入：647 字节，SHA-256 `42c7220731734e1ed9f3ee8284678edf17f56b85a74d0646f378e398c336e031`。
- DatasetTool 示例 manifest：[example-manifest.json](../../datasets/s4-05/example-manifest.json)，SHA-256 `c6be8781448b62ad64d6eba82e64ea58beae7eb42affe6991913c3307ff1f949`。
- 示例 JSONL SHA-256：`e1717d4cd2acb50020b327f87f70a6eebe63e3fe9dac1436cb2cbd05b42e907b`。

Coverage 只检查模型 manifest 与 tokenizer hash，不读取模型权重。本次输入和输出均只有仓库原创 oracle 示例，不含外部题集或私人内容；报告中的完整 prompt diagnostics 仅用于该工具验证。

## 实际命令

工作目录为 `D:\source\Sezika`，输出目录为新建的 `.artifacts/laya-oracle-work/supporting-tools-01`。三次运行各执行一次，无失败重试。

```powershell
& tools/Invoke-BoundedProcess.ps1 -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @(
  'D:\source\Sezika\.artifacts\build\s3-contract-final\bin\Sezika.Benchmarks\release\Sezika.Benchmarks.dll',
  '--self-test'
) -TimeoutSeconds 60 -LogName 's3-benchmarks-self-test'

& tools/Invoke-BoundedProcess.ps1 -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @(
  'D:\source\Sezika\.artifacts\build\s3-contract-final\bin\Sezika.DatasetTool\release\Sezika.DatasetTool.dll',
  'audit-splits', 'D:\source\Sezika\datasets\s4-05\example-manifest.json',
  'D:\source\Sezika\.artifacts\laya-oracle-work\supporting-tools-01\s4-05-example-audit.json'
) -TimeoutSeconds 90 -LogName 's3-dataset-audit-example'

& tools/Invoke-BoundedProcess.ps1 -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @(
  'D:\source\Sezika\.artifacts\build\s3-contract-final\bin\Sezika.Coverage\release\Sezika.Coverage.dll',
  'D:\source\Sezika\.artifacts\models\laya-mmbert',
  'D:\source\Sezika\.artifacts\laya-oracle-work\supporting-tools-01\synthetic-oracle-choice-en-short-format-adapter.jsonl',
  'D:\source\Sezika\.artifacts\laya-oracle-work\supporting-tools-01\coverage-synthetic-adapter.json',
  '42c7220731734e1ed9f3ee8284678edf17f56b85a74d0646f378e398c336e031', '1', '60', 'strict'
) -TimeoutSeconds 90 -LogName 's3-coverage-synthetic-adapter'
```

DatasetTool 与 Coverage 内部期限都是 60 秒；Benchmarks `--self-test` 不进入模型加载和性能循环，使用外层 60 秒期限。输出路径已存在，复现需另选新的任务路径。

## 观察结果

| 工具 | 子进程 exit | runner 状态 / JSON 耗时 | 结果含义 |
| --- | --- | --- | --- |
| Benchmarks --self-test | 0 | Succeeded / 3.343 s | 最近秩分位数、单样本分位数、source-generated 请求、参数上限自检通过 |
| DatasetTool audit-splits | 3 | Failed / 3.375 s | 预期准入阻断，报告正常生成；runner 以任何非零退出码记 Failed |
| Coverage 合成格式输入 1 行 | 0 | Succeeded / 4.007 s | schema v2、哈希绑定、严格/兼容 eligibility 与长度/顺序字段可用 |

DatasetTool 报告为 `status=blocked`、`record_count=5`、`compared_pairs=10`、10 个问题，内部耗时 `0.1133331 s`；`admission_checks_passed=false`、`requires_human_acceptance=true`、`represents_model_quality=false`。问题如下：

- `fixture_only`：原创示例不能冒充真实准入数据集。
- `license_not_approved`、`use_not_allowed`：许可待审，指定用途尚未进入 allowlist。
- `human_review_required` × 5：五条记录均缺少完整人工复核证据。
- `sealed_test_exposed`：已查看的内容不能成为新封存测试。
- `test_not_sealed`：尚未具备责任人、时间及 hash 绑定的封存证据。

这次预期阻断证明该示例的失败路径会实际生效，不能推导真实数据集已经完成隔离、许可审核或封存。

Coverage 报告关键字段：

| 字段 | 实际值 |
| --- | --- |
| SchemaVersion / RenderingVersion | `2` / `sezika.prompt.laya-4066d5d5.v2` |
| LengthPolicy | `strict` |
| PrefixTokenBudget / TotalTokenBudget | `256` / `1024` |
| Processed / TokenEligible / StrictEligible / CompatibleEligible | `1 / 1 / 1 / 1` |
| RequiredTokens / RetainedTokens | `61 / 61` |
| CandidateLabels | `z_delivered`, `a_transit`, `m_unknown` |
| OriginalInstructionTokens / RetainedInstructionTokens | `12 / 12` |
| OriginalOptionTokens / RetainedOptionTokens | `[9, 8, 10] / [9, 8, 10]` |
| PrefixTokensIncludingSpecials | `45` |
| OriginalStateTokens / RetainedStateTokens / DroppedStateTokens | `15 / 15 / 0` |
| PrefixUnclipped / TotalFitsAfterPrefixClipping / WasTruncated | `true / true / false` |
| StrictFailureCode / CompatibleFailureCode / FailureCode | 均 `null` |

`Diagnostics.LengthPolicy=laya_compatible` 表示报告保存的是共享 builder 的兼容构造诊断；它不改变本次所选的 strict 统计策略。短输入两种策略均接受，未发生截断。这一行没有验证长输入上的 strict 拒绝/compat 截断分叉，该分叉应由 S3 主验收证据覆盖，不能将短 smoke 夸大为完整边界测试。

已另作只读报告断言，检查上述 61/61 长度、预算、候选顺序、两种 eligibility、无截断与失败码，以及 DatasetTool 的 5/10/10 阻断字段，全部通过。

## 进程、清理与本地报告

| 工具 | 启动 UTC | 父链 | 外层上限 |
| --- | --- | --- | --- |
| Benchmarks | 2026-09-25 14:34:51.3708937 | `53660 → 20852 → 44972` | 60 s |
| DatasetTool | 2026-09-25 14:34:51.3117902 | `53660 → 39444 → 69232` | 90 s |
| Coverage | 2026-09-25 14:34:51.4764147 | `53660 → 64636 → 61308` | 90 s |

三个 runner 的 `CleanupErrors` 均为空；root 已自然退出，无按进程名称清理。短进程可能在 CIM 快照前退出，前三个 identity 中 `CommandLine` 采样均为 null，Benchmarks/DatasetTool 的 `Processes` 为空；原始 `FilePath`、`Arguments`、开始时间与 `Process.Start caller` 父 PID 和 launcher 身份仍完整记录。这里不把缺失快照解释为父链实测完整；父链由启动调用者记录及 launcher CIM 身份共同给出。

日志前缀均位于 `.artifacts/processes`，保留 `.identity.json`、`.result.json`、stdout/stderr：

- `20260925-143450-344-s3-benchmarks-self-test`。
- `20260925-143450-390-s3-dataset-audit-example`。
- `20260925-143450-440-s3-coverage-synthetic-adapter`。

本地输出目录 `.artifacts/laya-oracle-work/supporting-tools-01` 中：

- `coverage-synthetic-adapter.json` SHA-256：`4f2d8f7abf04e2dba7d829a969373ba027bd7368715741f43c49d6a1d7151fa0`。
- `s4-05-example-audit.json` SHA-256：`c7ab45368e800da46c8dcc60cc886e30da0eff9b1998c0e68b43dc88052ce01b`。
- `synthetic-oracle-choice-en-short-format-adapter.jsonl` 保留实际 smoke 输入与来源绑定。

这些 `.artifacts` 文件是本地执行证据，本页不宣称它们已随仓库分发。以上三个工具 smoke 不构成性能、语言质量、独立模型数值对齐或 Native AOT 通过声明。
