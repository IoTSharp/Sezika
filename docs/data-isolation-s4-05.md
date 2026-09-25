# S4-05 数据来源准入与 split 隔离入口

状态：**代码与原创 schema 示例已实现并构建，5条示例审计已实际运行，真实数据集尚未验收**。示例按预期返回 `blocked` / exit 3，10项问题包括许可/用途、5条人工复核、已查看测试槽位与未封存状态，见[最小运行证据](evidence/s3-supporting-tools-2026-09-25.md)。S4-05 的真实来源许可审签、人工复核、污染检查和封存交付仍待完成。阶段计划只在 [ROADMAP](../ROADMAP.md) 维护。

## 入口与产物

[DatasetTool](../tools/Sezika.DatasetTool/Program.cs) 保留既有 PAWS Parquet 转换命令，增加 `audit-splits <manifest.json> <new-report.json>`。新增路径只使用 C#/.NET BCL，通过静态 record、枚举和 source-generated `System.Text.Json` 读写，不调用模型或网络，也不生成数据、分配 split、自动批准许可或修改输入文件。

旧项目仍引用 DuckDB 作为离线 Parquet 转换依赖，不能据此宣称 DatasetTool 或新增命令已经支持 Native AOT；该依赖没有进入 Sezika 模型运行时。新增审计路径的 AOT 可行性也尚未实际发布验证。

输入是固定来源清单、审计记录 JSONL、原始来源资产与必要的许可/封存证据。每个文件都必须在清单目录内以相对路径引用并给出 SHA-256；清单本身和记录文件的实际 SHA-256 进入报告。工具拒绝路径的词法越界、未知 JSON 字段、重复属性、缺失必填字段、无效枚举与超限输入。路径边界不解析链接：清单目录必须由审计操作者控制，不允许放置指向目录外的符号链接或 reparse point。

报告只包含记录 ID、split 计数、来源用途清单、固定算法/阈值、配对数、问题和耗时，不输出原始题文。即使全部检查通过，状态也只会是 `checks_passed_pending_human_acceptance`，`requires_human_acceptance=true`、`represents_model_quality=false`。该状态不授予执行或发布权限，不代表许可法律意见、已校准或质量达标。

## 来源、用途与人工审核

每个来源独立记录 `upstream_id`、`upstream_split`、`revision`、原始文件路径/hash、`license_expression`、许可审签人/时间/hash 证据、`allowed_uses`、`allowed_splits` 与查看历史。模型、tokenizer 的许可不能替代数据许可；上游代码仓库的许可证也不能自动推导到其数据。

`intended_use` 必须出现在每个来源的 allowlist 中：`Research`、`Commercial`、`OpenSourceRedistribution` 各自独立准入，不能从研究许可推断商业使用或数据再分发许可。多个用途需分别运行并保留各自清单/hash 和报告；`allowed_splits` 则独立控制训练、提示开发、校准、封存测试与审计用途。许可状态 `Pending`/`Rejected` 总会阻断；`Approved` 还必须提供非未来时间、审核人和真实 hash 绑定证据。工具只验证声明与文件一致，审核人必须确认授权文本与拟用范围确实匹配。

记录的 `human_review` 同样要求人工审核人、非未来时间和 `review_note`。审核内容应涵盖来源链、去标识化、语义和标签（若关联数据有标签）、翻译等价性、反事实是否真的改变了目标、实体别名及污染风险。不能把生成器或模型自身的判断填成“人工批准”。本工具的 `text` 是隔离审计用的完整相关题文，不是训练器数据格式；关联模型输入、候选、标签及其版本仍需单独管理，人工审核必须确认审计题文没有遗漏真实输入。

## 已查看评测的隔离

根据 [既有质量证据](evidence/quality-2026-09-25.md)，已经查看的 PAWS test 250 与 Nimble eval 324 只能进入 `Audit`。它们及任何翻译、改写、反事实衍生物都不能进入训练、提示/顺序/阈值开发、校准或新的封存测试。

清单显式使用 `origin=PawsViewedTest250` 或 `NimbleViewedEval324`；所有来源/记录的 `exposure=EvaluationViewed` 也会强制 audit。工具额外识别既有 PAWS 250 JSONL SHA-256 `c295258fdd73452f196b2673bdbee58cf01fa3f24400c2f1c0f26fd1126bc6f3` 与 Nimble 324 SHA-256 `8e9e48b8de5206593912ae01ddc95bd77e40ad2ecf4c9292c1711290eca0d896`，使其本地改名不能直接解除限制。这两个值沿用既有证据，本轮没有重新打开或分发这些外部原题。

规则不按名称中是否出现 `paws`/`nimble` 判断内容，也不把整个上游仓库或 PAWS 全量其他 split 自动封禁。其他来源必须保留真实 split、许可与人工审核记录；故意谎报 provenance、隐藏变体或另存未登记原始文件不能靠文本 hash 自动识破。原始已查看题文需由有权限的审核者纳入同一次审计清单，才能检测其他来源与它们的内容重叠。只运行新数据自己的清单不能声称已经排除了对这些历史题目的污染。

## 分组、近重复与多语言衍生

`family` 和 `entities` 是全清单范围的稳定 ID，不随语言、领域或来源分组放松隔离。同一家族、共享实体、标准化文本 fingerprint 或达到近重复阈值的记录跨任何 split 都会阻断；同一 split 内保留翻译和反事实是允许的。领域常用词不应被当作实体，否则会不必要地合并整个领域；实体别名和中英名称必须由来源整备/人工审核映射为同一个 ID。

标准化规则固定为 NFKC、invariant uppercase、保留 Unicode 字母/数字/组合标记、去掉空白与标点，然后 UTF-8 SHA-256。近重复使用标准化后 Unicode scalar 三元字符集合的 Jaccard 相似度，阈值固定为 `0.82`，少于三个 scalar 时使用完整串。该保守规则会合并标点、空格或大小写不同的内容，但仍可能漏掉低词面重叠的语义改写、跨语言翻译、同义替换，也可能误报真实不同题；报告中的近重复必须人工复核，不能为通过验收反复使用测试集调阈值。

`derivation=Translation/Counterfactual/Paraphrase` 必须提供 `parent_id`。所有祖先都要存在于当前审计清单；工具有界遍历父链，拒绝环并检查整个链的家族、split 和已查看题暴露状态。跨来源衍生仍继承 audit 限制。该入口审核已经形成的衍生清单，不声称实现自动翻译生成、语义验证、隐式家族发现或与模型预训练语料的完备重叠检测。

## 封存状态与失败语义

完整 S4-05 准入清单至少包含 `Development`、`Calibration` 和 `SealedTest` 三个非空 split；训练与 audit 可按实际用途纳入。记录和来源只要被标为已查看，就不能伪装成新的未见封存测试。`DevelopmentViewed` 并不自动禁止校准，但校准标签/输出不可回流提示或模型调参，实际访问流程仍需人工审签。

`test_seal.status=Sealed` 还要求 custodian、非未来 UTC 时间和 hash 绑定的证据 JSON。证据包含 `dataset_id`、`records_sha256`、`custodian`、`sealed_utc`，工具逐项与当前清单/记录实际 hash 核对。冻结的是整份审计 inventory，变更其中任何记录都需要重新冻结。该证据必须由独立保管人通过实际流程形成；机械一致性检查不能证明题目未被人查看或物理访问控制真的有效。

执行审计需要读取题文，封存审核应由保管人完成，开发者只接收不含题文的审计报告。不要把待封存的真实题文提交到开发者可见目录，再仅靠修改 JSON 状态声称未见。

| 退出码 | 含义 |
| --- | --- |
| `0` | 结构/准入检查通过，仍待人工最终签收 |
| `1` | 输入、hash、I/O、上限或报告写入失败；不能当成完整审计 |
| `2` | 命令用法错误 |
| `3` | 审计完成，报告有准入/泄漏/封存阻断项 |
| `4` | Ctrl+C 取消或 60 秒 deadline；不能当成完整审计 |

输出使用 `CreateNew`，不覆盖历史报告。若写入中失败或取消，可能留下不完整 JSON；只有完整 JSON、对应退出码和运行记录共同构成证据。工具不自动删除输入或残留文件。报告目录必须事先存在。

## 有界执行与尚未执行的复核

硬上限为 32 个来源、1000 条物理 JSONL 行、每文件 4 MiB、每条题文 8192 UTF-16 单元、每条 32 个实体、499500 个记录对、10000 个问题，以及总墙钟 60 秒。NFKC 展开上限为原题上限的 18 倍，全部标准化题文合计不超过 2097152 个 UTF-16 单元。所有主循环/父链/字符处理/集合比较共享 deadline、取消 token 和每秒进度；较大来源需要另外设计带原始完整资产 hash 的有界分片流程，不能把截断后的不完整审计当作完整通过。

[极小示例](../datasets/s4-05/README.md) 有 5 条公开原创记录：三个同家族中英/反事实开发样本、一个校准槽位和一个未封存测试槽位。已审阅循环比较条件（记录行 `<= MaxRecords + 1` 用于拒绝第 1001 行、配对 `right = left + 1; right < count`、父链 `depth < count`），并在本轮明确授权后实际运行：5条记录、10个配对、10项问题，按预期退出 `3`；未把待人工批准的样例自动改成通过。

复现时仍按仓库规则获得相应执行授权，并经 [有界进程工具](../tools/Invoke-BoundedProcess.ps1) 记录 PID、启动时间、命令行、父链、超时、日志和子树回收。以下是命令模板；本轮实际采用的隔离构建路径和新报告路径见执行证据：

```powershell
& ./tools/Invoke-BoundedProcess.ps1 -FilePath dotnet -ArgumentList @(
  'build', 'tools/Sezika.DatasetTool/Sezika.DatasetTool.csproj', '-c', 'Release', '-v', 'minimal'
) -TimeoutSeconds 180 -LogName 's4-05-build'

& ./tools/Invoke-BoundedProcess.ps1 -FilePath dotnet -ArgumentList @(
  'tools/Sezika.DatasetTool/bin/Release/net10.0/Sezika.DatasetTool.dll',
  'audit-splits', 'datasets/s4-05/example-manifest.json',
  '.artifacts/s4-05-example-audit.json'
) -TimeoutSeconds 75 -LogName 's4-05-example-audit'
```

后续验收还需要独立的坏 hash、重复 JSON 属性、超限/取消、家族/实体/近重复跨 split、衍生父环、已查看来源改名/祖先暴露、封存绑定不符等用例，并完成真实许可、人工审核与封存流程。当前源码、固定格式和极小示例阻断证据不能据此宣称“无泄漏数据集已建成”。
