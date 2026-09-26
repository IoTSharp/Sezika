# S5-06：短、中、长输入性能画像工具准备

本轮schema v3的[真实续验证](evidence/s346-continuation-2026-09-26.md)已完成四项正例及受控CUDA截止负例：long-32 end_to_end单样本38.319秒，独立分项未测；CPU long-1为42.183秒，long-32两遍估算超总预算而未启动。3进入/2完成/0正式样本及失败释放路径均有真实证据。下述旧矩阵结果继续按原构建身份保存。

状态：2026-09-25 已实现工具代码并迁移到 S3-09/10 共用的 `PromptSequenceBuilder`，画像报告升级至 schema v2。2026-09-26 已完成真实 CUDA 九行 × 三正式样本；SIMD 九行尝试中八行各测得一个正式样本，long-32 在 discovery 触发既有单请求 300 秒期限，完整失败报告已保留。见 [CPU/CUDA 实测与输入预检证据](evidence/s5-profile-2026-09-26.md)。本页说明采集口径；整体阶段状态以执行证据及 [ROADMAP](../ROADMAP.md) 为准，不能将 SIMD 矩阵写成全部通过。token 预检和真实 encoder/head 测量分别记录。

入口为 [Sezika.Benchmarks](../tools/Sezika.Benchmarks/README.md) 的 `--mode profile`，默认 `benchmark` 保留 S5-04/S5-05 短请求路径。后端仍为 scalar、SIMD、W8A32、CUDA，模型由既有 loader 检查固定版本和权重/tokenizer hash。报告位于 source-generated `BenchmarkReport.profile`，当前画像 schema v3（原报告为 v2）；旧基准字段保持独立。

报告的 `code_artifacts` 绑定实际进程可执行文件；普通 .NET 模式还在加载模型前对输出目录中三个固定应用程序集（`Sezika.Benchmarks.dll`、`Sezika.dll`、`Sezika.Cuda.dll`）记录路径和 SHA-256。任何程序集缺失即在身份阶段失败，避免仅保存 dotnet host/apphost 的哈希却无法识别真实实现。Native AOT 用原生可执行文件哈希标识代码，保持模型和 tokenizer 独立身份。

## 固定输入与渲染身份

2026-09-26 续实现的画像 schema v3 新增 `discovery_completed_forwards`、`instrumented_entered_forwards` 和 `instrumented_completed_forwards`。进入 pipeline 后，只有后端返回有限且形状正确的 marker logits 才增加 completed；中断中的一次调用不计完成，部分完成也不算整请求或正式样本。

`--profile-detail` 默认 `full`，保留独立分项采样；显式 `end_to_end` 用于将很慢的 CPU 长请求限定在输入 discovery 和正式 E2E 采样的窗口，分项状态写为 `not_requested_end_to_end_detail`，`cases_with_stage_timings` 为 0。数值 smoke、取消/恢复和资源检查仍执行。报告中的成功覆盖率只针对显式选定的测量范围，不能将 E2E 模式当作完整分项画像，或把另一行的分项时长填入本行。

画像专用参数 `--request-deadline-seconds 1..1800` 可显式设置每个多题请求的期限，默认仍为 300 秒；工具总期限仍最多 1800 秒，两者共同约束。核心 `DecisionResourceBudget` 的显式上限相应扩展为 30 分钟，核心默认 30 秒及 CLI 的 300 秒参数上限保持原值。较长预算能用于测量慢请求，本身不构成性能优化，也不能满足旧 300 秒预算。新旧测量须按实际预算分别解释。

成功及失败路径均在释放后记录 `cleanup`：CPU workspace、CUDA owned allocations/modules/release failures、单独的清理异常。`collection_status` 区分未测、回收成功和失败；通过弱引用观察本次确实加载的模型对象。原始推理错误不会被清理观测覆盖，取消后不会继续运行数值 smoke 或恢复诊断。进程被外层强制终止时，仍以 runner 的进程清理证据为准。

输入集 `sezika.performance-inputs.v1` 使用 state 文本 `The device is ready.`，以单个空格连接；short/medium/long 分别重复 4/20/100 次。每个长度选择 1、8、32 问，共最多九行，每题两个候选，按 Choice/Score/Boolean 循环，说明为 `type`、候选为 `yes`/`no`。单题预设从 Choice 开始；只有 8/32 问行同时包含三种 primitive。所有问题依旧逐题独立 forward，不称作并行 GPU batch。

预设是固定文字负载，不宣称准确等于某个 token 长度，也不是语言质量数据。每行完整请求 JSON 与 SHA-256、重复次数、候选数、渲染 token 总数和每题 token 数/marker/token hash 都进入报告。token hash 使用 int32 little-endian 序列的 SHA-256，保证编码口径明确。重复文本可能与自然语言真实长上下文的分布不同，性能结论只能限定在此输入集。

`rendering_version=sezika.prompt.laya-4066d5d5.v2` 标识共享 builder 的当前输入合同：使用 `choice / score / noul question:` 题型前缀，Choice 保持输入候选顺序，Score 保持数组顺序，Boolean 语义为 `false → true`，选项标签/结构化 JSON 与 mask 清理均由核心 builder 负责。工具不再复制一份旧版输入渲染实现。输入集文字虽仍是 v1，渲染版本已改变，旧画像结果不能用来证明当前实现性能。

预渲染使用核心 `PromptSequenceBuilder` 的 `laya_compatible` 策略，计算拟保留的 tokens、候选顺序和完整截断诊断；这次准备不执行 encoder，也不修改真实请求。每行 `runtime_prefix_token_budget` 记录模型 prefix 内容预算 256；`runtime_sequence_limit=min(default limit, encoder max)` 记录完整序列上限 1024。prefix 的 BOS/EOS 等固定开销不包含在 256 内容预算中，不能将完整序列与 256 比较。`original_total_tokens` 记录所有片段都未截断时的原始长度，`rendered_total_tokens` 记录兼容方案拟保留长度，`proposed_truncation` 标明是否有 token 丢失。

真实请求仍使用其 `length_policy`，当前固定输入默认 `strict`。只要兼容方案需要截断，strict runtime 必须拒绝；工具保留发现请求的真实拒绝。将来显式兼容请求可以执行拟保留序列，但必须连同截断诊断报告，不能把原始长度冒充实际推理长度。顶层 `request_token_budget=32768` 与 `request_deadline_seconds` 来自实际创建的 session budget（期限默认 300 秒，可显式配置）；工具总取消期限不会延长这个逐请求期限，长输入多题行可能在 token 合法时仍触发真实请求超时。

## 真实输入核对和失败

工具通过静态 `IMarkerDecisionPipeline` 装饰器观察真实 runtime 实际交付的 token IDs、marker 与 type，随后调用原 CPU/CUDA pipeline 取得真实 logits。发现请求与额外 instrumented 请求在 scoring 前逐项核对共享 builder 的预渲染结果，任何差异立即失败。strict 请求若意外接受需要截断的方案，也按渲染合同不一致处理。

`rendered_sequences` 表示兼容方案拟保留的预渲染，并带 `candidate_labels` 和 `diagnostics`；其中 `diagnostics.length_policy=laya_compatible` 仅描述诊断方案，真实请求策略在行的 `length_policy`。`actual_sequences` 仅记录发现请求中真正进入 pipeline 的 token/marker/type，pipeline 接口没有传递候选标签或诊断，因此实际序列的这两个字段为 null。若在 tokenizer/预算检查时失败，actual 可以为空；若前几题成功、后续题失败，actual 仅有真实到达 pipeline 的那几题，整行不计成功。失败调用经过 pipeline 也只证明提交了这些 token，是否成功完成还必须结合行 `status`/响应判定。有调用但发现请求未完成时，`rendering_verification=observed_pipeline_calls_request_incomplete` 只标识观察到调用，不提前宣称输入匹配或推理完成；成功核对后才改为 `exact_token_marker_type_match`，明确输入差异记为 `mismatch`。成功请求的 `usage.token_count` 必须与拟保留的渲染总数一致，pipeline 调用数必须等于问题数。

参数解析后先保存选中的长度/题数，模型身份核验、模型加载、CUDA 初始化和 cold 首请求之前构造全部输入计划。`planned_cases` / `planned_questions` 的分母直接来自所选矩阵，因此即使准备计划时取消，也不会因只登记了部分行而缩小分母。`request_coverage` 为完整测量成功行数 / 所有计划行数；`question_coverage` 为成功行问题数 / 所有计划行问题数。拒绝、失败、取消、尚未执行均保留在分母，它们不是准确率或拒答选择性风险。发现阶段真实 runtime 的 `decision_token_budget_exceeded`/`decision_token_limit_exceeded`（包括 strict 截断拒绝）记为 `rejected` 后继续下一行；其他异常、中途阶段异常和输入合同差异终止本轮并保存已有行。前置加载等异常也会把 `profile.status` 设为 `incomplete`，保留原始计划和零成功覆盖率。

每行资源快照分别捕获 RSS 与 CUDA 观测异常，写入 `resource_observation_errors`（观测阶段、资源、错误码、消息），未取得的数值为 `null`。前置快照失败会停止本行；后置快照失败不会覆盖正在传播的原始推理异常。若推理已完成但后置资源快照失败，该行由 `measured` 改为 `failed`，不计成功覆盖率，并终止采集；已有拒绝/推理错误保持自身原始错误，资源错误独立保留。全局进程 RSS 高水位的读取错误也会独立记录，仍尝试输出报告，不覆盖已有运行错误。

`profile.status=complete_with_rejections` 只表示计划行均已尝试采集且部分拒绝，不能用顶层工具 `passed` 推导长输入已支持。外部强杀仍可能来不及写最终 JSON，应保留有界 runner 的日志和进程身份记录。

## 计时与资源口径

| 字段 | 采集范围与限制 |
| --- | --- |
| `tokenizer_and_rendering_milliseconds` / allocated bytes | 独立运行一次共享 builder 的兼容预渲染，JSON 已解析；包括候选文本、JSON 序列化、截断诊断、列表和 token 数组，不是纯 tokenizer kernel 时间。 |
| `discovery_wall_milliseconds` / allocated bytes | 真实发现请求，包含 parse、tokenizer、pipeline 与 JSON 输出；装饰器核对输入，有 instrumentation，不能混入正式分位数；拒绝也保留本次时间。 |
| `end_to_end_milliseconds` / `latency` | 每行 1–30 个正式原始样本，关闭 token 捕获/计时及 CUDA event profiling；包含 UTF-8/JSON parse、tokenizer、逐题 encoder/head、typed response JSON。p50/p95/p99 为最近秩，小样本仅探索。 |
| `encoder_and_head_milliseconds` | 一次额外真实 instrumented 请求中所有 `pipeline.Score` 墙钟区间之和；不会用 E2E 减去估算 tokenizer 来推导。 |
| `encoder_milliseconds` / `head_milliseconds` | CPU 额外独立 pass：每题 `Encode` 后 `ScoreEncoded`。head 含保护性 hidden-state copy；与原流水线的缓存/分配路径有差别。CUDA resident 无独立 head 计时边界，置 null 且标 unavailable。 |
| `cuda_instrumented_forward` | 一次额外真实请求的既有 H2D/D2H 字节及 host 时间、CUDA event kernel 时间与 launch 次数；同步会改变调度。CPU 标 unavailable。 |
| working set / lifetime peak / CUDA snapshots | 每行前后 RSS/working-set 与进程生命周期高水位；CUDA owned peak 为最近一次 ResetTelemetry 以来的峰值，每次额外 instrumented 请求都会重置为当时 owned bytes；前后快照可能属于不同重置窗口，不能当作 context 生命周期累计峰值。free/total 属于全设备；不包含完整 Driver/context 内存开销。 |
| `allocated_bytes` | 正式样本前后进程范围的托管累计分配差；不等于存活对象大小，也不等于 RSS。 |

分项采自不同 pass，不能相加或相减组成另一种 E2E。CUDA 分项/CPU split 当前每行只采一次，没有独立分位数或稳定尾延迟结论。每行仍有真实 discovery 请求，设置 warmup 为零不会得到真正未预热的正式样本。模型加载、每轮原有 cold 三题响应、数值 smoke、取消/卸载检查与报告写入均在原有独立字段和边界内；整体 RSS 高水位包含这些阶段。

## 执行边界与尚待验证

所有新增矩阵/采样循环使用 `<` 比较并受明确数量上限约束：最多 3 个长度、3 个问题数、9 行、每请求最多 32 问、每题 2 候选；每行最多 1 次 discovery、5 次 warmup、30 个正式样本、1 次 instrumented 请求，CPU 额外最多 32 次 split。hash 循环保留最多 8192 token 的保护上界，当前实际每序列受 1024 总预算限制。全部继承现有 1–1800 秒运行取消期限与 Ctrl+C，长阶段前后及逐题检查 token，并输出行/预热/样本/分项进度。共享 builder 另有单次 10 秒期限，并把取消传入 tokenizer；外部硬墙钟期限与进程树回收仍由既有 runner 负责。

画像模式需要独立的极小输入及矩阵验证。执行对应授权范围时，先用 `--lengths short --questions 1 --samples 1 --warmup 0 --cycles 1` 完成极小验证，再分别运行所需后端/长度/题数，不在单次调用中无界重试。旧 `Summarize-S5.ps1` 未修改，不把旧矩阵汇总器当作 S5-06 验收器。

2026-09-26 tokenizer 预检实测单题长度为 short 36/40/37、medium 116/120/117、long 516/520/517（依次 Choice/Score/Boolean）；无截断，32 问 long 请求的 token 总和为 16566。正式性能仍需在其他 AOT/oracle 重负载结束后按顺序采集。当前构建输出可按以下两次有界调用先小后大执行 CUDA；每次都创建独立报告，模型无需下载：

```powershell
$profileDll = '.artifacts/build/s5-profile-20260926/bin/Sezika.Benchmarks/release/Sezika.Benchmarks.dll'
& tools/Invoke-BoundedProcess.ps1 -FilePath 'C:\Program Files\dotnet\dotnet.exe' -TimeoutSeconds 180 -LogName 's5-profile-cuda-smoke' -ArgumentList @(
    $profileDll, '--mode', 'profile', '--backend', 'cuda', '--model', '.artifacts/models/laya-mmbert',
    '--lengths', 'short', '--questions', '1', '--samples', '1', '--warmup', '0', '--cycles', '1',
    '--timeout-seconds', '150', '--cpu', '13th Gen Intel(R) Core(TM) i9-13900HX', '--environment', 'windows-local',
    '--output', '.artifacts/s5-profile-20260926/cuda-smoke.json'
)
# Only after checking the smoke report and returned resources.
& tools/Invoke-BoundedProcess.ps1 -FilePath 'C:\Program Files\dotnet\dotnet.exe' -TimeoutSeconds 1750 -LogName 's5-profile-cuda-matrix' -ArgumentList @(
    $profileDll, '--mode', 'profile', '--backend', 'cuda', '--model', '.artifacts/models/laya-mmbert',
    '--lengths', 'short,medium,long', '--questions', '1,8,32', '--samples', '3', '--warmup', '1', '--cycles', '1',
    '--timeout-seconds', '1700', '--cpu', '13th Gen Intel(R) Core(TM) i9-13900HX', '--environment', 'windows-local',
    '--output', '.artifacts/s5-profile-20260926/cuda-matrix.json'
)
```

SIMD 可将第一条命令改为 `--backend simd` 和独立的 `simd-smoke.json`。其完整矩阵宜用第二条命令按单一长度、单一问题数拆为最多九次独立调用，每次仍为 3 正式样本、1 预热、1 生命周期，内部 1700 秒、外部 1750 秒；先按 smoke 实际耗时决定下一行，整个批次应另设不超过九项、最多 4.5 小时的墙钟预算。每请求 300 秒依然有效，若某行超时保留失败及进度，不提高预算来改写覆盖结论。上述最大预算用于执行控制，不是预计延迟，也不是性能测量结果。

静态审阅核对 source-generated JSON、输入哈希编码、真实 pipeline 转发、循环上限、取消与进度、原始异常保留、分项 unavailable、显存/工作区清理与改动范围；`git diff --check` 仅用于空白检查，不表示构建或推理通过。S3-09/10 已使渲染版本改变，后续必须按 v2 在同条件重采集并保留旧报告；不能从本工具迁移推导优化收益、质量或长输入 AOT 通过。
