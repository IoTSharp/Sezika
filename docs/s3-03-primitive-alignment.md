# S3-03 primitive 对齐契约

状态：`✅ 实现完成`。`PrimitiveAlignment` 和 [synthetic fixture](../tests/fixtures/primitive-reference-synthetic.json) 已加入仓库，用于固定比较格式、中文/英文输入结构和数值容差；fixture 使用确定性 logits，只证明比较器和 typed response 的结构，不代表真实 mmBERT 质量或中英语言准确率。

每个参考 case 绑定 `model_revision`、`tokenizer_revision`、prompt schema、语言和 primitive。运行时 response 保留候选顺序对应的 raw logits、概率、Score legend 与 `answered`/`abstained` 及拒答原因。比较器逐键检查有限数值、候选集合、绝对误差、legend JSON 深度相等和 abstention 语义；缺失 tokenizer/model revision、primitive 类型或 raw alignment 值会失败。

本轮固定了 4 个中英文输入 case（英文 choice/score，中文 choice/boolean），并验证 logits、概率、Score legend 和 abstention 字段的逐键比较。真实模型对齐报告仍应补充模型与 tokenizer hash、运行版本及真实参考实现输出；该质量/模型正确性证据与本任务的比较契约实现分开验收。
