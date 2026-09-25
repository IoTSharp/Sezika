# 固定 Laya 输入与长度合同

对应路线图 S3-09 / S3-10。参考源码固定为 Laya `4066d5d5fbf08b66c6757ddeedbd797bd7655bc0` 的 `laya/common.py`（`serialize_state`、`render_criterion`、`render_options`、`build_sequence`）及 `laya/agent.py`（`_to_internal`、`_encode_state`、`_decode_answers`）。此文说明实现合同；真实 oracle、数值、CPU/CUDA、AOT 和质量验收分别以对应运行证据为准。

2026-09-25 最终 solution 构建为零 warning/error，`--prompt-contract` 的 **137 项检查通过**。检查以已捕获的 46 条真实固定 Laya 参考记录为输入：38 条在当前 Sezika 请求合同内的成功输入逐项 token IDs、marker 与候选标签一致，并验证严格策略拒绝兼容策略中发生的截断；其余 4 条候选数量合同差异与 4 条上游预期校验失败均单独计数。扩展 JSON 格式检查使用另列固定期待值，不冒充新采集的模型输出。SIMD/CUDA 的真实 logits、概率、预测对照见[独立数值报告](evidence/laya-parity-2026-09-25.md)，这些结果不代表语言质量已通过。

## 共享输入构造器

2026-09-26 补充：[修正路径两 RID Native AOT 回归](evidence/input-aot-2026-09-26.md)已完成。Windows / Ubuntu WSL2 原生输入检查各138项通过；SIMD/CUDA 的支持范围真实数值与 scalar 三条明确长输入分别通过冻结容差。此证据补齐 S3-10 的 AOT 门槛，不改变本文合同或将旧质量报告迁移到新输入。

`PromptSequenceBuilder.Build(TokenizerJson, JsonElement state, Question, PromptSequenceOptions?, CancellationToken)` 同时供 `ModernBertDecisionEngine` 和离线导出使用，返回 `PromptSequence`：`TokenIds`、`MarkerPositions`、`CandidateLabels`、`TypeId` 与完整诊断 `Diagnostics`。

序列为 `BOS + 题型/说明 + EOS + (MASK + 候选文本) × N + EOS + state + EOS`。三个题型分别使用 `choice question: `、`score question: `、`noul question: `；type ID 为 0、1、2。公共 JSON 问题类型仍为 `choice`、`score`、`boolean`。

| 题型 | 候选文本与顺序 | 解码合同 |
| --- | --- | --- |
| Choice | 保持传入字典的枚举顺序；通常为 `原始标签: 描述`，null 或空字符串描述只渲染原始标签 | logits、概率和预测使用原始标签；不排序、不重命名 |
| Score | 数组顺序为准；`level 0: 描述`、`level 1: 描述` 等 | 概率键为零基数字字符串；score 为级别索引期望值 |
| Boolean | 语义顺序始终为 `false → true`；默认显示前缀 `false: ` / `true: ` | `ProbabilityTrue` 始终取第二个 marker 的概率；显示标签不会改变语义键 |

Boolean 可省略 criteria，或省略其中任一描述；空字符串/null 使用上游默认文案 `no, the statement does not hold` / `yes, the statement holds`。可设置 `labels.false` 与 `labels.true`，二者经去首尾空白后必须非空且不同。未知的 Boolean criteria/labels 属性被拒绝。现有 Sezika 请求合同仍要求至少两个 Choice/Score 候选，默认上限分别为 32/10；这属于对上游合同的明确限制，不能把拒绝此类输入写成与上游完全一致。独立序列构造器允许 1–64 个候选用于离线观察；生产引擎先执行请求校验。

字符串 state/说明/描述保持字符串本身。结构化 JSON 采用 Python `json.dumps(..., ensure_ascii=False)` 的逗号后、冒号后单空格，保留属性顺序与 Unicode，统一字符串转义、布尔值、null 与数值表示。整数不先转为 double，避免丢失大整数精度。浮点数使用 round-trip 文本并转换为 Python 的指数阈值和格式；数值格式仍需与真实参考样例共同验收。

说明、每个已渲染候选和 state 的字面量 `<mask>` 都替换成单个空格，然后编码；真实候选 marker 由构造器独立加入。完整诊断保留渲染前后文本供离线审核，业务响应只返回轻量计数与截断标志，不重复回传输入文本。

## 两种长度策略

请求 `length_policy` 默认 `strict`；显式设置 `laya_compatible` 才允许丢弃输入 token。严格策略先计算同一条兼容序列及诊断，只要说明、候选或 state 将被裁剪，就抛出 `PromptTruncationException`，错误码为 `decision_token_budget_exceeded`。异常带完整诊断，不执行推理。

构造器按固定上游的顺序分配预算：

1. 每个候选的文本最多保留前 48 token；再在前方插入一个 marker。
2. `optionBudget = PrefixTokenBudget - sum(候选文本及 marker 长度)`。当其小于 16 时，每个候选（包含 marker）再限制为 `max(4, floor((PrefixTokenBudget - 16) / N))`，然后重算余量。
3. 题型与说明文本保留前 `max(8, optionBudget)` token。默认 `PrefixTokenBudget=256`；这一预算本身不包括 BOS 和两个前缀 EOS，因此它不是整条序列的 256-token 上限。
4. state 填充总预算扣除前缀和最终 EOS 后的余量。字符串与对象保留开头（右侧截断）；JSON 数组按会话语义保留末尾（左侧截断）。零余量保留零 token，避免 `[-0:]` 错误。
5. 总序列默认最多 1024 token；若最终边界删除了任何候选 marker，则拒绝，不输出缩减后的候选集合。

引擎前缀预算来自已锁定资产的 `HeadMaxTokens`；完整预算为请求 `MaxTokensPerQuestion` 与 encoder `MaxTokens` 的较小值，不再取 `min(..., HeadMaxTokens)`。不修改模型资产或哈希。结构校验仍执行字节、深度、候选数限制；`bytes/4` 只作诊断估计，不能代替真实 tokenizer token 数来拒绝输入。通用小模型引擎仍通过自己的 tokenizer 执行精确预算校验。

每个答案的 `input_diagnostics` 包含策略、是否截断、说明/候选/state 截断标志、原始与实际总 token 数、原始与保留 state token 数、丢弃数量、保留起点和方向，以及 mask 文本清理标志。独立构造器额外提供每段原始/保留 token 数、候选文本、序列化 state 等离线诊断。`UntruncatedTotalTokens` 与 oracle 含义一致：已经按前缀规则裁剪，但尚未裁剪 state 的长度；`OriginalTotalTokens` 才是裁剪任何一段前的长度。

## 资源与验收边界

单次构造默认最多 262,144 个渲染字符，每段编码最多 1,048,576 token，10 秒墙钟 deadline；选项具有固定硬上限。JSON 深度最多 32，候选循环最多 64，BPE 每次合并严格减少符号数量并响应 cancellation。未提供新的权重、校准结果、质量分数或性能承诺。

候选字符预算在逐项渲染时累计检查，避免先保存所有候选的大段全文才拒绝。构造器有独立的预处理 deadline，并接受引擎的总请求取消 token；引擎总 deadline 到期与调用方主动取消继续使用各自的结构化错误语义。完整 state 先编码才执行兼容截断，因此极长无空格片段的 BPE 成本仍可能高，取消与 deadline 负责限制这条路径；本批输入对齐不构成这类输入的性能保证。

既有入口限制须区分：JSON 字节入口在解析前检查 `MaxRequestBytes`（默认 1 MiB）；直接传入 C# DTO 时，没有原始 JSON 字节数，现有 validator 的请求总字节数是估计值，没有执行整体序列化字节硬限制，仍依靠字段、候选数及构造器总字符限制。结构校验发生在引擎总 deadline 启动之前，因此该 deadline 不包括初始 DTO 校验耗时。此次没有扩大重构这两处入口边界，不能把 token/BPE 取消检查描述为已覆盖所有输入校验时间。

旧 `MarkerSequenceBuilder` 仅保留为历史 prompt-v1 fixture helper；正式引擎和新导出共享 `PromptSequenceBuilder`。旧数据/校准 profile 的 prompt 身份不能自动沿用到修正后的输入。应分别记录短/中/长 token 与 marker 一致性、真实 logits/概率误差、失败合同差异、左右截断和预算边界，再进行语言质量与性能验收。
