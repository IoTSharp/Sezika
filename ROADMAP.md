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

### 已选定方向

1. **独立模型来源。** 直接从经审核的原始 encoder 取得底座参数，优先评估 `jhu-clsp/mmBERT-base` 或同类可支持的编码器，独立记录 revision、配置、tensor/tokenizer hash 与许可；不能从 Laya 检查点抽取 encoder 后改称原始底座。本轮已完成固定 mmBERT 原始文件下载、hash、C# 受限 pickle 转换、134 tensor 形状检查和独立 loader smoke；上游逐层数值 oracle与许可发布审签仍未完成。自有决策头重新初始化和训练，不继承 Laya 训练后的 head/type/scorer 权重。
2. **任务与数据。** 先明确首批业务任务，中英分别覆盖 Choice/Score/Boolean 并建立基线和错误切片；具体领域、任务与样本仍待确定。人工标注数据作为基线，教师辅助标注或生成作为独立实验。分别管理 train、development、calibration、sealed test 和 audit，同家族、实体及翻译/改写/反事实衍生物保持隔离。已查看 PAWS 250/Nimble 324 及其衍生物继续仅作 audit。
3. **教师入口。** 可选教师改为 IoTSharp/Tomur 本地服务，首选待验证 catalog 候选 `qwen35-9b-q4km`，4B 仅作资源/质量对照；本机安装、模型身份、实际调用、输出格式、训练与再分发条款均未验证。独立 C# 离线采集工具在 Sezika 核心之外调用服务，先最多 32 条新人工金标作有界探索，经人工复核后才可进入训练器。无法取得有效使用许可时继续使用获准人工数据；未知条件不阻断本地训练工具研发。
4. **训练路线。** 首轮冻结独立 encoder，初始化并训练最小可用的自有决策头和 scorer；按所选架构训练需要学习的题型向量及中间层，不能冻结随机初始化的完整 head 后仅更新 scorer，作为默认训练方案。先建立人工监督硬标签基线，再比较可选教师硬标签的收益。只有取得按固定候选映射计算的真实教师分数，才另测软分布蒸馏；文字中的自报置信度不作为概率标签。根据独立开发集结果决定是否扩展 head、采用 encoder LoRA 或研究新架构；全量 encoder 训练、大规模预训练和学生模型压缩仍是后续研究。Laya scorer 微调仅可作另行选择的过渡实验，其产物仍属 Laya 衍生模型。
5. **实现与部署。** 教师 HTTP 调用放在独立 C# 离线数据工具中，采集结果经审核后以文件交给 C# 训练器；Sezika 核心及最终模型推理保持本地运行，不依赖教师 API。训练工具可先运行普通 .NET；GPU 训练仍需 C# 反向 kernels 与构建期 PTX/Driver 路径，已有 CUDA 前向验证不代表训练已支持。新权重使用独立身份、manifest/hash 和受限加载入口，原 Laya 校验及数值参考保留。
6. **协议与迁移。** 复用已审核的 C# 工程能力，定义 Sezika 自有 request/result、序列化、候选映射、special token、长度及拒答合同；内部格式依据新模型训练设计确定，不强制匹配 Laya prompt/schema。Laya 支持仅作为显式选择的 legacy 适配，旧资产身份和历史报告保持真实。新模型通过独立质量与部署验收后再切换默认路径，届时 README、默认 CLI 和新模型卡使用 Sezika 术语及实际底座名称。完成标准是默认路径无需 Laya 包、资产、模型 ID 或兼容模式即可装载独立 encoder 与自有训练 head、完成本地类型化决策，并具备自身数值、质量、校准及 AOT 证据。

### 下一步优先级

| 次序 | 任务 | 具体工作 | 通过条件 |
| --- | --- | --- | --- |
| 0 既有基础 | S3-08,S3-09,S3-10,S4-04 | 复用已归档的原权重数值参考，保留适用的 C# 算子、资源管理和诊断工具 | 已有独立 Laya/C# CUDA 574条对照；不把它写成新模型或语言质量验收。实现变化时按影响范围回归，不无条件重复全量审计 |
| 1 已完成文档合同 | S4-14 | 审核原始 encoder/tokenizer 来源，确定最小自有 head、输入协议与资产身份，列出 Laya 依赖迁移清单 | [S4-14 审核与合同](docs/independent-model-contract-s4-14.md)记录固定来源元数据、许可待审、原始 pickle 转换门槛、三题型可训练 head 和迁移验收；实际资产证据另见独立报告 |
| 1 数据与诊断 | S4-05,S4-06 | 确定首批中英三题型任务，建立获准人工训练/开发数据及独立校准/封存测试，冻结分任务指标和错误切片 | 来源、标签、用途、家族隔离及人工审核可追溯；原模型基线与训练样本分离 |
| 1 并行工具研发 | S4-07,S4-12 | 已写三题型线性 marker head 的纯 C# 前反传、有界预计算特征 SGD、hash 绑定 checkpoint、独立 decision-v1 序列/特征导出和推理 head asset | [独立证据](docs/evidence/s4-marker-head-prototype-2026-09-30.md)：350 项测试通过；真实 encoder 12 条原创开发 smoke 已完成，指标仅作小样本开发记录 |
| 2 独立资产与输入 | S4-15 | 导入审核后的原始 encoder，实现自有输入路径和显式 legacy 选择 | `IndependentModelLoader` 已验证独立身份、hash、134 tensors、special IDs、严格 decision-v1 和真实 CPU forward；缺独立上游数值 oracle，默认切换仍须 S4-08 |
| 并行可选教师入口 | S4-10 | 核对 IoTSharp/Tomur 本地服务、`qwen35-9b-q4km` 候选身份/接口与输出训练许可，形成 C# 离线采集合同；4B 仅作对照 | 本机尚未安装或调用候选；许可、模型身份、输出与错误语义明确后才能采集；服务只在外部工具边界，不进入核心推理 |
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
| S3-06 | C 数值诊断 | 🚧 进行中 | 在输入契约修复后核对 scalar/SIMD/CUDA 的短、中、长 marker logits 与边界附近预测 | S3-09,S3-10,S5-02 | [本轮续实现](docs/evidence/s346-continuation-2026-09-26.md)：核心18组/54个三后端输出通过冻结合同，972次内部完整张量诊断；首次1740秒超时保留，再以同程序补齐5组。全量质量参考发现added-token边界缺陷，修复后267条独立tokenizer参考、137项输入契约及受影响例三后端通过。独立层张量/阈值、真实近并列及新tokenizer实现的两RID AOT仍未验收 |
| S3-07 | C 上游审阅 | ✅ 已完成 | 固定 Laya 0.3.20 源码与同权重资产，审计输入、预算、解码及加速路径 | S1-01 | [源码审阅记录](docs/laya-upstream-review-2026-09-25.md)；只读分析，未运行上游模型 |
| S3-08 | C 参考 oracle | ✅ 已完成 | 固定 Laya 代码/依赖与已锁定权重，离线导出三种题型的 token IDs、marker、raw logits、概率和失败语义 | S3-07 | [固定真实捕获及执行证据](docs/evidence/laya-oracle-2026-09-25.md)：46/46，42 answered、4非法拒绝，覆盖三题型×中英×短中长及顺序/边界；容差未变；[比较器负向回归](docs/evidence/oracle-comparator-2026-09-25.md)通过 |
| S3-09 | C 输入契约 | ✅ 已完成 | 对齐题型前缀、Choice/Score/Boolean 候选渲染及顺序、state/说明序列化和 mask 文本处理 | S3-08 | [共享输入构造器](docs/input-contract-s3-09.md)与[真实数值证据](docs/evidence/laya-parity-2026-09-25.md)：38条支持范围内逐token/marker/label相同，SIMD/CUDA数值通过，4条非法拒绝一致；4条数量合同差异明确保留，不宣称全上游合同兼容 |
| S3-10 | C 长度语义 | ✅ 已完成 | 拆分 256-token 前缀预算和 1024-token 总长度；按字符串/对象/对话核对截断方向，显式区分兼容截断与严格拒答 | S3-08,S3-09 | 默认strict与显式laya_compatible、轻量诊断、256/1024边界及左右保留方向已验证；[两RID Native AOT](docs/evidence/input-aot-2026-09-26.md)各138项输入检查，SIMD/CUDA各38回答+4拒绝对齐、scalar各3条960/1024-token长输入通过；[token覆盖率](docs/evidence/laya-coverage-2026-09-25.md)另列；不代表更大head、质量或性能验收 |
| S4-01 | D 质量 | ✅ 已完成 | 提供二分类线性头示例 trainer、温度拟合与 accuracy/F1/NLL/Brier/ECE 评估器 | S3-01 | 可复现示例 trainer/metrics；该 trainer 不更新真实 marker head |
| S4-02 | D 质量 | ✅ 已完成 | 建立中英测试/校准协议夹具、来源与隔离清单 | S4-01 | 24 条原创中英 fixture、数据卡、SHA-256 manifest、split/entity/fingerprint 隔离与有界验证脚本；无真实训练集，fixture 不代表代表性质量 |
| S4-03 | D 质量 | ✅ 已完成 | 建立初始校准 profile 合同、安全加载器与冻结门槛 | S4-02 | 12 个初始 prompt-v1 的 hash 绑定 profile，保持 `pending_measurement`；当前 prompt 或新权重的 profile 须另建并实测，不沿用旧绑定 |
| S4-04 | D 质量 | 🚧 进行中 | 对修正后的相同权重/输入分别运行 Laya 与 Sezika，建立可比较质量和覆盖率基线 | S3-08,S3-09,S3-10 | [全量独立参考与修复后复测](docs/evidence/s346-continuation-2026-09-26.md)：PAWS250+Nimble324共574条独立比较通过，双方均170/250及137/324正确，均为compatible；另12条原创中英fixture逐题通过、双方8/12正确。初轮Nimble8条token差异和超时保留，修复后同一构建全量重捕获。574条近并列观察命中0；Boolean负类召回仍1/57、外部题集语言未标注、代表性中英质量与校准未验收；旧strict报告单列 |
| S4-05 | D 数据 | 🚧 进行中 | 审核来源许可，构建独立训练/开发/校准/封存测试及多语言反事实流水线 | S0-01 | `tools/Validate-S4Data.ps1` 已提供有界 manifest、许可/用途、split、实体/家族/近重复、衍生父链、污染和封存绑定审计；项目自有 12 条 train/development smoke 已入库并绑定 records/manifest hash；正式许可复核、calibration/sealed test、污染检查和人工封存仍未完成；PAWS/Nimble 只作 audit |
| S4-06 | D 诊断 | 🚧 进行中 | 在新独立开发集建立目标任务基线，按角色、否定、词面相似性、语言、题型与候选顺序诊断 | S4-05；独立模型基线：S4-15；Laya对照可选 | `IndependentModelTool evaluate-original` 已实现有界 development 评估，输出逐题候选顺序、覆盖率、准确率、NLL/Brier/ECE、margin、宏 AUROC 和 Wilson 置信区间，并按语言/题型分组；角色/否定/词面相似性在记录未提供审核标签前明确不推断；仍只有 6 条开发 smoke，未形成 calibration、代表性质量或封存测试证据，详见 [S4-06 评估合同](docs/independent-development-evaluation-s4-06.md) |
| S4-07 | D 自有head训练 | 🚧 进行中 | 用C#实现独立encoder上的自有head/scorer前反传与初始化；支持完整逐题特征缓存及人工/可选教师硬标签实验 | 工具：S4-14；真实实验：S4-05,S4-06,S4-12,S4-15 | 350 项测试、真实独立 encoder 12 条原创开发 smoke、24 步人工硬标签训练和 head asset 已有证据；结果明确为 development smoke，不是质量验收 |
| S4-08 | D 质量门槛 | ⏳ 计划中 | 验收独立模型数值、质量、校准/拒答profile与部署；既有Laya证据单列 | S4-06,S4-07,S4-12,S4-15 | 自有模型封存测试按覆盖率、balanced accuracy/负类召回、AUROC、NLL/Brier/ECE、Choice/Score、逐语言及反事实对报告，含置信区间和回退；自身同权重参考、CPU/CUDA与两RID AOT回归，不提供Laya资产的环境仍能运行 |
| S4-09 | D 可选对标 | ⏳ 计划中 | 按产品需要审计其他检查点的C#支持；Laya Agent/Router仅作可选对照 | S3-07,S4-04仅限Laya对照；新增资产独立审核 | 各资产许可/revision/hash、显式模型选择与同路由同题集报告；不阻断独立模型主线，新增资产另验数值/AOT/性能/校准 |
| S4-10 | D 教师入口 | ⏳ 计划中 | 核对IoTSharp/Tomur本地服务、catalog `qwen35-9b-q4km` 的实际模型身份/接口/错误合同、资源预算及输出训练/再分发许可；4B仅作资源/质量对照，设计独立C#离线采集工具 | S0-02,S4-05数据合同 | `Sezika.TeacherTool` 已实现候选 catalog 与最多32条采集记录的离线合同检查，保留响应 hash、失败/拒答分母并阻止未经人工审核进入训练；候选本机尚未安装或实际验证，服务 native runtime 仅限外部工具边界，Sezika 核心及最终推理仍纯本地C#；先核对条款、上限与取消，无许可记录不进入对应训练用途 |
| S4-11 | D 教师数据 | ⏳ 计划中 | 对最多32条新人工金标有界采集教师硬标签，人工复核后才交训练器；与仅人工训练做对照，软分布后续另验 | S4-05,S4-10 | 扩量前冻结样本/请求/重试/墙钟/资源上限；保存来源、完整输入合同、模型/提示/响应hash、家族及审核链；失败/拒答保留分母，教师标签不作封存测试真值，自报置信度不作软标签 |
| S4-12 | D 训练资产 | 🚧 进行中 | 实现自有head checkpoint、训练恢复状态、版本化manifest及受限导出/加载入口；显式区分独立与Laya衍生资产 | S4-14,S1-03,S3-05；与S4-07工具研发同步 | 训练 checkpoint 与独立 head asset v1 均已实际保存/加载，绑定模型/encoder/tokenizer/manifest/feature hash 并拒绝篡改/错形状/身份不符；正式模型包、校准与发布仍未完成 |
| S4-13 | D 条件适配 | ⏳ 计划中 | 首轮自有head报告后按开发证据扩展架构，必要时研究C# encoder LoRA | S4-06,S4-07,S4-12 | 按所选架构记录参数与新增反向算子梯度检查、资源/恢复证据及独立消融；GPU反向另行实现与验证；无收益也保留报告，正式交付走S4-08 |
| S4-14 | A 独立合同 | ✅ 已完成文档审核/设计 | 审核原始encoder/tokenizer来源，定义自有head初始化、输入协议、资产身份与Laya迁移边界 | S0-01,S0-02 | 合同已固定原始 revision、许可边界、pickle 转换门槛、三题型 head、自有协议和迁移清单；实际资产/训练证据转入 S4-15/S4-07 |
| S4-15 | B 独立入口 | 🚧 进行中 | 实现原始encoder受限导入、自有输入构造与显式legacy适配，将Laya固定校验限定在旧模型入口 | S4-14,S1-03,S3-05；checkpoint接入：S4-12 | 已有原始权重/tokenizer hash、C# SafeTensors 转换、134 tensor 形状、独立 manifest/loader、decision-v1 真实 CPU smoke；上游数值 oracle、CUDA/AOT、默认切换须 S4-08 |
| S5-01 | E GPU/AOT | ✅ 已完成 | 生成带 hash/ABI 的 C# kernel PTX 与静态 Driver loader | S0-02 | 14 个 kernel manifest/PTX 和生成复现记录 |
| S5-02 | E GPU/AOT | ✅ 已完成 | 完成 resident CUDA encoder/head 与 CPU logits 对齐 | S2-01,S5-01 | RTX 4070 实卡 head 误差报告 |
| S5-03 | E GPU/AOT | ✅ 已完成 | 完成 tiny CPU/CUDA win-x64 与 CPU linux-x64 Native AOT smoke | S5-01 | 发布物、运行输出、依赖图 |
| S5-04 | E 性能 | ✅ 已完成 | 建立 scalar/SIMD/量化 CPU 与 CUDA 冷/热、H2D、kernel、端到端基准 | S2-03,S5-02 | Windows 四后端、固定模型/hash/硬件/线程、1/8/32 问各 5 样本，冷启动/重载、p50/p95/p99、吞吐/分配/RSS/显存及独立 CUDA 分项；[实测与限制](docs/s5-performance-aot.md) |
| S5-05 | E 性能 | ✅ 已完成 | 完成真实模型 CPU/CUDA AOT、多 RID、显存/内存回收和失败诊断 | S3-04,S5-02,S5-03 | win-x64 / Ubuntu WSL2 linux-x64 四后端真实模型 AOT，预取消/执行中取消/失败恢复、两轮 unload 和对象回收；[8 份报告矩阵](docs/evidence/s5-2026-09-24/summary.json) |
| S5-06 | E 性能画像 | 🚧 进行中 | 对现有及对齐后序列建立短/中/长、1/8/32 问 CPU/CUDA 成本画像 | S5-05 | [schema v3续验证](docs/evidence/s346-continuation-2026-09-26.md)：区分进入/完成forward，3秒CUDA负例为3进入/2完成/0正式样本且回收通过；本轮SIMD long-1单样本42.183秒，估算long-32两遍超1800秒上限而未启动，CPU完整矩阵仍未验收。CUDA long-32 end_to_end单样本38.319秒，分项明确未测。此前[九格画像及CPU300秒失败](docs/evidence/s5-profile-2026-09-26.md)保留；普通.NET小样本不能证明稳定尾延迟、AOT性能或优化收益 |
| S5-07 | E 性能实现 | ⏳ 计划中 | 在对应模型数值质量门槛下逐项优化 C# CPU/CUDA 算子、工作区和有界逐题批处理 | S5-06；既有模型：S3-06,S4-04；独立模型：S4-08 | blocked/tiled GEMM、归约/融合/launch、受限批处理逐项消融；固定质量不降、成本改善，取消/卸载与两 RID AOT 回归；共享 state 改变语义须另立模型 |
| S6-01 | F 发布 | ✅ 已完成 | 生成带 README、许可证、NOTICE 和第三方声明的开发 NuGet 包 | S0-02 | `.artifacts/packages/Sezika.0.1.0-dev.nupkg` |
| S6-02 | F 发布 | ⏳ 计划中 | 完成独立模型默认入口、正式版本、CLI、模型卡/数据卡、签名、NuGet 发布和示例 | S4-08,S4-15,S5-07 | 发布清单、签名校验、跨平台包与文档；默认教程/CLI采用Sezika术语及独立模型，Laya限于历史、来源与可选legacy说明；声明实际支持的检查点与质量范围 |
| S6-03 | F 集成 | ⏳ 计划中 | 为 IoTSharp 组织项目提供进程内适配和独立服务试点 | S4-08,S4-15,S5-07 | 保持核心无业务执行器；Tomur/IoTSharp/SonnetDB 等试点合同、版本化 HTTP API、鉴权/限流/租户预算、负载与失败回退证据 |

S5-04、S5-05 已在固定硬件与两个 RID 的记录范围内闭环；S5-06、S5-07 是新增性能工作。Linux 使用 Ubuntu WSL2 smoke，未测量裸机 Linux 性能。S6-02 正式发布仍未完成。S4-03 的 profile 仍保持 `pending_measurement`，不把 fixture、数值对齐或性能基准写成多语言质量已达标。

质量、训练、性能和生态的背景研究见 [下一阶段研究](docs/next-stage-research-2026-09-25.md)，以上执行表是唯一当前阶段计划。该研究文档保留原日期的背景及旧结果，当前优先级采用2026-09-30校核后的独立模型/自有head与可选离线教师路线。S4-04仍未完成；S4-14已完成文档审核/设计，S4-15已有原始资产受限导入、decision-v1 CPU smoke 和独立 loader 证据，S4-07/S4-12已有自有 head 原型、checkpoint/asset 保存加载与开发 smoke；S4-10至S4-11、S4-13以及独立模型的质量、许可和 AOT 门槛仍未完成。任何竞品分数、论文加速比或云端算力规格均不能替代本项目实测。

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
