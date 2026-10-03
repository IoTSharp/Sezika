# S4-10 可选教师入口合同

`Sezika.TeacherTool` 是独立于核心推理库的离线合同检查工具。它不把 IoTSharp/Tomur 客户端、远端 API 或教师服务带入 Sezika 核心，也不会把教师输出自动视为训练标签。

当前候选固定为 `qwen35-9b-q4km`，服务固定为 `IoTSharp/Tomur`，它仍是计划候选，未完成身份审计。`validate-catalog` 接受包含候选身份、服务、revision、许可证和状态的 JSON；状态为 `unverified/blocked` 时工具返回退出码 3 并生成 `blocked_unverified` 报告。声明 `verified` 需要 http(s) loopback endpoint 与条款证据字符串；拒绝非本地地址、URL 内用户名/密码和 fragment。该检查不证明声明真实：catalog 报告 schema 2 始终输出 `independently_verified: false`，声明通过的状态为 `valid_declared_verified_not_independently_verified`。不能用该状态替代服务或许可审签。

`validate-records` 最多读取 32 条、每条最多 **65536 UTF-8 字节**的 JSONL，支持 CRLF 和文件开头单个 UTF-8 BOM，拒绝无效 UTF-8。默认总时限 30 秒（可在 100–120000 毫秒内调整），逐条检查取消并输出 elapsed/progress。文件大小先限制在 `32×(65536+2)+3` 字节以内，以有界快照同时计算 hash 和解析，避免 hash 与内容来自两次读文件。空文件、超大文件、超过 32 条和无效编码是整文件错误，不生成报告。

每条记录要求唯一 `record_id`、`split`（只允许 train/development/calibration/audit，禁止 `sealed_test`）、来源与 `family_id`、教师模型 ID/revision、request/prompt SHA-256。同一合法记录的 family 不得跨 split。进入 `accepted/rejected` 的响应还必须带 response SHA-256 和人工审核 ID；任意状态中如果提供 response SHA-256，都检查其格式。可选字符串提供错误类型也会拒绝。catalog 与 records 都拒绝任意深度的重复 JSON 属性，包括 Unicode 转义后相同的名字，避免最后一个字段覆盖身份或审核状态。无效记录不会占用合法记录的 ID 或 family。

即使结构检查通过，报告仍标记 `valid_pending_human_review`，`teacher_labels_permitted_for_training` 为 false；人工审核、模型许可、资源用量、提示与响应内容的审签和训练准入必须在后续独立记录中完成。records 报告升级到 schema 3，分母满足 `total = pending + accepted + rejected + failed + abstained + invalid`：rejected 仅表示声明为人工拒绝的合法记录，格式错误单独计入 invalid，保留其他可解析记录。无 invalid 时退出码 0（包括合法的人工 rejected），存在 invalid 时退出码 3、状态 `invalid_records_present`。这是格式检查结果，不能解释为教师准确率或训练准入。

报告采用 `FileMode.CreateNew` 写入，现有输出和输入不会被覆盖。致命错误退出码 1；取消/超时退出码 124。循环以字节数、最多 33 次记录读取及总墙钟双重限制，Ctrl+C 取消；无自动重试或联网。

针对性验证入口是 `tools/Sezika.TeacherTool/Verify-Contract.ps1`。先执行 `-MaximumCases 1` 极小试运行，再执行完整 24 组合成合同案例；外部工具进程每次最多 10 秒、工具预算 3 秒，验证脚本最多 32 项/240 秒，复用 `Invoke-BoundedProcess.ps1` 记录进程身份和清理结果。只使用合成字段和虚构审核 ID，完全不代表真实许可、人工批准或教师质量。脚本删除本次 GUID 目录内临时输入，保留 `.artifacts/teacher-contract-*` 下审计日志/报告。

本工具没有执行网络调用。候选本机安装、实际接口、错误语义、响应格式、许可和资源预算仍是 S4-10 的阻塞条件；完成工具构建不代表教师可用或任何质量结果已验收。
