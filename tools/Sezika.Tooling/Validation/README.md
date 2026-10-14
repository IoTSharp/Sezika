# 校验工具

所有新回执都区分固定历史输入、synthetic schema/protocol fixture、实际 native 子进程、当前模型数值/质量/校准/性能与部署验收。不存在由 C# 生成一份自有 reference 后冒称独立 Oracle 的路径。

`Sezika.Tooling validation <命令> --request <typed-json> --output <new-receipt-json>` 接 profile、summarize、data-admission、language-audit、batch、near-ties。`ValidationJsonContext` 使用显式类型与 source generation；输入 JSON 禁止重复字段，测量数值不接受 string 或 Boolean。输出采用 CreateNew。

**输出路径与退出码**

请求和新回执 JSON 使用 PascalCase；历史业务输入继续使用各自原 schema。`Output` 必须是绝对路径，并在打开业务输入或运行操作前与 CLI 绑定：profile、summarize、data-admission、near-ties 必须等于 CLI `--output`；language-audit、batch 必须等于 CLI `--output` 加 `.artifacts`。目录操作先写各自的 `manifest.json` 或 `report.json`，CLI 读回并核对完整 typed result，再以 CreateNew 写外层回执。文件操作已经写过回执，CLI 只读回，不进行第二次 CreateNew。

| typed report 的明确状态 | CLI 退出码 |
| --- | ---: |
| `PASS_HISTORICAL_PROFILE_CONSISTENCY_ONLY`、`PASS_HISTORICAL_S5_REPORT_CONSISTENCY_ONLY` | 0 |
| `original_fixture_audit_not_independent_quality_acceptance`、`PASS_BOUNDED_NATIVE_VALIDATION`、`COMPLETE_HISTORICAL_MARGIN_OBSERVATION_ONLY` | 0 |
| `PARTIAL_OR_FAILED_VALIDATION_PRESERVE_RECEIPTS`、`PARTIAL_HISTORICAL_MARGIN_OBSERVATION`、`blocked` | 1 |
| `checks_passed_pending_human_acceptance` | 3 |
| 未知 typed report 或未知状态 | 2 |

退出码 0 只表示相应表项的检查通过，不能提升历史报告、原始 test fixture 或 asset preflight 的证据范围。`blocked` 不会返回 0；即使 synthetic 协议数据写有 `Approved` / `Sealed`，pending-human 也仍返回 3。`RequireAdmission=true` 的拒绝保留已写出的原始 blocked 回执。输入异常由外层错误处理保留失败，不能冒充上述业务状态成功。

**2026-10-14 的验证范围**

冻结候选 `validation-candidate-v2` 的 Windows/Linux 操作检查各 13 项通过；真实 CLI 检查在 Windows/Linux JIT 与 Windows/Linux NativeAOT 工具上各 4 项通过。AOT CLI 检查的测试驱动仍是 JIT，日志中的 `native_aot=False` 属于驱动；目标工具身份由实际子进程 argv 和独立 AOT publish 回执绑定。13 项操作检查中的注入式 batch fixture 仍标 `SyntheticTransport=true`，不能代替真实进程执行。

随后使用同一 Windows NativeAOT 工具完成真实 batch 目录 tiny1 / full2：显式 `AssetPreflightOnly=true`，固定当前 dotnet、IndependentModelTool assembly 和历史 synthetic 输入 SHA；分别精确要求 `independent_model_not_installed` 与 `independent_manifest_invalid`。外层 CLI exit 0、每个预期拒绝子进程 exit 1，输入输出完整，cleanup PASS / remaining 0。full2 的内部目录回执与外层回执原字节 SHA 相同。这里 `SyntheticTransport=false` 表示实际调度进程，输入依然是 synthetic asset，`NewInference=false`。

随后 Linux 真实 batch 目录 tiny1 / full2 已执行通过，同样只做 synthetic 资产预检；真实模型或 GPU 校验保持 `NOT_RUN`。最终 canonical 接线与新执行器的 Linux/AOT 绑定仍由主集成者验收。完整证据、原始格式失败及冻结身份见 [T6 最终准备证据](../../../docs/evidence/tooling-validation-final-2026-10-14.md)与[结构化拒绝补充](../../../docs/evidence/tooling-validation-json-target-2026-10-14.md)。

候选 v4 新增结构化非零退出兼容：原13项操作检查保留，新增2项（总15），覆盖无目标stderr的精确exit3／JSON计数／布尔输出，以及错误数值类型、数值、布尔类型、布尔值和同时声明的错误stderr。候选 v5 已用恢复的私有 Windows SDK 构建并实际通过 tiny1、操作15、CLI4及 baseline32；项目不锁SDK版本，回执记录实际工具身份。操作检查仍使用明确synthetic transport与实际写出的输出文件，不能冒充native TeacherTool执行。此前 `validation-normal-directory-v1` 的真实CliChecks正控在其原第12项失败，失败原件保留，不改无关CliChecks、不计为通过。

旧 `Verify-Contract.ps1` 实际有24项。实际矩阵保留 `v1-original24` 的原夹具／期望，并另列 `v2-corrected24`：仅将第20／21项synthetic catalog的model值由 `qwen35-9b-q4km` 更正为当前明确合同要求 `qwen3.5-9b-q4-k-m`，保存前后bytes／SHA与理由；id、status和independently_verified=false语义不改。两版均 `StopOnFailure=false`，先tiny1再24项；原版实跑22 PASS／2 FAIL，修正版24 PASS。目标为已固定SHA的既有 TeacherTool DLL，这不代表从当前源码重新构建，也没有调用真实教师或模型。原版两项失败与全部回执保持，不构成模型许可或人工标签批准。

| inventory 中的旧校验入口 | 新实现与证据边界 |
| --- | --- |
| `Sezika.Benchmarks/ValidateProfileChecks.ps1` | `ValidationChecks` 的四份归档 positive、错误突变矩阵和 duplicate JSON；没有新 benchmark 或新速度声明 |
| `Sezika.Evaluation/Invoke-CaptureChecks.ps1` | `ValidationBatch` 的固定 exe/assembly/input SHA、复制输入、typed mutation、targeted stderr 与输出读回，复用现有 Evaluation `--score-capture` 规则 |
| `Sezika.Evaluation/Invoke-OracleBatches.ps1` | 新 native ProcessExecutor + `ValidationBatch` 的 frozen-reference 比较/核验；Python reference 生成执行退休。实际捕获由现有 C# OracleCapture 在另外授权的运行范围执行，本地编码不冒称 GPU/质量验收 |
| `Sezika.Evaluation/Prepare-LanguageAudit.ps1` | `LanguageAudit`，保留原四文件三条 test fixture、input/label/candidate 顺序和 Boolean 映射，新版本 SHA；不是独立语言质量测试 |
| `Sezika.Evaluation/Verify-BaselineChecks.ps1` | `ValidationBatch` 保留 positive control、所有未执行分母、显式复制/错误突变/hash 绑定/拒绝输出，复用现有 C# BaselineVerification 强规则；不另写一套质量验收器 |
| `Sezika.IndependentModelTool/Verify-Preflight.ps1` | `ValidationBatch(AssetPreflightOnly=true)` 的 targeted 负向 synthetic asset 检查，必须固定 native tool SHA 与 stderr；不加载真实模型、不是推理验收 |
| `Sezika.NumericParity/Observe-QualityNearTies.ps1` | `NearTies`，固定 plan/index/capture/来源身份，574 原分母、失败/未处理、0.001 threshold 与前18 followup；只观察已有 raw logits |
| `Sezika.TeacherTool/Verify-Contract.ps1` | `ValidationBatch` 运行现有 native TeacherTool 的合同检查，固定输入/错误类型/输出/pending 分母；没有真实教师或金标批准 |
| `Summarize-S5.ps1` | `Summarize`，source/run/RID/backend 不池化，原 sample 与 nearest-rank、alignment、CUDA unload 以及指定历史 matrix 一致性；没有当前性能或 AOT 证明 |
| `Validate-S4Data.ps1` | `DataAdmission`，固定 source/raw/evidence SHA、许可与人工审核、来源 exposure、family/entity/近重复/衍生 ancestry、sealed test；问题是 blocked，审批不是自动发生 |
| `Validate-S5Profile.ps1` | `Profile`，原 schema3 matrix/拒绝分母/输入 SHA/测量数值/marker/token/独立 stage/coverage 完整校验；只是历史报告一致性 |

`ValidationBatch` 执行固定项目白名单的 C# native apphost，或固定 SHA 的 dotnet + managed assembly；不允许 interpreter 或 Python Oracle。负例必须先有实际成功的 positive control；无对照的独立模型负向 asset-preflight 仅在显式 `AssetPreflightOnly` 下允许。非零 `ExpectedExitCode` 预检必须有非空targeted `ExpectedStandardError`，或显式 `MustExist=true` 输出的精确JSON状态／字段约束；空泛stdout、只给exit、空字典和声明不存在的输出均不能成为结构化拒绝目标。所有已声明的stderr及JSON检查都须通过，JSON不会兜底放行错误stderr；asset-preflight继续强制targeted stderr。每个 case 最多256个 argv、1024个冻结输入、128次 JSON 数据突变、128个声明输出和1..1800秒；总case<=64、复制字节<=512MiB。复制字节按整个 batch 累计，不会在下一个 case 归零；ProcessExecutor负责身份/子树回收。

`ValidationOutput` 在原四个参数末尾增加可选 `JsonEquals` 字典（旧请求无需改动），约束同一个required输出JSON的顶层字面字段名。非null时必须1..32字段，每个key非空且不超过256字符，值必须为已定义JSON。比较同时要求准确JSON类型和值，数值字符串不能冒充数值，字符串 `"false"` 不能冒充Boolean false。已有 `JsonStatusProperty/JsonStatusValue`、输出存在性、字节界、SHA读回、native exit／cleanup／EOF均保留。例如：

```json
{"RelativePath":"report.json","MustExist":true,"JsonStatusProperty":"status","JsonStatusValue":"invalid_records_present","JsonEquals":{"total":2,"pending":1,"invalid":1,"teacher_labels_permitted_for_training":false}}
```

突变只作用于 CreateNew 复制的 task-owned JSON，保留每次修改前 bytes。`set` 是 JSON value 替换；`bind-sha256` 是绑定明确 task-owned 文件；`duplicate-root-property` 专用于拒绝 fixture。`{case_root}` 只替换为本case目录，没有脚本、命令字符串或代码求值。预期失败仍需 exact exit、PASS cleanup、完整 drain、明确stderr或required结构化JSON拒绝及全部独立输出检查；其余 case 保持 UNPROCESSED。

注入的 protocol/schema executor 只用于显式 synthetic fixture，报告 `SyntheticTransport=true`。真实 native batch 必须有 PID、launcher、process records、cleanup 与实际输出读回，不能由 synthetic pass 代替。模型回执与独立参考来源保持历史 SHA；Python exporter 与历史 evidence 原件保留并退出执行，不改旧 SHA、不伪造新 reference。

以下四个历史入口退出当前执行：`Sezika.IndependentOracle/export_oracle.py`、`Sezika.LayaOracle/Invoke-LayaOracle.ps1`、`Sezika.LayaOracle/export_oracle.py`、`Sezika.LayaOracle/export_tokenizer_boundaries.py`。对应 requirements、source-lock、contract、capture/tokenizer fixture 及历史来源记录继续保留为原始数据和证据。新的独立 C# 参考生成使用 [Sezika.ReferenceTool](../../Sezika.ReferenceTool/README.md)，明确标记 `port-derived`、`upstream_executed=false`；本验证批次本身不生成参考，生产推理不能冒称独立来源，模型质量与完整数值对齐另行验收。
