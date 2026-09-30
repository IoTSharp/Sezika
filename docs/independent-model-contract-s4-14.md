# S4-14 独立模型来源与最小合同

状态：**合同设计完成；原始资产已隔离下载、C# 转换并通过本地导入 smoke；数据/质量/许可发布门槛仍未完成**。核对日期 2026-09-30。本文固定独立路线的候选来源、首轮可训练 head、输入/资产边界和迁移验收项；阶段计划只在 [ROADMAP](../ROADMAP.md) 维护。实际 hash、转换和运行证据见 [S4 独立证据](evidence/s4-marker-head-prototype-2026-09-30.md)。

## 原始来源审核

首选候选是 Hugging Face [`jhu-clsp/mmBERT-base`](https://huggingface.co/jhu-clsp/mmBERT-base)，commit `c5955035435e2bf121cde7f3c8863ef52ff35d82`。2026-09-30 先只读核对了该 revision 的 [模型 API](https://huggingface.co/api/models/jhu-clsp/mmBERT-base/revision/c5955035435e2bf121cde7f3c8863ef52ff35d82)、[文件树 API](https://huggingface.co/api/models/jhu-clsp/mmBERT-base/tree/c5955035435e2bf121cde7f3c8863ef52ff35d82?recursive=true)、[模型卡](https://huggingface.co/jhu-clsp/mmBERT-base/blob/c5955035435e2bf121cde7f3c8863ef52ff35d82/README.md)、[配置](https://huggingface.co/jhu-clsp/mmBERT-base/blob/c5955035435e2bf121cde7f3c8863ef52ff35d82/config.json)和 [tokenizer 配置](https://huggingface.co/jhu-clsp/mmBERT-base/blob/c5955035435e2bf121cde7f3c8863ef52ff35d82/tokenizer_config.json)，随后下载固定文件并完成本地 hash/形状/运行校验。直连下载超时后，按仓库规则对同一请求使用本地代理；完整命令和结果见独立证据。

| 项目 | 固定 revision 的只读发现 | 准入边界 |
| --- | --- | --- |
| 身份与许可 | API `sha` 与上述 commit 一致；模型卡/front matter 和 API 均标 `mit` | 文件树无独立 `LICENSE`；公开发布前核对完整 MIT 授权文本、版权/通知、模型卡和 tokenizer 来源的再分发义务。模型卡标签不替代最终许可审签 |
| 原始权重 | `pytorch_model.bin`，1,231,188,142 字节，LFS SHA-256 `8ea64ec1ea4eb8fca0fc14b69a2ae571de6bfbc25fd214bb932dd4aba6a3a04e`；实测 ZIP/pickle 结构与 hash 一致 | C# `Sezika.IndependentModelTool` 已按受限 pickle 子集转换 134 个 `model.*` encoder tensor 为 SafeTensors；未知 opcode、非连续 view 和非 FloatStorage 拒绝。输出 hash/shape 证据见独立报告；推理时不读 pickle |
| 原始 tokenizer | `tokenizer.json`，17,525,329 字节，LFS SHA-256 `197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356`；实测逐字节 hash 一致并由 C# parser 加载 | 不借用 Laya tokenizer；added-token/混合脚本完整上游 oracle 与 Gemma 2 归属审签仍待补齐 |
| 配置 | `ModernBertForMaskedLM`、22 层、hidden 768、12 heads、MLP 1152、词表 256000、global 每 3 层/local 128、RoPE theta 160000、norm eps 1e-5、最大位置 8192、声明 `torch_dtype=float32` | 形状与现有 encoder 的主要配置相近，但原 checkpoint 的 tensor map、算子数值和真实上下文支持未验证；MaskedLM 任务头不得作为决策头 |
| 特殊 token | config：pad=0、eos/cls/sep=1、bos=2、mask=4；tokenizer config：`add_bos_token=true`、上限 8192 | 需从原 `tokenizer.json` 独立核对 pipeline、added-token 边界及中英/混合脚本 token IDs；不得由 Laya oracle 继承通过状态 |

模型卡列预训练数据和 Gemma 2 tokenizer 来源。模型、tokenizer、原始训练数据和将来用于监督训练的数据各有独立许可/来源；不从模型的 MIT 标签推断训练数据可再分发。Laya 资产的固定身份及 hash 见 [既有来源记录](model-source.md)；其 tokenizer 为 34,363,188 字节、SHA-256 `609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f`，与上述原始仓库的 tokenizer 文件身份不同。Laya manifest 限制 1024 token、F16，并包含已训练 head；这些不是原始底座的原生配置或独立 head 证据。不能从 Laya 检查点抽取 encoder 后改称原始权重。

## 自有输入协议草案

拟定独立 `decision-v1`，与旧请求和模型 manifest 分开版本化。`schema_version` 固定整数 `1`，`model_id` 必须精确匹配完整 checkpoint 身份，`state` 是非 null 的 JSON 字符串/对象/数组，`questions` 是 1–32 项的有序数组。每题有唯一非空 ID、显式 `type` 与非空 `instruction`。Choice 有 2–32 个按数组顺序排列、ID 唯一且文本非空的候选；Score 有 2–10 个非空文本的有序等级，等级键固定为 `0..K-1`；Boolean 必须有非空 `statement`、`when_false` 和 `when_true`，固定候选顺序为 `false,true`。首轮覆盖中文、英文三题型；具体业务任务与获准数据由 S4-05/S4-06 冻结。旧 Laya ID 不是隐式别名。

以下仅展示请求形状，`model_id` 是尚未创建的占位身份，不是可加载资产或模型输出：

```json
{
  "schema_version": 1,
  "model_id": "sezika/<checkpoint-id>",
  "state": { "text": "示例文本" },
  "questions": [
    { "id": "intent", "type": "choice", "instruction": "选择意图", "candidates": [
      { "id": "a", "text": "查询" }, { "id": "b", "text": "其他" }
    ] },
    { "id": "quality", "type": "score", "instruction": "评估质量", "levels": ["低", "高"] },
    { "id": "valid", "type": "boolean", "instruction": "判断命题", "statement": "文本包含查询请求", "when_false": "不成立", "when_true": "成立" }
  ]
}
```

每题构造**一条完整 cross-encoder 序列**：受控 BOS、类型/指令段、受控 EOS、state JSON 段、受控 EOS、按输入顺序的候选段（每段前由构造器插入一个专用 marker）、末尾受控 EOS。题目、state、候选段分别以固定键顺序的 JSON 文本编码；字符串按 System.Text.Json 规则转义，`<`/`>` 必须转义，数值按版本固定的 invariant 格式呈现。用户段编码后若含 BOS/EOS/marker 等结构控制 ID，应拒绝；用户文本不得成为分段边界。重复/未知属性、重复 ID 和不支持的 JSON 类型在构造前拒绝。marker 位置由构造器记录，绝不靠搜索文本恢复。原始 tokenizer 的 BOS/EOS/marker ID 及 added-token 行为必须先由 S4-15 核验；若选用 mask=4 作为 marker，也必须核对原始 tokenizer 与参考 encoder 的真实语义。训练、缓存和推理复用同一版本构造器与完整 token ID/marker 序列。

初始总上限 1024 token，且不得超过已验证的 encoder 上限及调用方预算；默认严格拒绝任何会丢失指令、state 或候选的输入，不静默截断或删除候选。请求/字段长度、题数、候选数、总 token、内存、截止时间和取消在分配与 forward 前检查，返回结构化原因。若未来引入截断，必须新开显式版本/策略并重新训练、校准和验收；`laya_compatible` 不属于 `decision-v1`。输出沿用类型化 Choice 分布、Score 等级分布/期望和 Boolean `P(true)` 的语义，保留 logits、模型/协议身份、`uncalibrated` 状态及明确的拒答/失败；分布集中度不是正确率或执行授权。

## 首轮最小可训练 head

选定**三题型各一行的线性 marker scorer**，不继承 Laya 的 `type_emb`、两层 Transformer head、scorer 或温度。冻结原始 encoder 后，对每个候选 marker 的最后层 hidden `h_i ∈ R^H` 计算 `logit_i = W[type] · h_i`；`W` 形状 `[3,H]`，按 Choice/Score/Boolean 显式索引。H 从通过验证的 encoder 配置读取，首选候选为 768。所有 `3H` 个 head 参数均由自有训练器更新；以全零权重开始，记录初始化算法和版本，不存在随机初始化却冻结的题型向量或中间层。同一题各候选共享的偏置在 softmax 中恒相消，故最小合同不设偏置。当前 `DecisionHead.cs` 的两层 head 前向可以保留作 legacy 参照，不能冒充本合同已实现。

Choice/Score/Boolean 均按候选顺序使用 softmax 与人工硬标签交叉熵，Boolean 的真类固定索引 1；Score 同时报告等级期望与 MAE，并在独立开发证据下比较 ordinal 损失。按题型/语言分别记录训练与开发分母，避免一种题型或语言替代其他题型的验收。首轮冻结 encoder 的每个原始参数及 tokenizer；仅对完整题目序列的冻结 encoder 输出缓存特征，缓存 key 包含原始 encoder/tokenizer hash、协议版本、完整 token IDs、marker、长度策略和数据记录 ID。优化器、梯度、有限值检查、种子/样本顺序、步数/时间/取消预算及 checkpoint 恢复由 S4-07/S4-12 实现和验证；先做极小张量梯度检查，再做最多 32 条经审核的原创训练冒烟。冒烟结果不代表质量。

现有 [`HeadTraining.cs`](../src/Sezika/HeadTraining.cs) 仍仅训练二分类线性示例特征，不代表本合同。S4-07/S4-12 原型已通过 350 项测试；S4-15 已把真实独立 encoder 的完整序列特征接入 12 条原创开发 smoke，并保存独立 head asset。更复杂的可训练题型向量、MLP/Transformer head 或 encoder LoRA 只能在独立开发证据表明必要时扩展；若增加这些参数，须一起训练并核对梯度，不能冻结随机层后仅训练末端 scorer。当前训练仍不是质量验收。

## 资产身份与迁移清单

新资产包须使用与 Laya 不同的 `model_id`/checkpoint revision，并记录原始 encoder 仓库+commit、原始权重 hash、转换工具版本与输出 hash、tensor map/形状/dtype、独立 tokenizer 仓库+commit/hash、`decision-v1`/head 版本、训练数据清单 hash、训练配置/种子、head checkpoint hash、分开的许可/通知。优化器状态只属于训练 checkpoint；校准 profile 单独绑定新模型/head/tokenizer/协议/split，默认 `uncalibrated`。权重不嵌入二进制或 NuGet。

| 当前绑定 | 独立迁移验收项 | 阶段 |
| --- | --- | --- |
| `ModernBertModelLoader` 和 ModelTool 硬编码 Laya ID、revision、hash，并要求其 head/temperature 张量 | 独立受限导入原始权重、安全转换、单独 head 资产；坏 hash/形状/来源拒绝。Laya 校验只在显式 legacy 入口 | S4-15、S4-12 |
| `DecisionModelRuntime` 默认经固定 loader；`PromptSequenceBuilder` 使用 `noul`、Laya 文本/预算和 `laya_compatible` | 独立模型选择、自有构造器与 `decision-v1` 严格输入；无 Laya 包环境下可运行，旧路径须显式选择 | S4-15 |
| `DecisionHead.cs` 只实现既有两层 head 前向；`HeadTraining.cs` 是二分类示例 | 三题型仿射 marker head 的前反传、冻结 encoder 特征和保存恢复；独立数值 oracle 与真实训练证据 | S4-07、S4-12 |
| 既有 tokenizer oracle、Laya 质量/AOT/校准 profile | 对原始 tokenizer 重新做 token IDs、added-token 和混合脚本验证；新权重独立做数值、分语言/题型质量、校准、CPU/CUDA 与两 RID AOT 验收后才切换默认入口 | S4-15、S4-08 |

## S4-05 数据候选依赖

2026-09-30 只读核对各 Hugging Face [数据集 API](https://huggingface.co/docs/hub/api) 和对应 revision 的数据卡；以下是**候选筛选**，不是 S4-05 许可审签、原始文件 hash 验证、人工标签复核或训练准入。数据集卡许可不自动决定训练后权重的许可，也不能替代原始上游来源/再分发条款。

| 候选与固定数据卡 revision | 卡片许可及标注线索 | 原始许可/人工标签/公开发布仍待核查 |
| --- | --- | --- |
| [MASSIVE 1.1](https://huggingface.co/datasets/AmazonScience/massive/tree/ff6bd8e4b27c3543e4f8fe2108f32bb95a6f8740) `ff6bd8e4b27c3543e4f8fe2108f32bb95a6f8740` | `CC-BY-4.0`；卡片列 `expert-generated`、52 种语言、意图/槽位标签，源于 SLURP 本地化 | 逐项确认 SLURP 原始内容与翻译权利、原始许可、具体版本/配置及署名；中英标签语义经人工审核，分别决定数据与权重发布方式 |
| [CLINC150 / clinc_oos](https://huggingface.co/datasets/clinc/clinc_oos/tree/155b9c710419136e17307b80d0a13e68cd46b4ec) `155b9c710419136e17307b80d0a13e68cd46b4ec` | 数据卡 `CC-BY-3.0`；卡片列 expert/crowdsourced、150 intents + OOS，存在 `imbalanced`/`plus`/`small` 配置 | 对照原始 CLINC `oos-eval` 仓库许可与标注流程，固定配置和原始文件；核对署名、数据再分发及权重发布条件，英语样本不算中文质量证据 |
| [Civil Comments](https://huggingface.co/datasets/google/civil_comments/tree/f2970eb3a55777454c94069077cc8d9b5866312d) `f2970eb3a55777454c94069077cc8d9b5866312d` | 卡片 `CC0-1.0`；含 toxicity 等浮点标签，但卡片标注流程/标注者为 `More Information Needed` | 向原始 Jigsaw/Civil Comments 来源核实人工评分方式、许可及隐私/敏感文本范围；不得把浮点评分直接当二分类金标或已获准再分发 |
| [Aegis 2 / Nemotron Content Safety V2](https://huggingface.co/datasets/nvidia/Aegis-AI-Content-Safety-Dataset-2.0/tree/d86bb8bedff51d25ac834ab7838f1cc61acb7a2c) `d86bb8bedff51d25ac834ab7838f1cc61acb7a2c` | 卡片 `CC-BY-4.0`；`prompt_label_source=human`，`response_label_source` 可为 human、LLM jury 或 augmentation；prompts 涉 Anthropic HH-RLHF 等来源 | **仅筛选明确人工 prompt 标签**；逐来源审核原始 prompt 许可、二次授权、内容处理和公开权重条件，排除 LLM response 标签冒充人工金标 |
| [HelpSteer2](https://huggingface.co/datasets/nvidia/HelpSteer2/tree/990b2711a36180dd19d9c94b8627844866f8982a) `990b2711a36180dd19d9c94b8627844866f8982a` | 卡片 `CC-BY-4.0`；Scale AI 对五个维度做人工 0–4 评分及偏好标注；prompts 多来自 ShareGPT，responses 为模型生成 | 分别核对 ShareGPT prompt、模型生成响应及人工标签权利与版本；定义 Score 维度映射/偏好转换，经人工复核后再判训练用途和公开权重条件 |

各候选均需在 S4-05 单独存原始许可文本及审签证据、版本/配置/文件 SHA-256、允许的研究/商业/开源再分发用途、标签 provenance 和去标识化记录；无批准清单不得进入正式训练。train、development、calibration、sealed test、audit 按家族/实体/翻译/改写/反事实隔离；已查看的 PAWS 250/Nimble 324 及衍生物仅入 audit。S4-10/S4-11 的 Tomur 教师输出仍须独立许可及人工复核，不替代人工真值。
