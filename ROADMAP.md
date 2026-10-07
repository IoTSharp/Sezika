# Sezika Roadmap

本文件是 Sezika 的阶段计划与验收入口。Sezika 的目标是 **Multilingual, non-autoregressive System 1 decision engine**：使用 C# / .NET 10 实现本地类型化决策模型推理，并通过 Native AOT 发布。

状态：`✅ 已完成`、`🚧 进行中`、`⏳ 计划中`。代码存在、构建通过、数值一致、真实模型推理、多语言质量与性能达标分别记录。

## 阶段

| 顺序 | 状态 | 范围 | 验收产物 |
| --- | --- | --- | --- |
| 0 | ✅ | 研究、仓库与契约草案 | 固定参考版本、架构决策与 C# 契约草案；未执行验证 |
| 1 | ✅ | 模型资产与 tokenizer | 可验证 manifest、安全张量格式、与固定 tokenizer 一致的 token IDs；发布目录、loopback 断点下载、安装清单和 lease 卸载已通过有界验收 |
| 2 | ✅ | 纯 C# encoder | embedding、attention、RoPE、norm、MLP 的标量正确性与逐层 oracle；SIMD/资源边界已完成 |
| 3 | 🚧 | 决策头与完整推理 | choice / score / boolean 的真实本地输出、预算、取消、session 生命周期 |
| 4 | 🚧 | 独立模型、多语言数据、教师蒸馏与校准 | 独立底座准入、自有协议与 head 训练、离线教师标签、新权重加载、校准与测试报告 |
| 5 | 🚧 | AOT 与 CPU/GPU 性能 | 既有 Windows 基准与 Windows / Ubuntu WSL2 真实模型 AOT 已验收；长输入画像和有质量门槛的优化待完成 |
| 6 | 🚧 | 开源发布 | NuGet、CLI、模型卡、许可清单、跨平台发布与示例 |

当前已具备固定 Apache-2.0 mmBERT/Laya 模型资产、C# tokenizer oracle、真实 CPU encoder/marker-head typed inference、ILGPU 构建期 PTX/ABI 产物、CUDA Driver resident encoder/head，以及 win-x64 / linux-x64 四后端真实模型 Native AOT 验证。阶段 5 已完成本机 Windows 基准与 Ubuntu WSL2 的既定验收范围，新的长输入与多题性能目标尚未验收。详见 [S5 性能与 AOT 证据](docs/s5-performance-aot.md)、[阶段证据](docs/stage-evidence.md) 与历史[闭环审计](docs/closure-audit-2026-09-23.md)。

## 当前执行顺序与门槛

2026-09-30 按最新讨论校核路线：目标调整为 **Sezika 的默认模型、输入协议及运行路径不再依赖 Laya**。已有 Laya 资产用于过渡对照和历史数值证据；主线转为独立审核的原始 encoder、自有决策头和独立数据，可选离线教师方向改为 IoTSharp/Tomur 本地服务。不把完成 Laya 微调作为独立路线的前提。S4-14 文档审核/合同已完成；本轮已完成原始资产下载/hash、C# 受限转换、独立 loader/decision-v1 smoke、12 条原创开发 smoke，以及 16 条候选数据的 train/development/calibration/sealed_test split 合同审计。许可发布、上游数值 oracle、人工准入、独立封存、校准、封存质量、CUDA/AOT 仍未完成；候选审计当前明确为 blocked。

2026-10-03 从已推送的 `a53eb0d` 继续并行工程续审，范围和验收见 [本轮统一证据](docs/evidence/continuation-2026-10-03.md)。S4-08 补全校准指标分母、基线/封存 split/有限值门槛；S4-10/S4-11 加固离线审核合同；S4-15 固定 encoder 数值配置与 tensor 布局；S5-06 加固画像真实性检查。当前工作区没有原始独立模型权重，真实教师身份/许可、数据人工准入/独立封存、上游数值 oracle、独立模型 CPU/CUDA 与两 RID AOT 仍 blocked/pending。本轮 Windows AOT 合同 fixture 与历史画像校验均不能关闭这些门槛，S5-07 尚无优化收益结论。

2026-10-04 在上述合同基础上加入可运行的独立 `decision-v1` runtime facade、hash-bound calibration/sealed-test 评估工具和 CPU RoPE 因子复用候选。runtime 已用 tiny C# encoder/head fixture 验证身份、三题型输入路径、有限分布和卸载失败语义；校准工具只消费已有预测 JSONL，温度仅在 calibration split 拟合，fixture-only 明确返回 `not_eligible`；RoPE 表保持参考数值一致并计入 resident bytes。没有下载缺失独立权重、真实训练/教师调用或新模型质量/性能采样，因此 S4-08、S4-10/11、S4-15 上游 oracle、S5-06 完整画像和 S5-07 收益仍 pending/blocked。
2026-10-07 用户授权监督者按本任务板持续派发子智能体，每个独立任务验收后单独提交，已设置本聊天 30 分钟检查跟进。首批工程子任务为 S4-15/S6-02 的显式独立 CLI（保留现有入口，正式默认切换依赖 S4-08）与 S4-07/S4-12 的 checkpoint 保存/恢复/导出流程；另行核查全部剩余门槛及依赖。实现前先登记范围；工具闭环不关闭真实数据人工准入、独立封存、数值、质量、AOT、性能或正式发布门槛。执行交接见根 HANDOFF.md。

全量核查后接续 S4-08/S4-15 的独立预测导出工具：以获准 split、完整模型/head/协议/数据身份和有界真实 runtime 输出接到现有 CalibrationTool，保留失败分母、文件 hash 和实际调用范围。工具 fixture 不具备正式准入资格；不自动读取或批准真实封存集。CUDA、独立画像和审核切片按依赖接续，详见 [全量监督核查](docs/supervision-audit-2026-10-07.md)。

首批 S4-07/S4-12 checkpoint/恢复/导出工程子任务已验收：保存三项产物并绑定完整 v2 身份，2→4 与连续 4 步 fixture 的权重及文件字节一致；开发评估拒绝 v2 身份宽度丢失。最终核心 173 项检查、CLI 41 项互操作检查及零警告/错误构建通过。整项仍需真实获准数据与实验；验证范围见 [监督证据](docs/evidence/supervision-2026-10-07.md)。

首批 S4-15/S6-02 的显式独立 CLI 工程子任务已验收：managed 41 项和 Windows Native AOT tiny fixture 42 项通过，产品 CLI 原生发布及 help 通过；完整训练报告可直接绑定 head。未恢复真实独立权重、不改变默认路径，不关闭 S4-08 的真实质量、CUDA、两 RID AOT 或正式发布门槛。

2026-10-08 接续的 S4-08/S4-15 独立预测导出工程子任务已验收：极小1项、完整11项检查及实际 CalibrationTool 联调通过，4条tiny fixture以匹配的组合身份/预测hash返回exit3/not_eligible；原子目录发布故障回归通过。正式准入资料先于题文核验，未读取真实封存题。恢复固定原始资产、上游数值 oracle、独立 CUDA/画像/审核切片按依赖继续；人工审核/独立封存、实际质量和正式发布门槛仍开放。

下一批已登记并派发 S4-15 的静态可注入独立 CPU/CUDA pipeline、三行自有 marker head GPU forward（复用既有C# PTX/Driver，不使用legacy head），以及固定原始资产恢复。两子任务先核对设计/来源/许可/资源预算，在监督者完成本批提交后开始实施；CUDA真实数值、原始资产oracle、质量/AOT/性能继续分别验收。

2026-10-08 上述 S4-15 pipeline 工程子任务已验收：核心190项、CUDA完整59项、CLI41项和预测导出11项回归通过，Windows Native AOT 独立 CPU18项/实卡CUDA17项通过；关闭竞态与同线程重入释放已修复，首次失败保留。仅 tiny 合同/算子/Windows AOT 范围通过，真实原始模型上游 oracle、自有 head 数值、Linux AOT、质量和性能继续开放，见[独立管线证据](docs/evidence/independent-pipeline-2026-10-08.md)。原始权重恢复另行验证，不从该 fixture 结果推断。

同日用户确认首批任务为通用中英文本分类、程度评分、真假判断，并希望用本地教师作答。先建立32题格式试运行候选：中英各6Choice/5Score/5Boolean，32个独立family，全部AI起草、audit/pilot、人工审核及训练准入pending，没有答案或教师响应。候选包67项文件/hash与统计已由监督者复验；教师采集仍需真实服务/模型/许可身份及严格响应验证，后续扩量按试运行结果确定。见[候选包与证据](docs/teacher-general-pilot-2026-10-08.md)。

2026-10-08 固定原始资产恢复任务已闭环：4个原始文件字节/hash通过，纯C#转换134个encoder tensors并匹配固定SafeTensors身份，真实scalar CPU smoke得到46 tokens/35,328个有限hidden元素；305.15秒执行预算及进程/partial回收通过。纠正source lock中的“固定模型卡含MIT全文”错误，完整通知与tokenizer发布审签仍pending；该smoke不关闭上游数值oracle、trained head、CUDA/AOT或质量门槛。见[资产恢复证据](docs/evidence/independent-assets-restore-2026-10-08.md)。

2026-10-08 S4-10 C#采集器工程任务已验收：严格decision-v1单题/候选映射、固定模型与条款证据绑定、字面loopback、32次无重试/1200秒总预算及原子输出已实现；取消保留全分母和原响应hash/usage的独立失败审计。最终重建零警告/错误及1→10内存合成检查通过，旧离线合同1→24回归通过。实际GGUF未下载/hash，默认服务一次连接失败，真实教师答案和训练准入仍pending；见[采集合同与执行证据](docs/teacher-collection-s4-10.md)。

同日取得官方Qwen3.5-9B固定revision的Apache-2.0全文、量化资产身份与Tomur最小CPU构建/登记/启动合同，完成AI来源工程核查；量化base revision等价、人工训练/发布审签及实际模型使用另验。随后独立单GGUF恢复已闭环：5,680,522,464字节/固定SHA-256通过后原子发布，唯一代理149.70秒，含准备668.64秒≤25分钟，进程与partial回收通过；见[恢复证据](docs/evidence/teacher-weight-restore-2026-10-08.md)。来源/资产就绪不等于实际教师推理，运行前证据另见[运行准备](docs/evidence/teacher-runtime-readiness-2026-10-08.md)。

真实教师试运行前的固定2048-token context_size请求上限已补齐，成功及取消归档请求使用同一上限，manifest记录maximum_context_tokens。重建零警告/错误、1→10合成合同验证实际序列化context_size/max_tokens/stream通过；真实服务执行及内存用量仍另验。

S4-15独立数值capture工程任务已验收：固定46-token请求、267项CPU张量/51,720,192字节与完整来源/产物hash已实际捕获，1→8项自检及监督者逐文件复核通过，见[捕获证据](docs/evidence/independent-numeric-capture-2026-10-08.md)。仍须可信独立上游oracle及冻结容差，不关闭数值parity或质量门槛。

接续S4-15单输入独立上游oracle：复用固定46-token/markers25,35及原始mmBERT权重，使用已安装固定PyTorch/Transformers离线eager FP32 CPU参考，仅作单case数值核验。先冻结25个张量checkpoint（raw/norm embedding、22层hidden、encoder final）与marker向量的26项合同，再实现有界离线导出和静态类型C#比较；独立记录源权重/代码/环境、输入/输出hash、逐元素固定容差及失败。参考不进入核心，也不代替267项全部算子、正式训练、代表性质量、AOT或性能验收；扩展至CUDA和更多输入另按GPU及资源预算执行。

其中C#比较器工程已验收：最终零警告/错误构建，1→16合成case/19行为变体通过；26项/884,736元素完整失败分母、合同源码绑定、fixture/共用路径自比拒绝、JSON/finite/hash/不覆盖保护已核。合同测量前改用实存Python3.14.3/Torch2.12.0/Transformers5.13.1，NumPy缺失、安装授权pending；没有真实上游输出。详见[比较器工程证据](docs/evidence/independent-numeric-compare-2026-10-08.md)，S4-15上游数值门槛保持进行中。

独立Python oracle导出工具工程亦已验收：标准库tiny1/full16通过，原权重/输入与源码lease、首次import前hash及bytecode缓存隔离、25hook/marker、Windows原子发布与实际Move后取消回滚已实现；修改后再次1→16通过，任务进程与owned fixture已关闭。没有安装NumPy或真实forward，不能关闭上游parity；准备探测越界和Python安装管理器意外更新事实保留，见[导出工具证据](docs/evidence/independent-oracle-tool-2026-10-08.md)。

接续S4-15/S4-08独立模型Native AOT smoke工程：新增静态core/CUDA引用的独立样例。原始converted package与46-token实现输入仍在，历史head-v2.asset/开发报告已不在，因此明确调整为真实encoder+固定内存诊断head，不造训练report或schema3正式准入。诊断head记录内存F32身份/算法，runtime字段中的hash仅作明确的诊断绑定，不能称真实训练manifest/features/head文件。分别核真实backend、AOT标志、有限类型化输出、预取消/卸载资源；CUDA缺失失败，不把legacy/tiny当真实独立证据。Windows先验，Linux另开窗口；正式训练head、上游parity、质量/校准和完整独立模型两RID仍开放。

该Windows诊断工程已验收：最终零警告/错误、managed/native各tiny1→8、一次win-x64发布、native probe与唯一真实46-token CUDAcase通过；311次kernel、552-byte D2H、预取消零新增launch、释放后资源归零。监督者核六文件冻结/实际exe与报告hash，12份runner及45身份无匹配存活，首次CS1061失败保留，见[Windows证据](docs/evidence/independent-aot-smoke-2026-10-08.md)。接续Linux诊断切片复用冻结样例，在Ubuntu用独占artifacts路径核Native AOT发布、tiny/full文件身份合同、driver probe及最多1个同输入CUDAcase；不用Windows/Linux共用obj，不安装缺失工具或改变head/输入以获得通过。它仍不是正式trained-head、质量、两RID正式交付或性能验收。

Linux诊断切片已通过：基线b837be7、冻结六源码，唯一明确local源/cache发布、native1→8文件身份合同、driver probe和唯一46-token CUDAcase成功，311launch/552-byte D2H、预取消零launch、释放归零，见[Ubuntu证据](docs/evidence/independent-aot-linux-2026-10-08.md)。Windows/Linux仅两个固定诊断head发布物通过；退出竞态修复及旧失败保留，源码冻结在Linux编译结束后才释放。正式trained-head与新代码/新权重部署仍须自身两RID验收，不把这份profile时间称性能结论。

用户追加资源约束：正式训练和批量教师采集/特征计算必须使用GPU；CPU只用于小规模兼容性、数值参考及故障诊断。32题教师pilot先完成CUDA后端/显存核验再执行；S4-07 GPU训练需实现并验证C#反向kernels与PTX/Driver，当前前向证据不能写成训练支持。

S4-07 GPU训练最小工程已闭环：冻结encoder的三行marker head以纯C# kernel在GPU完成CE、梯度与SGD，构建期17项静态PTX/ABI导出、CPU参考27项/GPU21→61项及59项既有CUDA回归通过，见[证据](docs/evidence/gpu-training-design-2026-10-08.md)。下一切片接正式GPU encoder特征计算/训练CLI及backend/kernel/device恢复和报告身份，禁止CPU fallback；`train-original`现有CPU路径不作为正式训练使用。encoder反传、正式数据/质量、训练AOT另验。

S4-07/S4-12训练器异常清理工程已验收：共用独立5秒/39 buffers+1 kernels owner/17 modules预算，主训练对象与原始抛出帧保留，initial/packed副本finally清零，以纯managed摘要核caller基线/残留。最后一次native返回也检查超期；false release及SafeHandle关闭与context销毁分开记录。最终第三build零警告/错误、CPU1→3/GPU1→8清理case及唯一既有21项训练smoke通过，9个最终case的caller context实际回收，见[清理证据](docs/evidence/trainer-cleanup-2026-10-08.md)。本批无正式数据训练或AOTpublish，正式数据/质量门槛不变。

原生资源取得与登记加固工程已验收：buffer/module/context/profiling event在native前准备wrapper/容量/计数与pending归属，成功后无分配adopt；release诊断OOM仍保留真实operation/code及owner，缓存旧异常回归通过。最终零警告/错误、CPUtiny1/full7、GPUtiny1/full11及既有training21/cleanup9case通过；实际context回收、十一runner/37身份终审和五源码/四DLL身份已核，见[登记证据](docs/evidence/cuda-native-adoption-2026-10-08.md)。注入不是实际heap OOM，设备调用含snapshot/Dispose须caller串行；本批没有AOT/正式数据训练。

下一独立切片验证Native AOT中的GPU CE/梯度/SGD：H4合成Choice/Score/Boolean三题，schema3三文件从3步磁盘恢复到6，与从零连续6对照；原生flags须早于任何Open核验。managed、Windows及Linux分别先tiny1再full11、每环境最多16实际GPU更新，新增代码/二进制各自冻结，不用encoder前向AOT代替训练支持。每RID最多两次有理由publish/600秒，每阶段最多25分钟；正式数据、encoder反传、质量及性能门槛保持开放。

S4-05/S4-11的32题人工审核入口工程已验收：[逐题审核表](datasets/teacher-general-v2-review/review.md)与JSONL模板包含完整题文/候选/实际教师建议，绑定固定v2源及64原字节引用。生成tiny1/full32、独立字段/映射验收及监督者五文件hash复核通过，见[证据](docs/evidence/teacher-review-entry-2026-10-08.md)。八个人工字段均null/pending，教师答案曝光=true、封存资格=false；未替人审核或批准，没有自动训练准入，也未重跑教师。实际人工审核、许可审签与独立封存继续开放。

GPU CLI 接线按[复核设计](docs/evidence/gpu-training-cli-design-2026-10-08.md)分三项：A 为严格 schema3 报告与真实人工准入合同；B 为静态 kernel/设备 UUID 身份及三文件 GPU workflow 发布恢复；C 为显式 `train-original-gpu`、完整 GPU encoder 特征导出与评价报告适配。每项单独工程验收提交，CPU v2 合同保持兼容；fixture 不进入正式恢复或评价，不制造人审声明。实际正式数据训练、质量及训练 AOT 继续另验。

切片A工程已验收：schema3内嵌完整v2训练报告并严格绑定执行声明，正式输入读取器核验真实字节/hash、人工及许可准入、source/family/split与全分母；1→64项纯CPU合同检查、190项既有核心回归通过，构建零警告/错误。没有GPU执行、真实数据训练或人工批准；见[合同证据](docs/evidence/gpu-training-contract-2026-10-08.md)。B/C继续实施。

教师提示v2准备任务已验收：32条原请求逐字节、题序/family/候选映射保持，提示明确所有answered候选ID为JSON字符串；1→32生成与独立源67项/新69项绑定复验通过。仅准备新的audit/pilot包，真实v2 GPU调用另验；旧响应与失败保留，严格parser不放宽，见[提示证据](docs/evidence/teacher-prompt-v2-2026-10-08.md)。

S4-10本地GPU首轮实际调用已完成：固定9B教师33/33层offload，首题严格JSON通过；32题HTTP200，13格式通过/19候选ID类型失败，133项引用逐hash/bytes复核通过。监控CIM查询超时与server主动exit-1保留，已卸载/停止服务，147份结果/1337次身份观测无匹配存活；见[首轮GPU证据](docs/evidence/teacher-gpu-pilot-2026-10-08.md)，旧失败不覆盖。

v2 GPU教师试运行已验收：Score/Boolean各1题严格字符串ID通过后，32题全部格式有效、零失败/拒答；共34次HTTP、32个不同题目、无重试，完整32题耗时115.6265689秒。151项引用逐hash/bytes通过；实际33/33层GPU offload，峰值5953MiB，卸载/主动停止后324MiB。monitor最终Deadline、server主动exit-1及文档超过原执行窗口事实均保留；58份runner清理为空，75身份及58个root PID/start后核无匹配存活。全部仍AI/audit/pilot/pending，正式输出训练许可、人审、质量及S4-11正式教师实验继续开放；见[第二轮GPU证据](docs/evidence/teacher-gpu-prompt-v2-run-2026-10-08.md)。Tomur本地和origin/main已同步b07ae414，复用教师host仍为32675f8构建。

GPU切片B工程已验收：真实UUID、17项静态kernel/manifest身份、schema3三文件发布及完整恢复元数据实现；CPU准备3/GPU tiny5/full31、stamp身份正负检查通过，21份结果/70身份无匹配存活。重新export的字节漂移未接受，原PTX/manifest保持；无正式训练数据、质量或训练AOT结论。C继续接显式CLI/真实GPU encoder导出，见[workflow证据](docs/evidence/gpu-training-workflow-2026-10-08.md)。

GPU切片C工程已验收：train-original-gpu明确选择设备、五项恢复输入和正式准入门禁；完整序列特征前向及head更新均用GPU，没有CPU fallback，评价另外声明scalar后端。CPU合同2→22/GPU导出8→12/旧CUDA59通过，固定真实mmBERT的48-token特征smoke及310次kernel/资源归零通过；31个身份无存活/清理错误。实际正式数据head训练、质量、数值上游oracle及训练AOT仍待验，见[CLI证据](docs/evidence/gpu-training-cli-2026-10-08.md)。

### 已选定方向

1. **独立模型来源。** 直接从经审核的原始 encoder 取得底座参数，优先评估 `jhu-clsp/mmBERT-base` 或同类可支持的编码器，独立记录 revision、配置、tensor/tokenizer hash 与许可；不能从 Laya 检查点抽取 encoder 后改称原始底座。本轮已完成固定 mmBERT 原始文件下载、hash、C# 受限 pickle 转换、134 tensor 形状检查和独立 loader smoke；上游逐层数值 oracle与许可发布审签仍未完成。自有决策头重新初始化和训练，不继承 Laya 训练后的 head/type/scorer 权重。
2. **任务与数据。** 首批已选通用中英文本分类、程度评分、真假判断，分别覆盖 Choice/Score/Boolean 并建立基线和错误切片；正式样本与质量接受范围仍待冻结。人工标注数据作为基线，教师辅助标注或生成作为独立实验；当前32题AI起草候选仅作audit格式试运行，不是人工金标或获准训练集。分别管理 train、development、calibration、sealed test 和 audit，同家族、实体及翻译/改写/反事实衍生物保持隔离。已查看 PAWS 250/Nimble 324 及其衍生物继续仅作 audit。
3. **教师入口。** 可选教师为IoTSharp/Tomur本地服务，固定catalog `qwen35-9b-q4km`、API model `qwen3.5-9b-q4-k-m`；4B仅作后续资源/质量对照。独立C#离线采集器及固定9B GPU输出已实际验证，v2的32题AI起草audit格式试运行全部格式有效，保持待审核身份；来源/许可工程核查已记录，正式训练/再分发审签仍待完成。随后对获准人工金标进行质量探索，经人工复核才进入训练器，教师标签不能充当封存真值。无法取得有效使用许可时继续使用获准人工数据；未知条件不阻断本地训练工具研发。
4. **训练路线。** 首轮冻结独立 encoder，初始化并训练最小可用的自有决策头和 scorer；按所选架构训练需要学习的题型向量及中间层，不能冻结随机初始化的完整 head 后仅更新 scorer，作为默认训练方案。先建立人工监督硬标签基线，再比较可选教师硬标签的收益。只有取得按固定候选映射计算的真实教师分数，才另测软分布蒸馏；文字中的自报置信度不作为概率标签。根据独立开发集结果决定是否扩展 head、采用 encoder LoRA 或研究新架构；全量 encoder 训练、大规模预训练和学生模型压缩仍是后续研究。Laya scorer 微调仅可作另行选择的过渡实验，其产物仍属 Laya 衍生模型。
5. **实现与部署。** 教师HTTP调用放在独立C#离线数据工具中，采集结果经审核后以文件交给C#训练器；Sezika核心及最终推理保持本地运行。正式训练、批量教师采集和批量特征计算使用GPU。冻结encoder的自有marker head已有C# CE/gradient/SGD静态PTX、GPU特征导出与train-original-gpu接线证据；encoder反传、正式数据训练、训练AOT与质量仍须独立验收。新权重使用独立身份、manifest/hash和受限加载入口，原Laya校验及数值参考保留。
6. **协议与迁移。** 复用已审核的 C# 工程能力，定义 Sezika 自有 request/result、序列化、候选映射、special token、长度及拒答合同；内部格式依据新模型训练设计确定，不强制匹配 Laya prompt/schema。Laya 支持仅作为显式选择的 legacy 适配，旧资产身份和历史报告保持真实。新模型通过独立质量与部署验收后再切换默认路径，届时 README、默认 CLI 和新模型卡使用 Sezika 术语及实际底座名称。完成标准是默认路径无需 Laya 包、资产、模型 ID 或兼容模式即可装载独立 encoder 与自有训练 head、完成本地类型化决策，并具备自身数值、质量、校准及 AOT 证据。

### 下一步优先级

| 次序 | 任务 | 具体工作 | 通过条件 |
| --- | --- | --- | --- |
| 0 既有基础 | S3-08,S3-09,S3-10,S4-04 | 复用已归档的原权重数值参考，保留适用的 C# 算子、资源管理和诊断工具 | 已有独立 Laya/C# CUDA 574条对照；不把它写成新模型或语言质量验收。实现变化时按影响范围回归，不无条件重复全量审计 |
| 1 已完成文档合同 | S4-14 | 审核原始 encoder/tokenizer 来源，确定最小自有 head、输入协议与资产身份，列出 Laya 依赖迁移清单 | [S4-14 审核与合同](docs/independent-model-contract-s4-14.md)记录固定来源元数据、许可待审、原始 pickle 转换门槛、三题型可训练 head 和迁移验收；实际资产证据另见独立报告 |
| 1 数据与诊断 | S4-05,S4-06 | 确定首批中英三题型任务，建立获准人工训练/开发数据及独立校准/封存测试，冻结分任务指标和错误切片 | 来源、标签、用途、家族隔离及人工审核可追溯；原模型基线与训练样本分离 |
| 1 并行工具研发 | S4-07,S4-12 | 已写三题型线性 marker head 的纯 C# 前反传、有界预计算特征 SGD、hash 绑定 checkpoint、独立 decision-v1 序列/特征导出和推理 head asset | [独立证据](docs/evidence/s4-marker-head-prototype-2026-09-30.md)：350 项测试通过；真实 encoder 12 条原创开发 smoke 已完成，指标仅作小样本开发记录 |
| 2 独立资产与输入 | S4-15 | 导入审核后的原始 encoder，实现自有输入路径和显式 legacy 选择 | `IndependentModelLoader` 已验证独立身份、hash、134 tensors、special IDs、严格 decision-v1 和真实 CPU forward；缺独立上游数值 oracle，默认切换仍须 S4-08 |
| 并行可选教师入口 | S4-10 | 固定IoTSharp/Tomur本地9B教师身份与C#采集器，完成正式输出训练许可审签；4B仅作对照 | 固定GPU格式试运行已通过；许可工程记录与正式训练审签区分，服务仅在外部工具边界 |
| 后续可选教师数据 | S4-11 | 先对最多32条新人工金标做有界教师格式/质量探索，经人工复核后才交训练器；扩量另定预算 | 记录全部成功、失败、拒答、资源用量和人工复核；只将获准记录纳入目标 split，不以教师答案替代独立测试真值 |
| 3 自有 head 真实实验 | S4-07,S4-12 | 冻结独立 encoder，比较仅人工训练和可选教师硬标签训练；Laya 仅作另列对照 | 同任务/评测合同/开发集比较，模型内部输入格式分别记录；保存全部实验身份、成本及正负结果；数据和工具准备完成后执行 |
| 4 质量、校准与部署验收 | S4-08 | 在独立 calibration split 拟合策略，按冻结合同评价新权重并验证部署 | 分语言/题型报告准确率、负类召回、NLL/Brier、Score MAE、覆盖率/风险与置信区间；新 profile 和两 RID AOT 证据独立记录，无 Laya 资产环境可运行 |
| 5 条件扩展 | S4-13；可选 S4-09 | 按开发证据扩展自有 head/LoRA；另行研究其他检查点及 Laya 对照 | 独立开发证据说明扩展必要性；每项新增资产/算子有独立训练、数值、质量、资源及部署验收 |
| 并行维护 | S3-06,S5-06,S5-07 | 补既有路径逐层阈值、近并列和新 tokenizer AOT；按实测瓶颈推进性能 | 不阻断独立模型合同和极小训练原型；真实训练按实测成本设预算，新权重交付仍须自身数值与 AOT 门槛 |

数据准入和真实质量门槛约束正式实验及交付；工具合同、极小梯度原型与保存加载实现可以先开展。所有生成/采集/训练循环必须有最大样本/请求/重试/step、单项与总墙钟上限、取消和进度；真实调用前先核对循环比较条件，并从极小输入验证。实际执行继续遵守 AGENTS.md 的授权、进程记录与清理规则。

后续工作以本路线图的最新方向、任务和依赖为准；[工作推荐模板](docs/next-work-prompt.md)不是阶段计划，其未跟踪内容不在本次 S4-14 修改范围，也不授予执行训练或教师调用的权限。

### 当前证据与历史修复

2026-09-26 的 [S3/S4/S5 续验证](docs/evidence/s346-continuation-2026-09-26.md)已经将独立 Laya/C# CUDA 比较扩展到 PAWS250 + Nimble324，共574/574通过；双方实际正确数分别为170/250与137/324，Boolean负类召回仍为1/57。外部题集没有逐语言标签，原创中英fixture每种语言只有6条；这些证据不能关闭代表性语言质量或校准任务。该历史验证不包含本轮新增的真实独立 scorer/head 反向传播、受限新权重加载和原创开发 smoke；教师采集工具仍未实现。

该轮全量参考发现并修复 added-token 边界缺陷，保留了初轮失败和超时，修复后通过267条独立 tokenizer 微型参考与受影响案例三后端回归。18组/54个核心输出及972次内部张量诊断保留各自程序身份；独立上游逐层张量/冻结阈值、真实近并列及新 tokenizer 实现的两 RID AOT 仍未验收。

2026-09-26 已完成的 [S3-10 两 RID AOT](docs/evidence/input-aot-2026-09-26.md)限定于其记录的较早实现哈希：SIMD/CUDA 各38条回答与4条非法拒绝对齐、4条数量合同差异保留，scalar 各3条长输入通过，输入检查各138项通过。不能将这份结果自动转移到后续 tokenizer 修复或新训练权重。

2026-09-25 对 [Laya 0.3.20 固定源码](docs/laya-upstream-review-2026-09-25.md)的复核修正了题型前缀、候选渲染/顺序、JSON 文本及256前缀/1024总长度语义。旧 [PAWS/Nimble 报告](docs/evidence/quality-2026-09-25.md)继续限定于旧输入实现；当前共享构造器和固定参考已具备支持范围的证据，4条候选数量合同差异仍保留。原权重、tokenizer 与配置内容未变，无需为输入对齐重新下载模型。

原权重的**同检查点、同输入、同策略对齐**保留为既有模型的独立证据；自有模型使用自身同权重数值参考、简单基线和独立数据验收。Laya 对照与 Router 多检查点研究均为可选，不是独立 head 原型或正式训练的前提。训练、提示、阈值和教师采集均不能用已查看的 PAWS/Nimble 逐题结果选参。所有模型推理与训练交付仍使用 C#，参考 Python 代码仅用于离线数值 oracle。

## 编号任务板

任务使用 `S0`–`S6` 阶段编号和两位序号。状态只表示当前证据：`✅ 已完成`、`🚧 进行中`、`⏳ 计划中`、`⛔ 阻塞`。同一泳道内的任务可以并行；跨泳道按依赖推进。实现、真实推理、质量、AOT 和性能分别验收，不能用构建成功替代真实模型证据。

| 编号 | 泳道 | 状态 | 任务 | 依赖 | 验收产物 |
| --- | --- | --- | --- | --- | --- |
| S0-01 | A 契约 | ✅ 已完成 | 固定模型身份、请求/响应、错误码、概率/集中度/拒答语义 | — | `src/Sezika` 契约与架构文档 |
| S0-02 | A 契约 | ✅ 已完成 | 冻结纯 C#、CUDA Driver 例外、AOT 与不使用 native 数值库的边界 | S0-01 | [GPU/AOT 设计](docs/gpu-aot.md) |
| S1-01 | B 资产 | ✅ 已完成 | 锁定 mmBERT/Laya revision、许可证、model.json v1 与 tensor hash | S0-01 | `model-manifests/laya-mmbert` 与模型来源记录 |
| S1-02 | B 资产 | ✅ 已完成 | 实现 tokenizer JSON、Unicode/byte fallback 规则和中英/CJK/RTL oracle | S1-01 | `tests/fixtures/tokenizer-mmbert-oracle.json` 与 tokenizer smoke |
| S1-03 | B 资产 | ✅ 已完成 | 实现 SafeTensors F16/F32/BF16 读取及重叠、越界、dtype、hash 负向校验 | S1-01 | `ModelAssetVerifier`、`SafeTensorReader` 与负向测试 |
| S1-04 | B 资产 | ✅ 已完成 | 完成发布目录、断点/下载、安装清单和模型卸载生命周期 | S1-01,S1-03 | `ModelPackageStore`、loopback Range 断点/校验、安装清单、staging 修复和 lease 卸载测试；不下载真实模型 |
| S2-01 | C CPU | ✅ 已完成 | 完成 scalar embedding、attention、RoPE、norm、MLP、mask 与形状检查 | S1-01,S1-02 | 真实 encoder smoke 与配置校验 |
| S2-02 | C CPU | ✅ 已完成 | 固定小模型和真实模型的逐层/逐算子 oracle 夹具 | S2-01 | trace 文件、误差容差与生成版本 |
| S2-03 | C CPU | ✅ 已完成 | 建立 SIMD/scalar 对齐、取消、deadline、工作空间和并发矩阵 | S2-01,S2-02 | `EncoderWorkspacePool`、Vector<float> kernels、逐 trace 对齐与有界资源测试；S5-04 性能矩阵另行验收 |
| S3-01 | C 推理 | ✅ 已完成 | 实现 marker 序列、Choice/Score/Boolean、稳定 softmax 和 expected score | S2-01,S2-02 | 三种 primitive 的 typed CPU smoke |
| S3-02 | C 推理 | ✅ 已完成 | 实现输入结构校验、重复属性拒绝、候选/token/问题预算和结构化错误 | S0-01,S1-02 | `DecisionRequestParser` 与限制负向测试 |
| S3-03 | C 推理 | ✅ 已完成 | 建立逐 primitive logits、概率、legend 和 abstention 的比较契约 | S3-01,S3-02 | `PrimitiveAlignment`、4 个合成固定输入 fixture；不代表真实 Laya 推理对齐 |
| S3-04 | C 资源 | ✅ 已完成 | 完成 session busy、micro-batch、取消、deadline、unload 和内存上限矩阵 | S3-01,S3-02 | session gate、每问题 bounded micro-batch、workspace/resident budget、取消/deadline/unload 测试证据 |
| S3-05 | C 独立使用 | ✅ 已完成 | 提供独立模型 session 与 inspect/predict CLI，读取 JSON 并输出真实 typed decision | S3-01,S3-02,S3-04 | `DecisionModelRuntime`、CPU CLI、中英文三种问题的真实模型文件/stdin 推理、并发卸载回归和结构化错误；[运行证据](docs/standalone-cli-smoke.md)，正式发布归 S6-02 |
| S3-06 | C 数值诊断 | 🚧 进行中 | 在输入契约修复后核对 scalar/SIMD/CUDA 的短、中、长 marker logits 与边界附近预测 | S3-09,S3-10,S5-02 | [本轮续实现](docs/evidence/s346-continuation-2026-09-26.md)：核心18组/54个三后端输出通过冻结合同，972次内部完整张量诊断；首次1740秒超时保留，再以同程序补齐5组。全量质量参考发现added-token边界缺陷，修复后267条独立tokenizer参考、137项输入契约及受影响例三后端通过。当前诊断合同新增已知辅助 trace、未知 checkpoint 拒绝、快照隔离、坏 reference 结构化失败和跨报告未处理 ID 校验；本轮进一步拒绝非 core ID 和伪 complete/incomplete 清单，自检29项；独立层张量/阈值、真实近并列及新tokenizer实现的两RID AOT仍未验收 |
| S3-07 | C 上游审阅 | ✅ 已完成 | 固定 Laya 0.3.20 源码与同权重资产，审计输入、预算、解码及加速路径 | S1-01 | [源码审阅记录](docs/laya-upstream-review-2026-09-25.md)；只读分析，未运行上游模型 |
| S3-08 | C 参考 oracle | ✅ 已完成 | 固定 Laya 代码/依赖与已锁定权重，离线导出三种题型的 token IDs、marker、raw logits、概率和失败语义 | S3-07 | [固定真实捕获及执行证据](docs/evidence/laya-oracle-2026-09-25.md)：46/46，42 answered、4非法拒绝，覆盖三题型×中英×短中长及顺序/边界；容差未变；[比较器负向回归](docs/evidence/oracle-comparator-2026-09-25.md)通过 |
| S3-09 | C 输入契约 | ✅ 已完成 | 对齐题型前缀、Choice/Score/Boolean 候选渲染及顺序、state/说明序列化和 mask 文本处理 | S3-08 | [共享输入构造器](docs/input-contract-s3-09.md)与[真实数值证据](docs/evidence/laya-parity-2026-09-25.md)：38条支持范围内逐token/marker/label相同，SIMD/CUDA数值通过，4条非法拒绝一致；4条数量合同差异明确保留，不宣称全上游合同兼容 |
| S3-10 | C 长度语义 | ✅ 已完成 | 拆分 256-token 前缀预算和 1024-token 总长度；按字符串/对象/对话核对截断方向，显式区分兼容截断与严格拒答 | S3-08,S3-09 | 默认strict与显式laya_compatible、轻量诊断、256/1024边界及左右保留方向已验证；[两RID Native AOT](docs/evidence/input-aot-2026-09-26.md)各138项输入检查，SIMD/CUDA各38回答+4拒绝对齐、scalar各3条960/1024-token长输入通过；[token覆盖率](docs/evidence/laya-coverage-2026-09-25.md)另列；不代表更大head、质量或性能验收 |
| S4-01 | D 质量 | ✅ 已完成 | 提供二分类线性头示例 trainer、温度拟合与 accuracy/F1/NLL/Brier/ECE 评估器 | S3-01 | 可复现示例 trainer/metrics；该 trainer 不更新真实 marker head |
| S4-02 | D 质量 | ✅ 已完成 | 建立中英测试/校准协议夹具、来源与隔离清单 | S4-01 | 24 条原创中英 fixture、数据卡、SHA-256 manifest、split/entity/fingerprint 隔离与有界验证脚本；无真实训练集，fixture 不代表代表性质量 |
| S4-03 | D 质量 | ✅ 已完成 | 建立初始校准 profile 合同、安全加载器与冻结门槛 | S4-02 | 12 个初始 prompt-v1 的 hash 绑定 profile，保持 `pending_measurement`；当前 prompt 或新权重的 profile 须另建并实测，不沿用旧绑定 |
| S4-04 | D 审计基线 | ✅ 已完成 | 对修正后的相同权重/输入分别运行 Laya 与 Sezika，建立可比较质量和覆盖率审计基线 | S3-08,S3-09,S3-10 | [2026-10-07闭环证据](docs/evidence/s4-04-baseline-closure-2026-10-07.md)：冻结索引、六份质量报告与16份完整比较的身份/分母/指标关联通过，586条历史审计基线可自动复核；17项评估器自检、16项正负回归通过，构建零警告/错误。原始捕获/模型已清理，本轮无新推理。双方PAWS170/250、Nimble137/324、中英fixture8/12正确；近并列命中0、Boolean负类召回1/57、外部语言未标注。代表性质量与校准继续由S4-05/S4-08验收，旧strict报告单列 |
| S4-05 | D 数据 | 🚧 进行中 | 审核来源许可，构建独立训练/开发/校准/封存测试及多语言反事实流水线 | S0-01 | `tools/Validate-S4Data.ps1` 已提供有界 manifest、许可/用途、split、实体/家族/近重复、衍生父链、污染和封存绑定审计；项目自有 12 条 train/development smoke 已入库并绑定 records/manifest hash；正式许可复核、calibration/sealed test、污染检查和人工封存仍未完成；PAWS/Nimble 只作 audit |
| S4-06 | D 诊断 | 🚧 进行中 | 在新独立开发集建立目标任务基线，按角色、否定、词面相似性、语言、题型与候选顺序诊断 | S4-05；独立模型基线：S4-15；Laya对照可选 | `IndependentModelTool evaluate-original` 已实现有界 development 评估，输出逐题候选顺序、覆盖率、准确率、NLL/Brier/ECE、margin、宏 AUROC 和 Wilson 置信区间，并按语言/题型分组；角色/否定/词面相似性在记录未提供审核标签前明确不推断；仍只有 6 条开发 smoke，未形成 calibration、代表性质量或封存测试证据，详见 [S4-06 评估合同](docs/independent-development-evaluation-s4-06.md) |
| S4-07 | D 自有head训练 | 🚧 进行中 | 用C#实现独立encoder上的自有head/scorer前反传与初始化；支持完整逐题特征缓存及人工/可选教师硬标签实验 | 工具：S4-14；真实实验：S4-05,S4-06,S4-12,S4-15 | 原12条开发smoke/24步人工硬标签及head asset保留；[GPU CLI](docs/evidence/gpu-training-cli-2026-10-08.md)已接GPU encoder特征与CE/gradient/SGD，CPU合同2→22/GPU导出8→12/旧CUDA59通过，真实48-token encoder smoke资源归零。正式获准数据训练、收益、质量及训练AOT仍未验收 |
| S4-08 | D 质量门槛 | 🚧 进行中 | 验收独立模型数值、质量、校准/拒答profile与部署；既有Laya证据单列 | S4-06,S4-07,S4-12,S4-15 | S4-04仅闭合同权重审计基线，既有Laya与独立模型的代表性中英质量及正式校准均未验收，数据准入/封存由S4-05承接。[校准续审](docs/evidence/calibration-continuation-2026-10-03.md)补基线/封存 split/有限值门槛及全样本/拒答分母；真实验收 pending。自有模型封存测试按覆盖率、balanced accuracy/负类召回、AUROC、NLL/Brier/ECE、Choice/Score、逐语言及反事实对报告，含置信区间和回退；自身同权重参考、CPU/CUDA与两RID AOT回归，不提供Laya资产的环境仍能运行 |
| S4-09 | D 可选对标 | ⏳ 计划中 | 按产品需要审计其他检查点的C#支持；Laya Agent/Router仅作可选对照 | S3-07,S4-04仅限Laya对照；新增资产独立审核 | 各资产许可/revision/hash、显式模型选择与同路由同题集报告；不阻断独立模型主线，新增资产另验数值/AOT/性能/校准 |
| S4-10 | D 教师入口 | 🚧 进行中 | 核对IoTSharp/Tomur本地服务、catalog `qwen35-9b-q4km` 的实际模型身份/接口/错误合同、资源预算及输出训练/再分发许可；4B仅作资源/质量对照，设计独立C#离线采集工具 | S0-02,S4-05数据合同 | C#采集器、固定9B资产和GPU runtime已验证；[v2实测](docs/evidence/teacher-gpu-prompt-v2-run-2026-10-08.md)为32题全格式有效、34次HTTP/无retry、151引用通过，服务/资源已关闭。旧13通过/19失败保留；正式训练/再分发许可审签、人工正确性及质量待验，native runtime仅限外部教师工具 |
| S4-11 | D 教师数据 | ⏳ 计划中 | 对最多32条新人工金标有界采集教师硬标签，人工复核后才交训练器；与仅人工训练做对照，软分布后续另验 | S4-05,S4-10 | 扩量前冻结样本/请求/重试/墙钟/资源上限；保存来源、完整输入合同、模型/提示/响应hash、家族及审核链；失败/拒答保留分母，教师标签不作封存测试真值，自报置信度不作软标签 |
| S4-12 | D 训练资产 | 🚧 进行中 | 实现自有head checkpoint、训练恢复状态、版本化manifest及受限导出/加载入口；显式区分独立与Laya衍生资产 | S4-14,S1-03,S3-05；与S4-07工具研发同步 | CPU v2与[GPU schema3 workflow](docs/evidence/gpu-training-workflow-2026-10-08.md)绑定完整模型/数据/特征、kernel/UUID及恢复元数据，GPU CLI校验五项恢复输入；fixture恢复一致与拒绝篡改通过。正式模型包、校准、训练AOT及发布仍未完成 |
| S4-13 | D 条件适配 | ⏳ 计划中 | 首轮自有head报告后按开发证据扩展架构，必要时研究C# encoder LoRA | S4-06,S4-07,S4-12 | 按所选架构记录参数与新增反向算子梯度检查、资源/恢复证据及独立消融；GPU反向另行实现与验证；无收益也保留报告，正式交付走S4-08 |
| S4-14 | A 独立合同 | ✅ 已完成文档审核/设计 | 审核原始encoder/tokenizer来源，定义自有head初始化、输入协议、资产身份与Laya迁移边界 | S0-01,S0-02 | 合同已固定原始 revision、许可边界、pickle 转换门槛、三题型 head、自有协议和迁移清单；实际资产/训练证据转入 S4-15/S4-07 |
| S4-15 | B 独立入口 | 🚧 进行中 | 实现原始encoder受限导入、自有输入构造与显式legacy适配，将Laya固定校验限定在旧模型入口 | S4-14,S1-03,S3-05；checkpoint接入：S4-12 | 固定原始资产/hash、C#受限转换134 tensors、独立loader/decision-v1真实CPU smoke及267项数值capture已验证；[静态pipeline](docs/evidence/independent-pipeline-2026-10-08.md)完成Windows AOT tiny合同，[GPU CLI](docs/evidence/gpu-training-cli-2026-10-08.md)完成真实encoder特征smoke，[Windows诊断AOT](docs/evidence/independent-aot-smoke-2026-10-08.md)完成真实encoder+未训练内存head的46-token CUDAcase及资源归零。可信上游oracle、正式trained-head/两RID AOT、质量和默认切换仍由S4-08验收 |
| S5-01 | E GPU/AOT | ✅ 已完成 | 生成带 hash/ABI 的 C# kernel PTX 与静态 Driver loader | S0-02 | 14 个 kernel manifest/PTX 和生成复现记录 |
| S5-02 | E GPU/AOT | ✅ 已完成 | 完成 resident CUDA encoder/head 与 CPU logits 对齐 | S2-01,S5-01 | RTX 4070 实卡 head 误差报告 |
| S5-03 | E GPU/AOT | ✅ 已完成 | 完成 tiny CPU/CUDA win-x64 与 CPU linux-x64 Native AOT smoke | S5-01 | 发布物、运行输出、依赖图 |
| S5-04 | E 性能 | ✅ 已完成 | 建立 scalar/SIMD/量化 CPU 与 CUDA 冷/热、H2D、kernel、端到端基准 | S2-03,S5-02 | Windows 四后端、固定模型/hash/硬件/线程、1/8/32 问各 5 样本，冷启动/重载、p50/p95/p99、吞吐/分配/RSS/显存及独立 CUDA 分项；[实测与限制](docs/s5-performance-aot.md) |
| S5-05 | E 性能 | ✅ 已完成 | 完成真实模型 CPU/CUDA AOT、多 RID、显存/内存回收和失败诊断 | S3-04,S5-02,S5-03 | win-x64 / Ubuntu WSL2 linux-x64 四后端真实模型 AOT，预取消/执行中取消/失败恢复、两轮 unload 和对象回收；[8 份报告矩阵](docs/evidence/s5-2026-09-24/summary.json) |
| S5-06 | E 性能画像 | 🚧 进行中 | 对现有及对齐后序列建立短/中/长、1/8/32 问 CPU/CUDA 成本画像 | S5-05 | [画像校验续审](docs/evidence/performance-continuation-2026-10-03.md)加固矩阵、原始JSON类型/重复字段、覆盖率/吞吐分母、身份及分项范围，仅重验历史报告，无新增性能测量；[schema v3续验证](docs/evidence/s346-continuation-2026-09-26.md)：区分进入/完成forward，3秒CUDA负例为3进入/2完成/0正式样本且回收通过；本轮SIMD long-1单样本42.183秒，估算long-32两遍超1800秒上限而未启动，CPU完整矩阵仍未验收。CUDA long-32 end_to_end单样本38.319秒，分项明确未测。此前[九格画像及CPU300秒失败](docs/evidence/s5-profile-2026-09-26.md)保留；普通.NET小样本不能证明稳定尾延迟、AOT性能或优化收益 |
| S5-07 | E 性能实现 | ⏳ 计划中 | 在对应模型数值质量门槛下逐项优化 C# CPU/CUDA 算子、工作区和有界逐题批处理 | S5-06；既有模型：S3-06,S4-04；独立模型：S4-08 | blocked/tiled GEMM、归约/融合/launch、受限批处理逐项消融；固定质量不降、成本改善，取消/卸载与两 RID AOT 回归；共享 state 改变语义须另立模型 |
| S6-01 | F 发布 | ✅ 已完成 | 生成带 README、许可证、NOTICE 和第三方声明的开发 NuGet 包 | S0-02 | `.artifacts/packages/Sezika.0.1.0-dev.nupkg` |
| S6-02 | F 发布 | ⏳ 计划中 | 完成独立模型默认入口、正式版本、CLI、模型卡/数据卡、签名、NuGet 发布和示例 | S4-08,S4-15,S5-07 | 发布清单、签名校验、跨平台包与文档；默认教程/CLI采用Sezika术语及独立模型，Laya限于历史、来源与可选legacy说明；声明实际支持的检查点与质量范围 |
| S6-03 | F 集成 | ⏳ 计划中 | 为 IoTSharp 组织项目提供进程内适配和独立服务试点 | S4-08,S4-15,S5-07 | 保持核心无业务执行器；Tomur/IoTSharp/SonnetDB 等试点合同、版本化 HTTP API、鉴权/限流/租户预算、负载与失败回退证据 |

S5-04、S5-05 已在固定硬件与两个 RID 的记录范围内闭环；S5-06、S5-07 是新增性能工作。Linux 使用 Ubuntu WSL2 smoke，未测量裸机 Linux 性能。S6-02 正式发布仍未完成。S4-03 的 profile 仍保持 `pending_measurement`，不把 fixture、数值对齐或性能基准写成多语言质量已达标。

质量、训练、性能和生态的背景研究见 [下一阶段研究](docs/next-stage-research-2026-09-25.md)，以上执行表是唯一当前阶段计划。该研究文档保留原日期的背景及旧结果，当前优先级采用2026-09-30校核后的独立模型/自有head与可选离线教师路线。S4-04已于2026-10-07闭合既有同权重审计基线，代表性语言质量与校准仍归S4-05/S4-08；S4-14已完成文档审核/设计，S4-15已有原始资产受限导入、decision-v1 CPU smoke 和独立 loader 证据，S4-07/S4-12已有自有 head 原型、checkpoint/asset 保存加载与开发 smoke；S4-10至S4-11、S4-13以及独立模型的质量、许可和 AOT 门槛仍未完成。任何竞品分数、论文加速比或云端算力规格均不能替代本项目实测。

## 0. 研究与契约

- 保留早期固定 Laya 源码提交与 TypeSafe Jev 官方协议的研究记录；独立路线采用自有协议和另行审核的模型来源，区分公开模型实现、客户端 SDK 与商业 API。
- 新仓库默认使用 Apache-2.0 许可；上游代码、权重、tokenizer、数据分别记录归属。
- 制定有界 request/result、错误、模型身份、概率、分布集中度、校准与拒答语义。
- 定义自有协议版本；初始 C# 接口允许在 0.x 阶段经记录后调整，1.0 前冻结。
- 名称 Sezika 是自造品牌名，灵感来自“直觉式判断”；当前 GitHub/NuGet 检索不构成商标或全局唯一性结论。

## 0.1. ✅ GPU / AOT 最小可行性关口

用户要求 GPU 支持且保持模型、算子与调度源代码纯 C#，运行时只允许系统/显卡驱动例外。采用 **C# kernels → 构建期 ILGPU → PTX + ABI manifest → Native AOT C# CUDA Driver loader** 的设计。原版 ILGPU 常规运行时依赖 IL 读取与 Reflection.Emit，不能直接纳入 AOT 运行路径。

完整 GPU encoder 开发前，用 vector add 和小型 GEMM 验证构建产物、参数布局、实卡 launch、结果、资源回收及 AOT 依赖图。该最小关口已在 RTX 4070 Laptop GPU（driver 596.08、CC 8.9）通过，并有 win-x64 Native AOT smoke；完整 encoder/head 的逐算子数值差异和性能矩阵仍按 [GPU / AOT 设计](docs/gpu-aot.md) 的边界记录，不能由本关口推导为阶段 5 已完成。

## 1. 模型资产与 tokenizer

1. 首个已验证资产是 Laya 多语言检查点；独立主线按 S4-14/S4-15 审核并直接导入原始 mmBERT-base 或其他合适 encoder，重新初始化自有 head。各自固定模型 revision、encoder 配置、tokenizer 及 head checkpoint，逐项完成许可审核后才下载或再分发，不能将旧资产改名充当新来源。
2. 设计 `model.json` schema v1：architecture、dtype、tensor layout/shards、SHA-256、tokenizer pipeline、特殊 token、context/head budget、question schema、校准 profile、来源与许可。模型 schema 与 HTTP schema 独立版本化。
3. 直接支持参考加载路径中的 `model.safetensors` 与所需 dtype/布局。禁止推理进程反序列化任意 Python pickle；若后续新增来源只有 PyTorch checkpoint，由隔离的开发转换步骤导出并生成 hash，发布物仅接收转换后的安全格式。
4. 实现 tokenizer JSON 中实际用到的 tokenizer pipeline；拒绝未知步骤。保留 Unicode、emoji、组合字符、CJK、RTL、混合脚本和 byte fallback 的精确行为，禁止凭语言猜测替换分词规则。
5. 限制 metadata 大小、维度乘积、张量数、文件数与总内存；拒绝路径穿越、链接逃逸、重叠/越界 tensor、缺片、hash 不符和不支持的 dtype。
6. 初始预算草案：请求 JSON ≤1 MiB、深度 ≤32、每次 1–32 问、choice 2–32 项、score 2–10 级；实际 token 上限取已验证模型配置与用户预算中较小者。超过上限返回诊断，不静默截断选项。所有数值在阶段 3 的资源评测后冻结。

验收：固定的中英及混合脚本样本 token IDs 与参考实现逐个一致；恶意/损坏资产在分配大张量前被拒绝。初始不承诺 255 选项或 32k/64k 上下文。

## 2. 纯 C# encoder

1. 以标量 FP32 为正确性参考，再加入 SIMD。完整实现所选配置所需的 embeddings、RoPE、全局/局部双向 attention、mask、LayerNorm、激活和 MLP；不能把 decoder-only KV-cache 逻辑直接当 encoder 使用。
2. 固定 special tokens、位置编号、RoPE theta、attention scaling、local window、norm epsilon、激活近似、tensor 转置与 bias，按真实配置读取。
3. oracle 覆盖 tokenizer → embedding → 每层 → encoder 输出；先使用小型确定性权重，再使用真实 encoder。测试夹具记录来源、数值误差容差及生成工具版本。
4. 单请求工作空间、只读共享权重、有界并发；按 block 检查 CancellationToken 和 deadline。CPU 并行度受 session 与宿主总预算共同约束，避免问题批处理与矩阵乘法叠加线程。
5. GPU 原型通过后，用 C# kernels 实现 GPU encoder/head，复用相同张量契约；权重与中间数据留在显存，以 CPU oracle 对齐每个算子和整层。不能依赖 cuBLAS/cuDNN 等 native 计算库。

验收：每层输出与固定参考容差相符；取消、超时、异常与 unload 后无遗留请求或未释放工作空间。CPU 吞吐未测量前不承诺毫秒指标。

## 3. 决策头与推理闭环

1. 既有检查点已实现 question prompt、候选 marker 定位、type embedding、pre-norm Transformer head、marker scorer；独立模型按自有 schema 确定训练与推理一致的输入、候选定位和 head，固定 state 序列化、候选顺序与 question-to-batch 映射，不要求复制 Laya 文本模板。
2. 实现稳定 softmax、版本化温度校准、choice argmax、score 期望与 boolean 的 P(true)。Score 的第 i 个等级取数值 i（0 起），结果为 Σ i×pᵢ，范围 [0,K−1]，legend/probabilities 键为不带前导零的十进制 i。概率必须有限、非负且归一化；NaN/Inf、候选数不合法、重复 ID、未知类型和无效温度返回诊断。JSON 入口在反序列化成 Dictionary 之前检查重复 question/criteria 属性，不能指望后续 DTO validator 发现被覆盖的键。
3. 既有版本每问题重复编码 state，与其参考检查点保持一致；独立模型另行固定逐题序列合同，复用有界 micro-batch。一请求可能包含多次 micro-batch forward，报告实际次数与 token 用量，不宣传任意问题数量固定成本。
4. 概率与分布集中度分开返回。未校准标为 `uncalibrated`；校准不匹配标为 `out_of_scope`。决策状态为 `answered` 或 `abstained`，运行失败走结构化错误，不能填充示例概率。
5. 已提供源码运行的 `inspect`/`predict` CLI 与 `DecisionModelRuntime`：指定固定模型目录和 JSON 请求，使用 CPU 返回真实决策，JSON 使用 source generation。`inspect` 执行资产预检查；正式可安装 CLI 与完整 `doctor` 仍归 S6-02，独立使用见 [使用说明](docs/standalone-usage.md)。
6. decision engine 不生成自然语言回答、不执行工具。需要解释或开放式文本时，由调用方自行转入生成模型。

验收：三个 primitive 都有对应真实模型的 C# / 同权重参考输出对齐，独立模型使用自身协议及数值参考；预算与取消生效；缺模型、未知算子或架构不能隐式回退到旧模型、关键词、远端 API 或 native runtime。

## 4. 多语言训练与校准

1. 首批发布质量语言是中文与英文；候选扩展日语、韩语、德语、西班牙语、阿拉伯语及印地语。必须逐语言验收，不能把底座词表覆盖计为语言质量通过。
2. 主线使用独立来源的encoder与重新初始化的自有head，首轮C#冻结encoder、训练最小head所需各层及scorer；先做极小梯度与训练冒烟，再执行获准人工数据上的基线和可选教师实验。根据开发证据考虑更复杂head和encoder LoRA；完整encoder反向传播与大规模预训练另行排期。Laya及其scorer微调只作可选对照，衍生身份保留。
3. train、development、calibration、sealed test、audit按来源实体、家族和时间隔离，翻译/改写/反事实衍生物不跨split泄漏。原始来源许可、教师输出使用范围、去标识化及人工标签进入数据卡。已查看PAWS/Nimble及其衍生物仅作audit。
4. IoTSharp/Tomur本地服务作为可选离线教师入口，由独立C#工具采集并审核文件；`qwen35-9b-q4km`仅为待验证catalog候选，4B仅作对照，具体模型、接口及条款需核对，Sezika核心不依赖教师服务。先最多32条新人工金标、人工复核后再交训练器，扩量按显式样本/请求/重试/墙钟/资源预算执行，记录全分母、来源、模型/提示hash及进度，不能自动填成人工批准。
5. Choice/Boolean先建立监督交叉熵和教师硬标签对照，Score按等级语义比较交叉熵、Brier/ordinal proper-scoring及MAE。软分布需真实候选分数与固定映射，不能采用教师文字中的自报置信度；RLCD/策略梯度仅在可复现实验证明收益后采用。
6. 训练产物按S4-12独立保存模型、优化器状态及身份清单，经S4-15独立入口重新加载；原Laya权重/hash与参考结果保留在legacy入口。冻结特征缓存必须绑定完整逐题序列；训练自有head可缓存冻结encoder输出，另选scorer微调时可缓存冻结head后的marker向量，不能用单独state embedding替代cross-encoder序列。
7. 温度profile绑定新模型hash、tokenizer、当前prompt schema、primitive、候选规模、语言/领域和split，变更任一身份须重建。只在calibration split拟合，评价和运行时使用相同温度范围及拒答策略；现有pending_measurement profile不代表已校准。
8. 每语言/primitive/领域报告accuracy、macro-F1、负类召回、AUROC、NLL、Brier、ECE、score MAE、拒答覆盖率与selective risk；提供置信区间，保留失败与覆盖率分母。教师一致率与人工真值正确率分别报告，OOD和高置信错误单独统计；新权重交付通过相关CPU/CUDA、两RID AOT与资源回归。

初始质量门槛：在预先冻结的中英测试集上优于随机与多数类基线，并在规定数值容差内复现对应模型的同权重参考实现；独立模型不以Laya parity或Laya微调完成为前提。校准不使 NLL/Brier 明显恶化，自动路由阈值根据验证集的误路由成本与 coverage 冻结。具体质量目标由独立基线测量后确定，不复制上游宣传分数。

## 5. Native AOT、CPU/GPU 性能与资源

- 核心/运行时库开启 `IsAotCompatible`，应用关闭反射 JSON；静态类型注册、不动态加载程序集、不使用 JIT 表达式生成，不屏蔽 IL 警告。ILGPU 仅在单独的普通 .NET 构建工具中运行。
- 跨平台先覆盖 win-x64、linux-x64，随后 linux-arm64、osx-arm64；每个 RID 独立 publish 与运行 smoke。AOT 编译成功不等于模型推理成功。
- 启用 Vector/Intrinsics 前建立 scalar fallback；量化分别验证 encoder/head 的误差与质量变化。FP16/BF16 读取不代表对应 CPU 有原生加速。
- GPU 首先覆盖 NVIDIA CUDA Driver：由 C# 编译 PTX、静态 launch stubs、明确 ABI/hash/SM/驱动矩阵，分别优化 tiling、shared memory、fusion 和显存复用。CPU fallback 由显式策略控制且报告原因；不能将其他 GPU 厂商标为已支持。
- 322M 参数粗估 FP32 权重约 1.29 GB、FP16 约 644 MB，尚未计 tokenizer、head、activations 与工作空间；session 必须在加载前给出总内存估计，attention 不得无界生成大矩阵。
- 基准固定硬件、线程、精度、模型 hash、state 长度与候选规模。覆盖 1/8/32 问、冷启动/加载/热推理、p50/p95/p99、吞吐、分配、峰值 RSS/显存与取消延迟；GPU 分开计 driver 编译、H2D/D2H、kernel 和端到端时间。
- Laya T4 时间与 Jev 托管端到端时间仅为来源资料。项目性能结论必须来自 Sezika 自身测量。

## 6. 发布与后续研究

- 当前可构建带 README、许可证和第三方声明的开发包 `Sezika.0.1.0-dev.nupkg`；正式版本号、签名、NuGet 发布、CLI、模型卡/数据卡和真实模型跨平台发布仍未完成。
- 提供库、Native AOT CLI、模型卡、数据卡、校准报告和 C# 示例；代码/资产分别发布，不把权重打包进 NuGet 或可执行文件。
- 正式发布前核对名称、远端归属、包 ID 与签名/许可证信息。
- 离线教师硬标签蒸馏已纳入S4-10/S4-11当前计划；更小学生模型蒸馏、state编码共享、更多问题/候选、长上下文、AMD/Intel/Apple GPU、全量encoder训练与新架构仍为独立后续研究。共享state会改变cross-encoder语义，不能作为无损缓存直接加入。

## 工作量判断

最初的阶段1–6工程周估算针对从零实现推理路径，已不适合作为本次剩余工作排期。下一步按原始底座/自有协议导入、head梯度/优化器、特征导出与checkpoint加载、数据审核、可选教师采集和独立评测分别估算；先测小样本前向/训练step成本、峰值内存/显存、教师单次用量/费用与恢复，再决定数据规模和是否租用算力。当前没有真实训练吞吐、教师价格或新权重质量收益测量，不给出总训练时长/费用承诺。更复杂head、LoRA、学生压缩、全量encoder及新架构另行测量投入。
