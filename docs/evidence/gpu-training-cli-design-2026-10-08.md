# S4-07 正式 GPU 训练接线设计草案

状态：只读设计已复核，工程实现另行验收。基线为 `6d36c70`；没有构建、实卡调用、正式训练、AOT 或质量验收。本文是本子任务的独立交付，不修改 ROADMAP、CHANGELOG、HANDOFF。设计阶段上限 12 分钟；另一子智能体只读复核 11 个命名文件，上限 6 分钟，没有产生临时文件或服务。

2026-10-08实现校正：GPU恢复须显式增加`--resume-head`，采用checkpoint/SHA/head/report/SHA五项完整输入，最多16选项对/33参数；旧CPU仍为四项。本文保留设计阶段原文，当前实现与验收以[切片C证据](gpu-training-cli-2026-10-08.md)为准。

## 选择和兼容边界

新增显式 `train-original-gpu`，复用 CPU 命令的必填输出和训练参数，增加 `--device 0..15`，默认 0；参数总数上限由31项显式增至33项。现有 `train-original` 仍走原 CPU API。GPU 命令的累计 `--max-steps`、seed、learning-rate、1..600 秒训练上限、1..1800 秒总上限及四项 resume 参数保持相同语义。未知、重复、缺值、非有限值、只给部分 resume 参数均明确失败，禁止选项错误回落到默认值。

GPU 命令必须通过显式 CUDA Driver/device 打开路径。缺失驱动、无设备、索引无效、入口不兼容、分配或 PTX 加载失败均返回失败；没有 CPU encoder 或 CPU SGD fallback。加载 IndependentModelLoader 创建的 ModernBertEncoder 只提供已审计 config/weights 和 tokenizer，不调用其 Encode；完整序列前向由 CudaModernBertEncoder 完成。

仍交付 checkpoint、head、report 三个文件。checkpoint v1 和 head asset 的生产字节格式保持不变；新增外层报告 schema 3：严格原 v2 报告位于 `training`，GPU执行合同位于必需的 `execution`。CPU v2 parser 已拒绝 schema 3，GPU resume 只接受 schema 3；这使旧 CPU checkpoint + v2 report 不能自动视为 GPU 连续轨迹。单一 schema 3 报告是最终发布标记，无需单独 sidecar 或第四输出角色。

## 数据准入和执行顺序

1. 解析有限参数，预检三个输出互异且不覆盖输入/已有文件，取得 records/manifest 只读 lease。
2. 调用共享 original-record 准入读取器：总数 2..32、有 train 和 development、record_id 唯一、严格 JSON、zh/en、Choice/Score/Boolean shape/target，正式准入状态及 family 不跨 split。保持源文件记录顺序和候选顺序；不排序、不自行生成标签、不采用教师 pending 记录。
3. SHA 绑定实际读取 records 和 manifest 字节。现有 Program.cs 的读取器尚未检查 review_status、training_admission、family_id；当前没有兄弟agent的新API，需新增有限静态准入读取器，不能只给报告加字段。GPU前向之前完成失败门禁。
4. 若 resume，先有限读取、核对预期 SHA、严格解析 schema 3；旧 v2 在设备或 encoder 前向前拒绝。
5. 打开一个 caller-owned CUDA device；取得执行身份，与 resume 的 execution 完整比较。加载固定模型/许可/版本/哈希门禁，再创建 GPU encoder。
6. 按源顺序导出 train 和 development 的完整序列 marker features。GPU计算完整 hidden，host只提取 marker 行并序列化/hash；该路径不是 CPU encoder 前向或 host 训练梯度。导出每题报告进度，最多32题，1024 tokens/题，取消和总期限贯穿。
7. GPU encoder 释放后才进入 head SGD，减少 resident GPU memory。设备保留并串行使用；训练题 feature 一次上传并驻留，GPU trainer 执行 FP32 CE、梯度及 SGD。恢复先用既有 Checkpoint.Load 校验 feature hash、identity、seed/rate/累计步及有限值，再交 GPU trainer。
8. development 使用 GPU 导出的 marker features、最终 head 的有限 CPU FP64 Score 计算分类统计；不调用 CPU Gradient 做更新。报告明确 `development_backend=scalar-linear-marker-head-fp64`；这是开发集诊断，不是训练 backend 或语言质量验收。
9. checkpoint/head 通过生产 loader 回读；严格校验报告，再发布 report。成功、失败、取消均释放本次 CUDA buffers/modules/context；外层 bounded runner 回收任务进程树。

## schema 3 的必需身份

外层仅固定 `schema_version=3`、`contract=cuda-marker-head-workflow-v1`、`training`、`execution`。保留所有现有 v2 字段及其严格检查，嵌套 v2 不单独写作恢复凭证。`execution` 至少包含：

| 字段 | 合同 |
| --- | --- |
| feature_backend | `cuda-modernbert-fp32-v1` |
| training_backend | `cuda-independent-linear-marker-head-sgd-fp32` |
| data_mode | 正式命令固定 `approved_original`；诊断helper固定 `synthetic_fixture`，正式resume/评价拒绝后者 |
| numeric_contract | `cuda-linear-ce-gradient-sgd-fp32-v1`，FP32/FMA、固定candidate累加顺序、有限检查后双buffer提交 |
| order_contract | `input-order-xorshift32-fixed-cycle-v1`，一次shuffle，累计步从固定周期继续 |
| development_backend | `scalar-linear-marker-head-fp64` |
| kernel_source_sha256 | 编译生成源身份；来自静态生成常量 |
| kernel_manifest_sha256 | 构建期生成的真实manifest字节SHA；新增静态生成常量 |
| compiler / compiler_version / target / ptx_version | 固定 ILGPU、1.5.3、sm_70、7.0 |
| kernels | 固定17项的 name、entry、block_size、parameter_count、实际静态PTX bytes SHA；顺序固定、不容缺失或重复 |
| device | UUID、name、driver_version、compute_capability_major/minor、total_memory_bytes；device_index只作诊断 |

当前 CudaDevice.Info 只有 name/driver/CC/total memory，无法区分两块同型号硬件。最小精确 device 合同增加静态 Driver `cuDeviceGetUuid` 绑定，读取16个原始bytes并保存32位大写hex；先核对官方 ABI，再实卡验收。缺 UUID 查询能力即失败，不将 device_index 冒充物理身份。现有公开 CudaDeviceInfo 的位置构造参数不变，UUID作为新增属性，避免破坏既有调用点。

KernelCompiler 先构造现有manifest JSON bytes并计算SHA，再将该SHA静态常量写入 GeneratedKernelArtifacts；既有17 PTX内容和参数ABI无算法改变。runtime identity helper 从真正加载的 GeneratedKernelArtifacts/CudaPtx spans计算各PTX SHA，禁止依赖外部可替换manifest文件或运行时反射/代码生成。Manifest SHA连同实际PTX清单共同绑定，不能仅拿外部文件hash宣称正在加载相同kernels。

恢复完整比较上述执行字段，UUID/driver/CC/name/memory及kernel/FP32/order身份不同时返回 `training_resume_backend_mismatch`。学习率、seed、records/manifest/feature、累计步与原报告检查继续使用 `training_resume_mismatch`。同GPU、同PTX、同features的tiny 2+2 = 连续4步已在engine验收；正式CLI需要重新覆盖完整报告链，不能把先前engine证据当作正式训练证据。

SHA值比较仍大小写不敏感；旧 Prepare 的 identity原始hex文本参与hash序列化，因此保存其字节合同。不得无版本canonicalize旧identity或checkpoint。重新导出的 GPU feature bytes须匹配checkpoint保存的FeatureSha，不能容差替代恢复检查。

## 报告和原子发布

新增 source-generated System.Text.Json 上下文，Unknown/duplicate字段拒绝；1 MiB、depth16、4096 tokens/10秒，所有小数组有固定项数上限并检查取消。嵌套training执行完整现有v2验证；正式GPU CLI只接受uses_real_independent_encoder=true、status=development_smoke_only、represents_quality_acceptance=false。GPU workflow诊断API可显式SyntheticFixture，保持uses_real_independent_encoder=false、status=fixture_contract_only和fixture feature backend；正式resume和评价adapter拒绝fixture。GPU报告不能仅凭feature_source证明训练设备。

checkpoint/head/report使用同目录本次临时路径、CreateNew、Flush(true)、无覆盖rename。report最后发布；report发布失败仅按本次记录的完整绝对路径和实际SHA回滚已创建文件，保留输入、旧输出、共享缓存和交付物。恢复report/checkpoint保持只读lease贯穿使用，避免已验证和实际消费字节不一致。三个输出不得与records、manifest、resume report/checkpoint以及package命名资产冲突。

evaluate-original 的报告adapter显式支持schema3，将已验证training绑定实际model identity/head，不绕过旧v2检查。既有评价是CPU Scalar encoder前向；评价报告须明确evaluation backend，不能把该结果描述为GPU评价。新增GPU评价入口不属于本切片。

## 提议文件归属和实施切片

实际输入状态：`datasets/independent-v1-original/manifest.json` 是无schema_version的旧development_smoke_only包，human_acceptance=project-owner-review-required，records没有review_status/training_admission/family/source；`independent-v1-original-admission` 是独立Candidate/Research准入元数据，source license_review和记录human_review均Pending。二者均不能自动用于新的正式GPU命令。teacher-general-v1为AI drafted/audit/pending，同样必须拒绝，不修改其状态或伪造human批准。

建议正式GPU输入使用显式 `training_contract=reviewed-original-records-v1` 新manifest（schema_version=2），绑定records_file/records_sha256/records_bytes、source、license_expression/license_review=accepted、allowed_uses、allowed_splits、精确split_counts、human_acceptance=accepted、training_admission=true及真实human_review_id。每行额外要求review_status=accepted、training_admission=true、真实human_review_id、family_id/source；row source必须与manifest来源身份一致，family不能跨train/development。manifest只允许Research/Development和Train/Development，本入口拒绝audit/calibration/sealed_test及pending/failed/abstained/rejected。批准值是外部提供的声明；工具只校验合同和字节，不制造人工批准或声称独立鉴定审查者身份。原有题型正文解析/target和candidate顺序复用；现有CPU入口的旧输入兼容不在本切片暗改。

本最小切片不增加audit/pilot训练CLI，也不加入 `--allow-unreviewed`。工程fixture由CUDA diagnostics中的独立显式入口提供，输入是固定tiny合成features，不读取待审核教师记录或写回其审核状态。真实encoder smoke可另设诊断入口读取明确fixture题，只导出/检查feature有限值与hash，不产正式checkpoint/head/schema3训练报告。若后续root要求32题audit/pilot GPU训练，需另一个显式命令和独立data_mode/report合同，原始pending/audit身份保留、禁止formal resume和质量接受；不能把该例外藏在正式命令的参数中。

| 文件 | 提议负责人/目的 |
| --- | --- |
| tools/Sezika.IndependentModelTool/Program.cs | 本子任务（root确认无其他owner）；GPU dispatch/export、新准入读取调用、schema3评价adapter和help |
| tools/Sezika.IndependentModelTool/OriginalTrainingCommand.cs | 与Program同owner；显式GPU命令/device参数，保持CPU解析兼容 |
| tools/Sezika.IndependentModelTool/Sezika.IndependentModelTool.csproj | 与Program同owner；静态引用Sezika.Cuda |
| 新 tools/Sezika.IndependentModelTool/IndependentCudaTrainingWorkflow.cs | 本子任务；GPU resume/report/publish、development Score诊断 |
| 新 tools/Sezika.IndependentModelTool/GpuTrainingReport.cs | 本子任务；schema3/execution严格静态JSON合同 |
| 新 tools/Sezika.IndependentModelTool/OriginalTrainingAdmission.cs | 本子任务；新正式manifest/record metadata门禁、SHA/bytes/source/family/split隔离，复用原题型正文解析 |
| tools/Sezika.IndependentModelTool/IndependentTrainingWorkflow.cs | 本子任务窄修改：提取internal校验/开发统计/发布helpers供GPU复用，旧Run和v2字节不变；若改动面过大则GPU独立实现同合同 |
| 新 src/Sezika.Cuda/CudaTrainingExecutionIdentity.cs | 本子任务；实际静态kernel identity与device snapshot，不引用工具程序集 |
| src/Sezika.Cuda/CudaNative.cs、CudaDevice.cs、CudaDiagnostics.cs | 本子任务；静态UUID Driver绑定与增量诊断属性 |
| tools/Sezika.KernelCompiler/Program.cs、GeneratedKernelArtifacts.g.cs | 本子任务；生成manifest SHA常量，17PTX保持原bytes |
| 新 tests/Sezika.Tests/GpuTrainingReportChecks.cs | 本子任务；无需GPU的parser/restore门禁/CLI正反合同 |
| 新 tests/Sezika.Cuda.Diagnostics.Tests/IndependentCudaWorkflowChecks.cs及其入口 | 本子任务；有界tinyGPU三文件报告和2+2恢复闭环 |
| docs/evidence/gpu-training-cli-design-2026-10-08.md | 本子任务独立设计/后续证据；共享文档和Git由监督者维护 |

建议分三个可单独提交的工程任务，各20分钟预算，不默默延期：A：schema3执行/报告纯合同、正式输入准入和CPU正反checks（无GPU）；B：静态kernel identity/UUID及三角色GPU workflow发布恢复，tiny报告/资源验收（最多4步/2题/90秒）；C：显式CLI GPU exporter和schema3评价adapter，缺GPU/准入前置门禁、help及完整实卡端到端。A先不改Program.cs；B不接真实模型；C最后静态接线。每个任务有独立证据和监督者审查提交点，不把前置工程称为正式训练完成。

构建必须排队，不与teacher/native构建叠加。实卡阶段等待root明确GPU窗口；正式数据已准入后才允许32题上限单次真实训练，目前没有已确认的正式输入。没有批准数据时可完成CLI工程和不训练head的真实encoder fixture smoke，必须保持fixture/人工未批准状态，不能宣称正式数据训练或语言质量。tiny报告保持fixture语义，不能作为originalencoder训练报告冒充正式证据。

必须覆盖：缺GPU失败路径、旧CPU报告拒绝、缺字段/unknown/duplicate、任一backend/FP32/order/kernel/UUID/driver变更拒绝、records/manifest/family变化、seed/rate/step不匹配、2+2=4、预取消/提交后取消/finite失败、输出路径碰撞/已有文件/中途发布失败、本次资源归零、生产loader回读。所有程序由固定pwsh7及Invoke-BoundedProcess记录PID/创建时间/命令/父链、数量+墙钟上限，finally只回收归属本任务的完整树。没有新长期服务；不运行Graphify，不广域扫描，不清共享bin/obj。
