# 修正输入路径后的固定题集质量与有限独立对照

2026-09-26 使用原固定多语言权重、tokenizer 和题集，完成 C# CUDA 的 Nimble 324 条严格/兼容模式及 PAWS 250 条严格模式真实评测。Nimble 严格模式实际回答 306 条、正确 133 条，已答准确率 43.46%，另 18 条在输入预算检查时拒绝；兼容模式回答 324 条、正确 137 条（42.28%）。PAWS 回答 250 条、正确 170 条（68.00%）。另对预先选择的 Nimble 前 45 条和 PAWS 前 1 条取得独立固定 Laya 参考，与 C# 的输入、数值及预测通过冻结合同。**独立参考只覆盖这 46 条，不能写成 574 条全部与上游对齐。**

本轮没有训练、修改模型权重、调整温度或依据已查看标签选择阈值。概率仍未经校准。这些结果是修正输入实现的新基线，不代表独立封存测试上的质量收益；S4-04 的完整同条件外部参考、中文/逐语言切片及质量验收仍有剩余工作。

## 完整题集的实际运行

| 题集与策略 | 实际处理 | 实际回答 | 正确 / 已答 | 已答准确率 | 正确 / 全部处理 | token 范围 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| [Nimble / `strict`](quality-aligned-2026-09-26/nimble-strict-324.derived.json) | 324/324 | 306 | 133/306 | 43.46405% | 41.04938% | 242–730 |
| [Nimble / `laya_compatible`](quality-aligned-2026-09-26/nimble-compatible-324.derived.json) | 324/324 | 324 | 137/324 | 42.28395% | 42.28395% | 242–730 |
| [PAWS / `strict`](quality-aligned-2026-09-26/paws-strict-250.derived.json) | 250/250 | 250 | 170/250 | 68.00000% | 68.00000% | 76–152 |

三个原始运行报告的 `FullDatasetProcessed=true`，`MeasurementOrigin=csharp_runtime`。独立核查每条 answered 行都具有实际 `ForwardCalls=1`、非空 marker logits、marker positions 和完整 token 序列 SHA-256；按上表顺序分别合计 306、324 和 250 次前向调用，行数及逐题 `Correct` 求和与总表一致。Nimble 严格模式覆盖率为 306/324（94.44444%），18 条失败均为 `decision_token_budget_exceeded`，`ForwardCalls=0`、`Tokens=0`，logits、概率、Selected、Correct 和 token/marker 结果均为 null。这是执行前的输入拒绝，不能计作模型拒答或以虚构概率计入已答分母。

Nimble 的 18 条兼容输入确实记录裁剪：12 条说明裁剪、6 条候选裁剪，state 裁剪为 0；其余 306 条没有输入损失。严格模式拒绝的恰为这 18 条，保留对应 `RejectedInput` 诊断。PAWS 的 250 条在严格模式下均无裁剪。

[严格/兼容模式内部一致性检查](quality-aligned-2026-09-26/nimble-strict-compatible-consistency.json)逐 ID 核对严格模式已回答的 306 条：输入及 token SHA-256、marker/候选顺序、token 数、题型、Selected/Target/Correct 一致，logits 和各概率指标最大绝对差均为 0。这个检查比较两次 C# CUDA 运行，`IndependentReferenceCases=0`，不增加独立 Python 对照条数，也不证明被拒绝 18 条的严格模式数值输出。

Nimble 题型切片：

| 题型 | 严格模式回答 / 总数 | 严格模式正确 / 已答准确率 | 兼容模式回答 / 总数 | 兼容模式正确 / 已答准确率 |
| --- | ---: | ---: | ---: | ---: |
| Choice | 130/146 | 57 / 43.84615% | 146/146 | 60 / 41.09589% |
| Boolean | 114/114 | 56 / 49.12281% | 114/114 | 56 / 49.12281% |
| Score | 62/64 | 20 / 32.25806% | 64/64 | 21 / 32.81250% |

Score 正确率使用最大概率等级，期望分数的 MAE 在严格模式为 `0.8171781400256534`，兼容模式为 `0.8080421487073931`。Nimble 162 对反事实样本在严格模式有 153 对完整回答，兼容模式为 162 对；两种模式均有 9 对预测翻转、1 对两题都正确。两套题集的源记录没有显式 language 元数据，报告保留 `unspecified`，不将其包装成中文或多语言分项验收。

## Boolean 与概率诊断

| 指标 | Nimble Boolean 114 条（两种策略一致） | PAWS Boolean 250 条 |
| --- | ---: | ---: |
| TP / FP / TN / FN | 55 / 56 / 1 / 2 | 104 / 63 / 66 / 17 |
| 正类召回 | 96.49123% | 85.95041% |
| 负类召回 | 1.75439% | 51.16279% |
| Balanced accuracy | 49.12281% | 68.55660% |
| AUROC | 0.4813788858110188 | 0.7912101992440259 |
| 预测 true 比例 | 97.36842% | 66.80000% |

Nimble 的 Boolean true 偏置仍明显，不能用输入覆盖率提高掩盖负类召回或排序区分能力。PAWS 的 170/250 高于本固定集多数类 false 的 129/250，但这仍是已查看评测集，不能据此选择下一轮提示、校准或阈值。

Nimble 严格模式已答 306 条的平均 categorical Brier 为 `0.81684944441853`、NLL 为 `2.451933695093029`、10 个等宽概率分箱的 ECE 为 `0.28809353564705215`；兼容模式已答 324 条对应为 `0.8313788164813612`、`2.452211562869833`、`0.29639334647014726`。PAWS 对应为 `0.48070631446590595`、`0.7512708487215182`、`0.18144793467794218`。Boolean Brier 沿用两类平方误差之和，不能与只报单个正类平方误差的指标直接混用；这些概率诊断不构成校准通过。

[旧质量证据](quality-2026-09-25.md)保持原样，仍对应旧输入序列和错误的整段 256-token 限制。新旧结果反映输入工程修复后的端到端测量，不是训练、权重适配或独立泛化收益。

## 独立外部参考的有限范围

先运行 PAWS 第一条 smoke，再使用固定 Nimble 文件的前 45 条。输入 manifest 在对应参考运行前生成；原始输入、参考及 C# capture 文件保留在忽略目录 `.artifacts/s4-quality-20260926/`，各文件身份见[原始文件索引](quality-aligned-2026-09-26/source-index.json)。

| 独立比较 | 参考 / C# 回答 | 参考 / C# 正确 | 最大 logits 绝对误差 | 最大概率绝对误差 | 冻结合同 |
| --- | ---: | ---: | ---: | ---: | --- |
| [Nimble 前 45 条](quality-aligned-2026-09-26/nimble-45-comparison.json) | 45 / 45 | 18 / 18 | `9.381771087646484e-5` | `2.2155537118540014e-5` | 45/45 通过，0 issues |
| [PAWS 第一条](quality-aligned-2026-09-26/paws-smoke-comparison.json) | 1 / 1 | 1 / 1 | `4.76837158203125e-6` | `8.570033946386779e-7` | 1/1 通过，0 issues |

“0 issues”表示输入合同、数值容差及预测判定没有失败，**不表示浮点结果完全相同**。两份比较均使用原冻结 `contract.v1.json`，没有放宽 logits/probabilities/Score 容差；`near_tie_cases=0`。`full_manifest_passed=true` 仅分别对应本次 45 条和 1 条 manifest，不能扩展到原题集的 324 条和 250 条。

离线评分报告也保留原分母：Nimble reference/CUDA 均 `Processed=45`、`DatasetTotal=324`、`FullDatasetProcessed=false`；PAWS 两侧均 `Processed=1`、`DatasetTotal=250`、`FullDatasetProcessed=false`。Nimble 子集覆盖 Choice 22 条、Boolean 13 条、Score 10 条，各题型正确数分别是 7、7、4。离线评分标记为 `offline_scoring_of_existing_capture_not_new_inference_or_parity_proof`，没有将读文件评分算成新增模型执行。

另有[完整运行与独立 C# capture 的关联检查](quality-aligned-2026-09-26/live-capture-link.json)，SHA-256 为 `f6e83d07c2d59c7ff0a5b664c680ead22ac61bd3bbbaf6e54be8e7a6b2bc683f`。它在 Nimble 324 条完整运行中按 ID 找到上述 45 条，对输入 SHA-256、实际 token SHA-256、marker 顺序、候选顺序、logits、selected/target/correct 逐项核对；45 条一致，最大 logit 绝对差为 0。归档时再次核对两份原质量报告 SHA-256 与各字段，且完整运行这 45 行均有 `ForwardCalls=1`。这是两次 C# CUDA 路径的结果关联，独立 Python/C# 误差仍以上表的非零值为准；记录明确 `compared=45`、`full_dataset=324`、`full_dataset_parity=false`，不能外推剩余 279 条的外部数值对照。

参考使用固定 Laya 0.3.20 / commit `4066d5d5fbf08b66c6757ddeedbd797bd7655bc0`，Python 3.12.10、PyTorch 2.8.0 CPU FP32；C# capture 使用实际生产 CUDA encoder/head。两侧使用相同权重、温度 1、输入顺序与 `laya_compatible`。此处独立参考对照仅覆盖这 46 条；C# capture 在 Windows 原生程序中执行，其身份留在 capture 和 runner 记录，不据此扩展两 RID AOT 的其他验收范围。

## 资产、程序与派生交付身份

模型为 `convaiinnovations/laya-multilingual@052592a15d198d9ad47da779604259b10b47b7aa`，权重 SHA-256 `9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204`，tokenizer SHA-256 `609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f`。Nimble 原文件 SHA-256 `8e9e48b8de5206593912ae01ddc95bd77e40ad2ecf4c9292c1711290eca0d896`；PAWS 250 条转换文件 SHA-256 `c295258fdd73452f196b2673bdbee58cf01fa3f24400c2f1c0f26fd1126bc6f3`。数据来源与标签限制沿用[原始来源说明](quality-2026-09-25.md)：Nimble 为未人工复核的合成规则标签，PAWS 为人标；均只作查看过的评测审计数据。

报告中记录的评分器二进制不是全部相同，不能把最后一次构建的哈希套到早先运行：

| 原始报告 | `Sezika.Evaluation.dll` SHA-256 |
| --- | --- |
| Nimble compatible 324 与 Nimble reference 45 离线评分 | `513a8710ac8c526e29db31359ce00eac9ccf4d196a6bdbc8a0d5c13acede804b` |
| Nimble strict 324、PAWS strict 250 与 Nimble CUDA 45 离线评分 | `1203916471cb81345ccd44797861696ecac344c21c5265be4ba36fdb8b5ee968` |
| PAWS reference/CUDA 单条离线评分 | `86ff2bce63cd40b29ded65b135c4d614e8bd4a00e403273e9ee39bcee1646896` |

每个派生报告保留原报告的完整 `CodeArtifacts`、运行环境和 capture 身份，不改写成最后版本。C# 两次 capture 的原生程序哈希均为 `dc0ab1a05abb909748fd74c597c04102fb3fa391b1464235beeeec87c66ce839`。

七个 `*.derived.json` 是明确标记的派生文件：保留数值指标、程序/资产身份、逐行 numeric evidence；删除 `ByTarget`、候选文本、Target/Selected 文本、原始 ID/family，ID 改为 UTF-8 SHA-256。原始输入 manifest、包含外部题文的 capture 及原质量报告没有复制进仓库，原文件 SHA-256 与派生文件 SHA-256 都在 `source-index.json`。比较报告和进程记录按原字节归档；严格/兼容一致性文件明确标为本地派生检查，哈希见[执行索引](quality-aligned-2026-09-26/execution-index.json)。这些哈希用于复核来源，不代表外部数据已获再分发或训练许可。

## 离线评分完整性

[当前有界完整性检查](quality-aligned-2026-09-26/capture-rejection-checks-guarded.json)先让未修改的 PAWS 单条参考 capture 正常评分，再执行 8 个有意修改的负例；基线 exit 0、stderr 为空，8 个负例均 exit 1 且错误全文逐项符合预期。每项都核对 runner PID、启动时间、父链、参数和清理结果，完整批次上限 360 秒、每项子进程上限 40 秒、外层 runner 上限 390 秒。离线评分读取已有 capture，不执行模型前向。

| 检查 | 预期拒绝原因 |
| --- | --- |
| probabilities | 概率与温度 1 下 logits 计算结果不符 |
| prediction | 预测与记录的分布不符 |
| incomplete | 未完成或失败的 capture 不能算作完整选择 |
| selection | 选择数与 manifest 数不一致 |
| selected-ids | 已选择 ID 与 capture 中的条目不一致 |
| failed-with-numbers | 失败记录带数值输出或缺少失败语义 |
| answered-with-failure | 已答记录带失败语义 |
| duplicate-json | JSON 属性重复 |

归档包含本批次 36 个子进程记录文件、4 个外层记录文件及原 `checks.json` 的精确副本；历史[8 个仅负例检查](quality-aligned-2026-09-26/capture-rejection-checks.json)仍保留，但当前验收依据是有成功基线且校验具体错误的完整批次。正常评分报告和修改后的 capture 含外部题文，只留在忽略目录，不作为推理或质量证据发布。

第一次外层启动使用不存在的 `C:\Program Files\PowerShell\7\pwsh.exe`，0.496 秒后失败：PID、启动时间和 exit code 均为 null，没有 identity 文件，也没有运行任何检查。其 3 个日志文件保留为启动器失败记录。随后按当前进程的实际 PowerShell 7.6.6 路径启动并成功完成一次完整批次；没有安装或修改环境。启动失败不计入 8 个负例或正常基线。

## 有界执行记录

Windows 10.0.26200，PowerShell 7.6.6，普通评测 runtime .NET 10.0.11。主要任务如下；实际 argv、PID、启动时间、父链、stdout/stderr 和清理结果保留在索引指向的 `processes/` 文件。

| 操作 | PID | 外层上限 | 结果 | runner 前缀 |
| --- | ---: | ---: | --- | --- |
| PAWS 单条独立参考 | 77568 | 210 秒 | exit 0，23.531 秒 | `20260925-164104-345-s4-paws-reference-smoke` |
| PAWS 单条 C# capture | 3276 | 180 秒 | exit 0，8.699 秒 | `20260925-164217-459-s4-paws-cuda-smoke` |
| Nimble 单条 C# evaluator smoke | 62320 | 180 秒 | exit 0，8.195 秒 | `20260925-164651-611-s4-nimble-live-evaluation-smoke` |
| Nimble 45 条独立参考 | 29704 | 1750 秒 | exit 0，214.215 秒 | `20260925-164651-527-s4-nimble-reference-45` |
| Nimble 324 条兼容评测 | 75396 | 1750 秒 | exit 0，289.958 秒 | `20260925-164732-513-s4-nimble-live-compatible-324` |
| Nimble 45 条 C# capture | 48828 | 330 秒 | exit 0，45.383 秒 | `20260925-165347-189-s4-nimble-cuda-45` |
| PAWS 250 条严格评测 | 67500 | 930 秒 | exit 0，61.019 秒 | `20260925-165502-158-s4-paws-live-strict-250` |
| Nimble 324 条严格评测 | 37452 | 930 秒 | exit 0，302.621 秒 | `20260925-173618-496-s4-nimble-live-strict-324` |
| 评分完整性基线与 8 个负例工作流 | 58548 | 390 秒 | exit 0，31.296 秒 | `20260925-173801-135-s4-capture-integrity-guarded-workflow-valid-host` |

本页存档的完整性增强构建为 PID 34324、180 秒上限，0 warnings / 0 errors；机械逻辑 self-test 10 项通过。执行索引包含 153 个文件和 37 条操作记录：36 条实际启动进程中，20 条 exit 0，16 条历史/当前格式负例故意 exit 1；另 1 条为未创建进程的启动器失败。所有记录均无清理错误。快速结束的子进程部分 OS `CommandLine` 观察值为 null，精确启动参数仍保留在 identity/result 的 `FilePath`、`Arguments` 中；不把未观测到的命令行或短命子树写成已观测。部分操作并行，耗时包含加载、验证、记录及资源竞争，不能据此报告 CPU/CUDA 或 Python/C# 性能比。

完整实测的命令形状为 `Sezika.Evaluation.dll <model> <frozen-eval.jsonl> <new-report.json> cuda <records> <seconds> <dataset-name> <dataset-total> <dataset-sha256> <strict|laya_compatible>`；Nimble 兼容参数为 324 / 1700 / `nimble-audit` / 324 / `laya_compatible`，严格参数为 324 / 900 / `nimble-audit` / 324 / `strict`；PAWS 为 250 / 900 / `paws-audit` / 250 / `strict`。对应完整参数与程序路径以原 runner JSON 为准。
