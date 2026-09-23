# Sezika Roadmap

本文件是 Sezika 的阶段计划与验收入口。Sezika 的目标是 **Multilingual, non-autoregressive System 1 decision engine**：使用 C# / .NET 10 实现本地类型化决策模型推理，并通过 Native AOT 发布。

状态：`✅ 已完成`、`🚧 进行中`、`⏳ 计划中`。代码存在、构建通过、数值一致、真实模型推理、多语言质量与性能达标分别记录。

## 阶段

| 顺序 | 状态 | 范围 | 验收产物 |
| --- | --- | --- | --- |
| 0 | ✅ | 研究、仓库与契约草案 | 固定参考版本、架构决策、C# 契约草案、Tomur 对接计划；未执行验证 |
| 1 | 🚧 | 模型资产与 tokenizer | 可验证 manifest、安全张量格式、与固定 tokenizer 一致的 token IDs |
| 2 | 🚧 | 纯 C# encoder | embedding、attention、RoPE、norm、MLP 的标量正确性与逐层 oracle |
| 3 | 🚧 | 决策头与完整推理 | choice / score / boolean 的真实本地输出、预算、取消、session 生命周期 |
| 4 | 🚧 | 多语言数据、适配与校准 | 分语言数据集、可复现 head 训练、独立校准与测试报告 |
| 5 | 🚧 | AOT 与 CPU/GPU 性能 | SIMD、C# GPU kernels、量化、AOT 二进制、资源与延迟证据 |
| 6 | 🚧 | Tomur R22 对接 | 同进程 provider、模型资产、专用 API、只读工具、诊断 |
| 7 | 🚧 | 开源发布 | NuGet、CLI、模型卡、许可清单、跨平台发布与示例 |

当前已具备固定 Apache-2.0 mmBERT/Laya 模型资产、C# tokenizer oracle、真实 CPU scalar encoder、真实 marker-head typed smoke、ILGPU 构建期 PTX/ABI 产物、CUDA Driver resident encoder/head 和多 RID tiny Native AOT smoke。逐语言质量、跨平台性能矩阵和 Tomur 宿主接入仍按各自证据门槛推进；详见 [阶段证据](docs/stage-evidence.md) 与 [闭环审计](docs/closure-audit-2026-09-23.md)。

## 0. 研究与契约

- 固定 Laya 源码提交与 TypeSafe Jev 官方协议资料；区分公开模型实现、客户端 SDK 与商业 API。
- 新仓库独立于 Tomur，默认使用 Apache-2.0 许可；上游代码、权重、tokenizer、数据分别记录归属。
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
5. 接入真实权重后才添加 `sezika predict` CLI；`inspect`/`doctor` 报告 provider、资产、session、校准与验证状态。CLI 使用 source-generated JSON，与库共用实现。
6. decision engine 不生成自然语言回答、不执行工具。需要解释或开放式文本时，由调用方明确转入 Tomur 的生成模型。

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

## 6. Tomur R22

依赖阶段 3 真实推理闭环和阶段 5 的目标 RID AOT 证据。阶段 4 质量报告决定能否启用自动路由。

1. 静态 NuGet 引用 Sezika 固定版本，Tomur `providers/Decision` 实现薄适配；新增决策契约，不改变文本生成接口。
2. 模型通过 Tomur Catalog/pull/installed 生命周期管理，资产保留在 `<data>/models`；provider ID 使用能力名 `managed-decision`，不使用参考项目名。
3. 新建 `GET /api/decisions/status`、`POST /api/decisions`。可选 `POST /v1/systemone` 仅在协议对照测试完成后启用；路径相同不代表 Jev 模型或校准语义相同。
4. 集成 session/资源预算、取消、卸载、doctor、API JSON context 与实际请求身份校验；不能假设普通 API 已存在统一鉴权。
5. 增加 `decision.predict` 只读 Agent 工具，输出候选路由；执行仍通过 Tomur 原有 allowlist、参数验证与显式确认。
6. Chat-first UI 仅提供上下文诊断与 Settings 状态；不新增独立服务或管理后台。

验收：真实中英请求经 Tomur → Sezika → 模型得到 typed answers；模型缺失、超预算、鉴权、取消、并发卸载均返回稳定结果；现有聊天协议和工具确认流程保持通过。详细落点见 [Tomur 对接设计](docs/tomur-integration.md)。

## 7. 发布与后续研究

- 当前可构建带 README、许可证和第三方声明的开发包 `Sezika.0.1.0-dev.nupkg`；正式版本号、签名、NuGet 发布、CLI、模型卡/数据卡和真实模型跨平台发布仍未完成。
- 提供库、Native AOT CLI、模型卡、数据卡、校准报告、C# 示例和 Tomur 接入示例；代码/资产分别发布，不把权重打包进 NuGet 或可执行文件。
- 正式发布前核对名称、远端归属、包 ID 与签名/许可证信息。
- state 编码共享、更多问题/候选、长上下文、AMD/Intel/Apple GPU、全量 encoder 训练、蒸馏与新架构均在独立实验中评估；共享 state 会改变 cross-encoder 语义，不能作为无损缓存直接加入。

## 工作量判断

单个有 Transformer 实现经验的工程师，从现有许可明确权重出发，CPU 路线阶段 1–3 暂估 6–10 工程周，阶段 4–5 暂估 4–8 工程周，阶段 6–7 暂估 2–4 工程周。新增 GPU/AOT 原型暂估 1–2 工程周，完整 C# CUDA kernels、调优与证据暂增 6–12 工程周。该估算不等同于日历承诺；GPU 原型、tokenizer/权重审计和数值对齐后重新估算。训练新基础模型、追平成熟 native 计算库性能及其他 GPU backend 不在估算中。
