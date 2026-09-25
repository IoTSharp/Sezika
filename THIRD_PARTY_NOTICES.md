# Third-party notices

截至 2026-09-23，模型权重和 tokenizer 只作为本机 `.artifacts` 中的已校验开发资产，不随 Git 仓库、NuGet 或可执行文件分发；训练数据未随本项目分发。

## Laya multilingual / mmBERT

- 来源：<https://huggingface.co/convaiinnovations/laya-multilingual>
- 固定 revision：`052592a15d198d9ad47da779604259b10b47b7aa`。
- 模型卡许可证：Apache-2.0；`model.safetensors` 与 `tokenizer/tokenizer.json` 的 SHA-256 记录在 `docs/model-source.md` 与本机 `source_lock.json`。
- 底座来源：`jhu-clsp/mmBERT-base` revision `c5955035435e2bf121cde7f3c8863ef52ff35d82`，模型卡声明 MIT；tokenizer 架构注明 Gemma 2 来源。
- 用途：真实本地 encoder、决策 head、tokenizer oracle 与 CUDA 数值验证。资产不嵌入二进制。

## Laya

- 来源：https://github.com/NandhaKishorM/laya
- 研究提交：`c7527708f9f5220c669d8aa385077cd28d04708a`
- 源码许可证：Apache-2.0，参见该提交的 LICENSE。
- 用途：研究双向 encoder、typed decision head、prompt、训练与校准方式。
- 后续若移植代码，必须保留相关归属与许可证、注明修改，并核对上游 NOTICE。不能只更换命名空间后声称全部为原创。
- 权重、ModernBERT/mmBERT 底座、tokenizer、数据集和测试夹具仍须独立核验，未完成审核前不随本项目分发。

## TypeSafe Jev

- 来源：https://docs.typesafe.ai/ 与 https://typesafe.ai/
- 用途：研究公开 System One 产品语义、typed primitives 和 HTTP 协议。
- 本次未发现可移植的公开模型实现或权重；公开 SDK 与示例的许可不等同于模型许可。
- Sezika 不是 TypeSafe 官方 SDK，不声称 Jev 模型复现或官方关联。兼容能力必须逐项列出并测试。

## ILGPU 与 CUDA Driver

- ILGPU 来源：https://github.com/m4rs-mt/ILGPU ，固定构建期包版本 1.5.3；只用于 C# → PTX 编译工具，不进入发布运行时项目。分发生成 PTX 前仍需按其 LICENSE/第三方组件要求核对。
- NVIDIA CUDA Driver 由系统显卡驱动提供，作为运行时设备接口，不由本仓库重新授权或默认打包。已记录 RTX 4070 Laptop、driver 596.08、PTX 7.0 / CC 8.9 证据。

后续新增代码依赖、转换工具、权重、数据或校准集时，在本文件与对应 manifest/model card 中记录具体版本、来源、许可与分发条件。

## 离线 PAWS 评测数据准备

- [DuckDB.NET.Data.Full 1.5.3](https://github.com/Giorgi/DuckDB.NET) 及其依赖 `DuckDB.NET.Bindings.Full 1.5.3` 的 NuGet 元数据均声明 MIT；仅 `tools/Sezika.DatasetTool` 离线读取固定 Parquet，不进入模型推理、AOT 或 NuGet 运行时包。若分发该数据工具及依赖，仍须随包保留相应许可文本。
- [Google PAWS](https://huggingface.co/datasets/google-research-datasets/paws) 的人标 test 数据，固定提交与文件 SHA-256 见 [质量证据](docs/evidence/quality-2026-09-25.md)；数据卡注明可免费用于任何用途并要求标注来源。原始数据和由其构造的评测 JSONL 仅保留在本机忽略目录，未随仓库分发。
- Nimble 的固定 PAWS ID 清单及 324 条合成题来自提交 `62076b4f2d365b5879dafcf7f6dd072a1fe76df7`。ID 清单只用于选择人标测试行；合成题仅本地评测，未用作训练且未随仓库分发。
