# S4-10 可选教师入口合同

`Sezika.TeacherTool` 是独立于核心推理库的离线合同检查工具。它不把 IoTSharp/Tomur 客户端、远端 API 或教师服务带入 Sezika 核心，也不会把教师输出自动视为训练标签。

当前候选固定为 `qwen35-9b-q4km`。`validate-catalog` 只接受包含候选身份、服务、revision、许可证和状态的 JSON；状态为 `unverified` 时工具返回退出码 3 并生成 `blocked_unverified` 报告。只有明确的 endpoint 与条款证据齐全时才允许写 `verified`，本仓库没有伪造这些证据。

`validate-records` 最多读取 32 条、每条最多 64 KiB 的采集记录，要求唯一 `record_id`，保留 `pending/accepted/rejected/failed/abstained` 全部分母，并要求进入 accepted/rejected 的响应带 SHA-256。即使结构检查通过，报告仍标记 `valid_pending_human_review`，`teacher_labels_permitted_for_training` 为 false；人工审核、模型身份、提示 hash、资源用量、许可和训练准入必须在后续独立记录中完成。

本工具没有执行网络调用。候选本机安装、实际接口、错误语义、响应格式、许可和资源预算仍是 S4-10 的阻塞条件；完成工具构建不代表教师可用或任何质量结果已验收。
