# S4-10 可选教师入口合同

`Sezika.TeacherTool` 是独立于核心推理库的离线合同检查工具。它不把 IoTSharp/Tomur 客户端、远端 API 或教师服务带入 Sezika 核心，也不会把教师输出自动视为训练标签。

当前候选固定为 `qwen35-9b-q4km`。`validate-catalog` 只接受包含候选身份、服务、revision、许可证和状态的 JSON；状态为 `unverified` 时工具返回退出码 3 并生成 `blocked_unverified` 报告。只有明确的 endpoint 与条款证据齐全时才允许写 `verified`，本仓库没有伪造这些证据。

`validate-records` 最多读取 32 条、每条最多 64 KiB 的采集记录，默认总时限 30 秒（可在 100–120000 毫秒内显式收紧或放宽），逐条检查取消并输出进度。每条记录要求唯一 `record_id`、`split`（禁止 `sealed_test`）、来源与 `family_id`、教师模型 ID/revision、request/prompt SHA-256；进入 `accepted/rejected` 的响应还必须带 response SHA-256 和人工审核 ID。工具保留 `pending/accepted/rejected/failed/abstained` 全部分母，格式错误也计入 rejected，不会因一条坏记录丢失整份报告。

即使结构检查通过，报告仍标记 `valid_pending_human_review`，`teacher_labels_permitted_for_training` 为 false；人工审核、模型许可、资源用量、提示与响应内容的审签和训练准入必须在后续独立记录中完成。报告 schema 2 额外保存 failed/abstained 计数，避免把拒答或调用失败当作教师正确率。

本工具没有执行网络调用。候选本机安装、实际接口、错误语义、响应格式、许可和资源预算仍是 S4-10 的阻塞条件；完成工具构建不代表教师可用或任何质量结果已验收。
