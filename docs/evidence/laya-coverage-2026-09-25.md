# Laya 输入合同修复后的 token 覆盖率诊断

日期：2026-09-25。这是 S3-10 的 **token-only 长度与信息保留诊断**。运行最终 `Sezika.Coverage` 的 schema v2，读取既有固定 manifest、tokenizer 和题集；没有读取模型权重、执行 encoder/head、生成答案、计算质量分数、训练、下载或发布。`strict` 是本次选择的策略，同时逐题计算显式 `laya_compatible` 的可构造性。

公开交付的是从原始报告按字段白名单导出的**无题文派生摘要**，不是原始 capture。摘要保留逐题 ID、题型、token 数、截断计数/方向和失败码，不包含 PAWS/Nimble 的 state、说明、候选文案或标签。原始 JSON 保留在本机忽略目录 `.artifacts/laya-oracle-work/coverage-v2/`；派生摘要绑定其路径、字节数和 SHA-256。

## 结果与含义

| 固定题集 | 条数 | strict 可构造 | compatible 可构造 | 裁剪前总 token 范围 | compatible 保留总 token 范围 | state 被截断条数 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| PAWS | 250 | 250 / 250（100%） | 250 / 250（100%） | 76–152 | 76–152 | 0 |
| Nimble | 324 | 306 / 324（94.44%） | 324 / 324（100%） | 242–730 | 242–730 | 0 |

全部 574 条都量得原始长度，`Unmeasured=0`。独立 256-token prefix 内容预算与 1024-token 总序列预算按固定 Laya 规则应用；prefix 的 BOS/EOS 开销独立计入序列。PAWS 的说明、候选、state 与最终序列均未裁剪，16,233 个 state token 全部保留。

Nimble 的 18 条 strict 拒绝均为 `decision_token_budget_exceeded`，全部发生于前缀：12 条裁剪说明，合计丢弃 620 个说明 token；6 条裁剪候选，合计丢弃 230 个候选 token。没有 state 或最终序列裁剪，90,179 个 state token 全部保留。完整长度的最小值/最大值恰好相同，不表示每条兼容序列都没有裁剪，逐题损失见派生摘要。

| Nimble 题型 | 总条数 | strict 可构造 | compatible 可构造 |
| --- | ---: | ---: | ---: |
| Boolean | 114 | 114 | 114 |
| Choice | 146 | 130 | 146 |
| Score | 64 | 62 | 64 |

`TokenEligible` 只表示请求结构与输入序列可构造，既不等于模型回答成功，也不等于预测正确。兼容模式中保留的 state token 数只衡量输入保留情况，不证明裁剪前缀对任务质量没有影响。此次没有通过切换默认策略来扩大 strict 覆盖率，默认策略仍为 strict。

派生结果：[PAWS 250](laya-coverage-2026-09-25/paws-250.derived-summary.json)、[Nimble 324](laya-coverage-2026-09-25/nimble-324.derived-summary.json)。两份先行极小输入也保留派生证据：[PAWS 1 条](laya-coverage-2026-09-25/paws-smoke.derived-summary.json)、[Nimble 1 条](laya-coverage-2026-09-25/nimble-smoke.derived-summary.json)，分别为 115 和 450 token，均无截断。

## 身份、来源与版本

模型身份为 `convaiinnovations/laya-multilingual@052592a15d198d9ad47da779604259b10b47b7aa`；渲染版本为 `sezika.prompt.laya-4066d5d5.v2`，参考 Laya 源码提交 `4066d5d5fbf08b66c6757ddeedbd797bd7655bc0`。工具核对 manifest 声明的固定模型身份/权重哈希，并实际计算 tokenizer 与题集 SHA-256；它没有读取权重文件来重新计算权重哈希。

| 输入/工具 | SHA-256 |
| --- | --- |
| PAWS `.artifacts/datasets/paws-public-benchmark/eval.jsonl` | `c295258fdd73452f196b2673bdbee58cf01fa3f24400c2f1c0f26fd1126bc6f3` |
| Nimble `.artifacts/datasets/nimble-2026-09-24/eval.jsonl` | `8e9e48b8de5206593912ae01ddc95bd77e40ad2ecf4c9292c1711290eca0d896` |
| tokenizer | `609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f` |
| `model.json` | `cf870335dee647a053a5416918d3cd79e61cca6416122cb8353014fc3ea119f6` |
| 本轮 `Sezika.Coverage.dll` | `ad3c998687c2db06bb39783875cf46449ea1d58b9862c7fe4776f947ede034c1` |
| 同目录 `Sezika.dll` | `4fa30c510dfb8f3512cc4bb8cb5a56ca442b2438a3e1194109d54a6deeacddd0` |
| `tools/Sezika.Coverage/Program.cs` | `1952f53e876a475be4b2b25e8350264a2254653ae7d48e5d0b31896e756374d2` |
| 有界进程 runner | `d2f2791c82b1378fb09cd331034390b7aa79b4b1bcb816ea006266cc4ca81019` |

PAWS/Nimble 题集来源、固定 ID 和既有审计用途沿用 [2026-09-25 质量报告的来源记录](quality-2026-09-25.md)。这些题已经查看过，只用于固定审计，不作为未见测试或本轮训练数据。没有将第三方题文重新打包到公开证据中。

执行环境为 Windows、PowerShell 7.6.6。调用的 `C:/Program Files/dotnet/dotnet.exe` 文件 ProductVersion 为 `10.0.11 @Commit: e2f47b0110ed922f21a1522da67279133ce28f32`；该值是 host 文件版本，不冒充本工具自行记录的 managed runtime 版本。最终程序集路径为 `.artifacts/build/s3-contract-final/bin/Sezika.Coverage/release/Sezika.Coverage.dll`，目标 `net10.0`；此子任务复用既有构建，没有额外 build。

## 执行命令与有界进程证据

固定执行 **4 次**：PAWS 1 条、Nimble 1 条，通过 schema/count/budget 检查后，再执行 PAWS 250 条和 Nimble 324 条。每次内部总期限 120 秒，外部 runner 180 秒；每个输入构造器另有 10 秒期限与 32,768-token 单段诊断保护上界。没有失败、超时或重试。工具每 10 条输出进度；循环条数为显式的 1、250 或 324。

每次调用均使用以下 runner，`ArgumentList` 的完整实值保存在对应 identity/result JSON 中：

```powershell
& 'D:/source/Sezika/tools/Invoke-BoundedProcess.ps1' `
  -FilePath 'C:/Program Files/dotnet/dotnet.exe' `
  -ArgumentList @(
    'D:/source/Sezika/.artifacts/build/s3-contract-final/bin/Sezika.Coverage/release/Sezika.Coverage.dll',
    'D:/source/Sezika/.artifacts/models/laya-mmbert',
    '<下表 dataset 完整路径>', '<该次首次输出 JSON 完整路径>',
    '<对应冻结 dataset SHA-256>', '<记录数>', '120', 'strict'
  ) `
  -TimeoutSeconds 180 `
  -LogName '<该次唯一日志名>' `
  -LogDirectory 'D:/source/Sezika/docs/evidence/laya-coverage-2026-09-25/processes'
```

| 运行 | dataset 路径（相对仓库） | records | 首次输出文件（位于 `docs/evidence/laya-coverage-2026-09-25/`） |
| --- | --- | ---: | --- |
| PAWS smoke | `.artifacts/datasets/paws-public-benchmark/eval.jsonl` | 1 | `paws-smoke.v2.json` |
| Nimble smoke | `.artifacts/datasets/nimble-2026-09-24/eval.jsonl` | 1 | `nimble-smoke.v2.json` |
| PAWS full | `.artifacts/datasets/paws-public-benchmark/eval.jsonl` | 250 | `paws-250.v2.json` |
| Nimble full | `.artifacts/datasets/nimble-2026-09-24/eval.jsonl` | 324 | `nimble-324.v2.json` |

上述原始输出在运行完成后核实绝对路径、文件名和本任务归属，逐文件移动至 `D:/source/Sezika/.artifacts/laya-oracle-work/coverage-v2/`，移动前后 SHA-256 相同，没有覆盖、删除或改写原始字节。进程记录继续保留实际执行时的首次输出路径，因此其路径与报告当前保留位置不同。

| 运行 | 开始时间 UTC | dotnet PID → 父 pwsh PID → 其父 PID | 记录到的子进程 | runner 墙钟秒 | 结果 |
| --- | --- | --- | --- | ---: | --- |
| PAWS smoke | 2026-09-25 14:30:44.3347921 | 65548 → 79308 → 53660 | 无 | 3.352 | Succeeded / 0 |
| Nimble smoke | 2026-09-25 14:32:05.3323329 | 49892 → 65944 → 53660 | 无 | 2.666 | Succeeded / 0 |
| PAWS full | 2026-09-25 14:38:54.0733205 | 69180 → 31060 → 53660 | conhost 52516，父 69180 | 1.866 | Succeeded / 0 |
| Nimble full | 2026-09-25 14:39:10.6338916 | 33868 → 79704 → 53660 | conhost 55444，父 33868 | 3.018 | Succeeded / 0 |

四次 `CleanupErrors=[]`，记录到的根进程及子进程都正常退出，没有按名称终止进程。runner 保存 PID、启动时间、命令行和父链，退出/超时检查最多 361 次、每次等待 500 ms，并有总墙钟上限；清理最多 6 轮/8 秒，只处理身份仍匹配的任务进程。进程快照可能漏掉两次快照之间创建又退出的极短子进程，限制保留在每份 result 内。这些墙钟时间含 runner/进程启动/回收和 tokenizer 初始化，**不是模型或受控性能 benchmark**。

结果日志：[PAWS smoke](laya-coverage-2026-09-25/processes/20260925-143043-780-laya-coverage-paws-smoke-v2.result.json)、[Nimble smoke](laya-coverage-2026-09-25/processes/20260925-143204-836-laya-coverage-nimble-smoke-v2.result.json)、[PAWS full](laya-coverage-2026-09-25/processes/20260925-143853-663-laya-coverage-paws-250-v2.result.json)、[Nimble full](laya-coverage-2026-09-25/processes/20260925-143910-072-laya-coverage-nimble-324-v2.result.json)。同目录保存对应 `.identity.json`、`.stdout.log`、`.stderr.log`；日志不包含题文。

## 原始报告与派生摘要的哈希

下列原始文件都位于本机忽略目录 `D:/source/Sezika/.artifacts/laya-oracle-work/coverage-v2/`：

| 原始报告 | 字节数 | SHA-256 |
| --- | ---: | --- |
| `paws-smoke.v2.json` | 3323 | `df476f5a8aa9567b25802ca6035dac9af7925b9c3f61a5dbf9e8ff95445da9f8` |
| `nimble-smoke.v2.json` | 6214 | `51d56a6631d906d208a73b09e5b53d48d1ffb2939161bf339673b185dfca4030` |
| `paws-250.v2.json` | 556049 | `a8e719075f06b740078c6197453481ad66b6fbe2e286bd728479ad17f10db794` |
| `nimble-324.v2.json` | 1641611 | `eb3d519954ddea3eb56ee349ebef30fba00a3b141c151ff4fb7da5054c230516` |

PAWS 250 派生摘要 SHA-256 为 `ee185e6bc0dffa7afd9946c69337c8939ca53193989d46b27a36aed6c77782b7`；Nimble 324 派生摘要为 `f5aedc5cd552dd216b3ced9b637f19cdc2c2098b546f0ad68c01717c660bde61`。派生脚本保留在本机 `.artifacts/laya-oracle-work/coverage-v2/derive-summary.ps1`，SHA-256 为 `d300db2f092050e0f7ef92550eda28518d4a5f0eab17de87dc89b941d59576d3`；固定处理 4 份报告，每份最多 324 条、每条最多 64 个候选计数，总期限 15 秒，不执行推理。

## 与旧统计的关系

旧报告的 PAWS 249/250、Nimble 8/324 是旧 prompt/长度路径的已执行推理覆盖率；本次的 250/250、306/324 或兼容 324/324 是新渲染版本的 token 可构造率，统计对象不同，不能直接写成新的真实回答数或正确率。旧长度 PAWS 68–260、Nimble 237–718 也属于旧渲染，不可直接套到修复后序列。

当前诊断说明：修复输入渲染和分离前缀/总预算后，Nimble 的这 18 条剩余严格拒绝需要显式前缀裁剪，均不是 state 超过 1024 的问题。是否采用兼容裁剪、是否改善原题集预测，以及其语言/题型收益，仍需按路线图独立运行完整推理与质量评估；本报告没有替代这些门槛。
