# Sezika 技术研究

访问日期：2026-09-23。本文基于公开源码与文档的只读研究，未运行上游模型、训练、构建或测试。性能与准确率数字仅转述对应来源，不构成 Sezika 的验证结果。

## 1. 研究对象与定位

Sezika 的目标是面向本地应用的多语言、非自回归 System 1 决策模型与纯 C# 推理引擎，使用 .NET 10，面向 Native AOT 发布。输入为状态和限定类型的问题，输出为可直接供程序使用的概率与决策。它不生成自由文本，也不自行执行业务副作用。

- [Laya 仓库](https://github.com/NandhaKishorM/laya)：固定研究版本 `c7527708f9f5220c669d8aa385077cd28d04708a`，`pyproject.toml` 包版本为 `0.3.6`。
- [TypeSafe Jev 官方介绍](https://typesafe.ai/blog/introducing-system-one-models-and-jev)：2026-09-15 发布，介绍 System One、并行输出和 RLCD。
- [TypeSafe 官方文档索引](https://docs.typesafe.ai/llms.txt)、[System One](https://docs.typesafe.ai/concepts/system-one.md)、[API](https://docs.typesafe.ai/api.md)、[confidence](https://docs.typesafe.ai/confidence.md)、[模型](https://docs.typesafe.ai/models.md) 均已在本次研究中读取。[问题类型](https://docs.typesafe.ai/primitives.md) 与 [Jev 1.13 局限](https://docs.typesafe.ai/model-jaggedness/jev-1.13.md) 为索引提供的进一步核对入口，未逐页核验。
- [TypeSafe Python SDK](https://github.com/typesafe-ai/typesafe-sdk-python)：公开客户端，不是 Jev 模型实现。本次读取的官方资料未提供可移植的 Jev 核心模型源码与权重。Sezika 可以借鉴公开接口与设计思想，不应声称复现未公开的 Jev 架构。

## 2. Laya 源码与产物

| 文件 | 职责 |
| --- | --- |
| [laya/common.py](https://github.com/NandhaKishorM/laya/blob/c7527708f9f5220c669d8aa385077cd28d04708a/laya/common.py) | 序列构造、DecisionModel、批处理、奖励函数和 confidence |
| [laya/agent.py](https://github.com/NandhaKishorM/laya/blob/c7527708f9f5220c669d8aa385077cd28d04708a/laya/agent.py) | 下载与加载、权重校验、推理及输出后处理 |
| [laya/router.py](https://github.com/NandhaKishorM/laya/blob/c7527708f9f5220c669d8aa385077cd28d04708a/laya/router.py) | 检查点选择、预加载、常驻模型和 LRU |
| [laya/lang.py](https://github.com/NandhaKishorM/laya/blob/c7527708f9f5220c669d8aa385077cd28d04708a/laya/lang.py) | 语言与文字检测；本次仅确认文件存在，未逐行读取 |
| [pyproject.toml](https://github.com/NandhaKishorM/laya/blob/c7527708f9f5220c669d8aa385077cd28d04708a/pyproject.toml) | 依赖和包元数据 |
| [README.md](https://github.com/NandhaKishorM/laya/blob/c7527708f9f5220c669d8aa385077cd28d04708a/README.md) | 模型说明、测量摘要和已知限制 |
| [训练 notebook](https://github.com/NandhaKishorM/laya/blob/c7527708f9f5220c669d8aa385077cd28d04708a/notebooks/laya_finetune_typed_decisions_2xT4_kaggle.ipynb) | 官方微调入口；本次未逐行核验 notebook |

运行时读取 `rl_agent_config.json`、`model.safetensors`、`tokenizer/` 和 `encoder/`。`agent.py` 使用 `safetensors.torch.load_file`，不是 `.pt`、`.pth` 或 pickle 反序列化路径。加载器检查 `encoder.`、`type_emb.`、`scorer.`、`act_head.` 前缀及每个参数的名称、形状，并严格加载 state dict。

因此，Sezika 不必先把 pickle 权重转换成 safetensors。推荐首先直接支持所需 safetensors dtype、形状与偏移校验，并显式实现权重名称映射。如果为性能引入独立打包格式，应将其定义为带版本、来源 revision、checksum、张量布局和精度信息的离线转换产物；不执行模型仓库中的任意 Python 代码。量化属于新的数值路径，须单独验证，不能把格式转换视为精度等价的证明。

`pyproject.toml` 声明 Python ≥3.10，依赖 PyTorch ≥2.0、Transformers ≥4.48、safetensors ≥0.4、huggingface_hub ≥0.20、NumPy ≥1.20。README 关于当时最新版依赖的描述不等于这些最低约束。

## 3. 推理结构

输入 state 可以是文本、JSON 对象或对话列表。questions 包含类型、instructions 和候选标准。每个问题独立构造如下序列：

```text
[CLS] type question: instructions [SEP]
[MASK] option0 [MASK] option1 ... [SEP] state [SEP]
```

多问题会形成一个 batch，每个问题都携带并重新编码 state。一次 forward 是批处理调用次数，不意味着共享一次 state 编码后即可无成本回答任意多问题。Sezika 若改变为共享编码，属于架构变更，不能默认沿用原权重行为。

DecisionModel 的路径为：

1. 双向 encoder 输出每个 token 的隐状态。默认模型分别基于 ModernBERT-large 或 mmBERT-base；encoder 的具体局部/全局 attention、RoPE、norm 和 tokenizer 仍须依据对应固定模型配置核对。
2. 对每个 token 加上三类 question type embedding。
3. 默认经过两层 `TransformerEncoderLayer`，`batch_first=True`、`norm_first=True`，因此是 pre-LN。head 数量为 `max(1, hidden_size / 64)` 的整数结果，FFN 为 `4 × hidden_size`。调用未传 activation，采用 PyTorch 层默认 ReLU；不要与后续 scorer 的 GELU 混淆。
4. decision head 使用反转 attention mask 得到的 `src_key_padding_mask` 屏蔽 padding，没有传因果 mask；因此这一段是非因果 attention。`TransformerEncoder` 未提供最终 norm，实际 forward 逐层调用 `head.layers`。推理 `.eval()` 关闭 dropout。
5. 在每个候选项的 `[MASK]` 位置 gather 向量，经过 `LayerNorm → Linear(d,d) → GELU → Linear(d,1)` 得到 logits；无效候选置为 `-1e4`。
6. 按问题类型及候选数选择温度，softmax 得到候选概率。

行动 head 使用 decision head 之后的第 0 个 token `h[:,0]`，不是 mean pooling。它拼接 top1、top1-top2 gap、归一化 entropy、`K/255` 四项特征，经 `Linear(d+4,256) → GELU → Linear(256,n_act)` 输出行动 logits。对外提供的 `act_probability` 是预测，不是执行许可。

## 4. 输出与 confidence

| 类型 | 数值输出 |
| --- | --- |
| `choice` | 最大概率候选标签及所有候选的概率 |
| `score` | 有序等级的期望值 `Σ i × pᵢ`、等级分布和 legend |
| `noul` | 二分类中的 `P(true)` |

Laya 的 confidence 公式不统一：

- `choice` 和 `score`：`1 − H(p) / log(K)`；`K < 2` 时为 1。
- `noul`：`max(P(true), 1 − P(true))`。

熵指标不是“答案正确的概率”，高 confidence 也不证明输入处于有效语言或任务分布内。Sezika 应区分候选概率、分布集中度、校准状态以及调用方阈值策略，保留计算定义和模型版本。

Jev 官方 API 的 Noul 只包含 `type` 和 `noul`，没有 confidence；Choice/Score 有 confidence，但不能把文档交互演示中的近似计算认定为模型服务的精确公式。Sezika 核心使用语义明确的 concentration，任何兼容映射需单独说明差异。

结构封闭可以保证返回候选集合内的类型和值，不能保证语义判断正确。项目文档不应将“不生成字符串”表述为“判断不会错”。

## 5. 检查点、预算与选项数

| 检查点 | 底座 | 参数量（README） | 默认总长度 / head budget |
| --- | --- | --- | --- |
| English | ModernBERT-large | 421M | 512 / 192 |
| Multilingual | mmBERT-base | 322M | 1024 / 256 |
| Typed decisions | ModernBERT-large | 421M | 1024 / 256 |

本次读取的 `common.py` 和 `agent.py` 没有“最多 32 个选项”的统一硬上限，候选数由动态 marker 张量和 token budget 约束。序列构造会缩短 option 文本；候选 marker 被截掉时，Agent 会报超出预算。`K/255` 是行动 head 的输入特征归一化，不是对 255 项的验证保证。任何 Sezika 初版 32 项限制都应明确为自己的资源与验收边界。

Jev 官方介绍写明最多 255 个候选项，更多候选采用两阶段策略。Laya README 承认 77 项在默认预算下每项仅分到约 3–4 token，影响辨识。Sezika 应提供预算诊断，避免静默截断使选项失去区别；若采用 shortlist，必须说明概率仅在 shortlist 内归一化。

Router 根据文字和语言选择英文或多语模型。专项 typed-decisions 模型默认不会仅因问题相似而自动启用，需要显式选择或 opt-in 的任务检测。默认只常驻一个模型，语言交替可能导致数秒级重复加载，生产集成需要明确预热与内存配额。

## 6. 训练、校准和基准限制

源码提供 log score、spherical score 与 ordinal ranked probability score 的组合奖励，README 将训练描述为 RLCD、GRPO-style policy gradient，并提供温度拟合；源码也包含 TD(λ) trajectory target。本次没有训练或复现损失曲线，不能把训练方法名称当作校准有效的实验证据。

上游 README 明确披露：

- base checkpoints 在 typed-decisions 上的零样本准确率约 0.35–0.36，低于 0.461 的 majority-class baseline；0.766 来自专项微调检查点。
- 100+ 语言是多语底座的覆盖口径；其报告中，达到“高于三倍随机准确率”标准的是 51 种语言中的 45 种，不等于所有语言都达到应用质量。
- 英文模型在部分非拉丁文字输入上可能自信地出错，不能仅靠 confidence 阈值补救。
- 多语检查点未附拟合好的温度。当前源码将温度限制到 `[0.5,5]`，而研究时读取的英文 Hub 配置中 `choice:11+` 为约 0.10058，运行时会钳制，相关 confidence 应视为未经充分校准。
- 约 33ms 是 T4 GPU 的测量，不是纯 C#、CPU 或 AOT 性能承诺。多问题 batch 延迟会随问题数上升。
- Laya 与 Jev 的表格使用第三方已发布 Jev 数据，样本、提示和运行环境不同，不是统一条件下的完整复测。

Jev 官方宣称新架构、并行 sampler 和 RLCD，给出托管端到端 70–500ms 与当时价格；它们是服务侧公开口径，不能用于推导 Sezika 单机算子吞吐。后续评估应冻结数据集、语言、schema、模型 revision、硬件、温度、失败样本与预热条件，再分别报告准确率、Brier/NLL、ECE、覆盖率与延迟。

## 7. 许可

Laya [源码许可证](https://github.com/NandhaKishorM/laya/blob/c7527708f9f5220c669d8aa385077cd28d04708a/LICENSE) 已确认是 Apache-2.0。若直接移植或改写其代码，需保留许可、归属与修改说明，按实际分发内容处理 NOTICE。

源码、权重、底座、tokenizer 和训练数据必须分别核对许可证。README 宣称 Laya 权重为 Apache-2.0，但本次未逐项核验各模型卡和所有组成资产，不据此批准重新分发。公开 TypeSafe SDK 的许可也不能推导 Jev 权重或服务使用权。Sezika 可采用独立开源许可证，并为外部模型资产维护清单及来源记录。

## 8. 推荐的纯 C# 实施路线

1. 固定 Choice / Score / Probability 契约、输入预算、确定性序列化、取消响应、模型 manifest 和未配置诊断；JSON 使用 source generation，避免反射动态加载。
2. 首先选择一个固定版本的多语 encoder，实现 tokenizer、safetensors 读取和明确的 tensor 映射，建立 token IDs、intermediate tensors、logits、probabilities 的上游对齐样本。
3. 使用 C# 实现所需 embedding、attention、位置编码、norm、FFN 和 decision head；以正确的标量实现为基线，再逐步引入 Span、SIMD、内存映射与低精度优化。内存上限、形状校验和取消必须贯穿加载和计算。
4. 先验证真实权重推理和 AOT 发布，再开展自有微调权重、数据、温度拟合及多语言基准。沿用外部权重和训练自有模型是不同交付，应分别记录来源与能力证据。
5. Tomur 通过稳定 C# 契约静态引用 Sezika，模型仍由 Tomur 的模型资产目录和生命周期管理；decision endpoint 与普通 chat endpoint 分开表达。模型缺失、格式不支持、预算超限和未校准应有明确诊断。

Native AOT 能与 ONNX Runtime 等 native 库共存，但这不属于纯托管推理。Sezika 若承诺纯 C#，不应通过未声明的 P/Invoke、Python 子进程或远程 API 满足推理路径。纯 C# 的主要投入在 tokenizer 精确一致性、数值内核、内存效率和性能验证，不能仅靠重写 SDK 接口获得模型能力。

用户已进一步明确 GPU 要求：算子仍由 C# 编写，系统/显卡驱动调用是允许的例外。采用 ILGPU 构建期生成 PTX、Native AOT C# 调用 CUDA Driver 的路线；原版 ILGPU 动态运行路径的限制与待验证事项见 [GPU / AOT 设计](gpu-aot.md)。该驱动例外不允许以 cuBLAS/cuDNN 等 native 数值库替代 C# 算子。

Tomur 可以用概率结果选择本地模型、筛选候选工具或标记需要确认的请求。任何有副作用的动作仍由 Tomur 既有显式 allowlist 与确认机制授权；Sezika 的输出不能绕过该边界。
