# 冻结前置工程的独立代码审查

2026-10-08，contract_review。结论：本次固定可学性实验的最小评价前置和最终runner可进行工程验收，未发现尚未解决的阻塞项。此结论仅为源码/合同静态审查；真实A/B/C实验、独立GPU产物与资源终核另验，不宣称模型质量、校准、AOT或性能通过。

本轮独立预算20文件/25分钟/10批短命令，实际20个唯一读取/哈希文件、8批，全部PowerShell7.6.6。未调用Git/GPU/网络/构建或训练，未修改源码；只在本子任务review目录保存交付物，无临时文件/长驻进程。监督者通报r2 build2零警告/错误、tiny1/full6 actual exit0与cleanup[]，以及最终10个shell bash -n和10KiB pack tiny通过；本审查不重复这些执行，且不把fixture当真实训练证据。

## 冻结绑定

source-freeze.json实物SHA256：**26A4C4A051F249E3BC9A89082D915E605DBA4E15C54079023B3F10127785DBAB**，声明baseline e5c0888b8ebb90ed54d3181ed21a6d0e82e2bea7。四个变更源码的实际hash逐一与该冻结清单一致：

| 文件 | SHA256 |
| --- | --- |
| Program.cs | EB41EB2E516F87265018617C02E9366DD77B9836B94BF9A944E5C8F4D42A26A3 |
| ExperimentReport.cs | 4DBBCAB65FC99CF66CC667D9D6E2D23E056202E2AB72FDFA94C0D9A02A587A1A |
| ExperimentEvaluation.cs | 62FDEF1FFF85ED23E3884E185004D595E570F37E368F76F7847F4E03DD805B70 |
| ExperimentEvaluationChecks.cs | A6260A8099D6EC181DB21FEF207261007C118A54FFDF6718AE9465E4D5507B3C |

最终scripts.sha256清单实物hash：**46430C7F3C74F4FDD10BC15416C5644CC46545B36F9963F7FDD88C8D46F388FA**，其10个shell实际SHA已逐一核对全部相同。最终phase-common为22D223C7E0B59B1EF45141C5FB08ED0339494BEA7DD7F0F2104F6832C79D0F51；Invoke-LearnabilityRemote.ps1为1237F3F2283EAE98735EF6F462A5203CF652AD30D5470FF208EB6D5658BB2058；Invoke-LearnabilityScript.ps1为1E9F11420FF9C4E47C1EA1B4D099C522EE8FB6B06F745FC3AC2D96A9A0438323。后续源码/脚本hash变化使此结论不再绑定新版本。

## 已核主链

- v1训练/验证DTO保留，LoadedInference仍为development8；新增独立static record与source-generated JSON报告。原旧report全程只读、入口与收尾核SHA，新报告CreateNew、精确owned-output清理，不把新字段写入旧v1报告。
- 左移以候选对象移动，ID和文本一起移动；教师标签由原目标ID查新索引。保留source record、split和原状态；8个变体不加入训练24。变体重新Build/tokenize/CUDA Encode，报告记录原/新ID顺序、文本hash、完整token/marker、featurehash、目标排名、预测ID及位置比较。
- 原批量logits经已有公开CudaGemm/静态Linear执行；head三行权重从type×H转成GEMM所需H×3，取candidate×3+type列，未调用CPU LinearMarkerHead.Score或新native/dynamic launcher。完整CE复用已有CUDA evaluator，并验证上传参数F32 hash和device参数身份。host仅softmax/NLL/分组与元数据聚合。
- A仅export train24与真实swap8，32 forwards；B/C原32实际特征+dev8完整pipeline+真实swap8为48 forwards，计数开始/完成、阶段上限及CLI wall均有检查。原始逐题head评价复用本次GPU特征；C为独立新进程、重编码原32/swap8，再对B逐题身份/feature/CE/logits/probabilities/目标排名/预测ID与磁盘report比较。数值比较明确2e-6，GPU CE与host NLL明确2e-5。
- diagnostics训练CLI固定768、seed17、LR0.001、strict1024、SGD≤300、CLI≤460；不resume、不改变encoder、不开发集选参。参数不匹配、来源身份不符、缺分母、失败或资源未释放均阻止completed报告。正式acceptance全false，概率和教师一致率语义明确。
- native评分复用已核CudaGemm/CudaKernels：module在取得时即登记固定槽，3个Execute缓冲由using所有权释放，部分module构造失败走已有独立恢复时钟。工具外部单次runner仍负责不可抢占driver的硬界；成功阶段RequireReleased校验bytes/alloc/modules/release失败归零，并实际CloseResources后才序列化Dispose返回证据。

## 发现与修复闭环

审查提出的评价入口primary异常被rollback异常覆盖问题已修：CompleteEvaluationRollback保存并聚合primary+cleanup。监督者提出的report读取与lease间身份gap已修：入口先CheckPath并取得report/head/checkpoint三lease，再从同一report lease核原字节；前核head/checkpoint hash、收尾重核全部lease，C另重核B评价lease。冻结版本另核compiler/version/target/PTX及全部17 kernel身份。

runner首轮的任务互斥与阶段依赖问题已修：固定task stage.lock由flock非阻塞持有至shell/继承子树退出，B要求A completed收据与A报告，C要求B completed收据，阶段mkdir继续拒绝重复执行。pack使用同一锁、明确task/thread scope、3目录/3秒数量时间界、31MiB输入/32MiB归档、du3秒/tar15秒/hash3秒。

取消合同现明确为：正在运行阶段写准确 `/root/sezika-experiments/20261008-head-learnability-v2/.artifacts/stage-a|b|c/cancel.request` 供已核Linuxrunner的file模式处理；task根cancel.request只作启动/收尾gate，无新增poll。这一明确双路径合同可接受，不能声称根级cancel自动取消正在运行的payload。

Windows530秒仍是最终硬界；预算余量较紧，极慢transport/收尾若先到界，必须读取保存回执并作独立接续终核，不能自动重训。final closure保留登记身份范围语义，不称全机零残留。以上是既定执行限制，不是新增GPU实验或扩大预算的授权。

工程审查通过后可由监督者分项commit，并依次启动唯一A/B/C固定实验；实际结果与回收证据仍需独立验收。
