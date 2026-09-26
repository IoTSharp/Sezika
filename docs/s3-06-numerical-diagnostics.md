# 修正输入路径的逐层与近并列诊断

S3-06 的 `--trace-oracle` 模式补充生产调用身份、内部逐层定位和冻结输出门槛。[本轮续实现](evidence/s346-continuation-2026-09-26.md)已完成18个核心案例、54个三后端输出合同检查和972次内部完整张量诊断，首次超时与同程序补齐分别保留；新增跨报告汇总自检后共22项通过。全量质量参考发现tokenizer的added-token缺陷，修复后另有受影响例三后端真实回归。574条预定参考近并列观察命中0，独立逐层张量/阈值及完整S3-06门槛仍未通过。此前[四条所选诊断](evidence/s3-diagnostics-2026-09-26.md)继续作为历史证据；构建、自检或内部一致不能代替独立层oracle、质量、Native AOT或性能结果。

## 输入与输出合同

2026-09-26 续实现将单次显式选择上限扩展为 18 条，仍顺序执行三后端并受总期限约束。schema v2 增加 `planned_backend_runs`、`passed_backend_runs`、`backend_coverage`、固定 18 格 `core_coverage` 和 `core_matrix_passed`。格子来自冻结核心 fixture 的 ID，不从题文猜语言；未选择、未完成和失败项都保留在分母。`core_matrix_passed` 只表示这 18 格输出/内部诊断完成，不改变独立层 oracle、真实近并列及完整阶段门槛。

所有选择在模型加载前预检。开始执行后若取消或异常，写出 `status=incomplete`、原始错误、已经观察的 backend 结果和 `unprocessed_case_ids`，不把当前未完成的 backend 写成成功；输入身份预检失败不生成诊断报告。报告仍以 CreateNew 保存。协作取消后的报告写入不复用已取消 token，外层 runner 仍约束整个进程与清理期限。

`--trace-summary <reference-sha256> <contract-sha256> <report-index.json> <new-output.json> <1..60 seconds>` 汇总多轮已保存的核心矩阵观测，不执行模型。索引 schema 为 `sezika.trace-report-index.v1`，`reports` 中每项为 `path`、`sha256`；路径相对索引解析，最多 64 份，每份 4 MiB、合计 64 MiB。仅合并 schema v2 且 reference/contract/全部程序哈希相同的报告，并核对重复 case 的实际输入。54 个固定 case/backend 格子必须分别有通过记录；任一已记录的失败尝试都会使对应格子失败，不能用后来成功覆盖它。被总期限取消而没有产生 backend 结果的格子可以在后续固定输入运行补测，原报告仍列为 incomplete。完整核心覆盖与单轮完整成功是不同字段，完整 S3-06 门槛仍为 false。

命令显式接受完整 reference SHA-256、contract SHA-256 和 1–18 个 case ID，不扫描题集寻找有利样本，不修改原始参考。校验模型、revision、权重、tokenizer、contract 身份及唯一 ID，限定 reference 文件 32 MiB、contract 文件 1 MiB、原始参考最多 64 条。仅选支持范围内的 answered case；非法输入和既有四条候选数量合同差异仍由完整 OracleCapture/OracleCompare 验收。

通过生产请求解析器加载展开输入，显式 `laya_compatible`。共享 `PromptSequenceBuilder` 重新生成 256-token 前缀内容预算、1024-token 整体预算的输入，并与冻结参考的 tokens、markers、候选标签逐项核对。包装 pipeline 再断言类型化引擎实际送入后端的输入相同，避免比较器检查的是未被模型使用的序列。

输出同时核对实际类型化返回值和 backend marker logits，按原冻结公式 `abs(actual-reference) <= absolute + relative*abs(reference)` 验收：logits `0.0005 + 0.0001*abs(reference)`，概率 `0.0001 + 0.0001*abs(reference)`，Score `0.001 + 0.0001*abs(reference)`。Choice/Boolean 预测要求相同；概率有限、位于 0–1 且总和满足既有门槛。

## 逐层证据的范围

每个 case 顺序执行 scalar、SIMD、CUDA，逐层比较基准是该次 scalar 的完整张量，不是独立 Python oracle。保存以下 checkpoint 的元素数、最大绝对误差、RMS 误差、最大相对误差、最大误差位置、两侧张量哈希：

- embedding normalization；
- 每个 encoder layer 的 residual hidden；
- encoder final normalization；
- 两个 decision-head layer 的 residual hidden；
- scorer 每个 token 的 logits。

checkpoint 必须全部出现、不能重复，元素数量须严格符合模型维度。每个元素都参与有限性与误差检查，不能通过 `Zip` 截断掩盖维度不同。张量哈希表示本机 FP32 字节，仅用于对应本次记录；跨架构验收仍依赖数值比较。

内部 CPU 的 `scorer/dense` 记录 GELU 后值，CUDA 的同名记录 GELU 前值。本工具选择上述语义一致的 checkpoint，不将相同名字误认为同一计算阶段。不改变生产 trace API。

逐层尚无独立上游张量及预先冻结的层级阈值，因此只报告诊断量，不给逐层误差通过声明。`selected_diagnostics_passed` 由所选输入和 marker 输出合同、有限 trace 与完整 checkpoint 共同决定；不能据此将 S3-06 全部标记完成。

## 近并列与缺口

本轮全量 PAWS/Nimble 参考捕获也作为预先固定的边界观测集合：保持完整 574 题分母和来源顺序，沿用 `top-two raw-logit margin <= 0.001`，记录全部命中及失败，不调整阈值或提示。若有命中，按来源顺序取最多 18 条用于额外三后端诊断；这是已公开审计集上的数值边界定位，不是语言质量或新的封存测试。零命中仍记未测，不能以扩大样本量代替真实边界证据。

`tools/Sezika.NumericParity/Observe-QualityNearTies.ps1` 接收 `PawsRunDirectory`、可选 `NimbleRunDirectory` 和全新 `OutputPath`，读取冻结观察计划及 hash 绑定参考索引。默认最多观察 574 条、两来源各最多 8 批，每批最多 46 条，30 秒内部期限；`MaxObservedRows=1` 用于极小正例，完整分母仍为 574。报告保留每条 margin、失败/未处理数量与最多 18 条固定顺序后续项。

近并列依然采用参考 top-two raw-logit margin ≤ 0.001。预测变更始终失败，即便每个 logit 的误差仍在容差内。报告真实近并列数量，数量为 0 时明确 `not_measured`，不会把非最大候选的接近、固定标签、合成 logits 或 reference 自比较当作真实边界样本。

当前冻结 46 条参考已报告近并列数量为 0。补足此门槛需要提前冻结独立的候选输入集，在用户授权的有界真实参考执行中测量，保留全部探测结果；不能先观察模型输出再改写验收阈值。单次最多选择 18 条，可以覆盖核心矩阵，但独立层参考和近并列仍须分别验收，因此 `full_s306_gate_passed` 固定为 false，完整阶段状态仍由路线图汇总。

## 资源与验证

每个 case 只保留 scalar snapshots，预估字节数为 `(encoderLayers + headLayers + 2) * tokens * hiddenSize * 4 + tokens * 4`，上限 128 MiB；在启动推理前拒绝超限维度。当前后端回调的临时数组、CUDA 读回、模型权重与推理工作区不计入这项快照预算，也不能从该预算推导进程峰值内存。

每个 checkpoint 在回调时检查取消，张量计算和哈希每 4096 元素检查一次；最多 18 case × 3 backend，整体期限 1–1800 秒、单请求最多 300 秒。使用 `tools/Invoke-BoundedProcess.ps1` 保留 PID、启动时间、命令、父链、外层期限与任务子树回收。输出路径必须新建，失败或取消保存明确 incomplete 的部分报告；stdout/stderr 仍保留已知失败阶段。

`--trace-self-test` 不加载模型，使用明确的合成诊断值检查冻结公式、近并列预测翻转、坏概率、形状/非有限错误、trace 完整性、快照上限、取消和 source-generated JSON。它证明工具拒绝错误输入的能力，不证明模型数值或语言质量。

2026-09-26 在 PowerShell 7.6.6 下完成隔离 Release 构建，0 warnings / 0 errors；`--trace-self-test` 19 项通过。构建使用 `dotnet build tools/Sezika.NumericParity/Sezika.NumericParity.csproj -c Release --artifacts-path .artifacts/build/s3-diagnostics-20260926 --nologo --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false`，runner 上限 180 秒；自检上限 30 秒。最终 runner 记录为 `20260925-163812-108-s3-diagnostics-final-build.*`（PID 62076，exit 0）和 `20260925-163821-175-s3-diagnostics-final-self-test.*`（PID 45288，exit 0）。真实单条 smoke 为 `20260925-164033-569-s3-diagnostics-real-short-smoke.*`（PID 67784，exit 0，runner 上限 210 秒），均无清理错误。结果与 12 个过程文件已[逐字节归档并建立哈希索引](evidence/s3-diagnostics-2026-09-26/execution-index.json)。

追加三条中长诊断沿用上述 DLL、冻结 reference/contract 和原容差，事先固定 `boolean-zh-medium,score-en-long,choice-zh-long`，仅单次运行。过程记录为 `20260925-173724-784-s3-diagnostics-real-medium-long.*`，PID 78820，exit 0 / `Succeeded`；runner 1750 秒、工具 1700 秒、单请求 300 秒上限，result 记录实际耗时 423.381 秒，清理错误为 0。9 次输出 oracle 比较全部通过，最大 marker-logit 绝对误差 `8.749961853027344e-5`。SIMD/CUDA 共 162 次完整张量比较且 0 缺失；最大内部层级差异 `0.156524658203125` 位于中文 Choice 长输入的 CUDA `layer/17/hidden`，没有独立层 oracle 或冻结逐层阈值，故不将其记为层级数值验收通过。

本次 `near_tie_cases=0`、`near_tie_acceptance=not_measured`、`full_s306_gate_passed=false`。报告和四个过程文件与原文件逐字节核对后追加归档，证据索引现有 18 项。此运行与另一项 CUDA 质量验证有重叠且启用了全量 trace，耗时不作性能基准，普通 .NET 执行也不计入 Native AOT 验收。
