# S3-06 / S4-04 / S5-06 续实现与有界验证

本记录保留 2026-09-26 本轮扩展诊断、全量独立参考、tokenizer 边界修复和性能观测的代码与执行证据。数值一致、模型实际执行、语言正确率、校准、AOT 与性能分别验收。用户已明确授权构建、自检及有界真实模型验证。

## 环境与身份

Windows 11 build 26200，PowerShell 7.6.6，.NET SDK 10.0.400 / runtime 10.0.11，i9-13900HX / RTX 4070 Laptop。本轮为普通托管 .NET 运行。独立 Python 仅用于既有离线 Laya oracle；生产推理仍为 C# CPU/CUDA Driver。没有训练、校准拟合、下载新权重或发布。

| 对象 | 固定身份 |
| --- | --- |
| 模型 | `convaiinnovations/laya-multilingual@052592a15d198d9ad47da779604259b10b47b7aa` |
| Laya 源码 | `4066d5d5fbf08b66c6757ddeedbd797bd7655bc0` |
| 权重 SHA-256 | `9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204` |
| tokenizer SHA-256 | `609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f` |
| 输出合同 SHA-256 | `771ca781c52f1d6f9cb22859bed007ef483a54a2543c644de33f88d6e0687eee` |
| 原核心参考 SHA-256 | `bf0d537305149672f07d34dc6b3e2d3614aeb2c26aa6d11aa7902578100c53fa` |
| PAWS 250 SHA-256 | `c295258fdd73452f196b2673bdbee58cf01fa3f24400c2f1c0f26fd1126bc6f3` |
| Nimble 324 SHA-256 | `8e9e48b8de5206593912ae01ddc95bd77e40ad2ecf4c9292c1711290eca0d896` |
| 原创语言 fixture 12 SHA-256 | `0aab426dbf4d69653527d402158693e10214c52a83a586728cd6247ea1e023f7` |

原始执行物位于本地忽略目录 `.artifacts/s346-20260926`。归档只含派生指标、数值比较、索引及过程记录；外部 PAWS/Nimble 原题文、manifest 与 raw captures 留在忽略目录。程序/程序集哈希随报告保存，不以 dotnet host 的哈希替代应用身份。

## S3：核心覆盖与分层诊断

`--trace-oracle` 支持显式 1–18 条选择，执行前核对全部输入；取消时保留部分结果、未完成 ID 和完整分母。新 `--trace-summary` 最多聚合 64 个 hash 绑定报告，要求程序、参考、合同及重复输入一致；后续成功不能抹去已记录的数值失败。

| 执行 | 核心案例 | 后端输出 | 状态 |
| --- | ---: | ---: | --- |
| 首次完整选择 `trace-core18.json` | 13 完成 / 18 计划 | 39 通过 / 54 计划 | 1740 秒内部期限触发，保留 incomplete |
| 固定剩余五条 `trace-remaining5.json` | 5 / 5 | 15 / 15 | 同参考、同合同、同程序完成 |
| 离线汇总 `trace-complete-summary.json` | 18 / 18 | 54 / 54 | 核心覆盖通过，仍列出 1 次未完成来源执行 |

覆盖 Choice/Score/Boolean × 中英 × 短中长。最大 marker-logit 绝对误差 `8.749961853027344e-5`，均满足原冻结输出合同。SIMD/CUDA 各与本次 scalar 比较 27 个 checkpoint，共 **972 次完整张量诊断**，无缺失。最大内部层差异为 `0.53082275390625`，出现在 `choice-zh-medium` CUDA `layer/18/hidden`。这些是内部 scalar 对照诊断，没有独立上游逐层张量或事先冻结的逐层阈值，不能表述为逐层数值门槛通过。

核心 54 项捕获使用本轮最初构建，发生在后述 tokenizer 修复之前；两轮报告身份一致，故可合并。修复后的输入契约回归仍逐项验证原核心参考的 token/marker/候选不变。不同构建的报告不混成同一三后端矩阵。

真实近并列按预先冻结的 `quality-near-tie-plan.v1.json` 观察：PAWS 在前、Nimble 在后，完整 574 条分母，top-two raw-logit margin ≤ 0.001；不用正确性标签选择输入，不改阈值。观察器先完成一条正例，并以错误来源核对精确拒绝消息及无输出副作用。

最终观察 **574/574**，失败0、未处理0、近并列0；最小 margin 为 `0.0029172897338867188`。没有命中，因此没有根据质量标签另挑边界输入，真实近并列验收仍为 `not_measured`。

## S4：完整参考工具与输入修复

`--prepare-oracle-batches` 按原顺序生成每批最多 46 条、最多 256 批的 hash 绑定清单，含尾批；`--score-captures` 拒绝跨批重复 ID、错误 hash、不同实现/后端/长度策略和不完整批次。汇总重新计算逐行指标，保留 `Unprocessed` 与 `Answered/DatasetTotal`，不平均各批准确率。数值差异照常进入评分分母并保留比较报告。

全量 Nimble 扩展在第二批揭示旧 tokenizer 的真实缺陷：其 added-token 切分缺失，双换行后的 `Before` / `Policy` 等文本缺少上游 metaspace 前缀；4 条案例的 token 与数值合同失败。已保留原失败报告。修复按 tokenizer 资产声明在 normalization 前执行最长 added-token 匹配，支持固定资产的 lstrip，并对各普通片段执行 metaspace prepend；不改模型、输入题文或数值容差。

初轮 Nimble 在总预算内完成 6 批、276 条，其中共有 8 条 token/数值失败（第2、5批）；第7批独立参考遇到剩余126秒的子进程时限，外层约1720.1秒退出。该次 `run.json` 保留8批计划与6批完成状态，进程树已回收；最后48条按原清单从offset 6在新运行中继续，不把中断中的输出充当完整参考，也不改变原失败记录。

修复前先冻结独立 `tokenizers 0.22.2` 的 267 条原创微型参考，覆盖全部 249 个 added tokens 及多换行、制表符、空格、Unicode、特殊 token 边界，分别核对有/无 BOS/EOS。最终核心检查 **310 项通过**，输入契约 **137 项通过**。首次新增构造逻辑暴露最小测试 tokenizer 没有 added_tokens 的兼容问题，修复为缺省空集合后重跑通过；两次初始失败过程记录保留。

修复后针对首个已失败案例 `scale-diverse-174-003-base` 另执行三后端真实回归（PID 56536，外层 510 秒、内部 480 秒，实际约 127.1 秒）：scalar/SIMD/CUDA 均通过原独立参考合同，SIMD/CUDA 的 54 个完整张量 checkpoint 无缺失。这是修复后构建的独立报告，不并入修复前的 54 格汇总。该执行与独立参考任务重叠且启用了 trace，耗时不作为性能数据。

修复后通过 `ReferenceIndexPath` 复用固定参考并重新执行 C# CUDA capture，逐批再次比较。参考复用有 hash 前后检查；参考输出不进入 C# 推理输入反序列化路径。

PAWS 修复后 6 批 **250/250 数值比较通过**，独立 Laya 与 C# CUDA 都答对 **170/250（68%）**。本轮长度策略均为 `laya_compatible`；历史 strict 结果仍单独保留。C# NLL `0.7512708487215182`、Brier `0.48070631446590595`。PAWS 没有显式语言标签，仍记 `unspecified`。

Nimble 独立参考由初轮前6批与续跑后2批组成，固定来源和环境身份经跨capture校验一致。随后使用修复后同一构建重新捕获全部8批，**324/324比较通过、0数值问题**。连同PAWS，外部审计集独立数值覆盖由历史46条扩展为 **574/574**；原Nimble初轮的8条失败仍单独保留。最终C#及独立Laya均答对 **137/324（42.28395%）**，没有正确率提升声明。

| Nimble 指标 | 独立 Laya CPU FP32 | 修复后 C# CUDA |
| --- | ---: | ---: |
| NLL | 2.4536780501305846 | 2.4536790524516676 |
| Brier（分类向量平方误差和） | 0.8317675934408456 | 0.8317675617152847 |
| Score MAE | 0.8079800288251135 | 0.8079803596040981 |
| Choice 正确 | 60/146 | 60/146 |
| Boolean 正确 | 56/114 | 56/114 |
| Score argmax 正确 | 21/64 | 21/64 |
| Boolean 负类召回 | 1/57 | 1/57 |

Boolean正类召回55/57、balanced accuracy约49.12%、AUROC约0.48138。完整参考证明实现对齐，也说明负类偏置在相同权重的上游基线中存在；它不能替代后续独立开发/校准数据、语言质量或训练收益验收。Nimble仍无显式语言元数据。

原创 12 条 fixture 修复后 **12/12 数值比较通过**，两实现均 8/12 正确，中英各 4/6。按语言×题型切片如下；Score 正确数按 argmax 标签，MAE 按零起点期望值：

| 语言 | Choice | Boolean | Score | Score MAE（C#） |
| --- | ---: | ---: | ---: | ---: |
| en | 1/2 | 1/2 | 2/2 | 0.168091 |
| zh | 2/2 | 1/2 | 1/2 | 0.432916 |

该 fixture 来自既有 S4-02 test，未使用 calibration。每种语言只有 6 条、Boolean 全是负类，因此不能推出正类召回、balanced accuracy 或 AUROC，也不能充当代表性语言质量验收。所有概率仍为 uncalibrated。

## S5：计数与失败路径

画像 schema v3 区分 entered/completed forward；只有返回有限且形状正确的 logits 才算完成。失败路径独立保存 CPU workspace、CUDA owned allocations/modules、释放失败和弱引用回收结果，不覆盖原推理错误。`--request-deadline-seconds` 允许显式 1–1800 秒，默认 300 秒；核心预算默认仍为 30 秒。

`--profile-detail end_to_end` 可以将长 CPU 请求限定为 discovery 与正式 E2E 样本，独立 instrumentation/CPU split 标记未请求；数值 smoke、取消/恢复与回收检查仍执行。扩大预算本身不构成加速；跳过分项不能写成完整阶段画像。

本轮先确认151条已完成进程结果无清理错误、已记录的任务进程均退出，再串行运行性能工具。没有并发的本任务模型/参考负载；未控制其他系统进程或清空操作系统/驱动缓存。以下均为1个正式样本、0次显式warmup、1个加载周期，仍包含cold请求和discovery；不据单样本计算稳定尾延迟或跨轮加速比。

| 后端/案例 | 范围 | 正式 E2E | 真实 token 总数 | 生命周期 |
| --- | --- | ---: | ---: | --- |
| SIMD short-1 | full | 1580.2861 ms | 36 | 数值smoke、取消/恢复、workspace和对象回收通过 |
| CUDA short-1 | full | 83.67 ms | 36 | 数值smoke、取消/恢复、CUDA释放和对象回收通过 |
| SIMD long-1 | full | 42183.4474 ms | 516 | 数值smoke、取消/恢复、workspace和对象回收通过 |
| CUDA long-32 | end_to_end | 38318.6567 ms | 16566 | 32次discovery全部完成，数值smoke、取消/恢复、释放和对象回收通过 |

CUDA long-32的 `cases_with_stage_timings=0`，encoder/head及CUDA event分项状态明确为 `not_requested_end_to_end_detail`。该单样本不替代历史九格各三样本的报告，也不构成与历史40.464秒p50相比的加速结论。

SIMD long-1的独立encoder/head合并计时为41727.2406ms，独立split的encoder为32683.8149ms、head为7396.4286ms；它们属于不同执行，不能相加替代E2E。当前单题正式耗时外推32题×discovery/正式两遍约2699.74秒，超过工具1800秒总上限，故本轮没有启动CPU long-32或生成该格正式样本。`profile-long32-preflight.json`明确标为运行时估算，不是实测；[历史300秒失败](s5-profile-2026-09-26.md)继续保留。当前数据不能证明CPU完成九格矩阵，也没有性能优化收益声明。

CUDA先以成功short-1建立正例（cold三题请求约251.5ms），再将long-32逐请求期限设为3秒，精确得到 `decision_deadline_exceeded` / `Decision evaluation exceeded its deadline.`，发生在discovery，进程exit 2。记录**3次进入、2次完成、0正式样本**；CPU workspace为0，CUDA owned bytes/allocations/modules/release failures均归零，模型弱引用哨兵已回收。该失败是受控负例，不计成功覆盖率，也不意味着未执行的末尾数值/恢复诊断通过。

## 执行与剩余门槛

所有构建、模型、自检与工作流通过 `Invoke-BoundedProcess.ps1` 记录 PID、启动时间、argv、父链和清理结果。runner 修复了快速退出进程与 CIM 身份查询竞争；最初 PAWS 计划进程的失败记录保留，修复后短进程正例和重新生成的计划分别验证。数值/质量任务有重叠，这些耗时不作性能基准；性能采样必须在模型验证任务全部退出后单独执行。

S3-06 的独立层阈值与真实近并列、S4-04 的代表性语言质量与校准、S5-06 的完整分项覆盖/稳定尾延迟及优化收益仍独立验收。新增 tokenizer 修复没有新的两 RID Native AOT 运行证据，既有 AOT 报告仅对应其记录的实现哈希。

最终核对157条已完成runner结果：所有记录的清理错误均为0，PID与启动时间匹配的任务进程无残留。失败、超时、部分汇总和负向检查逐条保留，不能把runner非零退出的总数当作剩余软件失败数。新增工具的自检分别为Evaluation 17项、Trace 22项及Benchmarks分位数/计数/预算/范围检查通过；最终solution Release构建0警告、0错误，核心310项及输入契约137项通过。构建、自检、真实推理和离线评分各有独立命令与过程记录。

归档入口为 [文件哈希索引](s346-continuation-2026-09-26/index.json)、[执行与完整比较汇总](s346-continuation-2026-09-26/validation-summary.json)、[实现源码身份](s346-continuation-2026-09-26/implementation.json)。原始复制项逐字节核对SHA-256；质量汇总移除外部原始Rows及目标/领域标签，索引同时记录原始与派生文件hash。可复核 [核心54格](s346-continuation-2026-09-26/trace-complete-summary.json)、[574条边界观察](s346-continuation-2026-09-26/near-tie-full.json)、[Nimble最终指标](s346-continuation-2026-09-26/nimble-final-actuals-quality.json)、[截止负例](s346-continuation-2026-09-26/profile-negative-check.json)和[CPU预算估算](s346-continuation-2026-09-26/profile-long32-preflight.json)。
