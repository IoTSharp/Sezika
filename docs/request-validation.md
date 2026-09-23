# 请求校验与预算

`DecisionRequestParser.Parse` 是 JSON 入口的严格边界。它先限制 UTF-8 字节数和 JSON 深度，再拒绝每个对象内重复的属性名，随后使用 `DecisionJsonContext` 反序列化并调用 `DecisionRequestValidator` 检查问题、候选和估算 token 预算。

```csharp
var request = DecisionRequestParser.Parse(utf8Payload);
var result = DecisionRequestValidator.Validate(request);
// result.EstimatedTokens 只是分配前的 UTF-8 估算。
// 真实 tokenizer 得到 token 数后，应再次调用 EnsureTokenBudget。
```

默认边界为：请求 1 MiB、JSON 深度 32、问题 32 个、choice 候选 32 个、score 等级 10 个、每个问题估算 token 1024 个、state 256 KiB、instructions 64 KiB、标识符 128 个字符。模型 manifest 可以创建 `DecisionLimits` 覆盖这些值，但更宽的边界需要独立的资源证据。

校验失败抛出 `DecisionException`，`Code` 可供 API 映射，例如：

| 输入 | Code |
| --- | --- |
| `{"model":"m","model":"m2"}` | `decision_duplicate_property` |
| 超过请求字节数或 JSON 深度 | `decision_input_limit_exceeded` / `decision_input_depth_exceeded` |
| 超过问题或候选上限 | `decision_question_limit_exceeded` / `decision_candidate_limit_exceeded` |
| 真实 tokenizer 超过预算 | `decision_token_budget_exceeded` |

UTF-8 估算不能替代模型 tokenizer；它只用于在分配大张量前设置保守边界。真实 tokenizer、模型上下文上限和请求方预算取其中更小值，并通过 `EnsureTokenBudget` 复核。

最小验证向量可以在不加载模型的情况下覆盖边界：

1. 将上表中的重复 `model` 属性传给 `Parse`，应得到 `decision_duplicate_property`。
2. 将 `score.criteria` 扩展到 11 项，应得到 `decision_candidate_limit_exceeded`。
3. 用 `new DecisionLimits { MaxTokensPerQuestion = 1 }` 调用 `Validate`，应得到 `decision_token_budget_exceeded`。
4. 构造两个合法 choice 候选并调用 `Validate`，应返回 `QuestionCount == 1` 且不抛出异常。

这些向量只验证输入边界，不代表 tokenizer、模型权重、CPU/GPU 数值或 Native AOT 已通过验收。
