# 中英三题型审计 fixture

12 条数据来自本仓库 S4-02 的四个 `test.jsonl`，每个语言/领域各 3 条，覆盖 Choice、Boolean、Score。代码和这些原创数据均使用 Apache-2.0；来源 SHA-256、转换规则与新数据 SHA-256 记录在 `manifest.json`。

`Prepare-LanguageAudit.ps1` 保留来源题文、标签、候选顺序和显式语言，仅适配 Evaluation 的输入容器及 Boolean 字段名称。没有读取 calibration split，没有增加模型输出或根据输出选择题目。

这些题目已公开且数量很小，只用于固定输入的同权重参考对照、分语言/题型报告及实际模型行为审计。它们不是独立封存质量测试，不能用于证明普遍多语言质量、校准有效性或训练收益。
