# S4-07 GPU 训练合同切片 A

状态：schema3 静态报告和正式输入准入工程已实现，managed构建及纯CPU合同检查通过。没有正式CLI接线、CUDA身份采集、GPU workflow发布/恢复、真实数据或GPU运行；没有训练AOT/语言质量结论。设计已由监督者复核并单独冻结，本切片从UTC 2026-10-07 19:13:54开始，上限20分钟；root维护共享文档和Git，本子任务没有stage/commit/push。

## 实现和字节兼容

新增 `tools/Sezika.IndependentModelTool/GpuTrainingReport.cs`。外层固定schema3、contract=`cuda-marker-head-workflow-v1`，内嵌完整WorkflowTrainingReport v2及必需execution。两个source-generated JSON上下文保持静态类型；新报告拒绝未知/缺失/重复字段、null身份、错误版本、非有限值、错误指标分母、错误backend/numeric/order/compiler和缺失/重排/ABI变化的17个kernel。JSON有1MiB、depth16、4096tokens/10秒上限，逐token取消；kernel比较至多17项/5秒。

execution区分approved_original和synthetic_fixture，固定feature/training/development backend、FP32/FMA提交合同、一次xorshift32 shuffle后固定周期合同。所有kernel源/manifest/PTX SHA必须合法；正式兼容检查绑定source/manifest、每个静态PTX、UUID/name/driver/CC/memory。device_index仅为诊断，枚举位置变化不替代物理UUID。当前这里只消费身份声明，不接触CudaDevice或制造硬件身份；切片B必须从实际静态产物和Driver取得值。

CPU v2 parser拒绝GPU外层v3，GPU reader拒绝旧外层v2。`BindApprovedIdentity`拒绝合成fixture，并把完整training交原v2 identity检查。正式调用者仍须先通过下述准入，再构造approved_original；仅写报告字段不授予准入。既有 `IndependentTrainingWorkflow.cs` 仅将原private Validate改internal供v3复用，没有改变字段、JSON协议、CPU算法、checkpoint v1或head asset bytes。

新增 `OriginalTrainingAdmission.cs`。正式manifest固定schema2及reviewed-original-records-v1；要求既有accepted人审/许可、training_admission=true、明确Original/training来源与用途及审核ID，records SHA/bytes和准确Train/Development分母。每行要求accepted/admitted、来源一致、审核ID、family_id，拒绝audit/pending/未知状态、跨split family、重复record/candidate ID、错误shape/target。Choice、Score、Boolean保留原输入/candidate/等级索引顺序。批准字段是外部声明，工具不写回、补齐或独立鉴定人审。

输入读取以同目录独立文件、FileShare.Read lease、单次有限快照和UTF-8字节哈希绑定records/manifest；返回对象持有两个lease，调用者应using到输出发布完成。最多32行加1次overflow probe；每行65536bytes，records最多2,097,219bytes/33个65536-byte块，manifest1MiB/16块，整体30秒，支持取消和逐记录进度。失败finally释放leases。旧CPU读取器与数据文件不改；现有original/pending/AI pilot不被自动批准。

新增 `tests/Sezika.Tests/GpuTrainingReportChecks.cs`。所有accepted字符串、UUID和SHA均为明确synthetic合同模拟，只写独占GUID临时目录，不修改真实数据或代表人审/设备证据。root接线了tests Program的两个显式纯CPU入口；独立审查确认代码无阻断项，并核对了33-block/33-line退出边界。

## 验证和修正

固定PowerShell7.6.6、.NET SDK10.0.401；managed build使用-m:1、nodeReuse:false、UseSharedCompilation=false、DOTNET_PROCESSOR_COUNT=4及禁server环境。所有外部程序经过Invoke-BoundedProcess，记录PID/创建时间/完整命令/父链、stdout/stderr/result及finally归属清理。

| 验证 | PID | 墙钟上限 | 结果 |
| --- | ---: | ---: | --- |
| 初次managed build | 19252 | 180秒 | 25.758秒、exit0、零warning/error |
| --gpu-training-contract-tiny | 31964 | 60秒 | 1项、exit0 |
| 初次完整合同 | 103004 | 60秒 | 63项、exit0 |
| 增量build，修正SHA检查 | 81936 | 180秒 | 6.560秒、exit0、零warning/error |
| 修正后完整合同 | 36956 | 60秒 | 64项、exit0 |
| 既有CPU默认回归 | 90496 | 60秒 | 190项、2.308秒、exit0 |

复验原因：独立审查指出初次wrong-records-SHA测试添加前缀0，实际覆盖65字符格式错误，未覆盖合法64位SHA与内容不符。改为64个0并新增records_bytes+1；root明确批准一次增量build/full复验，保留初次证据。没有额外算法或GPU运行。

64项覆盖完整v2/outer schema门禁、unknown/duplicate/missing/null、fixture与formal隔离、ABI/单个PTX/source/manifest/UUID/driver/CC/memory变化、指标分母、read-only leases、records/candidate/score顺序、source/family/审核/许可/split/shape/target及合法错误SHA和bytes、预取消。190项既有回归包含CPU 2+2恢复与连续4步的head/checkpoint字节一致、生产loader互操作和严格v2报告；legacy mmBERT tokenizer oracle因该默认路径无资产而skip，此处没有真实模型质量证据。

## 资源与交付边界

任务证据保留于 `.artifacts/gpu-training-contract-20261008-head-checkpoint`：logs、closure-result.json、temporary-audit.json。所有运行成功，runner CleanupErrors为空；记录的任务进程/launcher身份在收尾精确PID查询中均不再存活，没有新长期进程/MCP服务。checks的独占临时目录及其三个文件已在finally按准确路径/hash回收；两个命名SDK MSBuildTemp目录从本任务Roslyn命令行识别，均已由SDK删除。共享bin/obj及交付证据保留，不清缓存或用户文件。

最终test DLL SHA256：6DB1518CA3E789874C7D557225AB926DE2785177972D55B42F25ADE55C2F2C1D。

最终tool DLL SHA256：211175B558FC488FC1E24F99F3C805E0C879976B673803F2BFC9D88C1A23DAB6。

实现文件归属仅上述三个新C#文件及v2 Validate可见性一行；tests Program入口由root添加。本任务新增此证据文档，不改已冻结设计、Program CLI、GPU源代码或共享ROADMAP/CHANGELOG/HANDOFF。Git diff --check通过，提交由root完成。下一切片B才采集真实UUID/kernel身份并接GPU workflow发布/恢复，等待教师服务卸载后的明确GPU窗口。
