# Sezika 全量待办监督核查（2026-10-07）

本核查以当前 ROADMAP 为唯一任务计划，覆盖 15 项未完成任务：S3-06、S4-05/06/07/08/09/10/11/12/13/15、S5-06/07、S6-02/03。它是只读核查证据，不另设阶段计划，不改变任务状态。核查时首批 CLI 和 checkpoint 子任务正在实施，本文不提前验收它们。

**结论：还有可立即交付的工程功能；真实数据准入、独立封存、上游数值参考、真实质量与部署不能靠工具自检关闭。** 主线无需等教师或 Laya 再训练。宜先闭环当前 CLI/checkpoint 子任务，随后补独立预测导出、审核切片标签、独立 CUDA 前向和独立画像入口；并行恢复固定原始资产、准备可信数值参考及人工准入材料。每个工程子任务审查与有界验证后可单独提交，所属整项仍按真实验收门槛保留状态。

## 已完成与待验的边界

- S4-14 已完成来源审核/合同设计；固定原始 mmBERT revision、转换格式、三行自有线性 marker head、严格 decision-v1 协议和独立身份。它不是许可发布审签或真实模型质量验收。
- S4-15 历史上已下载原始文件、校验 hash、C# 受限转换 134 个 encoder tensors，并运行真实 CPU smoke。本次核对固定路径 `.artifacts/models/mmbert-independent/converted/encoder.safetensors` 返回不存在。历史 hash、有限值和 min/max 不能代替上游 oracle，也不能验收新代码。
- S4-07/S4-12 已有三题型解析梯度、预计算特征 SGD、hash-bound checkpoint/独立推理 head asset。2026-09-30 的 12 条真实 encoder smoke、24 步训练及 development 4/6 是小样本历史开发记录；每个语言/题型开发分母为 1。当前首批子任务要补 CLI 级保存/恢复/导出闭环；提交前仍须具体结果与定向验证。
- S4-04 于 2026-10-07 闭合的是 586 条既有同权重审计报告的合同关联。原始捕获和模型已清理；本轮没有新推理。PAWS/Nimble 及其已查看衍生物继续只允许 audit。
- 2026-10-03 的 tiny CPU/CUDA、Windows AOT 合同验证和 2026-10-04 的独立 runtime/calibration 工具验证证明代码或 fixture 范围；不关闭独立真实模型的上游数值、语言质量、CUDA、两 RID AOT 或性能门槛。
- S5-06 历史 CUDA 九格完成、SIMD 八格及 long-32 300 秒超时保留。后续 schema v3 的少量实测与检查器重验不能组成完整 CPU 矩阵、AOT 性能或稳定尾延迟。S5-07 RoPE 因子表有数值一致的 tiny fixture，但没有同身份前后真实消融收益。

证据入口：[独立资产/head 证据](evidence/s4-marker-head-prototype-2026-09-30.md)、[统一续审](evidence/continuation-2026-10-03.md)、[独立 runtime](evidence/independent-runtime-facade-2026-10-03.md)、[校准工具](evidence/calibration-tool-2026-10-04.md)、[审计基线](evidence/s4-04-baseline-closure-2026-10-07.md)、[历史画像](evidence/s5-profile-2026-09-26.md)、[RoPE 候选](evidence/performance-optimization-s5-07-2026-10-03.md)。本次没有重新运行其中任何构建、测试或模型。

## 15 项逐项核查

| 任务 | 当前真实实现/证据 | 仍缺什么及下一步性质 |
| --- | --- | --- |
| S3-06 数值诊断 | 18 核心案例/54 后端输出、972 次内部张量诊断；tokenizer 修复后的受影响例回归；固定 574 条近并列观察命中 0 | 独立上游逐层张量及事先冻结阈值、真实近并列、当前 tokenizer 两 RID AOT。内部 scalar 自参照不是上游 oracle；0 命中是未测。先固定新的有限候选集与参考身份，保留全部观测，不能调阈值或合成边界输出。此项为 legacy 维护，不阻止独立工程研发。 |
| S4-05 数据 | 有来源/用途、家族/实体/父链/近重复/污染/封存绑定审计；12 条原创 smoke；16 条候选分成 6/6/2/2 | `audit-report-20260930.json` 明确 blocked：许可 1 项、人工复核 16 项、未封存 1 项。16 条合同槽位不能解释为正式质量数据。缺目标任务/用途冻结、真实来源许可审核、逐条人工金标/切片标签、污染检查、独立保管人新未见封存集与实际访问流程；由真实审核与保管事实闭环。可先准备审核材料与版本化数据合同。 |
| S4-06 开发诊断 | `evaluate-original` 已按语言/题型输出分布、覆盖率、准确率、NLL/Brier/ECE、margin、置信区间、适用 AUROC、负类与 Score 指标；候选顺序逐题保留 | 只有 6 条 development smoke。代码明确 role/negation/lexical_similarity 不可用；尚无版本化、人工审核的切片标签，也无按它们的分组/候选顺序干预报告。标签 sidecar 与指标工具可先开发；真实目标任务基线须 S4-05 与原始资产，不能从文本自动猜审核标签。 |
| S4-07 自有 head | C# 三题型线性 scorer/梯度、有限差分、确定性 SGD 和完整序列特征身份；历史 24 步人工硬标签 smoke | 首批恢复流程待验收。正式人工基线及可选教师硬标签对照仍缺获准 train/development 和冻结实验合同、完整身份/资源/正负结果；teacher 不是必需前置。更复杂架构或 GPU 反向不属于当前最小 head 的默认完成条件。 |
| S4-08 质量/校准 | 校准/拒答 profile 合同、正确分母/基线/Score MAE、hash-bound calibration/sealed-test 工具；fixture gate 明确 not_eligible | 缺真实独立同权重上游参考、封存质量/置信区间/反事实切片、独立 calibration 拟合与拒答策略、CPU/CUDA 与两 RID AOT。校准工具只消费已有预测，尚缺面向独立获准 split 的预测导出通路和完整 release identity。另需将已验 profile 接入独立 runtime；当前 facade 始终 uncalibrated。 |
| S4-09 可选对标 | ROADMAP 保留条件性候选；旧 Laya 对照已有独立审计证据 | 未选择新 checkpoint，也没有它的来源/许可、tensor/tokenizer 支持、数值/AOT/质量报告。需实际产品需求后选择；不能为“所有任务完成”随意增加模型或将可选项虚报为完成。现阶段不阻断主线。 |
| S4-10 教师入口 | TeacherTool 只做离线 catalog/最多 32 条记录合同检查，明确非独立验证和未获训练准入 | 没有真实采集器网络调用；IoTSharp/Tomur 实际配置、catalog 到 revision/hash 的身份、接口/错误、输出训练及再分发条款、资源预算尚未验证。先读明确服务配置/可信源，再实现外部 C# 采集器；不能猜 endpoint 或把目录 ID 当已安装模型。核心推理不引入教师。 |
| S4-11 教师数据 | 有 all-state 分母、失败/拒答/invalid 隔离、family split 规则及人工准入边界 | 最多 32 条真实有界采集尚未执行；没有原始 input/prompt/response 附件与 hash 闭环、资源用量、真实人工复核和与仅人工训练对照。依赖获准人工金标与 S4-10；不将自报置信度当软标签，不把教师标签当封存真值。 |
| S4-12 训练资产 | 二进制 checkpoint/head asset 有版本和 encoder/tokenizer/data/feature/file hash；错身份/形状/有限值拒绝 | CLI 保存/恢复/导出正实施。还缺正式独立训练包 manifest、绑定独立 head 和校准的 release identity/加载发布通路。优化器状态与推理资产分离；不能将临时 head 文件改名当正式模型版本。 |
| S4-13 条件适配 | 只定义必要时扩展自有 head/LoRA 的门槛 | 没有获准首轮目标任务开发实验来证明扩展必要性。应先报告线性 head 正负结果，再决定架构；新增参数均需梯度、资源、恢复和消融，GPU 反向另验。当前不应无依据启动 LoRA 或随机冻结额外层。 |
| S4-15 独立入口 | 独立受限 loader、固定来源/config/134 tensor 合同、严格 decision-v1、CPU runtime facade；首批显式独立 CLI 正实施 | 当前 `IndependentEncoderModelPackage.Encoder` 是 CPU `ModernBertEncoder`，runtime 在主机提 marker 后线性打分；旧 CUDA pipeline 强绑定 legacy 两层 head。缺独立 marker CUDA 前向及 runtime 接线、原始 tokenizer/encoder 上游 oracle、真实 CUDA/AOT。正式默认切换依赖 S4-08。 |
| S5-06 画像 | legacy 原始样本/身份/分项工具、部分真实矩阵及失败保留；schema v3 完成数/分母检查 | `Benchmarks.RunCycle` 仍由固定 legacy loader/head 构造，缺独立 decision-v1 画像入口。需先工具接线与 tiny 真实执行，再恢复权重按新 head 独立采样；CPU 完整矩阵、分项与稳定采样须在测得预算内完成，长行超时保留失败，不从估算填数。 |
| S5-07 优化 | CPU RoPE 因子复用候选；小 tensor 与直接 ScalarOps.Rope 最大差 0 | 未有收益结论。对应独立模型数值/质量通过后才能评估正式收益；仍可提前实现有证据依据的候选。优先独立画像锁定瓶颈，再针对 SIMD linear/attention、GPU launch/工作区等逐项消融，保持相同输入/identity/预算和取消卸载，并回归两 RID AOT。不能把代码存在等同优化通过。 |
| S6-02 发布 | dev NuGet、模型安装生命周期、legacy inspect/predict；显式独立 CLI 正实施 | 正式独立默认入口/版本、模型卡/数据卡、签名、跨平台包、独立模型校准/质量/性能及发布回执缺失。可先做 reviewable 本地包/文档与签名验证准备；当前用户要求实现与本地提交，并未明确授权 push、NuGet 发布或对外部署。发布前按当前明确授权边界处理。 |
| S6-03 集成 | 有本地独立 runtime，无现成 IoTSharp adapter/HTTP service 项目（当前 solution 仅核心、CLI、CUDA、kernels、smoke 与工具） | 可先实现进程内 adapter 或离线集成 fixture，将预测/拒答映射为无业务执行的 typed 结果。真正试点尚缺选定宿主、服务 API、鉴权/限流/租户预算、负载、失败回退及部署环境；生产试点依赖 S4-08/S5-07 和部署授权。 |

## 可立即接续的具体工程交付

以下是现有任务下的实施建议，最终排期和范围仍登记 ROADMAP。不要仅为保持智能体工作而反复增加同类 JSON hardening。

1. **独立获准 split 预测导出（S4-08/S4-15）。** 当前 `ReadOriginalRecords` 仅 train/development，`evaluate-original` 仅 development，CalibrationTool 只接受已经物化的 predictions。增加独立 C# 离线导出命令，将模型/head/tokenizer/protocol/data/admission 与每个原始预测文件绑定，保存全量回答/拒答/失败、原顺序、各语言/题型；真实封存由保管人执行，开发者只收不含题文的批准报告。先以明确 tiny fixture 验合同，不读取或批准真正封存题。可与其他工具文件独占并行。
2. **版本化审核切片及报告（S4-06）。** 增加 hash-bound sidecar 标签合同与 reviewed/unreviewed 状态，支持 role/negation/lexical_similarity/counterfactual family/candidate order 的可比诊断；缺标签保持未测。审核 ID 格式不能成为实际批准；fixture 验证实现，真实数据等人工签收。避免与 checkpoint 子任务同时编辑现有大 Program.cs，可新增独立模块后由主线程接线。
3. **独立 marker head CUDA 前向（S4-15）。** 在 Sezika.Cuda 新增 pipeline，复用 resident `CudaModernBertEncoder.RunOnDevice`、已有静态 C# PTX Linear launch；支持三题型行与 marker 提取，完整取消/忙状态/有限值/卸载。tiny 实卡对照只能验算子与工具；原始 encoder 真实数值及 AOT 留到恢复资产后。无需先实现 GPU 反向。
4. **独立画像通路（S5-06）。** 新入口使用独立 encoder/head/decision-v1，不以 legacy head 数值 smoke 充当独立性能；记录完整 identity、raw samples、discovery/stage 完成数，明确独立 pass 计时范围。先 tiny，再用恢复的固定权重有界短/中/长、1/8/32 采样。与第三项共享 runtime 接口的部分由主线程串行接线。
5. **校准应用与本地交付材料（S4-08/S4-12/S6-02）。** 新 independent release identity 独立绑定 encoder 与 head，默认 uncalibrated；只在加载到通过真实 gate 且身份匹配的 profile 后应用温度/拒答。先 fixture 与本地包/示例，不宣布校准或发布。
6. **进程内集成合同（S6-03）。** adapter 容纳 typed 请求/分布/Score 期望/Boolean P(true)、deadline/cancel/rejection/error 与模型身份；与宿主动作授权隔离。可以先实现 reviewable 契约与 synthetic integration，不在核心引入业务 executor；实际宿主接线需确定项目/接口。

恢复固定资产及上游 oracle 可与这些工程步骤并行准备。下载/构建/必要训练在本次用户与 AGENTS 授权范围内，不应逐命令再问许可；执行前仍核对当前來源、许可范围、revision/hash、资源与墙钟并记录进程。独立 encoder 已有 1.2 GB 权重身份，先检查准确固定路径/已有登记记录，不广域寻找。上游 oracle 必须是独立可信参考执行/捕获，明确固定依赖与算法，不能由 C# 生产路径输出自称 oracle。

## 最少的外部事实与用户决定

当前工程交付无需逐项确认。只有以下事项不能由智能体声称已经发生：

1. **数据/目标合同及实际人。** 首批业务任务/用途/接受范围、真实许可审核人和逐条标签复核人、独立封存保管人及非开发者可见的新未见测试来源。智能体可以准备待审核材料、固定格式和开发样本；不能虚构人名/签字/审核日期/封存过程。现有公开候选的测试槽位不能只改状态获得新未见资格。
2. **可选分支的明确目标/现成配置。** 若要实际执行 S4-09，给出需要支持的新 checkpoint；若启用教师，提供或读取可信的 IoTSharp/Tomur 配置、固定模型身份和实际许可条款；若做宿主试点，确定目标项目与接口。先推进无需这些选择的主线，不把可选分支作为总停工理由。
3. **最后对外动作授权。** 审核完成后再针对具体 reviewable 包、签名、版本及目的地处理 push/公开发布/部署；用户当前本地逐任务提交授权不能自动扩展成这些动作。此项不阻止本地实现、测试、打包和提交。

数值参考、资产重新下载、CUDA/AOT 验证和有界性能采样属于待执行的工程工作，本身不是必须让用户逐项决定的外部阻塞。需要新工具时先核对必要性与现有授权；不以缺权重或缺本地 fixture 作为永久阻塞，亦不为绕过准入而制造“人工批准”数据。

## 监督闭环与本次核查记录

监督者每次接续先读 ROADMAP/HANDOFF/Git 与活跃智能体状态，确认每个工作项只有一个文件写入者。子智能体完成后，主线程审查具体 diff、按影响范围串行有界验证、记录真实结果与未验收条件，再单任务提交。失败与预算不足原样留存；30 分钟 heartbeat 保持同一接续范围，已完成项派发下个独立交付，无实质可执行项时写明所缺事实，不重复空跑 validator。ROADMAP/HANDOFF 由主线程维护。

本核查使用固定 `C:\Program Files\PowerShell\7\pwsh.exe`，版本核对 7.6.6；只读核对文档、指定源码和配置，约 40 个不同文件，低于 100 文件上限。未读取真实封存题文，仅查看候选准入汇总报告。各命令实际均在 5 秒内返回；没有构建、下载、训练、网络请求或长期子进程，没有生成轮询/搜索循环，没有 Graphify 或广域工具发现。写入仅限本文件；没有创建临时文件，无需删除对象；没有修改共享 README/ROADMAP/CHANGELOG/HANDOFF、源码或已有用户文件，未 commit/push。审阅未逐字读取全部历史原始 tensor/模型/执行日志，结论以明确所读合同、源码与归档报告为范围，不声明本次重新验收模型。
