---
license: unknown
license_link: https://huggingface.co/{{REPOSITORY_ID}}/blob/main/LICENSE-COMPONENTS.md
language:
  - en
  - zh
base_model: jhu-clsp/mmBERT-base
tags:
  - sezika
  - experimental
  - non-autoregressive
  - csharp
  - custom-format
  - safetensors
---

# Sezika mmBERT 8192 实验模型 / Experimental model

本包是 Sezika 在 2026-10-09 完成的固定实验资产：冻结的 mmBERT encoder、tokenizer、独立线性决策 head、训练 checkpoint 和原始 8192-token encoder manifest。它用于研究多语言、非自回归的 Choice / Boolean 决策。模型输出不构成业务动作的执行授权。

This package contains the frozen mmBERT encoder, tokenizer, independent linear decision head, training checkpoint, and unchanged 8192-token encoder manifest from Sezika's fixed experiment on October 9, 2026. It is an experimental multilingual, non-autoregressive Choice / Boolean decision model. Predictions do not authorize application actions.

源码 / Source: [IoTSharp/Sezika](https://github.com/IoTSharp/Sezika). Intended model repository: `{{REPOSITORY_ID}}`.

## 文件与加载 / Files and loading

| 文件 / File | 用途 / Purpose |
| --- | --- |
| `encoder.safetensors` | 冻结 FP32 encoder，134 tensors / Frozen FP32 encoder |
| `tokenizer/tokenizer.json` | tokenizer 原字节 / Original tokenizer bytes |
| `expanded-head.asset` | 本轮训练的三行线性 head / Trained three-row linear head |
| `expanded-training.checkpoint` | Sezika 自定义 checkpoint / Custom Sezika checkpoint |
| `model.json` | 原始 encoder manifest，runtime limit 8192 / Unchanged encoder manifest |
| `experiment-summary.json` | 精选实验统计及来源 SHA / Selected experiment statistics and source hashes |
| `publication-manifest.json`, `SHA256SUMS` | 发布文件身份与完整性 / Package identities and integrity |
| `LICENSE-COMPONENTS.md`, `license-provenance.json`, `notices/` | 分组件许可、来源和通知 / Component notices and provenance |

使用 Sezika 仓库中的 C# / .NET 10 加载及 CUDA Driver 执行路径。GPU kernels 在构建期由 C# 导出为 PTX；本包不包含可自动安装的运行时。`expanded-head.asset` 和 checkpoint 是 Sezika 自定义格式，不能直接用 Transformers `AutoModel` 加载。冻结 encoder 的 safetensors 文件也不代表本包提供了 Transformers 兼容推理配置。

Use the C# / .NET 10 loading and CUDA Driver execution path in the Sezika source repository. C# GPU kernels are exported to PTX at build time; this package does not include a runtime installer. The head and checkpoint use custom Sezika formats and are not directly loadable by Transformers `AutoModel`. The encoder's safetensors container does not imply that this package supplies a Transformers-compatible inference configuration. Acceptance of the newly trained head on Native AOT and performance targets remains pending.

公开包的 `experiment-summary.json/head_loading` 保存了 head API 所需的完整 identity 与两个 SHA。使用者应依据这些字段构造 `MarkerFeatureIdentity expectedIdentity`，然后调用下面的实际 C# API，返回资产的 `.Head`。该步骤只加载 head；完整 encoder/tokenizer 加载、GPU kernel 构建和推理集成仍需 Sezika 源工程。下面示例已与源代码签名对照，没有作为一次新的推理执行验收。

The `head_loading` object in `experiment-summary.json` supplies the identity fields and both hashes required by the head API. Construct `MarkerFeatureIdentity expectedIdentity` from those fields, then call the actual C# API below and use the returned asset's `.Head`. This loads the head only; complete encoder/tokenizer loading, GPU kernel building and inference integration require the Sezika source project. The example was checked against the source signature and was not independently executed as a new inference test.

```csharp
// expectedIdentity comes from experiment-summary.json -> head_loading.identity.
// modelDirectory and cancellationToken are supplied by the application.
var loadedHead = Sezika.IndependentMarkerHeadAssetStore.Load(
    Path.Combine(modelDirectory, "expanded-head.asset"),
    expectedIdentity,
    expectedFeatureSha256: "{{HEAD_FEATURE_SHA256}}",
    expectedFileSha256: "{{HEAD_FILE_SHA256}}",
    cancellationToken: cancellationToken);
var head = loadedHead.Head;
```

当前实验 CLI 要求原始完整 training report；本公开包刻意不附该逐题报告，因此其汇总文件不能冒充 CLI 输入。请通过以上 API 在源工程中集成，不能把下载完成等同于通用 CLI 可直接运行。

The current experiment CLI requires the full original training report. This public package omits that row-level report, so its aggregate summary is not a replacement CLI input. Integrate through the API above in the source project; a completed download does not establish generic CLI compatibility.

## 固定实验 / Fixed experiment

一次训练：24576 committed updates、learning rate 0.001、seed 17、strict 8192、全零 head 初始化、encoder 冻结。数据共 215 题：train 162、development 53；Choice 140、Boolean 75。训练集合实际经过 151 个完整周期和 114 题前缀，每题访问 151 或 152 次。Score 新增监督为 0。

One run used 24,576 committed updates, learning rate 0.001, seed 17, a strict 8,192-token limit, an all-zero head, and a frozen encoder. The 215 records comprise 162 train and 53 development records: 140 Choice and 75 Boolean. Training made 151 complete cycles plus a 114-record prefix; each train record was visited 151 or 152 times. Score received no new supervision.

独立新进程从磁盘加载 head，重新 GPU 编码 215 原题和 16 个训练 Choice 换序题，共 231 forwards。结果如下；教师一致性不是正式准确率。

A fresh process loaded the head from disk and repeated GPU encoding for the 215 original records plus 16 rotated train Choice records, totaling 231 forwards. These agreement measurements are not formal accuracy.

| 观察范围 / Observation | 教师一致 / Teacher agreement | Mean teacher CE |
| --- | --- | --- |
| Train, original | 118 / 162 (72.84%) | 0.8185527481 |
| Development, original | 27 / 53 (50.94%) | 1.0354540944 |
| Train Choice, rotated | 7 / 16 (43.75%) | 1.3445008621 |

16 个换序题中 6 个改变了 selected candidate ID，候选顺序敏感性仍存在。原 32 题是已曝光的修正审计回归，Boolean 8/10、Choice 4/12、Score 0/10；不能将其视为封存测试，也不能将 Score 的均匀输出称为已经学会评分。

Candidate rotation changed the selected candidate ID in 6 of 16 records. The exposed original 32-record corrected-audit regression observed Boolean 8/10, Choice 4/12, and Score 0/10. It is not a sealed test; the unsupervised uniform Score output is not an accepted scoring capability.

本轮 RTX 5090 CLI 报告时间为训练 35.2160222 秒、独立验证 28.9658498 秒、原 32 回归 8.6122972 秒。这是一次实验观测，不是通用 benchmark。独立证据核验 10/10 通过，仅证明本轮工程与数值证据完整。正式人审质量、校准、封存测试、新 head Native AOT 和性能验收仍待完成。概率或分布集中度不等于实际正确率。

The RTX 5090 CLI reported 35.2160222 seconds for training, 28.9658498 seconds for independent validation, and 8.6122972 seconds for the original 32-record regression. These are observations from one run, not a portable benchmark. All 10 independent engineering-evidence checks passed. Formal human quality review, calibration, sealed testing, newly trained head Native AOT, and performance acceptance remain pending. Probabilities and distribution concentration are not empirical correctness rates.

## 来源与许可 / Provenance and licenses

Encoder 源自 `jhu-clsp/mmBERT-base`，固定 revision `{{SOURCE_REVISION}}`。上游模型卡声明 MIT；保留原 manifest 中的 MIT 字段，仅表示该上游 encoder 声明，不是整个包的统一许可。tokenizer、head/checkpoint、训练数据来源和教师输出分别记录许可与已知缺口，详见 `LICENSE-COMPONENTS.md`。HF 元数据的 `unknown` 表示整个包尚无已核实的统一许可，不授予未核实的第三方权利。

The encoder derives from `jhu-clsp/mmBERT-base` at revision `{{SOURCE_REVISION}}`. Its upstream model card declares MIT. The MIT field in the unchanged encoder manifest records that upstream declaration; it is not an omnibus license for this package. The tokenizer, head/checkpoint, data sources, and teacher outputs have separate provenance and unresolved notice or rights questions described in `LICENSE-COMPONENTS.md`. The Hub metadata value `unknown` means the complete package has no verified omnibus license and grants no unverified third-party rights.

原始 training report SHA256：`{{TRAINING_REPORT_SHA256}}`。本包仅发布精选汇总，不含原题文、教师原始响应、完整逐题训练/验证报告、连接资料或本机进程回执。完整原始证据保留在源工程的审计资料中。

Original training report SHA256: `{{TRAINING_REPORT_SHA256}}`. This package publishes selected aggregates, excluding question text, raw teacher responses, full row-level training/validation reports, connection details, and local process receipts. The original evidence is retained by the source project.
