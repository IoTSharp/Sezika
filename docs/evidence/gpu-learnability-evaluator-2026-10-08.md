# GPU 可学性只读评价前置

2026-10-08，基线 `e5c0888`。本轮只为固定24题可学性及8条训练Choice候选位置诊断补评价入口，不改核心模型、训练算子、数据、标签或旧v1报告合同。实际训练结果另记独立证据。

`Sezika.ExperimentTrainer evaluate` 从磁盘读取已有训练report/head/checkpoint，GPU编码train24与固定左循环换序8，共32次encoder forward。换序移动完整候选对象（ID和文本），按原目标ID重映射索引，重新渲染、tokenize及GPU编码。单独source-generated评价报告记录每题GPU logits、稳定softmax概率、完整GPU CE、目标ID/竞争排名、原/新候选顺序、文本/token/marker/feature身份、中英题型分母及预测ID变化。并列排名为严格更高logit数量加一，预测并列取渲染顺序首项；概率未经校准，不表示实际正确率。

`train --diagnostics fixed-left1` 锁定768更新、LR0.001、seed17、strict1024、零初始化三行head、SGD≤300秒、CLI≤460秒，冻结encoder。保留原v1训练报告，另保存 `experiment-learnability-evaluation.json`；原32题逐题评分复用本次实际GPU特征，旧兼容dev8 pipeline与新swap8真实编码使本阶段48次encoder forward（上限72）。`validate --diagnostics fixed-left1` 独立进程持只读文件lease重新GPU编码，核磁盘参数、完整CE、逐题身份/概率/排名及B/C报告一致，48次forward；不更新参数。GPU评分复用既有public CudaGemm静态Linear PTX及完整CE evaluator，不用CPU head评分替代批量评价。

输入文件先持lease再snapshot，前后核实际SHA；输出CreateNew、不覆盖源目录。原始失败与清理失败同时发生时保留AggregateException两个原因。成功报告要求完整分母、有限值、明确语义、实际Dispose返回及GPU bytes/allocations/modules/release failures归零；失败不生成伪completed报告。全部正式准入、人审质量、许可发布、校准、封存、新head AOT、性能及发布标志保持false。

实现分两轮有界修订，共三次build，均零warning/error；每轮tiny1→full6合同分别有CLI30秒/外层60秒，日志原字节在 `.artifacts/gpu-learnability-20261008/implementation-r1/`、`implementation-r2/`。合同覆盖左移8条ID/文本同移与目标映射、旧v1字节SHA/diskhead读取、固定CLI/未知参数拒绝、source-generated回读与并列语义、逐项logit/probability/CE/feature/device参数/目标/分母篡改拒绝、输出只读及取消。fixture只用于CPU合同，不作为GPU训练证据。

新任务远端前核旧十项hash、主机、GPU UUID及15294MiB空闲均通过。新scope SSH包装真实exit7探针返回7；Linuxrunner `/bin/true` tiny真实completed/exit0与最终完整清理通过。最终十脚本syntax及10KiB pack fixture检查通过；跨阶段flock互斥，B依赖A完成、C依赖B完成，同阶段新run目录防重复。运行中取消必须写精确 `/root/sezika-experiments/20261008-head-learnability-v2/.artifacts/stage-{a,b,c}/cancel.request`，任务根取消文件仅作启动/收尾gate。Linux工作480+清理8秒、外围492+TERM3秒、Windows530秒；边缘超时的收尾余量与同步native非抢占限制保留。

监督者独占Git/GPU；独立子智能体负责源码和runner审查。冻结清单、独审及已登记进程终核保存在本任务artifact目录；本工程验收不表示真实A/B/C已经执行。其他ROADMAP与暂停源码继续保留，不push、不发布或对外模型上传。

最终四源码及未改三输入源的冻结清单SHA `26A4C4A051F249E3BC9A89082D915E605DBA4E15C54079023B3F10127785DBAB`，独审绑定同一清单且无未解决阻塞项，见[原审查](gpu-learnability-20261008/evaluator-review.md)。唯一linux-x64 self-contained/runtime10.0.12发布actual exit0（PID89404，16.181秒），196文件84,488,007 bytes独立冻结；发布物tiny1 actual exit0（PID51484）。本次core/CUDA程序集hash与旧bundle不同，实际新文件另冻结，不沿用旧DLL身份；静态kernel源码/17PTX身份由真实执行再核。时长只用于预算核查，不作为性能结论。
