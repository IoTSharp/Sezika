# S4-07 / S4-12 独立 head 训练文件流程

本轮实现 `train-original` 的 checkpoint 保存、严格恢复和推理 head 导出。复用纯 C# `IndependentMarkerHeadTrainer`、`MarkerTrainingCheckpoint`、`IndependentMarkerHeadAssetStore`；冻结 encoder，只更新三题型线性 marker head。该流程不包含 encoder 反向传播、GPU 训练或优化器动量，优化器合同固定为 `sgd_v1_no_momentum`。

训练只接收既有最多 32 条 train/development 原创开发记录合同。这是 `development_smoke_only` 工具路径，不能替代正式数据逐条人工准入、家族隔离、独立封存、教师身份/许可审核或发布质量验收。当前工作区没有真实独立权重；本轮检查用 tiny、预计算全序列 marker features，其报告标为 `fixture_contract_only` / `synthetic_fixture`，`uses_real_independent_encoder=false`、`represents_quality_acceptance=false`。该检查不能证明真实 encoder 数值、训练收益、语言质量、校准、CUDA 或两 RID Native AOT。

## 新训练与恢复

所有输出路径必须新建且彼此不同；不覆盖用户 checkpoint、head 或报告。原始 `train-original` 参数扩展为必须显式指定 `--checkpoint`，其余默认预算保持小规模开发 smoke。

```text
Sezika.IndependentModelTool train-original
  --package <independent-package>
  --records <records.jsonl>
  --checkpoint <run-1.checkpoint>
  --head <run-1.head>
  --report <run-1.training.json>
  --seed 20260930
  --max-steps 24
  --learning-rate 0.05
  --training-timeout-seconds 120
  --timeout-seconds 600
```

`--max-steps` 为全局累计完成步数终点，范围 1–10000；`--training-timeout-seconds` 为训练器预算，范围 1–600；`--timeout-seconds` 为整个 CLI 运行预算，范围 1–1800，包括模型加载和全序列特征导出。每条特征导出报告进度，SGD 至少每 5 秒及结束报告进度，Ctrl+C 取消；无重试或后台进程。非法、未知、重复或缺值参数直接拒绝，非法可选值不会回落默认配置。学习率为有限 `(0,1]`，seed 为有符号 32 位整数。

```text
Sezika.IndependentModelTool train-original
  --package <same-independent-package>
  --records <same-records.jsonl>
  --checkpoint <run-2.checkpoint>
  --head <run-2.head>
  --report <run-2.training.json>
  --seed 20260930
  --max-steps 48
  --learning-rate 0.05
  --resume-checkpoint <run-1.checkpoint>
  --resume-sha256 <run-1 checkpoint_sha256>
  --resume-report <run-1.training.json>
  --resume-report-sha256 <run-1 report_sha256>
```

四个 resume 参数必须同时给出。恢复首先验证调用者固定的报告文件 hash，再通过生产 checkpoint loader 绑定 model、encoder、tokenizer、manifest、完整 train feature 序列/token IDs/marker positions/labels/features 和 checkpoint hash。报告另外绑定整个 records JSONL（包含 development split）hash、训练/开发数量、seed、learning rate、已完成步数及 last loss。即使 train features 不变，development records 文件改变也不能沿用恢复报告。恢复总终点低于已完成步数拒绝；等于已完成步数只验证并导出，不额外执行更新；恢复到更大总终点只执行差值步数。恢复顺序从原累计位置继续，同 seed、同数据和同学习率产生与连续训练逐位一致的权重。

报告和 checkpoint hash 是 CLI 成功输出的 `report_sha256` / `checkpoint_sha256`，调用者需保存到可信实验记录；SHA-256 验证身份和完整性，不是来源签名或数据许可证明。

## 版本化产物

一次成功运行新建三种文件：训练 checkpoint (`SZMK1`)、推理 head (`SZMH1`) 和 source-generated JSON 训练报告 v2。保存后通过两个生产 loader 重新读取校验，报告最后以原子 rename 发布；成功报告的存在意味着本次 checkpoint/head 发布和重加载完成。失败或取消清除本次已成功创建且当前字节 hash 仍匹配的产物及本次唯一临时报告，保留原有或被其他拥有者替换的文件。强制进程终止可能留下已发布 checkpoint/head；没有最终报告时不把该目录视为完整运行。核心 store 自身也使用相邻唯一临时文件和原子 rename。

v2 根字段包括 `schema_version`、`status`、`identity`、`records_sha256`、`feature_sha256`、`head_sha256`、`checkpoint_sha256`、`seed`、`learning_rate`、`step_semantics`、`initial_steps`、`max_steps`、`completed_steps`、`last_loss`、`train_count`、`development_count`、`uses_real_independent_encoder`、`represents_quality_acceptance`、`optimizer`、`feature_source`、`training_timeout_seconds`、`elapsed_milliseconds`、`resume` 和 `development_groups`。所有字段必须存在，包括未恢复时显式的 `resume:null`。

`identity` 包含 `model_id`、`encoder_sha256`、`tokenizer_sha256`、`data_manifest_sha256`、`hidden_size`、`protocol:decision-v1`、`length_policy:strict`。`resume` 记录上一次 checkpoint/report SHA-256 和累计初始步数；`development_groups` 固定为最多中英三题型六组，每组记录实际 `count`、`correct` 与 `accuracy`。这些 accuracy 只作开发 smoke 指标；SGD last loss 为最后一次更新前的训练样本交叉熵，不是全数据 loss 或正确率。

`WorkflowTrainingReport.ReadBounded` 要求调用者提供报告 SHA-256；`ParseBounded` 拒绝重复/未知/缺失字段、错误 JSON 类型、未知版本、身份/预算不符、非有限数值以及错误指标分母。JSON 上限 1 MiB、深度 16、4096 个 token、解析 10 秒，可取消。`evaluate-original` 继续读取历史 v1 开发 smoke 报告，新报告通过严格 v2 parser；两个路径消费同一次最多 1 MiB、持只读 lease 的文件快照，不在版本判定后重新打开文件；fixture 报告不能冒充 original encoder 评价报告。`train-original` 的 records/manifest 输入在读取、hash、特征导出到产物发布期间持 `FileShare.Read` lease，使并发写入在支持此文件共享约束的平台上被拒绝。

v2 评估适配保留完整报告，通过公共 `BindIdentity` 将七项 `MarkerFeatureIdentity` 字段逐项与实际已加载 encoder/数据合同核对，再将报告原始 identity 交给生产 head loader。合法但不同的 `hidden_size` 也须拒绝，不能先用当前模型宽度重建 expected identity 而丢失报告断言。该边界不启动或替代 encoder。v1 仅兼容既有历史 `development_smoke_only`：它没有 `hidden_size`/`protocol`/`length_policy` 完整报告 metadata，使用当前明确 `decision-v1`/`strict` 的 loader 合同及实际宽度加载既有 head，不宣称具备 v2 的完整报告身份校验；v1 不能用于恢复，新实验必须输出 v2。

独立 CLI 的 `--head-binding` 可直接使用这个 v2 训练报告，提取完整 `identity` 和 feature/head hash；无需另写小绑定文件。推理 head 不带可执行代码或 encoder 权重，也不改变核心无业务动作执行器边界。

## 有界检查

`IndependentTrainingWorkflowChecks.Run` 在 15 秒总取消预算内，以三题型 tiny features 验证：两步保存后恢复至累计四步，与连续四步的权重/checkpoint/head 文件 hash 完全相同；生产 head loader 重加载；错误 checkpoint/report hash、records/manifest/seed/LR 和过小终点拒绝；不覆盖/路径碰撞；duplicate/unknown report JSON 拒绝；取消不发布报告；输出被另一拥有者抢占时回滚本次 checkpoint 并保留对方文件。测试临时目录使用唯一 GUID，finally 只删除核对过绝对路径和前缀的本次目录。

v2 评估回归直接消费上述实际 workflow 生成的报告，把 `hidden_size:2` 改成仍符合语法范围的 `hidden_size:1`，验证 parser保留该字段而公共 `BindIdentity` 在生产 head 加载前拒绝；同时覆盖 protocol 和 length policy 不匹配。该检查没有构造假 encoder，也不新增替代推理路径。

主监督者负责串行构建和运行共享测试入口，长命令通过 C# `ProcessExecutor.ExecuteAsync`／`process run` 的 `ExecutionRequest` 明确原生程序、结构化 `ArgumentList`、工作/新输出目录、数量/墙钟与取消边界，日志归 `.artifacts/supervision-20261007/training`；具体接线见[当前工作流](tooling-current-workflow.md)，通过数和运行记录以监督者实际验证日志为准。本文件记录工具合同，不预先声明构建/运行通过。
