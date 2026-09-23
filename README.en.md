# Sezika

**Multilingual, non-autoregressive System 1 decision engine.**

Sezika targets local, typed semantic decisions using **C#, .NET 10 and Native AOT**. Its inference path uses a managed bidirectional Transformer encoder and decision head to turn text or JSON state and questions into Choice, Score and Boolean answers.

## Status

This repository contains research, an implementation roadmap, a .NET 10 library, bounded loading for the pinned Laya/mmBERT development asset, tokenizer oracle fixtures, a scalar FP32 encoder/head, typed decision inference, calibration metrics, build-time ILGPU PTX/ABI artifacts, a resident CUDA encoder/head, and a win-x64 Native AOT smoke. **Model weights are not bundled for release; multilingual quality evidence and Tomur integration remain open.**

## Design

- C# model code and CPU/GPU kernels with explicit resource limits, cancellation and source-generated JSON. Runtime native calls are limited to operating-system/device drivers.
- NVIDIA path: compile C# kernels to PTX and an ABI manifest with ILGPU 1.5.3 at build time, then load and launch the static artifacts through C# CUDA Driver bindings in the Native AOT runtime. ILGPU's ordinary runtime JIT/launcher path is not considered Native AOT compatible. Native compute libraries such as cuBLAS/cuDNN are outside the design.
- A multilingual encoder and typed decision head; the first candidate follows the mmBERT-base architecture used by Laya's multilingual checkpoint.
- Full candidate distributions, ordinal expected scores and probability of truth. Distribution concentration and calibration status remain separate.
- Language quality, calibration, model correctness and latency each require their own evidence.
- Code, model weights, tokenizers and training datasets retain separate licenses and distribution checks.
- Callers control actions and permissions. The engine makes predictions and can abstain; it does not execute tools.

Sezika is an independent project. Tomur integration is planned through a statically referenced C# library and an in-process decision provider, with model assets managed by Tomur. Integration is not implemented yet.

See the [roadmap](ROADMAP.md), [research](docs/research.md), [architecture](docs/architecture.md), [GPU/AOT design](docs/gpu-aot.md) and [Tomur integration design](docs/tomur-integration.md). Project documentation is primarily in Chinese.

Owned source is licensed under [Apache-2.0](LICENSE). See [third-party notices](THIRD_PARTY_NOTICES.md). Sezika does not claim to reproduce Jev's closed model or inherit another project's performance results.
