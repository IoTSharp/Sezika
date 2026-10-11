# Sezika：SDK、工具、应用与外部资源学习建议

日期：2026-10-08。此报告负责分配的 **447 条 GitHub 仓库目录身份**，并对 **231 条非仓库链接**完成按目录说明初筛。逐仓库建议见 `integration-decisions.json`，每个分配身份恰好一条。仓库别名由总报告统一处理，本文件保留分配身份以免遗漏。

结论：最值得吸收的是 **类型化合同、显式不确定性、输入预算、版本与校准身份、数值证据、影子运行和实际结果闭环**。值得实验的是候选压缩、缓存、场景小头、检索重排与上下文筛选。浏览器、交易、GUI、机器人、代理框架主要提供上层案例；它们的执行器、账户、远端模型 fallback 不迁入 Sezika 核心。Python、MLX、ONNX Runtime、C++/ggml、cuBLAS 等实现依赖不符合核心 C#/.NET 10/Native AOT 约束。

## 覆盖与证据边界

- 五批阅读全部 447 项的目录说明，按具体场景筛选，非重点项目用同类方法初筛；生成脚本的逐项 `learn` 和 `rationale` 明确保留目录场景。**这是 catalog triage，不是 447 项源码审计或运行验收。**
- 重点阅读 20 个仓库的 README 快照：browser-use/jev-ultrafast、vercel/eve、deyna256/langchain-skill-router、chengyongru/fastjev、mizorewww/laya-mlx、zzhdbw/laya-Ascend、wiatrM/jevtpp、doeixd/discern、edgardcham/huncho、Mawfyy/jevflow、bladedevoff/stuntd、GhalebDweikat/winnow、compozy/yoshi、tamaratran/fast-jev-compaction、iamjonatha/decisionkit-dotnet、saibimajdi/typesafeai-dotnet-sdk、YYTbit/metajev、ariel-frischer/jevkit、abe75ch/tiltmeter、cisco-ai-defense/skill-scanner。较长 README 的公共采集快照为前部片段，故不能声称全文审完；各项证据限定于实际读到的合同和说明。
- 进一步阅读固定 commit 的 eve 两个源码文件、eve 决策研究文档、Cisco System One analyzer 设计文档，以及 Tenuo scoped-authority 文章。没有构建、运行外部项目、调用其推理服务、训练或下载权重。
- 不可访问的 `zzz1YAO/DataJev` 与 `jiawei686/jev-screen-mcp` 标为不可核实。大型仓库被清单收录不表示此次已核实其 Jev 集成；整个框架的 star 数不等于可选决策组件的成熟度。
- 分配项优先级：P0 10，P1 29，P2 341，P3 67。P0 指方法对当前 Sezika 验收重要，不表示采纳第三方代码或此刻应扩张全部功能。

## 最应该吸收的 20 项

| 项目 | 具体可学之处 | Sezika 采纳边界 |
| --- | --- | --- |
| [DecisionKit](https://github.com/iamjonatha/decisionkit-dotnet) | 问题决定答案的泛型类型；Core/DI/Testing 分层；取消、超时、输入错、provider 错可区别 | 用于本地 C# API 与测试合同，Jev HTTP provider 仅外部适配参考 |
| [TypeSafe .NET SDK](https://github.com/saibimajdi/typesafeai-dotnet-sdk) | `JsonTypeInfo<T>`、UnknownAnswer、完整原始分布、原始字段保留、NuGet 文档 | 只采用 source-generated 序列化路径；避免其 reflection overload |
| [Discern](https://github.com/doeixd/discern) | Match/Miss/Uncertain 三值与阈值区间检查；未处理的不确定性显式失败 | `p=0.62` 在 `[0.5,0.8)` 中不是 false，更不是安全；核心提供拒答，业务 policy 留宿主 |
| [JevT++](https://github.com/wiatrM/jevtpp) | 静态 enum 词表、abstention、schema ID、低基数诊断；区分集中度和准确率 | 以 C# 类型/source generator 重表达；不引入 C++、ONNX、ggml |
| [FastJev](https://github.com/chengyongru/fastjev) | 多 state/多 question 合同；raw rows/timings/checksums；calibrated=false 默认；校准身份绑定 | 采纳证据、输入预算和身份规范，不采纳 Python 多后端运行时 |
| [Laya-MLX](https://github.com/mizorewww/laya-mlx) | tokenizer/logit/probability/argmax parity；重复确定性、memory-growth；同步计时边界；state truncation telemetry | 是 C# kernel 移植的验收范本，不是 Sezika 的 MLX 依赖 |
| [Laya-Ascend](https://github.com/zzhdbw/laya-Ascend) | 新硬件的算子替换、同权重对照、CPU/NPU计时 | 首页主体沿用上游；NPU数字仅目录与他人声明。当前暂缓新硬件，先完成 CUDA/AOT |
| [metajev](https://github.com/YYTbit/metajev) | 分布是观察，阈值是 policy；存全分布以 replay/sweep；outcome 和 policy fingerprint | 补足权重、tokenizer、precision、format、校准身份后才可精确缓存 |
| [huncho](https://github.com/edgardcham/huncho) | decision-site ID、JSONL journal、enter/exit 迟滞、跨语言共同 fixtures | 迟滞按业务 key 保持状态，属于宿主策略；先实验其价值 |
| [JevFlow](https://github.com/Mawfyy/jevflow) | 原子问题与 deterministic policy 分离；matched-rule 记录 actual 值；只返回建议集 | 单独应用示例；不内置 workflow executor。五级 Score 示例用 `>=8`，提醒必须验量表单位 |
| [JevKit](https://github.com/ariel-frischer/jevkit) | 离线 lint、dry-run、stdin JSON、机器可读错误；一选项强制答案没有信息量 | 增强 Sezika CLI 的输入预检、退出码和解释性，不抄远端请求路径 |
| [winnow](https://github.com/GhalebDweikat/winnow) | 先 shadow；错误/不确定/未判块保留；可恢复原文；人工盲标 regret + AUC + ECE | 采纳验收方法；筛选类上层功能先实验，不能直接宣传替代 compaction |
| [Yoshi](https://github.com/compozy/yoshi) | source hash/offset、protected context、累计输入/cache writes/墙钟/质量同时报告 | 当前 v12 两次试验均更慢、有失败；把这个负结果写进优化判据 |
| [fast-jev-compaction](https://github.com/tamaratran/fast-jev-compaction) | call/result 成对、批请求预算、用户约束保留、明确失败 | 其 token 为估算，state 每批重发；Sezika用实际 tokenizer预算，保留副作用和授权上下文 |
| [stuntd](https://github.com/bladedevoff/stuntd) | per-site 小头、holdout coverage/interval、novelty gate、抽样复查、漂移降级 | 学场景适配闭环；teacher agreement 不是实际 accuracy；本地核心不加入远端 fallback |
| [Tiltmeter](https://github.com/abe75ch/tiltmeter) | 版本切换、分布漂移、阈值附近拥挤、预警去重 | 无标签 estimated accuracy 依赖校准持续成立，不能报告成真实正确率 |
| [browser-use/jev-ultrafast](https://github.com/browser-use/jev-ultrafast) | indexed action space；同 snapshot 批 operation/compatible target；freshness重验；DONE独立核验 | 为 SDK提供多问题与 observation ID 范例；浏览器executor留上层 |
| [langchain-skill-router](https://github.com/deyna256/langchain-skill-router) | limits/timeout 明确；分片 rank + need gate + verify；只加载 top skills；总输入与 cache miss 分别报 | 候选分层方案需数值/概率语义验收；Python middleware 外置 |
| [eve](https://github.com/vercel/eve) | 独立 `decide`、取消信号；durable choice 只存 key；provider 合同与confidence不强行统一 | 借外部框架适配形式；不是核心远端调用。原语改名属于实验API的版本风险 |
| [Cisco skill-scanner](https://github.com/cisco-ai-defense/skill-scanner) | 先验证label separation，失败明确、不truncate假成功；默认 advisory-only | 最重要反例：类型合法且有概率，仍可能比随机差；模型绝不决定授权 |

## 关键原理：缓存、省算与批处理不等于同一件事

1. **精确决策缓存。** 相同规范化 state、question、option顺序/文本、weight hash、tokenizer、format、backend/precision、校准文件产生同一可重复推理时，才能复用记录。policy threshold 不影响推理，可从 key 中排除。时间、租户权限、外部状态如果影响判断，必须显式进 state/version 或设置独立失效规则。`latest` 别名不足以证明模型相同。
2. **tokenization cache。** 缓存静态问题模板的 token IDs 相对安全，但要限定容量并绑定 tokenizer/prompt 版本。不同 state 的截断策略、option预算会影响最终输入，不能缓存错误的拼接结果。
3. **激活/prefix 复用。** Sezika 的双向 encoder 里，问题会影响 state token 的表征，同 state 不意味着所有层 state activations 相同。只有 attention mask/计算图能证明该部分独立才可 exact reuse；否则称近似实验，并核误差、argmax、校准和真实任务影响。FastJev 明确共享 prefix 可改变接近的 BF16 argmax；Laya-MLX 的普通 prompt cache 只复用 tokenization，每题仍做独立 encoder。
4. **独立问题批处理。** 每题共享可见 state，却不可以偷看另一题输出；缓存/复用与真正 GPU batching 分别验证。增加问题增加 input、attention、显存和排队；厂商“免费输出”不是本地计算免费。支持单 state 多题与多 state 同 schema 两种合同，保留原 request/question ID 与失败状态。
5. **大候选集分层。** 局部 shortlist softmax 是条件分布，不能直接拼成全局概率；未入围真标签已无机会。候选 selector recall、分片winner重排误差、总时延和不确定区间要一起测。Laya 的 token/head预算提醒长候选描述容易截断成难以区分标签。

## 模型与权限必须分离

[Tenuo scoped-authority](https://tenuo.ai/blog/jev-scoped-authority) 展示依赖升级案例：trusted code 先算 eligible transitions，模型只选择有限候选；宿主把 label 映射到 known worker，并独立收紧工具、参数、路径、版本、分支和 TTL warrant；测试作者、实现者、验证者权限分开，修改前仍验授权，结果另外验证。

Sezika应返回 prediction/probability/distribution/abstention，不应返回具有执行效力的 Authorization。高概率不能创造能力、扩大路径范围、提升身份或撤销用户约束。模型建议“执行”也需已有权限及确定性 validation；`no_decision`、invalid input、unsupported schema、resource limit、cancelled、runtime failure 是不同状态，不能统统填默认Choice或p=0。

Cisco的固定证据尤其值得吸收：[System One analyzer design](https://github.com/cisco-ai-defense/skill-scanner/blob/c3d7fd9fee50cf2ea45f0b381eb61f857360f825/docs/architecture/analyzers/system-one-analyzer.md) 在439条source-disjoint样本里报告 AUC 0.25–0.31，恶意平均p=0.568、良性p=0.683，多威胁提问几乎没有额外差异。该集成默认关闭，不能产生finding、改severity或verdict。**校准之前先确认模型能够分离标签，概率格式和模型名不足以决定启用。**

## 231 个外部链接怎样使用

它们包含官方文档/原语/Cookbook、在线 demo/托管入口、博客和媒体、评测/研究、X/Reddit/HN讨论、模型页面与包页面；这些类型不能混成231个已实现的项目。全部已按目录说明初筛，此处不声称逐页全文核验；其中 Tenuo 文章做正文阅读。

- **规范设计资源：** HTTP reference、State、Choice/Score/Noul、known jaggedness、confidence semantics、fan-out、pre-parsed extraction。学闭集候选、原子问题和失败边界，不沿用其校准或吞吐宣称。
- **产品实践：** SRE diagnosis、voice turn detection、LangChain harness/online evals、Chem autocomplete、Databricks ai_decide。学观察→测量→宿主确定性策略；来源/公开分母/真实baseline仍需核验。
- **独立效果与负结果线索：** Jev reranking did not beat vector retrieval、Jev Does Not Play Dice、Convex/LegalBench/Vals切片、calibration批评、compaction批评。把争论翻译成我们自己的随机/多数类/BM25/原模型对照、option shuffle、不能观察事实的拒答与多语言切片测试；不把博客结论直接迁成Sezika事实。
- **媒体和讨论：** 融资额、点赞、HN热度、发布公告只提供生态背景。所谓“永不幻觉”“全已校准”“低延迟替代所有LLM”不能作为功能证据；近期 signup状态也说明托管可用性会变。
- **在线 demo：** 能解释“输入文本→有限分布→结果”；对Sezika用户最有用的是本地无需密钥、明确模型包与版本、展示真实输入预算及拒答的playground。服务与视频的观感不能代替模型质量验收。

## 产品提升顺序与完成判据

**先补可依赖的核心合同。** 明确 `Choice<TEnum>`/有限候选、Probability、ordinal Score 的输入/输出；state用静态类型与 JsonTypeInfo；返回完整分布、原始集中度、校准身份与status，不用 ambiguous confidence 一个词。tokenizer应报告state/question/options实际token数、截断量、预算拒绝；unsupported unknown kind保持显式，而非忽略。失败时不造有效答案。

**再补可重放证据。** decision record 包含 schema/prompt/model/tokenizer/backend/precision/calibration hash、usage/timing、candidate vocabulary 与 human outcome引用。默认不保存原始私密输入。冻结holdout和source/time分割，按语言/任务/option count/length/OOD报告label separation、accuracy、Brier/NLL/ECE、coverage-risk、Score MAE，以及cold load/warm p50/p95、显存/峰值内存、取消与并发。agreement、集中度、概率、实际accuracy各自命名。

**首批只做三个独立样例。** 支持工单（department/urgency/needs-review）、值班日志（actionability/severity/team/duplicate）、本地文档检索（relevance/answer-evidence/none）。先shadow到足量人工outcome；成本与误判低而且可标注。host example负责路由/通知/索引，Sezika不负责执行。这些试点比交易、物理控制和大Agent框架更容易验收真实能力。

**再实验候选缩减、缓存和场景训练。** 只有对同一固定工作负载改善总墙钟/吞吐且quality/coverage未变坏的实验才能转supported；需要approximation时写清误差和适用域。online self-training/teacher proxy暂缓，先做离线GPU训练、人工heldout和版本回滚闭环。

**最后稳定分发。** NuGet只装代码，模型包外置且含license/source/revision/hash/tokenizer/format/calibration兼容矩阵；CLI `inspect`/`validate`/`decide`/`replay` 与机器JSON结果语义版本化。README短路径能从加载本地模型到真实typed decision，mock只测宿主策略且明确标注，真实语言质量与Native AOT支持分开验收。新能力建议交总任务写入唯一 ROADMAP，当前分析不直接扩张实现。

## 明确校正的目录说法

- [eve decide源码](https://github.com/vercel/eve/blob/89704cb494681480f22b162f9ce20f9fc459acf7/packages/eve/src/ai/decide.ts) 确认决策默认模型是Jev；其首页聊天模型仍另有默认，不能写Jev是整个框架默认智能。
- [eve研究](https://github.com/vercel/eve/blob/89704cb494681480f22b162f9ce20f9fc459acf7/research/jev-decision-models.md) 说明 `experimental_evaluate` 已改为 `experimental_decide`，contract仍可能在patch版本变；没有live质量/成本/延迟验收，跨provider分布/集中度能力有差异。
- fast-jev-compaction 的 macOS动画demo是scripted而非live API；其说明坦诚，不应拿动画当真实推理证据。
- Tiltmeter 的“无标签估计准确率”有持续校准假设，当前Sezika不具资格把此量称为实际正确率。
- 大项目“native Jev integration”、海量设备、零幻觉、绝对成本等仅目录声明，未本次源码验证的均留低证据。

## 执行与清理

全部shell使用固定PowerShell7路径，已核版本7.6.6。只读与本任务文档/JSON生成，没有构建、训练、第三方代码执行或后台服务。网络读取使用单次proxy7890、25秒/请求与明确已知仓库路径；eve树仅用于列出特定源码路径。生成循环人工核对 `-lt` 比较，先2项smoke，再447项/90秒deadline，50项进度；每次工具命令有退出结果。主生成PID、CreationDate、CommandLine、ParentProcessId在 `integration-owned/generation-447.json`，无创建子进程或持久服务，已结束。保留研究证据与可重放生成脚本，临时HTML与tree索引按明确归属逐文件清理，详见owned清理记录。
