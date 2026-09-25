# Oracle 比较器工具回归证据（2026-09-25）

本次完成 4 次不加载模型的 `Sezika.OracleCompare` 回归，全部得到预期结果。人工修改的 token 副本仅用于负向测试；上游 capture 与自身比较仅证明比较器能够处理该格式和 46 行数据。两者均不能作为 Sezika 推理、模型数值对齐、语言质量或校准证据。

## 输入来源与隔离

真实来源是 `.artifacts/laya-oracle-work/full-01/capture.json`，SHA-256 为 `bf0d537305149672f07d34dc6b3e2d3614aeb2c26aa6d11aa7902578100c53fa`。完整原始字节复制到新建的 `.artifacts/laya-oracle-work/comparator-tests-01/reference-original-copy.json`；执行后再次核对，两份文件 SHA-256 仍相同，原始真实 capture 没有修改。

负向副本 `actual-token-mutated-synthetic.json` 只将第一个 case `choice-en-short` 的 `token_ids[0]` 从 `2` 改成 `3`。通过定位原始 JSON 中的第一个 token 文本，仅替换这个 ASCII 数字，其他字节及所有原始数值不变。这个 token 仍满足比较器的非负整数及数组长度限制，因此预期是精确字段差异，而不是结构解析失败。

人工副本 SHA-256 为 `649aa948309ab9e1cb6094f1e14a30b557fd416732771a66d21e524f4ea36b0a`。其用途单独记录于 [SYNTHETIC-TEST-INPUT-MANIFEST.json](../../.artifacts/laya-oracle-work/comparator-tests-01/SYNTHETIC-TEST-INPUT-MANIFEST.json)，明确包含 `synthetic_test_input=true` 和 `not_model_inference_evidence=true`。这些副本不可混入任何真实模型对照报告。

使用预先构建的 `tools/Sezika.OracleCompare/bin/Release/net10.0/Sezika.OracleCompare.dll`，SHA-256 为 `56fa3487f5f7af5b3c1e62cd9eabe3dd0767139c159ef1664125370b73820246`。本子任务没有构建、推理、训练、模型下载或发布。PowerShell 已核对为 **7.6.6**。

## 四次实际运行

每次均通过 `tools/Invoke-BoundedProcess.ps1` 启动 `dotnet`，比较器内部截止时间为 30 秒，外层进程截止时间为 60 秒；先完成一条 token 变更回归，再并行执行另外三个独立的比较器检查。总调用数为 4，没有额外重试。

| 检查 | PID | 启动时间（UTC） | 外层记录秒数 | 工具退出码 | 实际观察 |
| --- | --- | --- | ---: | ---: | --- |
| 单 token 人为修改；明确选择 `choice-en-short` | 58880 | 14:18:28.6382206 | 1.921 | 1 | `passed=false`，恰好一个 `token_ids` 差异；比较 1/46，完整清单未通过。 |
| CSV 重复 `choice-en-short,choice-en-short` | 73540 | 14:18:53.6950716 | 2.728 | 2 | 拒绝重复 ID；没有生成报告文件。 |
| CSV 不存在 `__synthetic_missing_case__` | 73064 | 14:18:53.7657897 | 2.766 | 2 | 拒绝未同时存在于清单和两份 capture 的 ID；没有生成报告文件。 |
| 原始参考字节副本与自身比较，不传 CSV | 58180 | 14:18:53.8279029 | 2.747 | 0 | 46/46，42 个 answered pair、4 个 failure pair、0 差异。仅为比较器自检。 |

外层 runner 将预期退出码 1/2 记录为 `Failed`，这是被测工具的预期拒绝结果，并非回归未达到预期。4 个 runner result 的 `CleanupErrors` 均为空。准确启动参数、各次结果及 stdout/stderr 位置汇总于 [regression-summary.json](../../.artifacts/laya-oracle-work/comparator-tests-01/regression-summary.json)，完整身份与结果文件位于同目录下的 `processes`。

两份实际产生的报告为：

- [token-mutation-report.json](../../.artifacts/laya-oracle-work/comparator-tests-01/token-mutation-report.json)，SHA-256 `e1372fad329a1d35c548082eca8508df098757e45dcc2b7a6459fec4703a5492`。
- [reference-self-report.json](../../.artifacts/laya-oracle-work/comparator-tests-01/reference-self-report.json)，SHA-256 `6ba1207df1ae1717d38f154f10e665e2e36855310f1acd8bd2cbb98c19b8559c`。

自比较报告中 `full_manifest_passed=true` 只说明同一份输入复制后的离线全量比较通过。这次运行的参考和实际文件哈希完全相同，不是独立 C# 推理结果。

## 失败语义的静态核对

原始上游实际 capture 中 4 个非法输入均为 `status=failed`、`failure.stage=validate`、`failure.type=invalid_question`，且没有 tokens、markers、候选或有效数值。C# adapter 与 validator 对应路径如下；本表是代码审阅，不冒充该子任务执行过 C# 模型推理。

| 上游 case | C# 路径 | 对照失败语义 |
| --- | --- | --- |
| `invalid-empty-choice` | 保留空候选，由 `DecisionRequestValidator.ValidateChoice` 拒绝；`decision_candidate_limit_exceeded` | `validate / invalid_question` |
| `invalid-null-score-level` | 保留 null 等级，由 `ValidateScore` 拒绝；`decision_criteria_invalid` | `validate / invalid_question` |
| `invalid-boolean-criteria-key` | adapter 的 `RequireBooleanKeys` 拒绝 `maybe`；`decision_criteria_invalid` | `validate / invalid_question` |
| `invalid-duplicate-boolean-display-label` | 保留重复显示标签，由 `ValidateBoolean` 拒绝；`decision_boolean_labels_invalid` | `validate / invalid_question` |

这些拒绝发生在构建序列前，capture 失败行的 token、marker、label、logit、概率与预测字段保持 null。归一化只比较 stage/type，具体运行时诊断码与上游消息分别保留，因此两种实现不必伪造相同异常消息。

## 证据限制

这 4 个 dotnet 进程执行很短，均在 runner 的 CIM 根身份查询返回前退出。`identity.json` 通过进程句柄保存了 PID 和准确启动时间，也保存了已知 `FilePath`、参数与工作目录，但观测的 `CommandLine`、`ParentPid` 为 null，`result.json` 的 `Processes` 为空。本次**没有取得完整父链证据**，也没有根据推测补写父链。外层有界等待已确认进程退出；该短命进程记录限制已反馈给主线程，后续 runner 改进应在启动时记录自身作为父进程及已知 argv。

本次没有额外运行来源 hash 篡改、重复 capture 行、概率非有限值、在排除行中隐藏非法数据等负向案例；这些分支目前仅完成静态校验范围审阅。不得将本次 4 个回归扩写为比较器所有失败分支已经动态覆盖。
