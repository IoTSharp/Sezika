# Laya 离线捕获比较器

`Sezika.OracleCompare` 是独立 C# / .NET 10 开发工具：读取固定上游 oracle 与待比较实现导出的 `sezika.laya-oracle.v1` JSON，核对同权重、同输入的逐题证据。它不加载模型、不调用推理后端；JSON 使用 source generation。构建、实测和 Native AOT 状态分别以运行证据为准。

参考捕获由 [Laya oracle](../Sezika.LayaOracle/README.md) 生成。C# 实际捕获由 [OracleCapture](../Sezika.OracleCapture/README.md) 使用相同 schema 导出，保留实际 token、marker、候选标签及失败；不能将参考 token/logits 复制成 C# 实测。

## 比较规则

- 严格核对模型 ID/revision、权重和 tokenizer SHA-256、固定 Laya 源码 revision、原始输入清单/容差文件 SHA-256、1024/256 总长与前缀预算、固定温度策略。不同实现与 backend 可在 provenance 中另留诊断字段。
- 逐 case ID 核对输入、primitive、status；case 必须存在于固定输入清单并匹配其 primitive。token IDs、marker 位置和语义候选顺序必须逐项相同。默认比较全部捕获行，两份捕获之间缺行、多行不能通过。可用第 7 参数明确选择 case ID 子集；整个文件仍先校验身份、所有行的结构、重复 ID 和重复 JSON 属性，子集不能隐藏无效数据。
- 原始 logits、未 round 的概率及 Score 期望使用 `contract.v1.json` 中预先冻结的 `absolute + relative * |reference|` 容差。Choice/Boolean 离散结果必须相同；近并列只单独计数，不能自动豁免不一致。
- Boolean `P(true)` 对应 `false,true` 中的第二个 marker；Score 标签为 `0..K-1`，值为未 round 的概率加权期望。概率必须有限且归一化。上游公开 API 的 round4 输出仅作诊断，不用作 raw 比较。
- 失败比较归一化的 `stage/type`，错误消息保留在原始捕获中；失败行数值及 prediction 字段须为 null，避免把部分输出当作有效预测。全是失败行时返回未通过；失败一致不能证明模型数值对齐。

输出 `passed` 只表示**报告所列 case 子集**通过离线比较；`full_manifest_passed` 还要求原始输入清单全部覆盖，且两份 capture 均明确标记执行完成（`complete` 或 `completed`）。报告保留原始两份完整文件的 SHA-256、行数和 measurement status，另有 `selection_mode`、原样 `requested_case_ids`、两份文件各自被排除的 ID、实际比较的 `selected_case_ids`、相对于完整清单的 `missing_case_ids`、`input_coverage` 和 `complete_input_coverage`。例如 38/46 子集即使 `passed=true`，覆盖率仍为 38/46，`complete_input_coverage` 和 `full_manifest_passed` 均为 false。完整短/中/长、中英、边界矩阵覆盖和真实捕获来源仍须审核，不能代替语言质量、AOT 或性能结论。

## 有界运行

获准构建和执行后，通过仓库有界进程入口调用：

```powershell
pwsh -NoProfile -File tools/Invoke-BoundedProcess.ps1 -FilePath dotnet -ArgumentList @(
  'tools/Sezika.OracleCompare/bin/Release/net10.0/Sezika.OracleCompare.dll',
  '<reference.json>', '<actual.json>',
  'tests/fixtures/laya-oracle/contract.v1.json',
  'tests/fixtures/laya-oracle/cases.v1.json',
  '<new-report.json>', '60'
) -TimeoutSeconds 90 -LogName oracle-compare
```

报告路径的父目录须已存在，文件必须尚不存在；工具用 `CreateNew` 防止覆盖输入或既有证据。退出码：`0` 捕获比较通过；`1` 比较存在差异；`2` 输入无效、身份不符、取消/超时或 I/O 失败。无效输入不会产生可误认通过的完整报告。

需要限定支持范围时，在上述 `60` 参数后添加一个 CSV 字符串，例如 `'choice-en-short,score-en-short,boolean-en-short'`。最多 128 个 ID，每个 1–128 字符；空 ID、重复 ID、未存在于原始清单或任意一份 capture 的 ID 均拒绝。不会自动排除失败行、选取数值相近的行或按推理状态筛选；支持范围清单必须由调用者明确给出，并与完整结果一起保留。

最多 128 个 case、每个 1024 token/64 个参考候选（允许描述超出 Sezika 当前候选限制的上游输入，不表示运行时支持）；每份 JSON 至多 32 MiB、深度 32、500000 个节点。概率和采用合同的 0.000002 容差，Boolean 阈值为 `P(true) >= 0.5`。总墙钟上限可设 1–300 秒，支持 Ctrl+C；每行比较写进度。先以真实导出的 1 条短输入验收工具，再扩展矩阵。
