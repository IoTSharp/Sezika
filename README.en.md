# Sezika

**A local, multilingual, non-autoregressive System 1 decision engine, built with C#.**

Sezika turns text or JSON state into typed semantic decisions: **Choice**, **Score** and **Boolean**, with probability distributions. Business actions remain the caller's responsibility.

`C# / .NET 10` · `CPU / CUDA` · `Native AOT target` · `Apache-2.0`

> ⏸️ **Development and training are paused pending compute and funding.** Updated October 13, 2026. The GPU instance is shut down; full-corpus training, formal quality acceptance and probability calibration are unfinished.

## 🚦 Progress

| Status | Scope |
| --- | --- |
| ✅ Engine | Model loading, tokenizer, C# CPU/CUDA inference, typed decisions and development CLI |
| ✅ Engineering evidence | Numerical comparisons and Windows / Linux WSL2 Native AOT checks for specific models; these do not validate all platforms or newly trained weights |
| 🧪 Training | Real C# encoder/head backpropagation, FP32 AdamW updates and checkpoint restoration on RTX 5090 |
| ⏸️ Full corpus | Target: **14,592,720 updates**. Last inspected complete checkpoint: **45,056 updates, about 0.31%**; storage must be checked again after restart |
| ⏳ Acceptance | Multilingual quality, calibrated probabilities, sealed evaluation and deployment performance of the new weights |

The inference core uses C# operators, build-time PTX and static CUDA Driver bindings, without PyTorch, ONNX Runtime, cuBLAS/cuDNN or remote inference services. Model weights are managed separately from code and NuGet packages. The existing [experimental model](https://moark.com/IoTSharp/Sezika-Decision-Base) is not the completed result of this full-corpus run.

## 💰 Remaining training budget

At the observed rate of roughly **4 updates/second**, a single RTX 5090 would need about **960–1,014 more GPU hours**, or **40–43 days of continuous operation**. At the October 13, 2026 catalog price of **CNY 4/hour**, the remaining GPU cost is approximately **CNY 3,800–4,100**. Available compute funds are currently **CNY 0**.

This extrapolation excludes final independent validation, storage and other costs. Later sequence lengths, optimization and billing changes can affect both time and cost. It is not a delivery date or quality guarantee. See the [calculation and observed scope](docs/training-budget-2026-10-13.md).

## 🚀 Get started

Use the .NET 10 SDK and prepare verified model assets through the [standalone guide](docs/standalone-usage.md). Development entry points include `inspect` and `predict`; the [independent-model CLI](docs/independent-cli-s4-15.md) uses an explicit model format. Current probability outputs are uncalibrated.

- [中文首页](README.md) · [Roadmap](ROADMAP.md) · [Changelog](CHANGELOG.md)
- [Architecture](docs/architecture.md) · [GPU / AOT](docs/gpu-aot.md) · [Evidence](docs/stage-evidence.md)

Project code is licensed under [Apache 2.0](LICENSE). Model, tokenizer and dataset licenses are managed separately; see [third-party notices](THIRD_PARTY_NOTICES.md).

## ❤️ We need sponsors

Help fund the remaining training and independent validation, contribute GPU time, or work with us on C# / CUDA optimization and evaluation.

| WeChat sponsorship | IoTSharp WeCom community |
| :---: | :---: |
| <a href="docs/assets/wechat-sponsor.jpg"><img src="docs/assets/wechat-sponsor.jpg" alt="Maintainer-provided WeChat payment QR code for voluntary sponsorship" width="230" /></a> | <a href="docs/assets/iotsharp-wecom.jpg"><img src="docs/assets/iotsharp-wecom.jpg" alt="IoTSharp WeCom community QR code" width="230" /></a> |

Click either image for its full resolution. The community image comes from the [IoTSharp project](https://github.com/IoTSharp/IoTSharp). If the invitation stops working, check that project's current community information. [Image sources](docs/assets/SOURCES.md)

**🙏 Sezika needs sponsorship to continue real training and validation. Contributions of funding, compute and engineering time are welcome.**
