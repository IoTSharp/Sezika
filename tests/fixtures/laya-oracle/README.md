# Laya oracle 输入与数值合同

`cases.v1.json` 是原创 Apache-2.0 工程输入，`contract.v1.json` 是测量前冻结的比较合同。`reference.cpu-fp32.v1.json` 是固定 Laya 0.3.20、相同权重下的真实 CPU FP32 捕获，46 条输入包含 42 条回答与 4 条非法输入拒绝，SHA-256 为 `bf0d537305149672f07d34dc6b3e2d3614aeb2c26aa6d11aa7902578100c53fa`。命令及环境见[执行证据](../../../docs/evidence/laya-oracle-2026-09-25.md)。数据用于输入/数值移植检查，不是 ground truth、语言质量评测、训练或校准结果。

清单包含 Choice、Score、Boolean 三类，每类中英两语言、短/384-token 中输入/960-token 长输入，共 18 条基本组合。其余条目覆盖候选原始顺序、Unicode/对象/对话、结构化说明与候选、mask 字符去除、Boolean 默认文本与显示标签、总长 255/256/257 和 1023/1024/1025、1/2/32/33 Choice 候选、1/2/10/11 Score 等级、48-token 候选上限、前缀缩短及四类上游非法题目。

短输入直接存文本；中、长及总长边界保存有界配方。只有获准执行后，配方才会由固定上游 tokenizer 生成真实字符串并要求精确未截断总长。它不存猜测的 token IDs，也不以截断后的 1024 冒充 1025 输入。对话案例保留最近的纠正信息；字符串案例保留前部，同一方向差异在导出中显式记录。

1 个候选、33 个 Choice 候选与 11 个 Score 等级用于审阅上游与 Sezika 当前产品限制的差异。上游能回答不表示 Sezika 已支持；产品严格拒绝也不能伪造成同条件数值对齐。显示标签只影响文本；Boolean 数值语义标签始终是 `false,true`，`P(true)` 始终取 marker 1。

原始 JSON 字节的 SHA-256 写入导出 provenance，目录 `.gitattributes` 禁止 Git 换行转换。如果修改输入、门槛或模型身份，必须保留旧结果并生成新的版本/哈希，不能覆盖已测量合同后继续宣称同一基线。

工具、后续有界命令及完整输出合同见 [导出器说明](../../../tools/Sezika.LayaOracle/README.md) 与 [设计说明](../../../docs/laya-oracle.md)。
