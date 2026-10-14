# 独立 mmBERT 离线数值比较器

本 C# / .NET 10 开发工具只比较已有 raw F32 文件，不运行模型，不引用 Sezika core 或 Laya 决策 JSON。输入为现有 `IndependentNumericCapture` 的完整267项metadata、独立原始权重上游参考、测量前冻结合同及监督者外供合同/导出脚本/上游源码SHA-256。首轮只读取25个合同选中checkpoint与marker，共26项；通过不代表完整算子、tokenizer、语言质量、训练、校准、AOT或性能验收。源码/程序hash与原始权重执行来源仍须监督者核对日志，比较器不把metadata自报当来源证明。

命令 `compare --actual <capture.json> --reference <reference.json> --contract <json> --contract-sha256 <expected> --oracle-program <actual-script> --oracle-program-sha256 <expected> --upstream-source <actual-modeling-file> --upstream-source-sha256 <expected> --output <new-report.json> [--timeout-seconds 1..60] [--cancel-file <file>]`。输出父目录须存在，报告以CreateNew写入；退出0数值通过、1完整26项存在数值差异、2结构/来源/hash/IO拒绝、124取消/超时。无效输入不产生完整报告。

reference schema为 `sezika.independent-reference.v1`，status=`complete`，kind=`upstream_original_weights_capture`；source/weights/config/tokenizer/contract身份、case/protocol/length、integer数组与artifacts、request、execution、oracle_program、upstream_source、25项tensors和marker_vectors分别记录。Artifact字段为path/sha256/bytes，TensorArtifact另有name/dtype/shape/layout；code artifact另有package/version。上游execution须逐字段等于冻结合同；实际torch build/平台/完整依赖可保留额外诊断字段，但不把它们冒充完整环境hash封存。

actual支持整数schema_version=1及snake_case字段，必须cpu_capture_complete/csharp_cpu_capture/csharp_scalar_fp32；267项名称、顺序、shape与raw路径唯一性均校验，只读取合同选中25项。各端独立核验token/marker I32 bytes及JSON数组、请求与config/模型快照身份、selected F32字节/hash/有限值；marker必须恰等于各自final的第25、35行。比较公式为 `abs(actual-reference) <= absolute + relative * abs(reference)`，不得在观察后放宽此合同。数值失败保留26项完整分母及逐项失败元素数；结构无效、partial、fixture或来源失配阻断正式结果。

边界：每JSON≤1MiB/深度32/20000节点，actual≤267项、reference恰25项、selected payload≤4MiB/端、path祖先≤64，无链接/reparse、相对raw路径不得越界；两侧根和selected文件路径不得重合。内部deadline≤60秒，Ctrl+C/取消文件、逐项进度；外部使用 `Sezika.Tooling process run` / `ExecutionRequest` 记录PID/start/命令/父链与精准子树清理，字段见 [C# 调用合同](../../docs/tooling-current-workflow.md)。工具不创建后台任务或子进程。报告先写唯一同级partial再Move(false)，取消/失败时最多5秒仅核时间/bytes归属清理本任务文件，未知修改保留并报告。CPU用于这一条离线数值比较；正式训练、批量教师与批量特征使用GPU。

诊断相对误差使用 `abs(delta)/max(abs(reference),1e-12)`，报告显式记录这个分母floor；它不改变冻结的验收公式。v1的五类tol同时核精确固定值。reference还必须声明original_weights_verified=true；只有监督者实际审计原权重读取/执行证据后，才可把它作为来源证据。

`self-test --contract <json> --output <new-directory> --maximum-cases 1..16 --timeout-seconds 1..60` 先1项tiny，再至多16项合成测试：binary读/hash/全26点比较、全失败分母、模型/input/shape/缺项/重复、NaN/Inf、raw hash、路径越界、外供合同hash、内部一致却偏离合同的源码hash、已有报告保留、production拒绝fixture与同目录自比。模型/null DTO、NaN/Inf、fixture/共用根分别为2变体组，其余单变体；完整16case明确报告19实际变体，不隐藏分母。每case≤8MiB/80文件，总≤128MiB/1280文件/60秒；输出合成输入和报告作为证据保留，无权重或模型执行。所有fixture明确fixture_only、original_weights_verified=false且accepted_independent_reference=false；不会拿C#自比当独立oracle。

单项目构建即可；本工具不修改solution或shared docs。Windows只用固定PowerShell7路径；构建与 self-test 各用独立 `ExecutionRequest` 和新日志目录，先单项再扩大至明确分母。原 `--oracle-program` 与 `--upstream-source` 只核冻结来源文件的 bytes/SHA，不执行其内容，也不生成新上游参考。未测量真实参考、Native AOT或质量时，不宣称这些能力通过。

## Port-derived v2 入口与新验收

`compare-port --actual <capture.json> --reference <port-v2.json> --contract <run-contract.json> --contract-sha256 <expected> --port-proof <pinned-proof.json> --output <new-report.json> [--timeout-seconds 1..60] [--cancel-file <file>]` 使用独立移植C#参考，要求schema为sezika.independent-reference.v2、测量前port-derived合同及完整外供proof。proof同时绑定原134 tensor字节、实际编译来源/PE/PDB/完整runtime、已完成build/export和PID出生/argv/退出清理证据。它不执行上游脚本，也不把consumer自报或弱build摘要当成producer编译证明；实际Tooling仅是外部有界进程执行器，不是本项目编译依赖。

port结果单列accepted_port_derived_reference；accepted_independent_reference与upstream_executed仍为false，原compare的v1上游实际执行门槛不变。固定容差及26项完整分母不变。actual仍核完整267项metadata，只选25个检查点加marker，共884736元素；不是完整算子或质量验收。

`self-test-port --contract <port-v2-template.json> --output <new-directory> --maximum-cases 1..16 --timeout-seconds 1..60` 先tiny1再full16，明确24个断言变体，包含弱compound证明、缺失必需字段、错误request定位及fixture拒绝。新冻结Windows managed候选已实际完成7手写/110生成来源与5runtime绑定，legacy1/16、port1/16及真实26/884736零失败；原始过程与严格native收口见[本轮新消费者证据](../../docs/evidence/tooling-numeric-consumer-final-2026-10-14.md)。旧运行缺少当时consumer完整runtime pins的事实保留，不能用本次新哈希倒填。
