# 两份 awesome-jev 清单对 Sezika 的学习与采纳分析

研究日期：2026-10-08。结论：**优先补可靠数据、候选语义读出、独立质量证据和拒答校准，再优化性能和扩大集成。** Sezika 的纯 C#、CUDA Driver、Native AOT、静态类型与资产身份路线已经形成工程优势；眼下的瓶颈是模型能否在未见问题上作出可靠判断。

本报告是研究建议，未实施新训练、下载模型或引入运行时依赖；不另立阶段计划。建议接续现有 ROADMAP 任务，当前停止新增训练的状态保持为本报告基线。

## 1. 覆盖范围与证据等级

来源为 [中文清单](https://github.com/yzfly/awesome-jev-zh) 与 [英文清单](https://github.com/yibie/awesome-jev)。冻结快照分别为 `8194315113a43ab79eed0055d3f2a44c43705eab` 和 `0839b9264dfe10f7cb72aea8e9fd74d393a5155b`；本地 README 含此次待合并的 Sezika 条目，因此本项目作为自身基线计入一次。

| 范围 | 数量 | 实际完成的工作 |
| --- | ---: | --- |
| 两份清单主要条目 | 832 | 抽取条目、类别、链接与来源位置；存在跨清单重复 |
| 所有正文仓库 URL 身份 | 555 | 每个身份各有一条取舍，包含主要条目和说明中的仓库引用 |
| 仓库别名去重后的项目 | 552 | 逐项目保留建议、理由、优先级、证据等级、来源和固定 commit |
| 可访问项目 | 550 | 获取元数据及固定版本 README 证据；长 README 公共摘要最多 20,000 字符 |
| 重点 README 审读 | 58 | 阅读 README 或明确片段，形成针对性评审 |
| 选定源码/协议核查 | 19 | 核查明确文件；不代表完整代码审计或运行验收 |
| 其他主要资源 URL | 231 | 分别筛选规范、文章、在线演示、模型资源、评测和讨论；不作为额外项目数 |

`TheoLeeCJ/SemIf`、`AbdelStark/awesome-typesafe`、`AnotiaWang/awesome-jev` 三组新旧名字各归并一次。`zzz1YAO/DataJev`、`jiawei686/jev-screen-mcp` 当前不可访问，保留原清单说明并标为不可核实。

总表的 `catalog` 是目录级方法筛选，`readme` 是项目说明审读，`code` 是选定源码/协议核查。552 项中这三类分别为 **494、39、19**。未运行任何外部模型或 benchmark；下文外部性能和质量数字均为作者记录，而非本项目复测。

完整交付：

- [552 项逐项分析 CSV](research/jev-ecosystem-2026-10-08/projects.csv)：可按项目、类别、采纳方式、证据和优先级查看。
- [完整项目 JSON](research/jev-ecosystem-2026-10-08/projects.json)：保留别名、两份清单的逐行来源、固定 commit、README hash 与各评审意见。
- [231 项其他资源 CSV](research/jev-ecosystem-2026-10-08/external-sources.csv)、[来源与方法](research/jev-ecosystem-2026-10-08/provenance.json)。
- [模型与训练评审](research/jev-ecosystem-2026-10-08/models-review.md)、[评测评审](research/jev-ecosystem-2026-10-08/evaluation-review.md)、[SDK 与应用评审](research/jev-ecosystem-2026-10-08/integration-review.md)。

表中的“采纳”指建议采用方法；“实验”指提出受控对照；均不表示已经实现或获准复制代码/权重。P0–P3 表示对 Sezika 当前缺口的相关优先级，不是项目质量评分。仓库的 licenseInfo 只作代码许可线索，权重、tokenizer、数据和再分发权限分别核实。

## 2. Sezika 的真实起点

最新依据是 [同24题 GPU 可学性与位置诊断](evidence/gpu-learnability-2026-10-08.md)。它是读取时的工作区证据，尚未将本次研究提交到 Git；文件 hash 记录在 provenance 中。

固定原始 mmBERT encoder，三行线性 marker scorer、2304 个 F32 参数，24 train / 8 已曝光 development，标签来自真实教师但尚无人审。已有 72 步实验和唯一固定的 768 步实验均完成纯 C# GPU 更新、权重保存、新进程加载和真实重新编码。

| 最新768步结果 | 观察 | 意义 |
| --- | --- | --- |
| 训练题教师一致 | 17/24；CE 1.229626 → 0.992070 | 更新有效，但训练集仍未充分拟合 |
| 开发题教师一致 | 1/8；CE 1.268793 → 1.274028 | 没有泛化改善证据；8题也不足以模型择优 |
| 训练 Choice 换序 | 原6/8 → 换序4/8；6/8预测ID改变 | 整个输入构造、encoder与head仍明显位置敏感 |
| 保存后独立验证 | B/C逐题输出、特征与CE一致 | 持久化和复现链路可靠，不能替代语义质量 |

教师一致率不是人工金标正确率，softmax 集中度不是正确率，也不是执行授权。当前不足以区分优化步数、表示位置、head容量、输入设计、标签质量和encoder能力各自的影响。既有训练器、预测导出、人工审核、校准与 checkpoint 工具应继续复用；不需要从头再写一套框架。

## 3. 最值得学习与采纳的项目

| 学习对象 | 应采纳的具体方法 | 与当前 Sezika 的关系 | 判断 |
| --- | --- | --- | --- |
| [Nimble](https://github.com/bespokelabsai/nimble) | 只改变一个关键事实的反事实对；必要证据删除；source-family 隔离 | 补充否定、角色、政策与字段关系监督，抑制词面捷径 | 优先采方法 |
| [Luce](https://github.com/scienthoon/luce) | writer与teacher解耦；真实标签与合成标签分开；多数类塌缩与目标检查 | 32条教师题先人审；真实outcome决定质量与校准 | 优先采方法 |
| [typed-decisions](https://github.com/kotoba-lang/typed-decisions) | option/question文本span池化，`[q,o,q*o]`共享GELU scorer | 最贴近当前marker线性头的可控下一对照 | 优先实验 |
| [smalljev](https://github.com/isHeSatoshi/smalljev) | 精确option语义span、共享scorer、候选置换增强 | 减少letter/slot捷径；检验整个模型顺序敏感性 | 配合head实验 |
| [NanoJev](https://github.com/TianyuCodings/NanoJev)、[Jevlike](https://github.com/vinnylarouge/jevlike) | 动态候选set attention/query-to-state匹配，完整问题loss | span读出不足后，再比较更强的小head | 条件实验 |
| [jqv](https://github.com/Octalab-Inc/jqv)、[Laya](https://github.com/NandhaKishorM/laya) | 同权重数值比较、问题隔离负控、校准身份与候选token预算 | 为独立模型质量及算子优化建立可信基线 | 优先采验收方法 |
| [Verdict](https://github.com/Manavarya09/verdict) | 独立校准、风险覆盖曲线、LAC/APS预测集合 | 形成显式拒答与可检验适用域 | 能力达标后实验 |
| [jevcompat](https://github.com/mandu5/jevcompat) | 归一、argmax、Score期望、ID/顺序/邻题合同 | 扩现有合同验证，协议合法与语义正确分别验收 | 采纳方法 |
| [zh-decision-bench](https://github.com/CodyQin/zh-decision-bench)、[robustness](https://github.com/Yifan-Lan/awesome-jev-robustness) | 中文困难关系、候选重排、角色/否定/不可回答切片 | 对当前6/8换序ID改变建立可复现诊断集 | 优先采方法 |
| [spam-eval](https://github.com/bitnovus/jev-spam-eval)、[phishing-bench](https://github.com/anisselbd/jev-phishing-bench) | 家族划分、同证据基线、独立阈值选择与确认 | 避免教师模板、业务实体与改写泄漏到封存集 | 优先采方法 |
| [DecisionKit](https://github.com/iamjonatha/decisionkit-dotnet)、[Discern](https://github.com/doeixd/discern) | 泛型问题/答案、Core/DI分层、显式Uncertain | C# API与AOT友好的接入方式；先查现有能力再补差距 | 采接口思想 |
| [metajev](https://github.com/YYTbit/metajev)、[winnow](https://github.com/GhalebDweikat/winnow) | 全分布回放、policy版本、影子运行与人工outcome | 有数据的工单/日志试点形成业务质量证据 | 放宿主层 |
| [jev-ultrafast](https://github.com/browser-use/jev-ultrafast)、[Tenuo](https://tenuo.ai/blog/jev-scoped-authority) | 动态合法候选、同观察快照多问题、执行前重验、独立授权 | Sezika输出有限决策，宿主处理状态鲜度与执行权限 | 放宿主层 |
| [Cisco skill-scanner](https://github.com/cisco-ai-defense/skill-scanner) | 先确认标签可分离；失败明确，默认advisory | 防止把类型正确、有概率的弱模型当安全门禁 | 采失败教训 |

其中 typed-decisions 的 [encoder源码](https://github.com/kotoba-lang/typed-decisions/blob/e65215db7e554a31f557936f0bd1d8a0a78b6af5/src/typed_decisions/encoder.py) 确认了span池化与交互头；marker“不学习”的结果属于该作者实验，不能直接判定 Sezika 的同一原因。smalljev 的 [共享头源码](https://github.com/isHeSatoshi/smalljev/blob/65f74b49748f2663225a6fc68852c0a7d09fd0b9/smalljev/heads.py) 只保证给定feature顺序的等变性；encoder生成feature时仍可能受位置和前文影响。

## 4. 如何提升模型判断能力

### 先补独立数值参照与可信标签

接续 **S4-15**：冻结权重、tokenizer、46-token输入、张量检查点与容差，用独立上游输出核对 C# encoder。现有捕获和比较器已存在；这里建议完成真实oracle，不重做工具。没有数值参照，较差预测不能直接归因于head或数据。

接续 **S4-05/06/10/11**：先人工审核已有32题，保留教师回答与人审差异。这些已曝光题只能用于实验和诊断。后续数据以来源、实体、模板家族和时间划分 train/development/calibration/sealed-test；翻译、简繁变体、重排和反事实共用父链并留在同一split。教师用于补标或建议，封存质量由独立人审/可复核oracle决定。

应增加的有效样本包括：

- Boolean：含关键词但命题为假、引用/辟谣、否定范围、双重否定、角色互换，以及确实缺证据的情况。
- Choice：相似候选、无正确候选、候选描述与标签冲突；从小候选集合到大集合分层报告。
- Score：每级明确rubric与边界；等级分布、量测结果、人工等级和教师等级分别命名。
- 跨语言：中英同语义配对、简繁、混合语言、术语与单位；之后按实际目标市场增加语言，不因基座multilingual就宣称已支持质量。
- 反事实：单关键事实改变应翻标签；无关变化应保持标签；删除必要证据应走未知/拒答。父链与人审记录完整保留。

离线规则/模拟器可以提供可复核标签，运行时决策继续由模型完成。近重复检测不能只检查原始文本，还应检查实体、模板和衍生关系。

### 再设计可区分原因的head消融

接续 **S4-07/13/15**。当前768步已闭环，建议在获得下一轮执行授权、数值基线和标签复核后，仅预先固定以下四个对照；这是实验方案，不是本轮继续训练。

| 对照 | 读出与head | 损失 | 主要回答的问题 |
| --- | --- | --- | --- |
| A0 | 当前marker向量＋共享线性scorer | CE | 保留当前同版本基线 |
| A1 | option文本span均值＋共享线性scorer | CE | 仅改变读出位置，是否更可学 |
| A2 | question/option span及乘积＋两层GELU共享scorer | CE | 问题与候选交互、小head容量是否有增益 |
| A3 | 与A2相同 | CE＋Brier | 概率损失是否产生独立收益 |

固定encoder/输入内容/候选ID、数据划分、随机seed、样本遍历与训练预算；保留所有结果，不能在8条dev上搜索最佳配置。span提取依赖实际token边界，不通过字符串近似定位；相应pooling/GELU/head反向由 C# GPU kernels 实现并验梯度，现有三行head反向不能宣称已经支持MLP或encoder反传。

训练拟合、独立开发收益、位置稳定性与概率质量分别报告。仅训练题变好，不能进入正式模型发布。若标签、输入与数值已核而这些小head仍失败，再研究encoder LoRA/反向范围；若小head已泛化，再研究更小encoder/蒸馏。大模型、RLCD和set attention均应由失败证据触发。

### 对整个模型做顺序与隔离诊断

候选文本和ID一起旋转/反向/固定seed随机置换，重新渲染、tokenize并真实encoder编码，再映回语义ID。报告预测ID flip、首位偏好、正确目标排名/概率、概率总变差和性能开销。只重排旧feature仅检查head，不检查encoder。

从 [zh-decision-bench源码](https://github.com/CodyQin/zh-decision-bench/blob/5fd127f2e70b0f79bb4906a3a191dd20d04a82fa/src/permute.py) 学方法时，要改进其基于Python进程salt的 `hash(id)` seed：用显式seed/SHA并冻结permutation manifest。

多问题packing另测：问题ID改名、问题顺序改变、邻题插入/删除、邻题放置秘密线索。普通双向attention不能保证兄弟问题隔离，不能直接照抄 typed-decisions 的多问题输入并声称隔离成立。

## 5. 如何提升可靠性、拒答与校准

接续 **S4-08/12**，扩现有评估器和身份合同，避免新增重复框架。

| 能力 | 应报告的证据 | 常见误区 |
| --- | --- | --- |
| Boolean | balanced accuracy、负类召回、AUROC/PR与混淆矩阵 | 多数类高准确率掩盖不会判false |
| Choice | accuracy、macro分组、目标rank、大候选数与置换差异 | top1稳定或共享head等变不等于正确 |
| Score | 等级混淆、归一MAE、query内排序、ties | 排序正确与等级准确是不同能力 |
| 概率 | NLL/Brier/ECE、bin样本数及区间；未校准/校准分别报 | 低ECE不一定有较好区分能力 |
| 拒答 | risk-coverage、接受样本正确率、OOD误接受率、缺证据切片 | 只报告接受样本而丢掉失败分母 |
| 部署 | 同训练权重CPU/CUDA/两RID AOT与独立加载 | tiny/未训练诊断head通过不能替代正式模型 |

校准profile绑定实际weights、tokenizer、输入布局、head版本、precision、任务/题型/语言、适用候选数及拟合数据身份。温度在独立calibration拟合，策略在development确定，sealed-test一次确认；公开审计集和反复查看的题不能自动变封存题。不直接搬别人的0.7/0.8/0.9阈值或温度。

Verdict [conformal源码](https://github.com/Manavarya09/verdict/blob/76bc1fe9ba87ec5b746ed33faac48812f1d1a4ca/src/verdict/calibration/conformal.py) 的覆盖保证依赖exchangeability，并且是边际覆盖；不能解释为每题正确率、OOD承诺或执行权限。当前小样本首先补数据与标签可分离性，再考虑预测集合或独立正确率head。

显式区分 invalid-input、length-rejected、unsupported、cancelled、runtime/resource-failure、uncertain、missing-evidence/OOD。unknown候选是可学习标签，外部拒答阈值是产品策略，两者不混淆。未校准时照实标明，不能输出默认有效Choice来掩盖失败。

## 6. 如何提升速度与可部署性

接续 **S5-06/07、S4-08/15**。先对同权重同请求画像：tokenize、输入构造、H2D/D2H、attention/GEMM、归约、head、排队与完整端到端；冷加载、热推理、1/8/32题、短中长、p50/p95、显存/内存及失败分别记录。原有 W8A32 路径较SIMD慢且增加缓存内存，不能仅以低位宽宣称提速。

当前适合优先研究C# tiled GEMM、归约与kernel fusion、buffer复用、独立完整序列的有界batch。每项保留logits/概率误差、低margin argmax flip、质量/校准和AOT回归，只有同一真实工作负载的总成本改善才称有收益。

需要区分三种缓存：

1. **完整决策缓存**：完整state/question/options顺序与文本、权重、tokenizer、协议、precision与校准身份匹配；外部状态鲜度由宿主控制。
2. **tokenization与冻结训练feature缓存**：输入/hash完整匹配；冻结训练feature对应完整逐题序列，不是仅state向量。
3. **state激活/KV共享**：Kev/jqv的decoder共享prefix建立在因果计算图上；双向mmBERT里的state表征会受question/options影响。要改变attention图、拆成双编码器或近似计算，就作为新模型另行训练、数值与质量验收。

Jqv 的 [pointer head](https://github.com/Octalab-Inc/jqv/blob/40ac9b8ce4ed94d6442e4cf014bc3e6b2a028758/jqv/heads.py) 与 CLM 的独立候选embedding可用于固定菜单的条件实验，但候选独立缓存可能损失字段组合/细粒度关系。分片shortlist的softmax是条件分布，不能直接拼成全局概率；先测候选召回、重排误差、总时延和拒答。

## 7. 如何形成真实产品能力

接续 **S6-03**，首个试点只选一个容易获得人工outcome的场景：工单路由、值班日志分类或文档相关性。明确有限问题、候选、输入预算、拒答、模型/校准版本、原始分布与结果记录；先影子运行，让人继续做决定，逐项积累实际结果。IoTSharp/Tomur/SonnetDB 的宿主承担业务执行、鉴权、限流、租户预算和回退。

从 DecisionKit/TypeSafe .NET SDK 学 C# 泛型问题答案、`JsonTypeInfo<T>`和DI/Testing分层，保留静态类型/source generation；不照搬reflection overload。Sezika已存在相应机制时只补缺口。Discern 的 Match/Miss/Uncertain 值得采用：不确定不能压成false或成功。

从 browser-use 学动态合法候选和同一观察快照的operation/target联合问题；模型选出目标后宿主重验freshness及兼容性，DONE由独立结果检查确认。Tenuo展示的独立权限边界与Sezika一致：先算合法动作，再模型选择，再宿主授权/验证。核心保持无业务动作执行器。

接续 **S6-02**：正式权重质量、校准和两RID部署验收通过后，完成模型卡/数据卡、许可与hash、独立模型包、版本兼容矩阵、CLI/NuGet与本地无密钥示例。NuGet装代码，权重独立管理。README说明实际支持的语言、题型、候选/长度范围、拒答与已测指标；当前示例不能宣称已经达到生产可靠性。

## 8. 不宜立即采纳什么

- **扩大8B/27B、换多个encoder或铺新硬件。** 当前尚未核清数值、标签与head读出，规模不能解释这些失败；同时会增加C#算子/反向与AOT成本。
- **把RLCD当已知优胜配方。** Laya源码默认是噪声logit/GRPO式附加项＋soft CE，并说明当前split无额外测得收益；NanoJev等配方也不等于私有TypeSafe实现。先做CE与概率损失受控对照。
- **复制PyTorch/ONNX/MLX/vLLM/C++运行时。** 可学计算与验证方法，核心模型、CPU/GPU算子和调度继续纯C#；云API只属于另行授权的外部工具/适配。
- **把集成数量、star、视频或云价格当能力证据。** 大仓库的可选Jev组件不代表整个框架由Jev驱动；外部端到端成功不能移作Sezika语义质量。
- **复制headline校准或安全结论。** softmax、低熵、类型合法、同题协议一致与实际正确率各自验收；模型预测不授予行动权限。

值得保留的反例：

| 项目 | 核实到的限制 | 对我们的要求 |
| --- | --- | --- |
| Laya/Nimble | 当前多语言Laya未fit temperature；最新Nimble T=1未重新fit | 旧校准不跨checkpoint复用 |
| Luce | 作者更正maze高分是多数类塌缩；部分跨模型不同样本对比 | 公平同题baseline、负类指标与真实outcome |
| minojev | 当前1.7B＋约0.8M头，与旧目录架构不同；作者OOD低于生成baseline | 冻结feature小头不自动获得泛化 |
| SemIf | BF16 prefix复用改变若干argmax | 数学形式相似不代表实际推理等价 |
| spam-eval/judgekit | 目录旧样本/高分与当前版本不一致；部分同集选阈值 | 固定版本、split与阈值选择范围 |
| Cisco skill-scanner | 439条source-disjoint记录的AUC 0.25–0.31，方向倒置 | 标签分离失败时保持advisory，先停止门禁用途 |
| Yoshi | 当前节省token的两次试验仍更慢并有失败 | 看总墙钟与任务质量，不能只看token |
| eve | 当前为experimental_decide；整个框架聊天默认另有模型，无live横评 | 接口/集成存在与独立模型效果分开 |

Cisco的证据来自 [固定版本设计文档](https://github.com/cisco-ai-defense/skill-scanner/blob/c3d7fd9fee50cf2ea45f0b381eb61f857360f825/docs/architecture/analyzers/system-one-analyzer.md)，eve来自 [固定版本decide源码](https://github.com/vercel/eve/blob/89704cb494681480f22b162f9ce20f9fc459acf7/packages/eve/src/ai/decide.ts)。acento当前PREREG文件已有冻结登记，README旧段落未同步；本研究未核Git先后，不将文本矛盾当作预注册无效或造假的证据。

## 9. 对现有 ROADMAP 的建议顺序

这是依赖排序和建议，不修改唯一阶段计划，也不执行当前暂停的后续任务。

| 顺序 | 既有任务 | 下一项具体交付 | 进入后续阶段的依据 |
| --- | --- | --- | --- |
| 1 | S4-15 | 已有比较器接到可信独立上游真实张量；固定身份/容差/失败 | 数值基线可解释 |
| 2 | S4-05/06/10/11 | 32题人审、困难关系/反事实pilot、家族与封存划分 | 标签/数据身份可信，曝光题不作test |
| 3 | S4-07/13 | 固定小head读出消融、候选完整重编码换序 | 可学性和未见开发收益分别成立 |
| 4 | S4-08/12 | 独立校准、OOD/风险覆盖、模型与profile身份 | 正式质量与拒答范围有证据 |
| 5 | S4-08/15、S5-06/07 | 同训练权重两RID部署、完整画像、单项算子/batch优化 | 数值/质量不降，真实总成本改善 |
| 6 | S6-03 | 一个宿主影子试点、人工outcome与可回放决策 | 实际业务受益和失败回退可复核 |
| 7 | S6-02 | 正式CLI/NuGet、外置模型包、模型卡/数据卡与支持矩阵 | 发布门槛逐项通过 |

长期定位应是“可审计、可拒答、可独立部署的多语言有限决策引擎”。提升幅度以未见数据、实际outcome和资源成本证明；不以demo数量或推理格式代替能力。
