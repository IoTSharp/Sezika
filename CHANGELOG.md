# Changelog

## Unreleased

- 新增独立 CPU `inspect`/`predict` CLI 和 `DecisionModelRuntime.Load/Evaluate` 类库入口，支持模型目录、文件/stdin JSON、三种 typed decision、输入预算、取消和结构化错误；修复并发 Dispose 清空活跃推理权重的竞态。固定真实 Laya/mmBERT 的中英文三问题请求已通过运行验证，仍返回 `uncalibrated`。
- 完成 S1-04 的有界模型包发布目录、loopback HTTP 断点 Range 下载、SHA-256 校验、安装清单、staging 原子提交与 lease 卸载；完成 S2-03 的 scalar/SIMD encoder 对齐、取消/deadline、workspace 和并发矩阵；完成 S3-03 对齐契约与 S3-04 session/资源预算及 unload 生命周期；建立 S4-02 中英原创 fixture 数据卡、split/provenance manifest 和 S4-03 的 hash 绑定 `pending_measurement` profile 与冻结门槛。S1-04 不下载真实模型；真实中英 primitive 参考数值和多语言质量指标仍未验收。
- Added the real mmBERT marker-head request path (`ModernBertDecisionEngine`) with bounded total-token/deadline/head budgets and Choice/Score/Boolean typed smoke coverage. Added negative SafeTensors tests for overlapping, out-of-bounds, and unsupported-dtype ranges, plus CPU win-x64/linux-x64 Native AOT smoke samples. Kernel artifact generation is now BOM-free and newline-stable, with byte-identical PTX/manifest regeneration evidence.
- Made the core library packable as the development `Sezika.0.1.0-dev` NuGet package with repository documentation and license notices included.
- The pinned model loader now checks every manifest tensor's dtype/shape entry and raw-byte SHA-256 before constructing the encoder or decision head.
- Added bounded request parsing, duplicate-property rejection, SafeTensors model assets, the pinned Apache-2.0 Laya/mmBERT package manifest and tokenizer oracle, scalar FP32 encoder/head, typed Choice/Score/Boolean engine, calibration metrics, ILGPU 1.5.3 build-time PTX/ABI artifacts, resident CUDA encoder/head, and Native AOT GPU smoke evidence. The ignored model artifact is not a release or multilingual quality claim.
- 建立独立本地 Git 仓库、项目定位、参考分析、架构与路线图。
- 加入 .NET 10 类库工程及 typed decision 契约草案。
- 明确纯 C# CPU/GPU 与系统驱动例外，记录 ILGPU 构建期导出 PTX、Native AOT CUDA Driver 执行的设计及原型关口。

真实模型 CPU/CUDA 数值与 win-x64 AOT smoke 已有记录；多语言质量和跨平台性能仍未验收。
