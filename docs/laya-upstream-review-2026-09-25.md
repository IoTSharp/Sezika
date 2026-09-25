# Laya 最新源码与 Sezika 同权重对标审阅

审阅日期：2026-09-25。GitHub API 当时返回的最新提交为 [`4066d5d5fbf08b66c6757ddeedbd797bd7655bc0`](https://github.com/NandhaKishorM/laya/tree/4066d5d5fbf08b66c6757ddeedbd797bd7655bc0)，`pyproject.toml` 版本为 `0.3.20`。本次只读上游源码和 Hugging Face 文件元数据，未安装/运行 Laya、下载权重、运行 Sezika 模型或测量质量与性能。旧研究固定的 Laya 0.3.6 提交为 `c7527708f9f5220c669d8aa385077cd28d04708a`；下面涉及的关键题型/长度语义在该旧版源码中也已存在。

## 模型身份

上游 [`revisions.py`](https://github.com/NandhaKishorM/laya/blob/4066d5d5fbf08b66c6757ddeedbd797bd7655bc0/laya/revisions.py) 将多语言检查点的可选审阅 revision 列为 `e4e9ddf21a7b1903b7acffd8814ad4307bf63a67`，但默认加载仍跟随 Hugging Face 默认 revision，因此对标调用必须显式 pin。对 [新 revision 文件树](https://huggingface.co/api/models/convaiinnovations/laya-multilingual/tree/e4e9ddf21a7b1903b7acffd8814ad4307bf63a67?recursive=true)和 [Sezika 已锁定 revision 文件树](https://huggingface.co/api/models/convaiinnovations/laya-multilingual/tree/052592a15d198d9ad47da779604259b10b47b7aa?recursive=true)的元数据核对显示：

| 文件 | 两个 revision 的相同内容标识 |
| --- | --- |
| `model.safetensors` | LFS SHA-256 `9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204` |
| `tokenizer/tokenizer.json` | LFS SHA-256 `609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f` |
| `rl_agent_config.json` | Git blob `00e35f88bb731bb9126a914666ab1cdac8a204c8`，其中 `max_len=1024`、`head_max_len=256`、温度 `[1,1,1]` |

这只证明上述文件内容相同，不代表任意默认 Router 调用都使用该权重。比较时须固定模型路径/revision、输入、题型、候选顺序、长度策略、温度、精度与后端；不需要为这次内容对齐重新下载另一份权重。

## 影响正确率的输入差异

以最新 [`common.py`](https://github.com/NandhaKishorM/laya/blob/4066d5d5fbf08b66c6757ddeedbd797bd7655bc0/laya/common.py) 的 `render_options`、`build_sequence` 和 [`agent.py`](https://github.com/NandhaKishorM/laya/blob/4066d5d5fbf08b66c6757ddeedbd797bd7655bc0/laya/agent.py) 的 `_encode_state`、`_decode_answers` 为参考，当前 [Sezika 序列构造](../src/Sezika/MarkerSequenceBuilder.cs)与[决策引擎](../src/Sezika/ModernBertDecisionEngine.cs)有如下差异：

| 项目 | 固定 Laya `Agent` | 当前 Sezika | 对标要求 |
| --- | --- | --- | --- |
| 题型前缀 | `choice/score/noul question: <instructions>` | 字面量 `type question: <instructions>` | token 序列按真实题型对齐 |
| Boolean 候选 | `false: <描述>` 然后 `true: <描述>`；结果 `P(true)=p[1]` | true 描述在前、false 描述在后；结果 `P(true)=p[0]` | marker 顺序和语义映射一起对齐 |
| Choice/Score 候选 | Choice 保留输入键顺序及 `label: description`；Score 使用 `level i: description` | Choice 按键排序；两类仅使用描述 | 同一候选集合也必须保证文本/位置一致 |
| 文本与结构化 state | 字符串原样，结构化值重新 JSON 序列化；去除输入中伪装的 mask token | 使用 JSON 原始文本或字符串；未执行同样的 mask 文本处理 | 比较渲染后的字符、token IDs 和 marker；结构化值尤其要核对空格/Unicode |
| 长度语义 | `head_max_len=256` 约束说明及候选区域；`max_len=1024` 约束完整序列，state 填充余量 | 运行时取 `min(1024,256,请求预算)` 作为完整序列上限 | 拆分两种预算，重新测覆盖率和信息保留 |
| 超长处理 | 候选先限 48 token，必要时缩短；state 按总长度截取，对话列表保留末尾 | 超过完整 256-token 长度时返回结构化错误 | 严格模式保留无静默截断语义；另用显式兼容模式核对上游截断及受影响内容 |

旧 [质量报告](evidence/quality-2026-09-25.md)中的 316 个 Nimble 长度错误是**当前 Sezika 实现**的观测，不能据此称上游 Laya 的决策 head 只能接收 256-token 完整序列。旧报告按 Sezika 旧渲染计算的完整长度 237–718 token，也不能直接当作 Laya 渲染后的精确长度。PAWS 的 true 偏置与这些输入差异相关的程度尚未经过同权重对照，不能只凭源码审阅给出新正确率。

## 最新上游能力与边界

- `Agent.predict_batch` 对多个 state/问题行做有界 batch，并可按长度分组以减少 padding；它仍为每个问题分别构建双向 cross-encoder 序列，不是一次共享 encoder hidden state。Sezika 可用 C# 复现有界 batch，但要先固定单题 logits 和 batch 引入的数值容差。
- `Agent.predict_long` 将超过上下文的 state 分窗：Boolean 取窗口 `P(true)` 最大值，Choice/Score 取最有信心窗口。上游自己注明这是决定窗口的概率，不能当整篇文档的已校准概率；它不是默认 `system_one` 的等价计算。若引入 Sezika，应作为显式新策略与新质量评测。
- `Agent` 和最新 `ONNXAgent` 支持语言/候选数温度覆盖；现有固定多语言检查点配置温度均为 1。`Router` 可按语言/任务选英文、多语言或专项检查点，因此同权重 `Agent` parity 与 Laya 整体产品对标是两道独立门槛。英文 PAWS 不可直接拿单多语言检查点的成绩代表 Router。新增检查点需逐个审计来源、许可、hash、数值、质量与 AOT。
- 上游可选 `FastLaya` 使用 TileLang、16-bit resident 权重和 CUDA graphs。这些实现与项目 C# kernel/Driver/AOT 约束不兼容；只能比较公开的算法思路和在相同输入/硬件下独立实测，不把上游速度或后端当作 Sezika 现成能力。

## 实施与验收

唯一任务顺序见 [ROADMAP](../ROADMAP.md)。先建立开发期的固定 Laya oracle，导出三种题型、候选顺序、短/中/长、中英及边界样本的渲染文本、token IDs、marker、raw logits 与预测；参考 Python/PyTorch 不进入 Sezika 运行或训练交付路径。然后修正 C# 输入/长度契约，比较同一权重在相同输入上的逐题输出。数值门槛和近并列候选的处理先冻结，再重新运行原题集。

对标通过后才能确定旧 PAWS 偏置中多少是移植误差、多少是权重与任务不匹配。后者在独立开发集上诊断并以有许可的 hard negatives、角色互换和否定样本训练真实 marker head；阈值/温度只在独立校准集选择。PAWS 250 与 Nimble 324 已用于分析，继续作固定审计集；新模型收益用另行封存、按家族隔离的测试集验收。性能画像可与 oracle 工具准备并行，CPU/CUDA 算子优化必须守住数值、质量、AOT 和资源门槛。
