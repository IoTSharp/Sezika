# 下一阶段算法、质量、训练与生态研究

研究日期：2026-09-25。本文保留算法与生态的背景研究；当前唯一执行顺序以 [ROADMAP](../ROADMAP.md) 为准。仓库当前真实模型为 `convaiinnovations/laya-multilingual@052592a15d198d9ad47da779604259b10b47b7aa`；固定权重和 tokenizer 的来源见[模型记录](model-source.md)。竞品数字仅按其对应版本、数据和硬件引用，不作为 Sezika 成绩。Laya 最新源码与长度语义的更正见[固定提交审阅](laya-upstream-review-2026-09-25.md)。

## 1. 当前算法及已知差距

Sezika 目前移植的是 Laya 多语言检查点：22 层 mmBERT-base 双向 encoder、两层 pre-norm 决策 Transformer、候选 `[MASK]` 向量 scorer。每个问题将其 instructions、候选和 state 组成一条序列，独立运行 encoder/head。一个请求的 N 个问题对应 N 次 forward；当前所谓 micro-batch 大小实际为 1。模型权重来自 Laya，独立性体现在 C# 加载、算子、调度、协议和部署，而非原创基础权重或已优于上游的语义质量。

| 当前优势 | 当前不足及影响 | 证据 |
| --- | --- | --- |
| SafeTensors/hash 固定、tokenizer oracle、逐层数值对齐 | 一致性不证明语义正确；只有 24 条中英协议夹具，没有逐语言质量分数 | [阶段证据](stage-evidence.md)、[数据卡](data-card-s4-02.md) |
| scalar、SIMD、W8A32 与 CUDA Driver 完整推理，两个 RID 的 AOT smoke | CLI/`DecisionModelRuntime` 只选择 CPU；内部 CUDA 路径尚未成为通用公开入口 | [架构](architecture.md)、[S5 报告](s5-performance-aot.md) |
| CPU/CUDA 共用提示词、预算与后处理 | 每题重复编码 state；同一请求的题数接近线性增加成本 | `ModernBertDecisionEngine.Evaluate`、[S5 报告](s5-performance-aot.md) |
| 模型、校准、拒答和资源边界有独立类型 | 实际响应固定 `uncalibrated`；集中度拒答阈值尚无选择性风险验证 | `ModernBertDecisionEngine.EvaluateQuestion`、[校准门槛](calibration-report-s4-03.md) |

现有 Windows RTX 4070 Laptop 固定 13-token 短请求的 CUDA AOT 热 p50 为 1/8/32 问 `40.057/311.137/1228.334 ms`，CPU SIMD 对应 `1289.871/6913.985/21038.749 ms`。三题独立 profiling 记录 1053 次 kernel launch，event 总时间 112.196 ms。当前 W8A32 比 SIMD 慢，且保留 FP32 原张量并增加约 119.8 MiB 量化缓存。这些数字只适用于[记录的输入、机器和样本数](s5-performance-aot.md)，没有长文本或稳定尾延迟结论。

## 2. 算法论文与适用边界

| 论文/公开方法 | 对 Sezika 的可检验启示 | 边界 |
| --- | --- | --- |
| [ModernBERT](https://arxiv.org/abs/2412.13663)、[mmBERT](https://arxiv.org/abs/2509.06888) | 保留现代双向 encoder 的局部/全局 attention、RoPE 和多语言底座；按真实 token 长度测量。 | 底座可处理多种文字不等于 Laya 决策头在各语言正确；更换底座需重新训练和对齐。 |
| [FlashAttention](https://arxiv.org/abs/2205.14135)、[FlashAttention-2](https://arxiv.org/abs/2307.08691) | 在 C# ILGPU kernel 中实验分块、在线 softmax 和片上复用，减少长序列 attention 的显存读写；同时针对本模型 local/global mask 验证。 | 论文的 A100/训练加速倍数不能转述为本机推理收益；先测 GEMM 与 launch 占比。 |
| [MiniLM](https://arxiv.org/abs/2002.10957) | 若大模型质量基线通过，可用注意力关系和 logits 蒸馏更小的开源学生 encoder，建立边缘/CPU 版本。 | 改变架构、tokenizer 或 head 后不是原权重的无损优化；要重新评测、校准和授权。 |
| [FastBERT](https://arxiv.org/abs/2004.02178) | 多出口自蒸馏可研究容易样本的提前退出，兼顾 CPU/端侧延迟。 | 出口选择本身可能产生高置信错误；按语言与风险等级验证，不以当前分布集中度直接触发。 |
| [LoRA](https://arxiv.org/abs/2106.09685) | 在冻结大部分 encoder 的前提下训练少量适配参数，作为完整 head 训练后的扩展。 | 本项目训练与 GPU 算子仍须 C# 实现；论文使用的 PyTorch 实现不能进入交付路径。 |
| [神经网络温度校准](https://arxiv.org/abs/1706.04599) | 按模型、语言、题型和候选规模，在独立 calibration split 拟合并检查 NLL/Brier/ECE。 | 温度不改变 Choice 的 argmax，也不能修复知识、否定和 OOD 错误；Score 期望与 Boolean 阈值会变化。 |

Jev 的内部模型和训练代码未公开，不能声称复现其架构或 RLCD。Nimble 的公开实现展示了反事实成对数据、候选 logits 训练和共享前缀评分；其 Qwen decoder、单 token 候选码与 Sezika 当前 cross-encoder 不是可直接互换的权重格式。Nimble 的共享前缀多题评分只见于 MLX 路径，公开 CUDA 路径仍逐题重复上下文。任何架构替换都另立模型版本和质量验收。

| 项目 | 实现路线及模型可见性 | 主要优势 | 主要代价或未知项 |
| --- | --- | --- | --- |
| Sezika 当前版 | 纯 C#/.NET 10、固定 Laya 多语 mmBERT 权重、双向 cross-encoder + marker head，CPU/CUDA Driver、Native AOT | 本地可审计、无远端依赖、类型/预算/失败语义明确，322M 权重低于 Nimble 9B | 旧实现把 256-token 前缀预算误作完整序列上限且 prompt/候选未与 Laya 对齐；每题重复 state，CUDA GEMM/launch 尚未充分优化 |
| [Laya 0.3.6](https://github.com/NandhaKishorM/laya/tree/c7527708f9f5220c669d8aa385077cd28d04708a) | PyTorch/Transformers，多检查点 Router，mmBERT/ModernBERT 双向 encoder + marker head；训练 notebook/权重公开 | 可复用的非自回归决策结构、不同语言检查点及上游基准 | 依赖 Python/GPU 框架；多问题仍重复 state；上游专项检查点成绩不能移植给多语权重 |
| TypeSafe [Jev](https://docs.typesafe.ai/concepts/system-one.md) 1.13.0 | 托管 API；公开描述 System One、并行输出、RLCD，核心结构和权重未公开 | 已公开类型化多题 API、置信与服务口径；Nimble 固定题集公开复测质量较强 | 本地算子/训练无法审计或迁入 Sezika；API 成本、网络、服务限制和数据治理需评估 |
| [Nimble](https://github.com/bespokelabsai/nimble/tree/62076b4f2d365b5879dafcf7f6dd072a1fe76df7) | Qwen3.5-9B decoder + 单 token 候选码、LoRA hard-label 训练；MLX 共享前缀，CUDA 逐题评分 | 数据/训练/评测配方公开，反事实成对训练和人标公开子集可复现 | BF16 仅权重约 18 GB；代码依赖 Python/MLX/PyTorch；其模型/数据许可及测试泄漏需逐项审查 |

四者的关键分界是**质量、可部署性和每题成本**，不能只比较模型参数或单条延迟。Nimble 明确未从 Jev 蒸馏；其 324 题发布数据是窄领域合成标签，不能据此断言通用能力。模型、提示、题集、硬件和拒答覆盖率不同的分数不得直接排名。

## 3. CPU/CUDA 优化实验顺序

首先冻结模型、请求集、数值容差、机器/线程、预热、冷/热计时和质量结果。建立 `state` 32/128/512/1024 token，1/8/32 问，2/8/32 候选的矩阵；分别报告 tokenization、encoder/head、分配量、RSS/显存、H2D/D2H、kernel 与端到端 p50/p95。五次样本只作探索，尾延迟需增加独立运行和样本数。CPU 与 CUDA 都必须保留真实模型 AOT、取消、卸载和失败诊断门槛。

1. **CPU 先做画像。** 当前 SIMD `Linear` 对每个输出列分别扫完整输入，`Vector<float>.Count` 在现有 AOT 基准仅为 4；多处算子每次分配新数组。先按形状测 GEMM、attention、norm、tokenizer及 GC；再逐项实验权重预打包、blocked GEMM/缓存复用、受限线程并行、`System.Runtime.Intrinsics` 的运行时特性选择、工作区复用。量化须真正替换 FP32 驻留路径，并验证每语言/题型质量，不能只比较四 token 的误差。
2. **CUDA 先优化主计算。** 当前 `Linear` 每输出元素由一个线程串行遍历 K，`Norm` 和 `Softmax` 每行单线程处理；先实现共享内存分块 GEMM/并行归约，与现有 scalar oracle 逐层比对。随后尝试 bias/activation/residual/norm 融合、减少中间缓冲和 launch；长序列再评估 IO-aware attention。Tensor Core、FP16/BF16、Driver graph 仅在构建期 PTX/ABI 和实卡验证后列为可用，不预先承诺。
3. **多问题计算分两层。** 不改权重时先对完整的逐题序列做有界批处理，提高 GPU 占用，但总 FLOPs 仍随题数增长。共享一次 state 编码或迁移为 prefix-branch 模型会改变双向 attention 语义，须作为新模型路线训练，不能对现有 checkpoint 声称等价。
4. **每项变更独立验收。** 记录同权重 logits/probability 最大差、完整题集标签翻转、P95/吞吐/显存、能耗或功率（如可测）、AOT 两 RID 和取消/释放；若质量下降，性能提升不自动通过。按投入优先级，先修 GEMM 和 launch，再考虑架构蒸馏。

## 4. 公开题集与真实质量基线

评测分成三层：数值对齐（同权重同提示）、外部标签质量（正确率及概率指标）、业务迁移（组织项目数据）。三层结果分开报告。每条题保存来源 revision、原始 ID/家族、原始 schema、映射后的 schema、模型/tokenizer/prompt/hash、预测和失败原因；超预算或不支持的题计入覆盖率，不能从正确率分母中悄悄消失。Score 同时报告 argmax 级别准确率和期望值 MAE；Boolean 使用预先冻结的阈值；报告 Wilson/按家族 bootstrap 区间及按语言、领域、否定、数字、长上下文、候选规模的错误切片。

| 来源 | 可用范围 | 比较注意事项 |
| --- | --- | --- |
| [Nimble 冻结 324 题](https://github.com/bespokelabsai/nimble/blob/62076b4f2d365b5879dafcf7f6dd072a1fe76df7/data/eval.jsonl) | 原题公开、SHA-256 `8e9e48b8de5206593912ae01ddc95bd77e40ad2ecf4c9292c1711290eca0d896`；162 对反事实样本 | 标签是模型检查的合成规则标签，未经人工复核；仓库根目录未见数据许可，不纳入训练或再分发。Sezika 的 `noul`→`boolean`/候选排序为协议映射，需报告差异。 |
| [Nimble 13 个公开人标子集](https://github.com/bespokelabsai/nimble/blob/62076b4f2d365b5879dafcf7f6dd072a1fe76df7/docs/PUBLIC_BENCHMARKS.md) | 3880 个固定选中 ID，覆盖 Choice/Noul/Score，含同 ID 英德 MASSIVE | 原始数据依各来源许可取得；不可复用未授权代码。预先固定提示和同 ID 后方可与 Nimble/Jev 已发布数比较。 |
| [Laya 的公开基准说明](https://github.com/NandhaKishorM/laya/tree/c7527708f9f5220c669d8aa385077cd28d04708a) | 语言/任务切片和上游 notebook 可作复现实验入口 | 先确认确切输入、参考标签、checkpoint 与温度；`0.766` 属专项模型，不是当前多语言权重或 Sezika 分数。 |
| Jev 官方资料 | 协议、限制和失败类型可生成独立测试设计 | 其专有四工作流题集未公开；不得把 API 响应当无授权训练标签或称为人类真值。 |

公开训练数据的首批许可清单：HF 模型卡分别标 [Civil Comments](https://huggingface.co/datasets/google/civil_comments) `CC0-1.0`、[MASSIVE](https://huggingface.co/datasets/AmazonScience/massive) `CC-BY-4.0`、[HelpSteer2](https://huggingface.co/datasets/nvidia/HelpSteer2) `CC-BY-4.0`、[Aegis 2](https://huggingface.co/datasets/nvidia/Aegis-AI-Content-Safety-Dataset-2.0) `CC-BY-4.0`、[BoolQ](https://huggingface.co/datasets/google/boolq) `CC-BY-SA-3.0`。这些是候选，不等于已经批准训练或重新分发：逐项核对原始条款、标注来源、字段、可商用范围、衍生权重义务和版本。CC-BY-SA 与许可不明的数据先隔离；Nimble 仓库的公开数据尤其不能因“可下载”便用于商业模型。

**本轮实测及后续源码复核已改变优先级。** [逐题质量证据](evidence/quality-2026-09-25.md)显示旧 Sezika 实现对 Nimble 324 题仅回答 8 题、4 题正确，PAWS 250 ID 中回答 249 题、正确 124 题，负类仅 4/128 正确。先修正 Laya 输入/预算契约并建立同权重 oracle，重测后再判断训练目标、类别先验、提示敏感性或表示是否不足。温度不会改变固定 0.5 决策的 logits 排序；阈值/提示只能在独立开发集确定，不能用 PAWS test 调参。PAWS 只是英语 Noul 一项，不能推导中文或 Choice/Score 表现。

P0 用旧 Sezika 渲染测得 Nimble 324 题的完整长度均在 1024 内、只有 8 题在旧实现的 256-token 完整序列限制内；这不代表上游 Laya 的 head 只能处理 256-token 完整输入。PAWS Boolean AUROC 为 0.537642；222-token 单条的 CPU/CUDA 原始 logits 误差为百万分之几，尚不能替代 Laya 独立参考 oracle 或证明语义质量。详见[同日质量证据](evidence/quality-2026-09-25.md)。

数据流水线：固定原始来源和 SHA-256 → 显式字段映射 → 去重/近重复与实体/家族隔离 → 人工抽检歧义及标签 → 按语言、领域、时间切 train/calibration/test → 冻结测试集并禁止调参读取。英文数据不能代替中文质量；中文及目标语言需许可明确的原生标注，翻译改写对必须留在同一 split。对照样本要保留原规则和唯一变化事实，人工核验反事实确实翻转标签。

## 5. 两条模型训练线及模力方舟算力

**共享引擎，分离资产。** 开源模型只用许可、标注与再分发条件清楚的数据和 teacher；公开权重、模型卡、数据来源、训练配方、失败切片。商业模型可增加有授权的企业/领域数据和专有校准，但另有模型 ID、revision、权重、数据清单、评测和许可；不将客户数据或受限 teacher 输出回填开源训练集。两者都不能把输出当执行授权。是否从 Laya 权重继续训练，需保留 Apache-2.0 与 mmBERT/tokenizer 的 MIT 来源要求；若从新底座训练，另做权重与 tokenizer 许可审计。

1. **基线。** 在冻结题集上先运行现有 322M 模型，输出覆盖率、按题型/语言准确率、NLL/Brier/ECE、Score MAE、拒答风险及错误样本；与多数类/随机和原 Laya 同权重参考对齐。这一步决定哪些问题值得训练。
2. **低风险训练。** 当前 `DecisionHeadTrainer` 只是二分类线性头示例，不会更新真实 marker-head。先在 C# 中实现真实 scorer/完整两层 head 的可复现前反传，冻结 encoder，使用交叉熵与 proper-scoring 对照；保存 optimizer、随机种子、数据顺序、梯度检查、断点恢复与每步指标。只有独立测试集改善才进入下一层。
3. **参数高效适配。** 若 head 受限，对固定 mmBERT 线性层研究 C# LoRA；先在小张量上做数值梯度检查，再实现有界 CUDA 反向 kernel、FP32 主权重/优化器状态与低精度前向实验。不得把仅有 LoRA 文件或能运行一轮当质量通过。全量 encoder 训练另立资源和收益评估。
4. **蒸馏与新架构。** 仅从条款允许用于训练的公开权重/数据或自有模型生成 teacher 分布；混合硬标签、软分布与层间关系，分别做消融。Jev API、Nimble 数据或其他模型输出须先有明确授权，不能以可访问替代许可。小模型以 CPU/边缘 Pareto 为目标，准确率和校准与主模型分开报告。
5. **校准与发布。** 温度只在 calibration split 拟合；Choice/Score/Boolean 按实际范围验证，冻结低置信拒答和业务成本阈值；测试集只用于最终报告。任何权重或 prompt 改变都使旧 profile 失效。

模力方舟租机前先取得**具体** GPU 型号/显存/驱动、小时价格、存储/出网价格、作业最大时长与镜像/许可证条件，不凭平台名称推断有 H100 或给出预算金额。322M 参数若做全量 AdamW，按每参数 FP16 权重 2 字节、FP32 主权重/梯度/两个优化器矩各 4 字节，**仅参数及状态**的理论下界约 5.8 GB，尚未计入激活、临时算子缓冲、CUDA context、checkpoint 和 batch；真实峰值必须试测。用 1% 数据、极小 batch、少量 step 的 C# 前反传试运行记录峰值显存、step/s、checkpoint 大小与恢复，再估算 `训练步数 / 实测 step/s + 数据预处理 + 验证 + 失败缓冲` 的租用时间。冻结 encoder/head、LoRA、全量训练分别测算；训练作业须有总 step、墙钟、取消、定期进度、早停和清理自己进程树的边界。GPU 训练同样遵守 C# kernels + Driver 的项目要求。

## 6. IoTSharp 生态与服务形态

| 项目 | 首个有价值的接入点 | 优先形态与边界 |
| --- | --- | --- |
| [Tomur](https://github.com/IoTSharp/Tomur) | 生成模型前的意图/风险/路由，以及生成后有证据的核验问题 | 优先进程内 C# 库；Sezika 只返回分布与状态，Tomur 决定生成及工具授权。 |
| [IoTSharp](https://github.com/IoTSharp/IoTSharp) | 工单、告警文本和规则链事件的分类/优先级建议 | 独立内网服务先试点，多租户预算与审计在服务层；设备控制仍由规则链和权限系统决定。 |
| [SonnetDB](https://github.com/IoTSharp/SonnetDB) | RAG 检索候选重排、查询意图、异常解释文本判断 | 数据库引擎不内置模型；应用侧取回有界文本后调用本地库或服务。 |
| [IoTEdge](https://github.com/IoTSharp/IoTEdge) | 网关告警文本分流、运维建议 | 先调用局域网服务；322M 权重和当前内存不适合直接假定部署到所有边缘设备。 |
| [IoTCoWork](https://github.com/IoTSharp/IoTCoWork)、[Couplet](https://github.com/IoTSharp/Couplet) | 本地工作流路由、代码检索结果判断 | 在进程内资源足够时复用类库；测实际多题延迟，低质判断转人工或其他模型。 |

服务试点由独立 `Sezika.Service` 宿主承担，核心程序集保持无 HTTP/身份/业务动作依赖。ASP.NET Core 10 Minimal API 可提供版本化 `inspect`/`predict`/`health`、source-generated JSON、ProblemDetails、鉴权、租户并发/令牌预算、取消/期限、模型热加载与失败关闭；按 CPU/CUDA 可用性显式选 backend，禁止隐式静默回退。对外服务必须先有稳定质量、容量和授权证据，再定 SLA/价格；不以 AOT 构建成功或单机短请求 p50 代替生产验收。

鉴于当前 PAWS 的负类召回仅 3.13%，首个 Tomur/IoTSharp 试点应只记录影子预测：调用方保存请求 ID、模型/校准版本、候选分布、人工最终判断和耗时，用户业务决策仍按原流程执行。用该独立业务标注集评估每一类误判成本、拒答覆盖率和 CPU/CUDA 容量，通过预设门槛后才考虑建议展示或有限自动化。对外服务设计可以先做合同与容量原型，生产接入仍由质量门槛决定。

## 7. 下一次实证的通过条件

固定 Nimble 324 题与 PAWS 人标 250 ID 的首轮真实结果已见[质量证据](evidence/quality-2026-09-25.md)；S4-04 仍未完成，因为同权重 Laya 对齐、Choice/Score 与中文/多语言外部质量、跨提示稳定性仍缺。优化与训练实验都不得重用已查看的题作为选参或训练数据；任何新分数须附模型、硬件、软件、prompt 与数据 revision。具体任务依赖、性能并行画像及训练验收以 [ROADMAP 当前执行顺序](../ROADMAP.md) 为准。
