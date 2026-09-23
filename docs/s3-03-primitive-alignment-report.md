# S3-03 fixture 对齐报告

日期：2026-09-23  
报告类型：`fixture_reference_contract`（离线、确定性）

## 输入与范围

- Fixture：[`tests/fixtures/primitive-reference-synthetic.json`](../tests/fixtures/primitive-reference-synthetic.json)
- Fixture SHA-256：`4703e979109c31296934d0922f3d51d12ae09f0237e6173cb9847dd123720e38`
- Schema：`sezika.primitive-reference.v1`
- Model/tokenizer revision：`fixture-revision` / `fixture-tokenizer`
- Prompt schema：`sezika.prompt.v1`
- 比较容差：`1e-12`

Fixture 包含 4 个绑定输入：英文 choice、英文 score、中文 choice、中文 boolean。每条记录带有 state、instructions、criteria、primitive、候选顺序、raw logits、概率和 answered/abstention 字段。score case 的 legend 采用 JSON 深度相等比较。

## 结果

| 项目 | 结果 |
| --- | ---: |
| Fixture cases | 4 |
| Compared cases | 4 |
| 最大 logits 绝对误差 | `0` |
| 最大概率绝对误差 | `5.551115123125783E-17` |
| Score legend | 逐键相等 |
| Abstention 字段 | 逐键相等（本 fixture 全部 `answered`、无拒答原因） |
| 总体 | `PASS` |

通过 [PrimitiveAlignment](../src/Sezika/PrimitiveAlignment.cs) 的 fixture 校验后，比较器会拒绝重复 case ID、未知语言或 primitive、revision 不一致、非有限数值、概率键集合不一致、未归一化概率和不完整的 abstention 语义。测试还覆盖缺少 raw logits/probabilities、legend 或 revision 的响应失败路径。

## 验收边界

该报告证明 S3-03 的固定参考格式、输入绑定和 typed response 比较契约已经闭环。fixture 中的 logits 是确定性协议数据，不能推导真实模型的语言准确率、校准质量或执行授权。真实 mmBERT 对齐仍需独立参考实现和固定中英文真实输入的模型/tokenizer hash 报告；那属于后续真实模型质量验收。

验证命令（PowerShell 7）：

```text
dotnet build tests/Sezika.Tests/Sezika.Tests.csproj -c Release --nologo
dotnet run --project tests/Sezika.Tests/Sezika.Tests.csproj -c Release --no-build
```

本次核心测试结果：`22` 项通过，包含四 case fixture 对齐报告。
