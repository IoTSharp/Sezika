# Sezika Roadmap

本文件是 Sezika 的阶段计划与验收入口。Sezika 的目标是 **Multilingual, non-autoregressive System 1 decision engine**：使用 C# / .NET 10 实现本地类型化决策模型推理，并通过 Native AOT 发布。

状态：`✅ 已完成`、`🚧 进行中`、`⏳ 计划中`。代码存在、构建通过、数值一致、真实模型推理、多语言质量与性能达标分别记录。

## 阶段

| 顺序 | 状态 | 范围 | 验收产物 |
| --- | --- | --- | --- |
| 0 | ✅ | 研究、仓库与契约草案 | 固定参考版本、架构决策与 C# 契约草案；未执行验证 |
| 1 | ✅ | 模型资产与 tokenizer | 可验证 manifest、安全张量格式、与固定 tokenizer 一致的 token IDs；发布目录、loopback 断点下载、安装清单和 lease 卸载已通过有界验收 |
| 2 | ✅ | 纯 C# encoder | embedding、attention、RoPE、norm、MLP 的标量正确性与逐层 oracle；SIMD/资源边界已完成 |
| 3 | 🚧 | 决策头与完整推理 | choice / score / boolean 的真实本地输出、预算、取消、session 生命周期 |
| 4 | 🚧 | 多语言数据、适配与校准 | 分语言数据集、可复现 head 训练、独立校准与测试报告 |
| 5 | 🚧 | AOT 与 CPU/GPU 性能 | SIMD、C# GPU kernels、量化、AOT 二进制、资源与延迟证据 |
| 6 | 🚧 | 开源发布 | NuGet、CLI、模型卡、许可清单、跨平台发布与示例 |

当前已具备固定 Apache-2.0 mmBERT/Laya 模型资产、C# tokenizer oracle、真实 CPU scalar encoder、真实 marker-head typed smoke、ILGPU 构建期 PTX/ABI 产物、CUDA Driver resident encoder/head 和多 RID tiny Native AOT smoke。逐语言质量与跨平台性能矩阵仍按各自证据门槛推进；详见 [阶段证据](docs/stage-evidence.md) 与 [闭环审计](docs/closure-audit-2026-09-23.md)。

## 编号任务板

任务使用 `S0`–`S6` 阶段编号和两位序号。状态只表示当前证据：`✅ 已完成`、`🚧 进行中`、`⏳ 计划中`、`⛔ 阻塞`。同一泳道内的任务可以并行；跨泳道按依赖推进。实现、真实推理、质量、AOT 和性能分别验收，不能用构建成功替代真实模型证据。

| 编号 | 泳道 | 状态 | 任务 | 依赖 | 验收产物 |
| --- | --- | --- | --- | --- | --- |
| S0-01 | A 契约 | ✅ 已完成 | 固定模型身份、请求/响应、错误码、概率/集中度/拒答语义 | — | `src/Sezika` 契约与架构文档 |
| S0-02 | A 契约 | ✅ 已完成 | 冻结纯 C#、CUDA Driver 例外、AOT 与不使用 native 数值库的边界 | S0-01 | [GPU/AOT 设计](docs/gpu-aot.md) |
| S1-01 | B 资产 | ✅ 已完成 | 锁定 mmBERT/Laya revision、许可证、model.json v1 与 tensor hash | S0-01 | `model-manifests/laya-mmbert` 与模型来源记录 |
| S1-02 | B 资产 | ✅ 已完成 | 实现 tokenizer JSON、Unicode/byte fallback 规则和中英/CJK/RTL oracle | S1-01 | `tests/fixtures/tokenizer-mmbert-oracle.json` 与 tokenizer smoke |
| S1-03 | B 资产 | ✅ 已完成 | 实现 SafeTensors F16/F32/BF16 读取及重叠、越界、dtype、hash 负向校验 | S1-01 | `ModelAssetVerifier`、`SafeTensorReader` 与负向测试 |
| S1-04 | B 资产 | ✅ 已完成 | 完成发布目录、断点/下载、安装清单和模型卸载生命周期 | S1-01,S1-03 | `ModelPackageStore`、loopback Range 断点/校验、安装清单、staging 修复和 lease 卸载测试；不下载真实模型 |
| S2-01 | C CPU | ✅ 已完成 | 完成 scalar embedding、attention、RoPE、norm、MLP、mask 与形状检查 | S1-01,S1-02 | 真实 encoder smoke 与配置校验 |
| S2-02 | C CPU | ✅ 已完成 | 固定小模型和真实模型的逐层/逐算子 oracle 夹具 | S2-01 | trace 文件、误差容差与生成版本 |
| S2-03 | C CPU | ✅ 已完成 | 建立 SIMD/scalar 对齐、取消、deadline、工作空间和并发矩阵 | S2-01,S2-02 | `EncoderWorkspacePool`、Vector<float> kernels、逐 trace 对齐与有界资源测试；S5-04 性能矩阵另行验收 |
| S3-01 | C 推理 | ✅ 已完成 | 实现 marker 序列、Choice/Score/Boolean、稳定 softmax 和 expected score | S2-01,S2-02 | 三种 primitive 的 typed CPU smoke |
| S3-02 | C 推理 | ✅ 已完成 | 实现输入结构校验、重复属性拒绝、候选/token/问题预算和结构化错误 | S0-01,S1-02 | `DecisionRequestParser` 与限制负向测试 |
| S3-03 | C 推理 | ✅ 已完成 | 与固定参考实现逐 primitive 对齐 logits、概率、legend 和 abstention | S3-01,S3-02 | `PrimitiveAlignment`、4 个中英文固定输入 fixture、逐键数值/legend/abstention 对齐测试；真实模型质量另行验收 |
| S3-04 | C 资源 | ✅ 已完成 | 完成 session busy、micro-batch、取消、deadline、unload 和内存上限矩阵 | S3-01,S3-02 | session gate、每问题 bounded micro-batch、workspace/resident budget、取消/deadline/unload 测试证据 |
| S3-05 | C 独立使用 | ✅ 已完成 | 提供独立模型 session 与 inspect/predict CLI，读取 JSON 并输出真实 typed decision | S3-01,S3-02,S3-04 | `DecisionModelRuntime`、CPU CLI、中英文三种问题的真实模型文件/stdin 推理、并发卸载回归和结构化错误；[运行证据](docs/standalone-cli-smoke.md)，正式发布归 S6-02 |
| S4-01 | D 质量 | ✅ 已完成 | 提供冻结 encoder head trainer、温度拟合与 accuracy/F1/NLL/Brier/ECE 评估器 | S3-01 | 可复现 trainer/metrics 输入输出 |
| S4-02 | D 质量 | ✅ 已完成 | 建立许可明确、按语言/领域隔离的中英测试与校准数据集 | S4-01 | 24 条原创中英 fixture、数据卡、SHA-256 manifest、split/entity/fingerprint 隔离与有界验证脚本；fixture 不代表质量分数 |
| S4-03 | D 质量 | ✅ 已完成 | 绑定模型/tokenizer/prompt/primitive/split 的校准 profile 并冻结门槛 | S4-02 | 12 个严格 hash 绑定 profile、冻结质量门槛和安全加载器；profile 保持 `pending_measurement`，真实质量另行验收 |
| S5-01 | E GPU/AOT | ✅ 已完成 | 生成带 hash/ABI 的 C# kernel PTX 与静态 Driver loader | S0-02 | 14 个 kernel manifest/PTX 和生成复现记录 |
| S5-02 | E GPU/AOT | ✅ 已完成 | 完成 resident CUDA encoder/head 与 CPU logits 对齐 | S2-01,S5-01 | RTX 4070 实卡 head 误差报告 |
| S5-03 | E GPU/AOT | ✅ 已完成 | 完成 tiny CPU/CUDA win-x64 与 CPU linux-x64 Native AOT smoke | S5-01 | 发布物、运行输出、依赖图 |
| S5-04 | E 性能 | ⏳ 计划中 | 建立 scalar/SIMD/量化 CPU 与 CUDA 冷/热、H2D、kernel、端到端基准 | S2-03,S5-02 | 固定硬件/线程/模型 hash 的 p50/p95 和峰值资源 |
| S5-05 | E 性能 | ⏳ 计划中 | 完成真实模型 CPU/CUDA AOT、多 RID、显存/内存回收和失败诊断 | S3-04,S5-02,S5-03 | win/linux 目标矩阵与真实模型 smoke |
| S6-01 | F 发布 | ✅ 已完成 | 生成带 README、许可证、NOTICE 和第三方声明的开发 NuGet 包 | S0-02 | `.artifacts/packages/Sezika.0.1.0-dev.nupkg` |
| S6-02 | F 发布 | ⏳ 计划中 | 完成正式版本、CLI、模型卡/数据卡、签名、NuGet 发布和示例 | S4-03,S5-05 | 发布清单、签名校验、跨平台包与文档 |

本轮已闭环并标记完成：S1-04、S2-03、S3-03、S3-04、S4-02、S4-03。S5-04 可在 S2-03 资源边界基础上继续并行推进；S5-05、S6-02 必须等待对应依赖。S4-03 的 profile 仍保持 `pending_measurement`，不把 fixture 或冻结门槛写成多语言质量已达标。

## 0. 研究与契约

- 固定 Laya 源码提交与 TypeSafe Jev 官方协议资料；区分公开模型实现、客户端 SDK 与商业 API。
- 新仓库默认使用 Apache-2.0 许可；上游代码、权重、tokenizer、数据分别记录归属。
- 制定有界 request/result、错误、模型身份、概率、分布集中度、校准与拒答语义。
- 定义自有协议版本；初始 C# 接口允许在 0.x 阶段经记录后调整，1.0 前冻结。
- 名称 Sezika 是自造品牌名，灵感来自“直觉式判断”；当前 GitHub/NuGet 检索不构成商标或全局唯一性结论。

## 0.1. ✅ GPU / AOT 最小可行性关口

用户要求 GPU 支持且保持模型、算子与调度源代码纯 C#，运行时只允许系统/显卡驱动例外。采用 **C# kernels → 构建期 ILGPU → PTX + ABI manifest → Native AOT C# CUDA Driver loader** 的设计。原版 ILGPU 常规运行时依赖 IL 读取与 Reflection.Emit，不能直接纳入 AOT 运行路径。

完整 GPU encoder 开发前，用 vector add 和小型 GEMM 验证构建产物、参数布局、实卡 launch、结果、资源回收及 AOT 依赖图。该最小关口已在 RTX 4070 Laptop GPU（driver 596.08、CC 8.9）通过，并有 win-x64 Native AOT smoke；完整 encoder/head 的逐算子数值差异和性能矩阵仍按 [GPU / AOT 设计](docs/gpu-aot.md) 的边界记录，不能由本关口推导为阶段 5 已完成。

## 1. 模型资产与 tokenizer

1. 首个候选是 Laya 多语言检查点所用的 mmBERT-base 架构；固定模型 revision、encoder 配置、tokenizer 及 head checkpoint，逐项完成许可审核后才下载或再分发。
2. 设计 `model.json` schema v1：architecture、dtype、tensor layout/shards、SHA-256、tokenizer pipeline、特殊 token、context/head budget、question schema、校准 profile、来源与许可。模型 schema 与 HTTP schema 独立版本化。
3. 直接支持参考加载路径中的 `model.safetensors` 与所需 dtype/布局。禁止推理进程反序列化任意 Python pickle；若后续新增来源只有 PyTorch checkpoint，由隔离的开发转换步骤导出并生成 hash，发布物仅接收转换后的安全格式。
4. 实现 tokenizer JSON 中实际用到的 tokenizer pipeline；拒绝未知步骤。保留 Unicode、emoji、组合字符、CJK、RTL、混合脚本和 byte fallback 的精确行为，禁止凭语言猜测替换分词规则。
5. 限制 metadata 大小、维度乘积、张量数、文件数与总内存；拒绝路径穿越、链接逃逸、重叠/越界 tensor、缺片、hash 不符和不支持的 dtype。
6. 初始预算草案：请求 JSON ≤1 MiB、深度 ≤32、每次 1–32 问、choice 2–32 项、score 2–10 级；实际 token 上限取已验证模型配置与用户预算中较小者。超过上限返回诊断，不静默截断选项。所有数值在阶段 3 的资源评测后冻结。

验收：固定的中英及混合脚本样本 token IDs 与参考实现逐个一致；恶意/损坏资产在分配大张量前被拒绝。初始不承诺 255 选项或 32k/64k 上下文。

## 2. 纯 C# encoder

1. 以标量 FP32 为正确性参考，再加入 SIMD。完整实现所选配置所需的 embeddings、RoPE、全局/局部双向 attention、mask、LayerNorm、激活和 MLP；不能把 decoder-only KV-cache 逻辑直接当 encoder 使用。
2. 固定 special tokens、位置编号、RoPE theta、attention scaling、local window、norm epsilon、激活近似、tensor 转置与 bias，按真实配置读取。
3. oracle 覆盖 tokenizer → embedding → 每层 → encoder 输出；先使用小型确定性权重，再使用真实 encoder。测试夹具记录来源、数值误差容差及生成工具版本。
4. 单请求工作空间、只读共享权重、有界并发；按 block 检查 CancellationToken 和 deadline。CPU 并行度受 session 与宿主总预算共同约束，避免问题批处理与矩阵乘法叠加线程。
5. GPU 原型通过后，用 C# kernels 实现 GPU encoder/head，复用相同张量契约；权重与中间数据留在显存，以 CPU oracle 对齐每个算子和整层。不能依赖 cuBLAS/cuDNN 等 native 计算库。

验收：每层输出与固定参考容差相符；取消、超时、异常与 unload 后无遗留请求或未释放工作空间。CPU 吞吐未测量前不承诺毫秒指标。

## 3. 决策头与推理闭环

1. 实现 question prompt、候选 marker 定位、type embedding、pre-norm Transformer head、marker scorer；固定 state 序列化、候选顺序与 question-to-batch 映射。
2. 实现稳定 softmax、版本化温度校准、choice argmax、score 期望与 boolean 的 P(true)。Score 的第 i 个等级取数值 i（0 起），结果为 Σ i×pᵢ，范围 [0,K−1]，legend/probabilities 键为不带前导零的十进制 i。概率必须有限、非负且归一化；NaN/Inf、候选数不合法、重复 ID、未知类型和无效温度返回诊断。JSON 入口在反序列化成 Dictionary 之前检查重复 question/criteria 属性，不能指望后续 DTO validator 发现被覆盖的键。
3. 第一版每问题重复编码 state，与参考检查点保持一致；支持有界 micro-batch。一请求可能包含多次 micro-batch forward，报告实际次数与 token 用量，不宣传任意问题数量固定成本。
4. 概率与分布集中度分开返回。未校准标为 `uncalibrated`；校准不匹配标为 `out_of_scope`。决策状态为 `answered` 或 `abstained`，运行失败走结构化错误，不能填充示例概率。
5. 已提供源码运行的 `inspect`/`predict` CLI 与 `DecisionModelRuntime`：指定固定模型目录和 JSON 请求，使用 CPU 返回真实决策，JSON 使用 source generation。`inspect` 执行资产预检查；正式可安装 CLI 与完整 `doctor` 仍归 S6-02，独立使用见 [使用说明](docs/standalone-usage.md)。
6. decision engine 不生成自然语言回答、不执行工具。需要解释或开放式文本时，由调用方自行转入生成模型。

验收：三个 primitive 都有真实模型的 C# / 参考输出对齐；预算与取消生效；缺模型、未知算子或架构不能隐式回退到关键词、远端 API 或 native runtime。

## 4. 多语言训练与校准

1. 首批发布质量语言是中文与英文；候选扩展日语、韩语、德语、西班牙语、阿拉伯语及印地语。必须逐语言验收，不能把底座词表覆盖计为语言质量通过。
2. 分开评估零样本、领域 head 微调和自有检查点。首个自有训练阶段使用 C# 冻结 encoder、训练小型 head/校准器；完整 encoder 反向传播与大规模预训练是另行排期的研究任务，不作为推理引擎首版前提。
3. 训练集、校准集、测试集按来源实体和时间隔离；翻译/改写同源样本不得跨 split 泄漏。标注许可与 teacher 生成数据使用条件进入数据卡。
4. 先建立监督交叉熵、Brier/ordinal proper-scoring 基线；RLCD/策略梯度仅在可复现实验证明收益后采用。不能把采用同名训练方法等同于复现 Jev。
5. 温度 profile 绑定模型 hash、tokenizer、prompt schema、primitive、候选规模、语言/领域和 split。拟合与运行时使用相同温度范围，避免保存参数被推理时悄悄钳制。
6. 每语言/primitive/领域报告 accuracy、macro-F1、NLL、Brier、ECE、score MAE、拒答覆盖率与 selective risk；少样本提供置信区间。分布尖锐不等于正确，OOD 和高置信错误单独统计。

初始质量门槛：在预先冻结的中英测试集上优于随机与多数类基线，并在规定数值容差内复现同权重参考实现；校准不使 NLL/Brier 明显恶化，自动路由阈值根据验证集的误路由成本与 coverage 冻结。具体质量目标由独立基线测量后确定，不复制上游宣传分数。

## 5. Native AOT、CPU/GPU 性能与资源

- 核心/运行时库开启 `IsAotCompatible`，应用关闭反射 JSON；静态类型注册、不动态加载程序集、不使用 JIT 表达式生成，不屏蔽 IL 警告。ILGPU 仅在单独的普通 .NET 构建工具中运行。
- 跨平台先覆盖 win-x64、linux-x64，随后 linux-arm64、osx-arm64；每个 RID 独立 publish 与运行 smoke。AOT 编译成功不等于模型推理成功。
- 启用 Vector/Intrinsics 前建立 scalar fallback；量化分别验证 encoder/head 的误差与质量变化。FP16/BF16 读取不代表对应 CPU 有原生加速。
- GPU 首先覆盖 NVIDIA CUDA Driver：由 C# 编译 PTX、静态 launch stubs、明确 ABI/hash/SM/驱动矩阵，分别优化 tiling、shared memory、fusion 和显存复用。CPU fallback 由显式策略控制且报告原因；不能将其他 GPU 厂商标为已支持。
- 322M 参数粗估 FP32 权重约 1.29 GB、FP16 约 644 MB，尚未计 tokenizer、head、activations 与工作空间；session 必须在加载前给出总内存估计，attention 不得无界生成大矩阵。
- 基准固定硬件、线程、精度、模型 hash、state 长度与候选规模。覆盖 1/8/32 问、冷启动/加载/热推理、p50/p95/p99、吞吐、分配、峰值 RSS/显存与取消延迟；GPU 分开计 driver 编译、H2D/D2H、kernel 和端到端时间。
- Laya T4 时间与 Jev 托管端到端时间仅为来源资料。项目性能结论必须来自 Sezika 自身测量。

## 6. 发布与后续研究

- 当前可构建带 README、许可证和第三方声明的开发包 `Sezika.0.1.0-dev.nupkg`；正式版本号、签名、NuGet 发布、CLI、模型卡/数据卡和真实模型跨平台发布仍未完成。
- 提供库、Native AOT CLI、模型卡、数据卡、校准报告和 C# 示例；代码/资产分别发布，不把权重打包进 NuGet 或可执行文件。
- 正式发布前核对名称、远端归属、包 ID 与签名/许可证信息。
- state 编码共享、更多问题/候选、长上下文、AMD/Intel/Apple GPU、全量 encoder 训练、蒸馏与新架构均在独立实验中评估；共享 state 会改变 cross-encoder 语义，不能作为无损缓存直接加入。

## 工作量判断

单个有 Transformer 实现经验的工程师，从现有许可明确权重出发，CPU 路线阶段 1–3 暂估 6–10 工程周，阶段 4–5 暂估 4–8 工程周，阶段 6 暂估 2–4 工程周。新增 GPU/AOT 原型暂估 1–2 工程周，完整 C# CUDA kernels、调优与证据暂增 6–12 工程周。该估算不等同于日历承诺；GPU 原型、tokenizer/权重审计和数值对齐后重新估算。训练新基础模型、追平成熟 native 计算库性能及其他 GPU backend 不在估算中。
