# Changelog

## Unreleased

- Added bounded request parsing, duplicate-property rejection, SafeTensors model assets, the pinned Apache-2.0 Laya/mmBERT package manifest and tokenizer oracle, scalar FP32 encoder/head, typed Choice/Score/Boolean engine, calibration metrics, ILGPU 1.5.3 build-time PTX/ABI artifacts, resident CUDA encoder/head, and Native AOT GPU smoke evidence. The ignored model artifact is not a release or multilingual quality claim.
- 建立独立本地 Git 仓库、项目定位、参考分析、架构、路线图与 Tomur 对接设计。
- 加入 .NET 10 类库工程及 typed decision 契约草案。
- 明确纯 C# CPU/GPU 与系统驱动例外，记录 ILGPU 构建期导出 PTX、Native AOT CUDA Driver 执行的设计及原型关口。

真实模型 CPU/CUDA 数值与 win-x64 AOT smoke 已有记录；多语言质量、跨平台性能和 Tomur 宿主接入仍未验收。
