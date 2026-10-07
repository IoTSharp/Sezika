# 独立 mmBERT 离线数值比较器

本 C# / .NET 10 开发工具只比较已有 raw F32 文件，不运行模型，不引用 Sezika core 或 Laya 决策 JSON。输入为现有 `IndependentNumericCapture` 的完整267项metadata、独立原始权重上游参考、测量前冻结合同及监督者外供合同/导出脚本/上游源码SHA-256。首轮只读取25个合同选中checkpoint与marker，共26项；通过不代表完整算子、tokenizer、语言质量、训练、校准、AOT或性能验收。源码/程序hash与原始权重执行来源仍须监督者核对日志，比较器不把metadata自报当来源证明。

命令 `compare --actual <capture.json> --reference <reference.json> --contract <json> --contract-sha256 <expected> --oracle-program <actual-script> --oracle-program-sha256 <expected> --upstream-source <actual-modeling-file> --upstream-source-sha256 <expected> --output <new-report.json> [--timeout-seconds 1..60] [--cancel-file <file>]`。输出父目录须存在，报告以CreateNew写入；退出0数值通过、1完整26项存在数值差异、2结构/来源/hash/IO拒绝、124取消/超时。无效输入不产生完整报告。

reference schema为 `sezika.independent-reference.v1`，status=`complete`，kind=`upstream_original_weights_capture`；source/weights/config/tokenizer/contract身份、case/protocol/length、integer数组与artifacts、request、execution、oracle_program、upstream_source、25项tensors和marker_vectors分别记录。Artifact字段为path/sha256/bytes，TensorArtifact另有name/dtype/shape/layout；code artifact另有package/version。上游execution须逐字段等于冻结合同；实际torch build/平台/完整依赖可保留额外诊断字段，但不把它们冒充完整环境hash封存。

actual支持整数schema_version=1及snake_case字段，必须cpu_capture_complete/csharp_cpu_capture/csharp_scalar_fp32；267项名称、顺序、shape与raw路径唯一性均校验，只读取合同选中25项。各端独立核验token/marker I32 bytes及JSON数组、请求与config/模型快照身份、selected F32字节/hash/有限值；marker必须恰等于各自final的第25、35行。比较公式为 `abs(actual-reference) <= absolute + relative * abs(reference)`，不得在观察后放宽此合同。数值失败保留26项完整分母及逐项失败元素数；结构无效、partial、fixture或来源失配阻断正式结果。

边界：每JSON≤1MiB/深度32/20000节点，actual≤267项、reference恰25项、selected payload≤4MiB/端、path祖先≤64，无链接/reparse、相对raw路径不得越界；两侧根和selected文件路径不得重合。内部deadline≤60秒，Ctrl+C/取消文件、逐项进度；外部必须使用仓库 `tools/Invoke-BoundedProcess.ps1` 记录PID/start/命令/父链与精准子树清理。工具不创建后台任务或子进程。报告先写唯一同级partial再Move(false)，取消/失败时最多5秒仅核时间/bytes归属清理本任务文件，未知修改保留并报告。CPU用于这一条离线数值比较；正式训练、批量教师与批量特征使用GPU。

诊断相对误差使用 `abs(delta)/max(abs(reference),1e-12)`，报告显式记录这个分母floor；它不改变冻结的验收公式。v1的五类tol同时核精确固定值。reference还必须声明original_weights_verified=true；只有监督者实际审计原权重读取/执行证据后，才可把它作为来源证据。

`self-test --contract <json> --output <new-directory> --maximum-cases 1..16 --timeout-seconds 1..60` 先1项tiny，再至多16项合成测试：binary读/hash/全26点比较、全失败分母、模型/input/shape/缺项/重复、NaN/Inf、raw hash、路径越界、外供合同hash、内部一致却偏离合同的源码hash、已有报告保留、production拒绝fixture与同目录自比。模型/null DTO、NaN/Inf、fixture/共用根分别为2变体组，其余单变体；完整16case明确报告19实际变体，不隐藏分母。每case≤8MiB/80文件，总≤128MiB/1280文件/60秒；输出合成输入和报告作为证据保留，无权重或模型执行。所有fixture明确fixture_only、original_weights_verified=false且accepted_independent_reference=false；不会拿C#自比当独立oracle。

单项目构建即可；本工具不修改solution或shared docs。Windows只用固定PowerShell7路径，经有界runner执行build/self-test，日志单独置于 `.artifacts/independent-oracle-20261008/compare-processes`。未测量真实参考、Native AOT或质量时，不宣称这些能力通过。
