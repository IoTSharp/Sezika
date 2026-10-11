# 全量严格长度扫描与数据准入

这次实现为离线准备流程，使用项目真实 C# `decision-v1` renderer 和 tokenizer。它没有计算模型特征，没有执行 GPU 训练，也不证明准确率提高。原始 corpus、模型权重和教师答案不会被此流程改写。

`lengths-corpus` 在提供固定源范围政策时使用分块扫描；`--resume` 必须显式指定，并维持 corpus manifest、bindings、原始排除清单、tokenizer、政策、token 上限和分块大小的 SHA/参数身份。默认每块 4096 行，每次执行还受到物理行数、分块数、`--timeout-seconds` 墙钟、`--cancel-file` 取消文件和外层进程守护限制。

扫描分别统计原始排除、源范围排除、严格 token 超限和准入行。仅 `decision_v1_token_budget_exceeded` / `decision_token_limit_exceeded` 可以成为 token 排除；字符预算、deadline、结构和其他 renderer 错误仍使尝试失败。失败回执保留 source、split、record_id、shard、物理行位置和错误码，不记录 State 或答案。训练／开发集使用供应数据给出的 256-bit Family 身份进行精确隔离检查；跨来源的语义重复、基准污染和标签正确性仍需另外评估。

完整块先保存排除和 Family sidecar 的 SHA，再提交块回执。中断尝试保留在 `attempts/` 中，恢复只复用 SHA 验证通过的连续完整块，不重新渲染已完成的前缀。每个 shard 的最后一块必须验证 EOF。最终汇总清单已落盘而完成标记未落盘时，可以重新汇总并复用完全相同的清单字节；冲突证据不会被覆盖。

完整完成标记在所有物理行、shard、排除数量、训练／开发数量和 Family 隔离通过后才写入。排除身份总数上限为 1,000,000，Family 索引上限为 4,000,000；超限明确失败，不静默截断。部分块、pending 文件或失败回执不能用于准入。

`admit-corpus-lengths` 要求固定完成标记和政策 SHA，创建新的派生 corpus。它使用同卷硬链接复用原始 gzip shard，并重新检查字节和 SHA；不重新生成或裁剪原始记录。旧 manifest、bindings、排除清单归档到 `components/length-parent/`。新的排除清单、政策、第三方声明和完成标记分别绑定。新的 `lengths.json` 仅统计通过源范围和严格长度检查的训练／开发数据。GPU 训练读取前还会验证派生 manifest 的 `length_admission`、父数据身份和各项数量一致。

可移植 provenance 包将 `source-scope-policy.json` 作为私有源范围元数据、`length-scan-complete.json` 作为私有完整扫描元数据、`THIRD_PARTY_NOTICES.md` 作为来源声明。精确文件名、绑定 SHA 和元数据 schema 必须通过检查，不扩大为任意 JSON 或原始题目／答案载荷。扫描 sidecar 留在本地扫描证据目录；模型包中的完成标记属于元数据证据，不能单独作为重新执行全扫描的替代品。

验证记录位于 `C:\Users\mysti\.codex\artifacts\sezika-full-training-20261012-01a12b4c\length-agent-logs`。`length-scan-check-v3.json` 已通过 21 个合成 gzip/renderer 检查，包括中断与恢复、最终汇总恢复、SHA 篡改、Family 泄漏、排除上限、非 token renderer 失败、派生 corpus 和可移植元数据包。fixture v1/v2 的失败回执保留，用于区分测试 tokenizer 字段缺失与 JSON 安全转义行为。`scheduler-v5.json` 通过原有 42 个调度／provenance 检查和新增 4 个精确元数据角色、原始 payload 文件名、未绑定政策和伪装 raw 字段检查，合计 46 个。最终 Release build v6 为 0 警告、0 错误。10 个本任务守护回执均为 FinalCheckComplete=true、Live=0、Unknown=0；4 个归属已核实的 gate 目录已精确清理，日志、失败尝试和证据保留。汇总回执为 `owned-process-and-gate-cleanup-v2.json`。真实全量扫描结果、实际 GPU 训练和模型质量应引用后续各自的独立回执。
