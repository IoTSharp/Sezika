# Sezika

**Multilingual, non-autoregressive System 1 decision engine.**

Sezika targets local, typed semantic decisions using **C#, .NET 10 and Native AOT**. Its inference path uses a managed bidirectional Transformer encoder and decision head to turn text or JSON state and questions into Choice, Score and Boolean answers.

## Status

This repository includes bounded loading for the pinned Laya/mmBERT model, tokenizer oracle fixtures, C# scalar/SIMD FP32 and W8A32 encoder/head paths, real Choice/Score/Boolean decisions, calibration metrics, build-time ILGPU PTX/ABI artifacts, and resident CUDA inference. S5-04/S5-05 are complete within the measured scope: Windows `win-x64` Native AOT benchmarks cover all four backends at 1/8/32 questions with five samples per configuration; Ubuntu WSL2 `linux-x64` real-model Native AOT smoke covers all four backends at three questions with one sample each. **WSL smoke does not establish bare-metal Linux performance. Multilingual quality remains unaccepted, and model weights are not bundled for release.** See the [S5 performance and AOT evidence](docs/s5-performance-aot.md) and [raw reports](docs/evidence/s5-2026-09-24/).

The current W8A32 path is slower than SIMD. It retains the original FP32 weights and adds quantized caches, so it does not reduce total resident memory. Numerical alignment, actual inference, AOT compatibility, language quality and performance benefits remain separate claims.

After correcting prompt rendering and the separate 256-token prefix / 1024-token sequence budgets, the [2026-09-26 quality runs](docs/evidence/quality-aligned-2026-09-26.md) loaded the fixed model and executed the C# CUDA encoder/head on all 250 PAWS and 324 Nimble records. PAWS strict answered 250/250 with 170 correct (68.00%); Nimble compatible answered 324/324 with 137 correct (42.28%). Every answered row records an actual forward call, input/token hashes, markers and logits. Independent reference comparisons cover only 46 selected records, not the full 574. Nimble Boolean negative recall remains 1/57; probabilities are uncalibrated and per-language quality remains unaccepted. The [current performance evidence](docs/evidence/s5-profile-2026-09-26.md) reports actual model execution separately from tokenizer prechecks and records sample counts and limitations.

A separate complete Nimble strict run answered 306/324 with 133 correct: 43.46% of answered records and 41.05% of all processed records. The other 18 records were rejected before forward because they required truncation; no replacement answers were generated.

On an i9-13900HX / RTX 4070 Laptop with managed .NET 10.0.11, CUDA completed all nine short/medium/long × 1/8/32-question configurations with three formal samples each; long-32 p50 was 40.464 seconds. CPU SIMD completed eight configurations with one formal sample each; long-8 took 141.973 seconds, while long-32 hit the 300-second request deadline during discovery and produced no formal latency sample. The complete failure report is retained. These measurements identify substantial remaining performance work and do not establish stable tail latency or new Native AOT performance results.

## Standalone use

Sezika can load the pinned model package and evaluate typed decisions without a host integration. Prepare and verify the model assets using the [standalone guide](docs/standalone-usage.md), then run these commands from the repository root with the .NET 10 SDK:

```powershell
dotnet build Sezika.slnx -c Release
dotnet run --project src/Sezika.Cli -c Release --no-build -- inspect --model .artifacts/models/laya-mmbert
dotnet run --project src/Sezika.Cli -c Release --no-build -- predict --model .artifacts/models/laya-mmbert --input samples/requests/decision.en.json --deadline-seconds 300
```

The CLI uses CPU inference and returns Choice, Score and Boolean answers as JSON. Edit the supplied [English](samples/requests/decision.en.json) or [Chinese](samples/requests/decision.zh.json) request to try your own input. `inspect` checks pinned assets; loading still validates the complete tensor configuration. C# hosts can reuse a session through `DecisionModelRuntime.Load(...)` and `Evaluate(...)`. See the guide for the API example and output semantics. Results remain `uncalibrated`, and the development CLI has not been formally released.

## Design

- C# model code and CPU/GPU kernels with explicit resource limits, cancellation and source-generated JSON. Runtime native calls are limited to operating-system/device drivers.
- NVIDIA path: compile C# kernels to PTX and an ABI manifest with ILGPU 1.5.3 at build time, then load and launch the static artifacts through C# CUDA Driver bindings in the Native AOT runtime. ILGPU's ordinary runtime JIT/launcher path is not considered Native AOT compatible. Native compute libraries such as cuBLAS/cuDNN are outside the design.
- A multilingual encoder and typed decision head; the first candidate follows the mmBERT-base architecture used by Laya's multilingual checkpoint.
- Full candidate distributions, ordinal expected scores and probability of truth. Distribution concentration and calibration status remain separate.
- Language quality, calibration, model correctness and latency each require their own evidence.
- Code, model weights, tokenizers and training datasets retain separate licenses and distribution checks.
- Callers control actions and permissions. The engine makes predictions and can abstain; it does not execute tools.

See the [roadmap](ROADMAP.md), [stage evidence](docs/stage-evidence.md), [S5 performance and AOT evidence](docs/s5-performance-aot.md), [closure audit](docs/closure-audit-2026-09-23.md), [research](docs/research.md), [architecture](docs/architecture.md) and [GPU/AOT design](docs/gpu-aot.md). Bare-metal Linux performance and stronger tail-latency estimates need separate measurements. Project documentation is primarily in Chinese.

Owned source is licensed under [Apache-2.0](LICENSE). See [third-party notices](THIRD_PARTY_NOTICES.md). Sezika does not claim to reproduce Jev's closed model or inherit another project's performance results.
