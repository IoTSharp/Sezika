# Third-party notices

截至 2026-09-23，本仓库未包含第三方模型权重、tokenizer、训练数据或复制的上游实现。

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

- ILGPU 来源：https://github.com/m4rs-mt/ILGPU 。计划只作为构建期 C# → PTX 编译器，尚未加入包依赖。锁定版本时须核对 LICENSE/第三方组件与生成产物的分发义务。
- NVIDIA CUDA Driver 由系统显卡驱动提供，计划作为运行时设备接口，不由本仓库重新授权或默认打包。最低驱动版本和 PTX 兼容矩阵需实测记录。

后续新增代码依赖、转换工具、权重、数据或校准集时，在本文件与对应 manifest/model card 中记录具体版本、来源、许可与分发条件。
