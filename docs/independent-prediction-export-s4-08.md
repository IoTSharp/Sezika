# S4-08 / S4-15 独立预测导出合同

`tools/Sezika.IndependentPredict` 是独立 C# 离线工具。生产路径仅加载 `IndependentDecisionV1Runtime` 的原始独立 encoder 和另行绑定的 marker head；没有 legacy、HTTP 或教师 fallback。它将获准 calibration/sealed_test 记录的真实本地概率导出为现有 `Sezika.CalibrationTool` 可读取的 JSONL，不拟合温度、不批准数据、不声称模型质量。

2026-10-08 已完成实际工程验收：最终solution构建零警告/错误，先1项极小试运行，再11项完整检查通过；实际CalibrationTool读取4条tiny C# runtime预测，2 calibration+2 sealed_test，以完整匹配的身份/预测hash返回预期exit3/not_eligible。原子发布抢占回归和失败/取消分母通过，独立复核的发布顺序阻断已关闭。执行、首次编译失败和外层核验字段修正记录见 [监督证据](evidence/supervision-2026-10-07.md)。真实模型、正式数据准入、封存、质量、CUDA/AOT 与性能门槛均保持未验收。

## 输入与准入顺序

```text
export-manifest.json                  固定版本、primitive、拒答阈值与文件/hash
head-binding.json                     既有 CLI 支持的最小 schema 1 head binding
admission/model-independent.json      DatasetTool 原格式的 S4-05 manifest
admission/report.json                 DatasetTool 原格式的审计结果
admission/license-evidence.*           真实来源许可审核证据
admission/seal.json                    DatasetTool 原格式的独立封存证据
human-acceptance.json                  实际审核人完成的最终签收及 request/label 映射复核
requests.jsonl                        保管人持有的独立模型输入与金标
```

所有 artifact reference 使用 `{ "path": "relative/path", "sha256": "..." }`；相对路径只能位于其 manifest 目录内，文件及祖先不得为链接/junction。主 export manifest 另通过命令行显式 SHA-256 承诺，不把文件自带 hash 当作自证。工具只读输入，输出必须新目录。

`export-manifest.json` schema 1 的字段为：`schema_version`、`dataset_id`、`primitive`（choice/score/boolean）、`abstain_below`（0–1，采样前固定）、`requests`、`admission_manifest`、`admission_report`、`human_acceptance`、`head_binding`。最后五项均为 artifact reference。

正式流程先读取准入元数据并核对：

1. 既有 S4-05 report 必须为 `checks_passed_pending_human_acceptance`，检查通过、无 issues、`requires_human_acceptance=true`，与同 dataset 的 manifest/完整 audit inventory hash 绑定；FixtureOnly 或 blocked 均拒绝。
2. 来源独立许可已 Approved，审核人/非未来日期/证据 hash 存在；manifest 与 report 的 source ID、用途、split 与 exposure 一致；当前 intended_use 在许可 allowlist。
3. 封存为 Sealed，实际 custodian/日期和证据 hash 存在，seal 内容绑定 dataset、完整 inventory hash、custodian 和冻结时间。
4. 实际人类最终签收为 accepted_for_quality；签收绑定 dataset、用途、上述 manifest/report/inventory/seal hash、requests 完整 hash 与选定 record ID 集合，明确 input contract 与 labels 已复核。

只有这四步通过后，才打开 audit inventory 和 requests 的题文。随后逐 ID 比对 inventory 中的 split、source、语言、域、逐条人工复核以及未见封存语义，核对所有 split/记录分母。签收 ID 集合必须与 requests 完全一致，不从 passed 报告的一部分静默换题。开发者不能把公开候选改成未见封存题；正式运行应由有权限的保管人完成，不把真实题文提交开发者可见仓库。

`human-acceptance.json` schema 1 精确字段如下（这里只列字段，不提供任何伪造签收/封存示例）：

| 字段 | 绑定含义 |
| --- | --- |
| schema_version、dataset_id、status | 版本 1、当前 dataset、accepted_for_quality |
| intended_use、reviewer、accepted_utc、note | 与来源清单一致的用途、实际审核人、封存后且非未来的签收时间、审核记录 |
| input_contract_and_labels_reviewed | 真实人已核对完整模型输入、候选顺序、标签与 audit ID/题文映射 |
| admission_manifest_sha256、admission_report_sha256、admission_records_sha256 | 同一已通过审计的 manifest/report/inventory |
| requests_sha256、seal_sha256 | 本次 request/labels 完整文件及既有 seal 证据 |
| record_ids | 本次全部选定 ID，无重复，2–32 项 |

这些文件的一致性不证明人类身份、法律许可或物理访问控制真的成立；工具绝不创建这些事实，只核对已有审核链。模型许可不替代数据许可。requests 原题与金标必须由人类签收后才进入本次正式导出。

head binding 使用既有最小 v1 格式：`schema_version=1`、`identity`（model_id/encoder_sha256/tokenizer_sha256/data_manifest_sha256/hidden_size/protocol/length_policy）、`feature_sha256`、`head_sha256`。其 data manifest hash 是训练来源，评测准入 manifest 另列，不将二者混同。当前不直接接受 v2 training report；可从已验报告物化同身份的 v1 binding，并在 export manifest 外部绑定文件 hash。

requests JSONL 每行精确字段：`id`、`split`、`primitive`、`label`、`language`、`domain`、`request`。split 仅 calibration/sealed_test；language 仅 zh/en；request 是完整 decision-v1 JSON 对象，恰有一道题且 question ID 等于行 ID。label 是按候选输入顺序的 0-based 人工硬标签；Boolean 恒 false/true 两类，Score 为有序等级索引。每个导出文件只使用一种 primitive、相同候选维度、两 split 均非空；语言分组信息原样保留。Choice 候选 ID 的局部位置不构成全局标签词汇。

## 消费者输出与身份

输出目录包含：

- `identity.json`：固定字段顺序的 release identity 与组合 hash 输入。
- `predictions.jsonl`：UTF-8 无 BOM、每行 LF；字段精确为 id/split/primitive/label/language/domain/probabilities。
- `calibration-manifest.json`：只有所有原始记录都 answered 才生成；精确使用既有 `QualityCalibrationManifest` DTO。
- `export-report.json`：全量 outcomes/分母、输入/准入/封存/训练/head/protocol/output hash、消费者 identity 与原始失败码。

`identity.json` 的算法标识为 **sezika.encoder-head-identity.v1**。组合 SHA-256 对当前 source-generated serializer 输出的完整 UTF-8 文件字节计算；字段顺序固定为 schema_version、algorithm、model_id、source_revision、encoder_sha256、head_sha256、tokenizer_sha256、protocol、length_policy、protocol_sha256，hash 值规范为小写。消费者的 `model_weights_sha256` 使用该文件 hash，表示整个 encoder+head+tokenizer+协议绑定；encoder/head 各自 SHA-256 同时单列，不能只给 encoder hash 而让两个不同 head 共用校准身份。model_revision 为 `head-<head_sha256>`，tokenizer_revision 为固定来源 revision。

`prompt_schema_sha256` 对代码内固定 `sezika.decision-v1.contract.v1` UTF-8 合同描述计算，identity 中公开该 hash；它是版本合同身份，不能证明上游数值或质量。严格 1024-token、BOS=2/EOS=1/marker=4、Boolean 顺序和 JSON 段版本改变时必须升级此合同。二进制实现身份及新的真实数值回归由监督验证证据另管。

prediction 完整字节 hash 填 `records_sha256`；calibration/sealed_test split hash 分别对对应 JSONL 行原顺序、每行追加 LF 的字节计算，与现有消费者实现一致。manifest 省略可选 `manifest_sha256`，避免不可成立的自 hash；manifest 的实际 hash 记录在 export-report 中。`abstain_below` 沿用采样前冻结值。

现有 CalibrationTool 只接受有概率分布的 answered 行，无法把推理失败/拒答纳入模型执行覆盖率。本工具因此在任一失败、拒答、取消或未处理出现时 **不生成 calibration manifest**，report 为 not_eligible，并保留 total = answered + rejected + failed + unprocessed。部分 predictions 仅用于失败定位，不能独自生成新的通过清单或把失败题删掉后验收。消费者报告里的覆盖率是其概率阈值下的 selective coverage，不是独立推理执行覆盖率。

只有全部输出成功的正式签收数据返回 exported_pending_quality_evaluation；这不是 quality passed。self-test 产物始终 `fixture_only` / `not_eligible`，即使合成指标满足所有门槛也不能转成真实质量验收。

## 命令与验证边界

```powershell
# 使用固定 PowerShell 7+，由监督者通过 Invoke-BoundedProcess.ps1 记录进程。
dotnet tools/Sezika.IndependentPredict/bin/Release/net10.0/Sezika.IndependentPredict.dll export `
  --manifest <export-manifest.json> --manifest-sha256 <SHA-256> `
  --package <independent-package> --head <head.asset> --output <new-directory> `
  --max-records 32 --request-timeout-seconds 300 --timeout-seconds 1800
```

默认最多 32 请求、每请求 300 秒、总 1800 秒，可降低上限；每次只处理一题。元数据/签收/head binding/seal 各 64 KiB 或固定 4 MiB 上限，inventory/request 文件最多 4 MiB，每行 65536 UTF-8 字节；audit inventory 最多 1000 行、32 来源，实际请求最多 32 行；路径祖先 128 层、JSON 32 层/131072 token、解析 10 秒、每文件读预算 20 秒。全部循环有有限数量和共享取消；Ctrl+C 取消，逐题显示进度，无网络调用/轮询/重试。保存失败诊断的短写入不复用已取消 token，但仍位于有界外层进程范围。

退出 0 表示完整正式导出待质量评价；1 为准入/格式/文件错误，3 为 not_eligible，124 为取消/期限或未处理记录。所有输出先写到目标同父目录下本任务创建的 GUID staging，文件使用 CreateNew；完整 report 写成后逐文件核对本次记录的长度/hash，再以 Directory.Move 原子发布新目录。report 或最后发布失败不能在目标留下可供消费者使用的 manifest。失败回收仅删除本次登记且当前 hash 相同的文件；外部占用目标目录或篡改/未知 staging 文件均保留并报告，不递归清目录。重跑须新目录，不覆盖旧证据。runtime 在成功/失败/取消路径 Dispose；self-test 独占 GUID 临时 tokenizer/head 文件在 finally 逐路径回收，保留用户指定输出目录作为 fixture 证据。

监督验证必须先：

```text
self-test --output <new-pilot-directory> --maximum-cases 1
self-test --output <new-full-directory> --maximum-cases 8
```

自检调用与生产相同的导出循环，内部 tiny C# runtime seam 无产品命令开关。覆盖真实 tiny forward、split/输出 hashes、encoder+head 组合身份、实际 token-budget 拒答、取消全分母、重复字段、32 条上限以及 pending/blocked 准入在题文打开前拒绝。最后在全部输出/report 已完成的时点注入目标被其他所有者占用的发布故障，检查目标没有 manifest/report、本任务 staging 已回收、外部文件保留；参数解析另核对预取消与固定 5 秒预算。它只构造 pending 的合成审核元数据，不伪造 accepted 人工审批或 seal。

完整消费者联调须 **实际运行 CalibrationTool**，输入 `<full>/complete/predictions.jsonl` 和 `calibration-manifest.json`；从同目录 export-report.identity 原样复制 model/tokenizer/prompt 参数。预期 gate=not_eligible、退出 3；仅比字段或在 producer 内调用 core evaluator不够。当前未执行，监督者按本次授权进行串行构建和此联调后记录结果，不能将命令模板写成执行证据。
