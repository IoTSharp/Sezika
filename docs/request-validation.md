# 请求校验与预算

`DecisionRequestParser.Parse` 是 JSON 入口的严格边界。它先限制 UTF-8 字节数和 JSON 深度，再拒绝每个对象内重复的属性名，随后使用 `DecisionJsonContext` 反序列化并调用 `DecisionRequestValidator` 检查问题、候选与字节预算。UTF-8 token 估算只作诊断，不能用于提前拒绝真实 tokenizer 可以接受的请求。

```csharp
var request = DecisionRequestParser.Parse(utf8Payload);
var result = DecisionRequestValidator.Validate(request);
// result.EstimatedTokens 只是分配前的 UTF-8 估算。
// 真实 tokenizer 得到 token 数后，应再次调用 EnsureTokenBudget。
```

默认边界为：请求 1 MiB、JSON 深度 32、问题 32 个、choice 候选 2–32 个、score 等级 2–10 个、每个问题实际 token 最多 1024 个、state 256 KiB、instructions 64 KiB、标识符 128 个字符。模型 manifest 可以创建 `DecisionLimits` 覆盖这些值，但更宽的边界需要独立的资源证据。

校验失败抛出 `DecisionException`，`Code` 可供 API 映射，例如：

| 输入 | Code |
| --- | --- |
| `{"model":"m","model":"m2"}` | `decision_duplicate_property` |
| 超过请求字节数或 JSON 深度 | `decision_input_limit_exceeded` / `decision_input_depth_exceeded` |
| 超过问题或候选上限 | `decision_question_limit_exceeded` / `decision_candidate_limit_exceeded` |
| 真实 tokenizer 超过预算 | `decision_token_budget_exceeded` |

实际模型通过共享 `PromptSequenceBuilder` 分别执行 256-token 说明/候选前缀预算和 1024-token 总长度预算。默认 `length_policy: "strict"` 拒绝任何说明、候选或 state token 丢失；显式 `"laya_compatible"` 使用固定上游截断规则，并在成功 answer 的 `input_diagnostics` 中记录保留/丢弃计数和方向。严格拒绝抛出携带完整诊断的 `PromptTruncationException`，code 为 `decision_token_budget_exceeded`。数组 state 保留末尾，字符串和对象保留开头。详见[输入合同](input-contract-s3-09.md)。

最小验证向量可以在不加载模型的情况下覆盖边界：

1. 将上表中的重复 `model` 属性传给 `Parse`，应得到 `decision_duplicate_property`。
2. 将 `score.criteria` 扩展到 11 项，应得到 `decision_candidate_limit_exceeded`。
3. 用 `new DecisionLimits { MaxTokensPerQuestion = 1 }` 调用 `EnsureTokenBudget(2, limits)`，应得到 `decision_token_budget_exceeded`；单纯结构 `Validate` 不根据估算 token 拒绝输入。
4. 构造两个合法 choice 候选并调用 `Validate`，应返回 `QuestionCount == 1` 且不抛出异常。

这些向量只验证输入边界，不代表 tokenizer、模型权重、CPU/GPU 数值或 Native AOT 已通过验收。
