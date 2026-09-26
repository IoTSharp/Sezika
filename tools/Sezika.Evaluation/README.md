# 真实模型质量审计

该工具加载本地固定模型，通过 C# encoder/head 和明确选择的 scalar、SIMD 或 CUDA Driver 后端执行逐题推理，再使用输入文件中的目标标签计算指标。外部参考只用于离线评分和比较，不进入本工具的生产推理路径。`--self-test` 仅验证合成统计与适配逻辑，不加载模型，也不产生模型质量分数。

## 真实推理

```text
Sezika.Evaluation <model-dir> <eval.jsonl> <new-output.json> <cuda|simd|scalar> <records> <timeout-seconds>
  [dataset-name dataset-total dataset-sha256 [strict|laya_compatible]]
```

保留原有 6/9 参数命令；第 10 个参数显式选择长度策略，默认 `strict`。`laya_compatible` 允许截断，必须连同诊断解释，不能与 strict 的回答覆盖率混为一谈。读取来源 JSON 时，题型属性的位置不影响适配，候选对象的原始顺序则完整保留；Boolean 的可选描述和显示标签使用核心 JSON 契约校验。

schema v2 报告绑定模型/revision、权重/tokenizer SHA-256、输入渲染版本、长度策略、完整来源文件 SHA-256、执行程序及静态程序集。每行记录原始 `input` JSON 字节的 SHA-256、真正交给 backend 的 token IDs（int32 little-endian）的 SHA-256、marker 位置、原始 logits、候选 ID、forward 次数和不含题文的输入诊断。概率与原始 logits 由同一次真实推理产生。错误行不会生成替代答案；strict 截断拒绝记录拒绝诊断，实际 forward 次数为 0。

语言只读取显式 `language` 元数据；没有此字段时使用 `unspecified`，不通过题目内容猜测语言。分组含题型、目标、领域、来源家族、语言及失败类型。Choice/Score 保留各自候选空间；Score 同时计算 argmax 正确性和零起点期望值 MAE。Boolean 指标包括混淆矩阵、正/负类召回、balanced accuracy、AUROC。Brier 使用分类概率向量的平方误差和，因此 Boolean 是两项误差和，不与单项二分类 Brier 混报。ECE 为 10 个等宽信心分箱。

`DatasetTotal` 是完整题集分母，`Processed` 是明确选择的前缀条数；`Coverage=Answered/Processed`，`FullDatasetProcessed` 必须另看。只跑 1/250 条且这一条回答成功时，不能宣称整套题集覆盖率 100%。已查看的 PAWS/Nimble 只用于审计，不用于训练、选提示、调阈值、校准或新的封存测试。

单次最多 10000 条、来源文件最多 64 MiB、每行最多 1 MiB/65536 JSON nodes、深度 32、每行只允许一个 `decision` 问题。总期限 1–1800 秒，Ctrl+C 可取消，真实推理使用原有每请求 30 秒期限；每 10 条输出进度。通过仓库 `Invoke-BoundedProcess.ps1` 保存进程 PID、父链、argv 和清理证据。每行结果不是性能 benchmark：计时包括诊断装饰器，可能存在其他验证负载。

输出使用 `CreateNew`。真实评测在开始前保留输出路径，中途加载失败、取消或 deadline 可能留下空文件；空文件和不完整 JSON 都不能作为有效测量，只能结合 runner 日志认定这次执行失败。成功输出前再次检查来源 hash。既有报告不覆盖。

## 独立参考与离线评分

新增全量分批入口，原单捕获命令保持可用：

```text
Sezika.Evaluation --prepare-oracle-batches <eval.jsonl> <sha256> <dataset-total>
  <records-per-batch:1..46> <new-directory> <seconds:1..300>
Sezika.Evaluation --score-captures <eval.jsonl> <sha256> <dataset-total>
  <capture-index.json> <new-report.json> <seconds:1..300>
```

准备操作保持来源顺序，生成完整、不重叠、含尾批的清单，每批最多 46 题、总计最多 256 批；`batches.json` 保存来源身份、偏移、每批 ID 和 manifest hash，全部准备完才写出索引。PAWS 250 题为 6 批，Nimble 324 题为 8 批。每个 manifest 继续受 1 MiB 上限约束，数据准备不执行模型。

`Invoke-OracleBatches.ps1` 在显式本地 Python、上游源码、模型和构建路径下，按固定清单顺序执行独立参考 → C# CUDA capture → 冻结数值比较，不自动安装、下载或重试。先以单条输入验证，再扩大范围。参数 `MaxBatches` 默认 1，`BatchOffset` 可选择预定区间，整批期限最多 1740 秒，每个子进程通过仓库 runner 记录身份并回收；外层也应使用 runner 并预留清理时间。`CancelFile` 支持批次间和 Python 参考内的取消，Ctrl+C/外层 runner 负责整体取消。输出目录必须新建，`run.json` 保留未完成状态与完整计划分母。数值差异保留对应比较报告并继续固定计划，捕获仍进入评分索引，整轮标为 complete_with_differences；输入/身份无效、执行失败或清理失败则中止。未处理项保留在完整数据集分母，不通过排除数值差异挑选有利样本。

评分索引 schema 为 `sezika.evaluation-captures.v1`，`batches` 各项包含 `capture`、`capture_sha256`、`manifest`、`manifest_sha256`，路径相对索引文件解析。读取总量最多 256 MiB。逐捕获沿用原始输入/预测/概率/失败校验，并要求完整覆盖对应 manifest 的有序 ID；跨批次重复 ID、不同 backend/长度策略/实现程序集/参考环境均拒绝。报告保存各批来源，不把汇总器自己的程序集当作历史推理程序集。缺批时 `Unprocessed` 和完整数据集分母保留，`DatasetAnswerCoverage=Answered/DatasetTotal`；`Coverage=Answered/Processed` 的既有含义不变。汇总评分本身不证明数值对齐，须同时审核每批比较报告。

已有完整独立参考可通过 `ReferenceIndexPath` 复用：逐批核对 manifest 和 capture hash，参考数值仍只交给独立比较器；C# capture 继续重新执行模型。此模式可在 tokenizer/实现修复后重测，保留原失败报告，避免重新生成参考造成基线漂移。索引在执行后再次核对；所选批缺少参考时直接失败，不回退下载或生成。

报告另含 `ByLanguageAndType`，语言及其他分组增加覆盖率、按全部处理题的准确率、Boolean 指标、Brier/NLL/ECE 和 Score MAE。没有显式语言仍为 `unspecified`，非法元数据直接拒绝。

`Prepare-LanguageAudit.ps1` 仅适配既有 S4-02 的 12 条中英 test fixture，不读取 calibration：保留 state、说明、标签和顺序，仅将旧 Boolean 字段名转为当前合同，保留每个源文件 SHA-256。先 `MaxRecords=1` 验证，再 `MaxRecords=12`；输出及许可见 [语言审计数据](../../datasets/s4-04/language-audit-v1/README.md)。这是原始小型 fixture 的真实推理审计，不能充当新封存测试或统计充分的语言质量证明。

先从固定来源准备最多 46 条原始审计输入，不进行模型运行：

```text
Sezika.Evaluation --prepare-oracle <eval.jsonl> <sha256> <dataset-total>
  <first-records:1..46> <new-manifest.json> <seconds:1..300>
```

该 manifest 可传给已有离线 Python exporter 的 `--cases` 参数。使用其固定源码、隔离依赖、现有权重和原冻结数值合同；导出执行仍须使用仓库有界进程工具。manifest 和原始 capture 含外部题文，保存在本地忽略目录，不能将它们当作本项目原创 fixture 提交。`authorship` 字段明确保留原来源许可边界。

参考 capture 生成后，通过 `Sezika.OracleCapture` 执行相同输入的真实 C# 推理，再由 `Sezika.OracleCompare` 核对输入身份、tokens、markers、候选、logits、概率及预测。不要把一个任意的 capture 文件当作数值已对齐的证明。

以下命令只给**已有真实 capture**计算目标标签指标，不重新执行模型：

```text
Sezika.Evaluation --score-capture <eval.jsonl> <sha256> <dataset-total>
  <capture.json> <new-report.json> <seconds:1..300>
```

离线评分核对固定模型、源码 revision、冻结合同、256/1024 预算，捕获完成状态及选中数量，并逐行重建原始输入。候选的顺序必须一致。重复字段/ID、未知输入、非有限数组、概率与温度 1 softmax 不符、预测与分布不符均拒绝；来源和 capture 哈希在读取/评分后再次核对。报告保存原始 provenance、capture hash、manifest/contract hash 和完整/选中数量，`MeasurementOrigin` 明确标记离线评分；`ForwardCalls=null`，不会伪装成这次评分执行了 encoder。只有相应原始捕获、进程记录和独立数值比较合起来，才能构成实测证据。

`Invoke-CaptureChecks.ps1` 先对未修改的真实 Boolean capture 执行一次成功基线，核对离线评分来源、处理数量与题集身份，再对本地副本执行 8 个负向用例：改概率、改预测、未完成状态、数量不符、选中 ID 被替换、失败行携带数值、成功行携带失败，以及重复 JSON 属性。每个负向用例必须以 exit 1 和该校验对应的精确 `InvalidDataException` 消息结束，且不产生评分报告；无关的路径、hash、依赖或启动失败不能充当测试通过。

变异文件只作软件测试，不能混入真实推理数据。总计最多 9 次，每次内部至多 30 秒、外部至多 40 秒，按 360 秒批次剩余时间缩短，并预留 runner 清理时间；不足以再执行时直接中止。每次进度直接显示，日志分置于输出目录的 `processes/<case>`，最多枚举四个 runner 文件；`checks.json` 保留实际进程结果、身份、路径与预期/观察错误消息。所有文件保留，不自动删除。
