# Sezika

**Multilingual, non-autoregressive System 1 decision engine.**

Sezika targets local, typed semantic decisions using **C#, .NET 10 and Native AOT**. Its inference path uses a managed bidirectional Transformer encoder and decision head to turn text or JSON state and questions into Choice, Score and Boolean answers.

## Status

This repository includes bounded loading for the pinned Laya/mmBERT model, tokenizer oracle fixtures, C# scalar/SIMD FP32 and W8A32 encoder/head paths, real Choice/Score/Boolean decisions, calibration metrics, build-time ILGPU PTX/ABI artifacts, and resident CUDA inference. S5-04/S5-05 are complete within the measured scope: Windows `win-x64` Native AOT benchmarks cover all four backends at 1/8/32 questions with five samples per configuration; Ubuntu WSL2 `linux-x64` real-model Native AOT smoke covers all four backends at three questions with one sample each. **WSL smoke does not establish bare-metal Linux performance. Multilingual quality remains unaccepted, and model weights are not bundled for release.** See the [S5 performance and AOT evidence](docs/s5-performance-aot.md) and [raw reports](docs/evidence/s5-2026-09-24/).

The current W8A32 path is slower than SIMD. It retains the original FP32 weights and adds quantized caches, so it does not reduce total resident memory. Numerical alignment, actual inference, AOT compatibility, language quality and performance benefits remain separate claims.

The [2026-09-26 continuation](docs/evidence/s346-continuation-2026-09-26.md) extends independent Laya-versus-C# CUDA comparisons to **all 574 PAWS/Nimble records**. Full reference coverage exposed an added-token boundary bug in the C# tokenizer; after fixing it, all records were recaptured with one implementation and passed the original numerical contract. Both implementations answered 250/250 PAWS records with 170 correct (68.00%) and 324/324 Nimble records with 137 correct (42.28%), using `laya_compatible`. A separate 12-record original English/Chinese fixture audit also passed all numerical comparisons, with 8/12 correct. These small public fixtures do not establish language quality. Nimble Boolean negative recall remains 1/57, probabilities are uncalibrated, and the external datasets have no explicit language metadata.

A [prior-build Nimble strict run](docs/evidence/quality-aligned-2026-09-26.md) answered 306/324 with 133 correct: 43.46% of answered records and 41.05% of all processed records. The other 18 records were rejected before forward because they required truncation. Its implementation identity remains separate from the latest tokenizer fix.

Core three-backend diagnostics now cover all 18 primitive/language/length cells, with 54 output checks and 972 complete internal tensor comparisons; the initial timeout and continuation remain separately recorded. The tokenizer fix passes 267 independent boundary fixtures and a real scalar/SIMD/CUDA regression for an affected record. No real near tie was found among the 574 references at the frozen 0.001 margin threshold. Independent layer thresholds and fresh Native AOT validation of this tokenizer implementation remain open.

The latest managed .NET 10.0.11 observations on an i9-13900HX / RTX 4070 Laptop record a **38.319-second** CUDA long-32 sample in `end_to_end` mode, with separate stage timings explicitly unmeasured, and a **42.183-second** SIMD long-1 sample. Extrapolating discovery plus one formal CPU long-32 sample exceeds the tool's 30-minute total limit, so that configuration was not started in this continuation. A controlled CUDA deadline failure records three entered forwards, two completed forwards, no formal sample, and successful resource cleanup. [Earlier nine-cell profiles and the CPU 300-second failure](docs/evidence/s5-profile-2026-09-26.md) remain separate evidence. These small samples do not establish stable tail latency, new Native AOT performance or an optimization benefit.

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
