# Label audit final script review

结论：**本次限定范围的静态复核通过；没有发现新的阻断项。** 原两项 P2 与后续主失败保护问题已修复。这里只审核固定的三个文件，未执行主脚本、调用 API 或重新运行离线验证。

## 文件身份

| 文件 | SHA256 |
| --- | --- |
| Invoke-LabelAuditBatch.ps1 | 076AE5E6D6292B3A674F2051046F4151811522D7F2C829D4E18B058B33158A1E |
| Test-LabelAuditPayload.ps1 | A71A86A4446AE4255512018636E5B824F9CD68CB27C3295F4E9B283575E7853E |
| validator-offline.result.json | 44CE48C6B167B3D8E4C5077EF0CABEC7911C9DEA5900EDA2139F08A102911452 |

三个文件均来自 `D:\source\Sezika\.artifacts\label-audit-20261008`。主脚本 168 行；验证器 194 行；离线回执 59 行。

## 修复确认

- 主脚本第 14–16、36–38、94–95 行：共享阶段 deadline 纳入 Check-Budget；HTTP CTS/client timeout 取 240 秒、批次 260 秒剩余量及 API 阶段 900 秒剩余量的最小值，并为后续处理预留 5 秒。请求前余时不足则终止。
- 主脚本第 101 行：禁用自动重定向；应用只创建一次 SendAsync，请求目录仍禁止重复使用。
- 主脚本第 151–167 行：Cancellation 与五个资源 Dispose 分别捕获异常；result.json 写入也有独立 catch，在先前状态为 failed 时保留主失败，在先前成功时传播持久化失败。清理计时与错误写入回执。
- 验证器第 38–58、69–104 行：使用 JsonDocument，而非依赖网关 schema 执行情况；拒绝注释/尾随逗号、非对象顶层、未知/重复字段、缺失字段、错误类型、非法枚举与 revision flag/reason/text 不一致。
- 验证器第 136–159 行：精确 JSON Int32 索引与 ID 一致性；非 determinate 要求 JSON null；alternative_ids 逐项检查字符串、候选成员及唯一性。
- 验证器第 160–173 行：引文数量不超过四条，每条必须以 Ordinal 比较出现于一个原始字段；没有用人工换行拼接字段。
- 验证器第 59–68、86–91、174–188 行：源记录与题目身份、唯一记录、原标签范围及完整分母有检查。源标签仅用于本地结果对比，审核请求只发送题文与候选映射。

## 离线回执一致性

回执状态为 passed，记录三份既有响应与 17 条通过记录：tiny-max-01 为 1/1、batch-max-02 为 8/8、batch-max-03 为 8/8。六个负例全部记录为拒绝且 passed：fractional-index、unknown-enum、duplicate-property、cross-field-quote、string-boolean、revision-inconsistent。总数与逐项结果一致；回执 ValidatorSHA256 与本次读到的验证器 SHA256 一致；回执记载耗时 674 ms、NoApiCalls=true。

这确认了回执内部一致性及代码覆盖的对应拒绝路径。此复核未重放测试，未读取负例 fixtures 或旧响应文件，也未审查后来两批的响应结果。

## 范围和语义

验证器限制 1 MiB JSON、深度 32、最多 32 条记录、12 个字段、20 个候选/alternative、4 条引文、32 个原文片段及 30 秒，并在循环中检查取消/时间。主脚本检查批次/阶段时间与 cancel.request，HTTP 轮询有 245 次上限和进度输出。

这些是代码与回执审查结果，HTTP 故障路径、取消、清理超时和进程硬终止路径仍属于动态验证范围。清理的 5 秒计时记录超时而非抢占式中断。

模型与 effort 是网关响应声明，不能据此独立验证上游身份。AI 标签审核保留 human_review=pending、formal=false、eligible_as_sealed_test=false，不能转述为人工标签复核、模型质量验收或已校准。

## 执行与清理

使用 PowerShell 7.6.6。三个固定文件各读取一次，reader PID 50828，启动时间 2026-10-08T18:46:24.7833980+08:00，父 PID 52200；读取调用 exit 0，耗时 1.159 秒。读取与报告写入进程的完整 argv/父链、文件 SHA、完成时间与总复核耗时记录在 script-review-final-readers.json。仅新增本报告与该 readers 交付物，无子负载或临时文件。root 应核对记录中的精确进程身份在调用结束后均不存在。
