# S3-06 所选四条真实逐层诊断证据

2026-09-26 先对冻结参考中的 `choice-en-short` 运行 scalar、SIMD、CUDA 三后端，实际序列 61 token；随后按预先固定顺序追加中文 Boolean 384-token、英文 Score 960-token、中文 Choice 960-token 三条。四条共 12 次后端输出比较均通过既有独立 oracle 容差，每条的 SIMD/CUDA 各完成 27 个 checkpoint 与本次 scalar 的全张量比较，没有缺失项。这些是冻结 46 条参考中的 4 条所选诊断，未覆盖完整语言、题型与长度组合；真实近并列仍为 0，S3-06 全门槛未完成。

## 固定身份与范围

[真实报告](s3-diagnostics-2026-09-26/short-smoke.json) SHA-256：`4b5b434fb4cd7816a2afecd23cc71a7196810f81f9f524e69dc96a1247bdaf78`。报告保留模型、权重、tokenizer、独立参考和数值合同身份，以及实际执行的程序集 SHA-256。

- 模型：`convaiinnovations/laya-multilingual@052592a15d198d9ad47da779604259b10b47b7aa`。
- 权重 SHA-256：`9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204`。
- tokenizer SHA-256：`609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f`。
- 参考 SHA-256：`bf0d537305149672f07d34dc6b3e2d3614aeb2c26aa6d11aa7902578100c53fa`。
- 合同 SHA-256：`771ca781c52f1d6f9cb22859bed007ef483a54a2543c644de33f88d6e0687eee`。
- 实际工具 DLL SHA-256：`fb50e1ed352e62c658e78294503853a616e2be1d26e57a9ea1fc55beb5f4dc14`。

输入采用显式 `laya_compatible`、256-token 前缀内容预算与 1024-token 完整序列预算。生产构造器及实际 backend 调用的 tokens、markers、候选标签与顺序均与原冻结参考核对。这是普通 .NET 10.0.11 / win-x64 执行，不计作 Native AOT。

## 输出与内部层级结果

| 后端 | 独立参考输出判定 | 最大 logits 绝对误差 | 最大概率绝对误差 | 内部逐层比较 |
| --- | --- | ---: | ---: | --- |
| Scalar | 通过 | `3.0517578125e-5` | `9.00044261342714e-10` | 采集 27 个完整参考 checkpoint |
| SIMD | 通过 | `3.0517578125e-5` | `9.000484801902076e-10` | 27/27 与 scalar 比较，0 缺失 |
| CUDA | 通过 | `5.340576171875e-5` | `9.000227230160363e-10` | 27/27 与 scalar 比较，0 缺失 |

marker 输出沿用冻结公式与阈值，没有放宽容差。参考 top-two logit margin 为 `21.52091121673584`，高于近并列阈值 0.001，因此 `near_tie_cases=0`、`near_tie_acceptance=not_measured`；`selected_diagnostics_passed=true`、`full_s306_gate_passed=false`。

27 个 checkpoint 包含 embedding norm、22 个 encoder layer hidden、encoder final、两个 head layer hidden 和 scorer logits。前 26 个各比较 `61 × 768 = 46848` 个元素，scorer 比较 61 个元素。当前 case 保留 scalar 快照 `4,872,436` 字节；该值不代表进程峰值或模型内存。

SIMD 内部层级最大绝对差异为 `0.0205078125`，位于 `layer/12/hidden`，该张量 RMS 差异 `0.0001200315394921463`。CUDA 最大绝对差异为 `0.1043853759765625`，位于 `layer/19/hidden`，该张量 RMS 差异 `0.0005489921476973924`。逐层差异公开保留，不能套用 marker-logit 容差作通过判断。这里只完成 C# 后端内部定位，没有独立上游层张量及预先冻结的逐层阈值；输出判定通过也不能抹去这些中间张量差异。

## 执行与交付记录

PowerShell 7.6.6；普通 .NET runtime 10.0.11。初次 smoke 的 13 个原始结果和过程文件逐字节归档，追加的中长诊断另有 5 个文件，合计 18 个；SHA-256 与字节数见[索引](s3-diagnostics-2026-09-26/execution-index.json)，进程证据位于 [processes](s3-diagnostics-2026-09-26/processes/)。日志中的绝对路径保留原执行值；`.gitattributes` 对证据目录禁用换行转换。

| 操作 | PID | runner 上限 | 结果 | 原始记录前缀 |
| --- | ---: | ---: | --- | --- |
| 隔离 Release 构建 | 62076 | 180 秒 | exit 0，0 warnings / 0 errors，9.006 秒 | `20260925-163812-108-s3-diagnostics-final-build` |
| 诊断 self-test | 45288 | 30 秒 | exit 0，19 项通过，2.417 秒 | `20260925-163821-175-s3-diagnostics-final-self-test` |
| 单条真实三后端逐层 smoke | 67784 | 210 秒 | exit 0，25.444 秒；工具内部期限 180 秒 | `20260925-164033-569-s3-diagnostics-real-short-smoke` |

三次均记录命令、PID、启动时间、父链与结果，没有清理错误。self-test 使用明确的合成数值检查比较逻辑，不计作真实近并列输入。模型运行耗时含加载、验证、全量 trace 读回及记录，不作为后端性能比较。

真实运行命令：

```powershell
& tools/Invoke-BoundedProcess.ps1 -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @(
  '.artifacts/build/s3-diagnostics-20260926/bin/Sezika.NumericParity/release/Sezika.NumericParity.dll',
  '--trace-oracle', '.artifacts/models/laya-mmbert',
  'tests/fixtures/laya-oracle/reference.cpu-fp32.v1.json',
  'tests/fixtures/laya-oracle/contract.v1.json',
  '.artifacts/s3-diagnostics-20260926/short-smoke.json',
  'choice-en-short', 'scalar,simd,cuda', '180',
  'bf0d537305149672f07d34dc6b3e2d3614aeb2c26aa6d11aa7902578100c53fa',
  '771ca781c52f1d6f9cb22859bed007ef483a54a2543c644de33f88d6e0687eee'
) -TimeoutSeconds 210 -LogName 's3-diagnostics-real-short-smoke'
```

## 追加中长序列、多题型与中文诊断

追加运行在启动前固定 `boolean-zh-medium,score-en-long,choice-zh-long`，每条顺序执行 scalar、SIMD、CUDA；没有运行后筛选、重试或放宽 deadline/容差。[中长诊断原始报告](s3-diagnostics-2026-09-26/medium-long.json) SHA-256 为 `46f108edebe0af679c21ee2b03126e45c8c44bb6a7a9712062152edc6fd7f966`，135,960 字节。模型、参考、合同和实际工具 DLL 身份与上方 smoke 相同。

| case | 题型 / 语言 | 实际 token | marker / 候选 | 参考 top-two margin | 本次保留 scalar 快照字节 |
| --- | --- | ---: | ---: | ---: | ---: |
| `boolean-zh-medium` | Boolean / 中文 | 384 | 2 / 2 | `2.361504316329956` | 30,672,384 |
| `score-en-long` | Score / 英文 | 960 | 3 / 3 | `0.8321785926818848` | 76,680,960 |
| `choice-zh-long` | Choice / 中文 | 960 | 3 / 3 | `0.8327955007553101` | 76,680,960 |

| case | 后端 | 独立参考输出判定 | 最大 logits 绝对误差 | 最大概率绝对误差 |
| --- | --- | --- | ---: | ---: |
| `boolean-zh-medium` | Scalar | 通过 | `4.410743713378906e-6` | `4.3797159818281806e-7` |
| `boolean-zh-medium` | SIMD | 通过 | `3.4570693969726562e-6` | `3.253428652455481e-7` |
| `boolean-zh-medium` | CUDA | 通过 | `5.841255187988281e-6` | `3.5013700550035054e-7` |
| `score-en-long` | Scalar | 通过 | `1.1444091796875e-5` | `1.6324255610600247e-6` |
| `score-en-long` | SIMD | 通过 | `9.775161743164062e-6` | `1.6162763267768554e-6` |
| `score-en-long` | CUDA | 通过 | `1.9311904907226562e-5` | `1.1909389272535265e-6` |
| `choice-zh-long` | Scalar | 通过 | `3.8387253880500793e-5` | `8.816924735144394e-6` |
| `choice-zh-long` | SIMD | 通过 | `1.583993434906006e-5` | `3.527875091058341e-6` |
| `choice-zh-long` | CUDA | 通过 | `8.749961853027344e-5` | `9.690154308739096e-6` |

三条的 scalar 均采集完整基线；SIMD/CUDA 各有 27/27 checkpoint、0 缺失，共 162 次全张量比较。Boolean 的 26 个 hidden 张量各含 `384 × 768 = 294912` 个元素，scorer 含 384 个元素；两个长输入的 hidden 张量各含 `960 × 768 = 737280` 个元素，scorer 各含 960 个元素。归档时再次核对全部 162 项的完整形状、最差坐标边界、同名 scalar 基线哈希和快照字节数，均一致。快照字节数只描述逐条保留的基线，不代表进程峰值或总模型内存。

内部逐层差异中，各 case/backend 最大绝对差异及所在张量如下；这张表不作层级门槛通过判断。

| case | 后端 | 最大内部绝对差异 | 所在 checkpoint | 该张量 RMS 差异 |
| --- | --- | ---: | --- | ---: |
| `boolean-zh-medium` | SIMD | `0.0352783203125` | `layer/12/hidden` | `0.000108663038582132` |
| `boolean-zh-medium` | CUDA | `0.084228515625` | `layer/12/hidden` | `0.00016353398211100474` |
| `score-en-long` | SIMD | `0.03564453125` | `layer/12/hidden` | `0.0000728776176442142` |
| `score-en-long` | CUDA | `0.05889892578125` | `layer/12/hidden` | `0.00012439390120813428` |
| `choice-zh-long` | SIMD | `0.10791015625` | `layer/17/hidden` | `0.00014156648813230086` |
| `choice-zh-long` | CUDA | `0.156524658203125` | `layer/17/hidden` | `0.0002135840361318407` |

三条 margin 均大于冻结近并列阈值 0.001，报告明确保留 `near_tie_cases=0`、`near_tie_acceptance=not_measured`、`selected_diagnostics_passed=true`、`full_s306_gate_passed=false`。输出合同通过不能代替尚缺的独立上游层张量、冻结逐层阈值和真实近并列证据。

本次普通 .NET 运行于 `2026-09-25T17:37:25.2411955Z` 启动，runner PID 记录中的目标进程为 78820，父进程 62328；其已记录子进程为 conhost 56660。runner 上限 1750 秒、工具总期限 1700 秒，单请求仍受原有 300 秒上限约束。结果为 exit 0 / `Succeeded`，原始 result 记录耗时 `423.381` 秒、`CleanupErrors=[]`；任务进程自然退出，归档前对两个精确 PID 的再次查询均无存活进程。过程前缀为 `20260925-173724-784-s3-diagnostics-real-medium-long`，identity、result、stdout、stderr 均逐字节归档。本轮与另一个 CUDA 质量验证存在运行重叠，耗时也包含模型加载、检查和 trace 读回，因此不作 benchmark。

```powershell
& tools/Invoke-BoundedProcess.ps1 -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @(
  '.artifacts/build/s3-diagnostics-20260926/bin/Sezika.NumericParity/release/Sezika.NumericParity.dll',
  '--trace-oracle', '.artifacts/models/laya-mmbert',
  'tests/fixtures/laya-oracle/reference.cpu-fp32.v1.json',
  'tests/fixtures/laya-oracle/contract.v1.json',
  '.artifacts/s3-diagnostics-20260926/medium-long.json',
  'boolean-zh-medium,score-en-long,choice-zh-long', 'scalar,simd,cuda', '1700',
  'bf0d537305149672f07d34dc6b3e2d3614aeb2c26aa6d11aa7902578100c53fa',
  '771ca781c52f1d6f9cb22859bed007ef483a54a2543c644de33f88d6e0687eee'
) -TimeoutSeconds 1750 -LogName 's3-diagnostics-real-medium-long'
```

剩余 S3-06 缺口包括独立上游逐层张量、预先冻结的层级阈值、有独立参考的真实近并列样本，以及完整长度、题型和语言覆盖。质量重测、训练、校准、性能和两 RID Native AOT 各自验收，本页不扩展结论。
