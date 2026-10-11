# Jev 生态模型、训练与架构对 Sezika 的采纳评审

研究日期：2026-10-08。负责范围为两份清单中的 67 个仓库名引用，逐项结论见 `models-decisions.json`。`TheoLeeCJ/SemIf` 与 `TheoLeeCJ/SemIf-OpenJev` 已确认指向同一仓库、同一 commit；保留两条分配记录，合并统计时应去重。本组审读 20 个不同项目的当前 README 或首 20,000 字符摘要，检查 6 个项目的 8 个明确源码文件，没有运行外部代码、下载模型或复现性能。

“采纳”指采纳数据、架构实验或证据管理的方法，最终用 C# 实现；不表示引入它们的 PyTorch、MLX、ONNX Runtime、vLLM、FlashAttention 或 C/C++ runtime。README 数字均为项目方陈述；源码核查只证明代码包含某种实现，不证明训练成功或质量指标成立。标记 `catalog` 的 46 条仅作完整初筛，不能声称逐一深度审计。

## 判断

Sezika 当前最缺的是 **能证明读懂新问题的训练数据、可泛化的候选打分头、独立质量评测与校准**，而不是再添加十几个推理后端或扩到 8B/27B。已有 C# CPU/CUDA/AOT 路线是有价值的工程基础；32 条教师标签、24 train / 8 dev 的早期 72 次 GPU head 更新已经跑通。最新固定 768 步诊断也已完成真实训练、保存和新进程加载：train 教师一致 17/24、dev 1/8，训练 Choice 换序由 6/8 降至 4/8、6/8 预测 ID 改变，dev CE 从 1.268793 提升为更差的 1.274028；B/C 独立磁盘验证完全一致。这证明训练机制和持久化一致，并没有证明质量改善；三行 scorer 也未证明 encoder 反向训练。依据是仓库本日 [GPU 可学性证据](../../evidence/gpu-learnability-2026-10-08.md)，不是旧状态推断。

学习优先级：Nimble 的反事实数据工程、typed-decisions / smalljev 的读出消融、Luce 的真实数据与目标检查、Laya / jqv / Verdict 的校准证据；其次才是 NanoJev 的 set attention、CLM 的候选缓存、共享前缀优化和 RLCD。不能把“单次前向、无生成 token、返回 softmax”当作已经学会语义决策。

| 优先级 | 项目 | 具体借鉴 | 对 Sezika 的动作 |
|---|---|---|---|
| P0 | Nimble | 一事实变化的反事实对、证据删除检查、source-family 分割 | 建独立标签与人审 pilot；反事实家族必须同 split |
| P0 | typed-decisions | marker / option span / question span 的消融 | 用已有 mmBERT 隐藏状态比较共享线性头、两层匹配头，分别验收 GPU 反向 |
| P0 | Luce | synthetic 与 real 分开、多数类塌缩诊断、正确 target | 真标签决定 model selection、calibration 与 test；教师一致率另报 |
| P0 | Laya | 真实多语言失败、候选 token 预算、独立 temperature、loss 对照 | 固定当前 checkpoint；优先修已观察质量与输入预算，CE 基线先于 RLCD |
| P0 | jqv | 同输入多引擎等价、问题隔离负控、calibration 身份校验 | 固定权重/Tokenizer/input-layout/calibration 身份并拒绝不匹配的校准 |
| P0 | Verdict | prototype/logistic 对照、conformal set、risk-coverage | 先重写小型校准和集合预测；明确覆盖率条件与拒答语义 |
| P1 | NanoJev / Jevlike | 完整问题目标、动态候选 attention、独立 oracle | 加候选数、置换、干扰候选与新 schema 测试，实验更强的小头 |
| P1 | Kev / JevForge | 原权重继续适配、未见来源 split、回归和实验账本 | 模型更新须保持旧域质量并发布逐来源、逐题型证据 |
| P1 | AnyJev / smalljev | label prior、顺序置换、语义 span 绑定 | 检查整个 encoder+head 的顺序偏差，不能只检查 head 等变性 |
| P1 | CLM / Verdict | state 和候选独立嵌入、InfoNCE 与缓存 | 固定候选场景做双编码器可选实验，以结构性推理损失换缓存收益 |
| P2 | Bekko | 尺寸阶梯、候选共享前缀、广泛任务适应 | 数据/质量基础成熟后试 17M/68M；不能照搬英文模型与 ONNX 导出 |

## 重点项目分析

### Laya：最接近现有模型路径，也是最重要的失败证据

证据：[README 固定版本](https://github.com/NandhaKishorM/laya/blob/3cf26cbcb18725dbc2d127bb8bb2c4c43243ae63/README.md)、[train.py](https://github.com/NandhaKishorM/laya/blob/3cf26cbcb18725dbc2d127bb8bb2c4c43243ae63/laya/train.py#L7)、[calibrate.py](https://github.com/NandhaKishorM/laya/blob/3cf26cbcb18725dbc2d127bb8bb2c4c43243ae63/laya/calibrate.py#L1)。

Laya 的 mmBERT/ModernBERT 路线与 Sezika 已实现算子最相近，适合研究输入布局、动态候选预算、逐语言失败和训练目标。当前 README 承认多语言 checkpoint **没有拟合温度**；typed-decisions 上未微调 mmBERT accuracy 0.352、majority 0.461；0.766 属于另一个针对该训练集微调的英语 checkpoint，README 还说该行没有已提交结果文件。不能把这几个权重和证据混为一谈。

源码中 `rlcd_loss` 对 detached logits 加 Gaussian noise，用 proper reward 的标准化 advantage 权重 log-density，再加 soft CE；开头明确作者在该 split 未测得额外 RLCD 项收益。可学习 objective 定义、完整问题 masking、option-order augmentation、train/calibration 分离、塌缩诊断；**不能声称这是 TypeSafe 的私有 RLCD 方法或已经证明更好**。先在相同标签、预算、seed 和固定 OOD 上比较 CE、Brier、CE+Brier，再考虑采样梯度研究。

最急验收：否定句、假设句、引用/非本人意图、JSON 字段组合、多语言、标签与描述冲突、大候选数/截断。README 有否定取消请求高置信误判以及 77 候选每项只剩少数 token 的具体故障。增加 `max_len` 不能当作长上下文质量证明。代码 Apache-2.0；当前权重、基座和校准文件必须分别保留来源及身份。

### Nimble：优先借鉴数据生产，不搬 9B runtime

证据：[README 固定版本](https://github.com/bespokelabsai/nimble/blob/dcfdbd9a64f0d869f658d7a72f1beaee32737773/README.md)。

它最有用的是 contrastive data curation：两条记录只改一个相关事实，其他 policy、问题和背景保持一致；分别核事实与规则；删去任一必要证据后，结论应变成未知；用独立代码 oracle 给 checked facts 求标签；反事实 pair 与同 source family 始终同 split。用于 Sezika 能防止模型只记关键词、选项顺序或教师文风。代码 oracle 只用于离线标签与评测，不是运行时规则替代推理。

原始 2,676 train / 324 frozen holdout 只是六个 held-out 类别，不能当全域质量。最新 checkpoint T=1 尚未重新 fit；旧 release T=2.179 以及 v2 transferred default 不证明当前 release 校准。MLX 共享前缀与 CUDA 每 field 重跑也不是相同吞吐实现。应把 checkpoint + adapter/base + tokenizer + prompt schema 与 temperature 绑定；避免重复“只要 softmax 就是校准”。清单自述 Apache-2.0，GitHub licenseInfo 本轮为空，复制前还须读 LICENSE 和模型/数据许可，不能据此判断无许可或全资产同许可。

### typed-decisions：读出位置可能比多训练几步更关键

证据：[README 固定版本](https://github.com/kotoba-lang/typed-decisions/blob/e65215db7e554a31f557936f0bd1d8a0a78b6af5/README.md)、[encoder.py](https://github.com/kotoba-lang/typed-decisions/blob/e65215db7e554a31f557936f0bd1d8a0a78b6af5/src/typed_decisions/encoder.py#L24)。

README 自述新 `[OPT]` marker hidden state 在多项训练配置中停在 label prior，而读取 option text 和 question text 的 mean 后开始学习。源码确认默认 `pool="span"`，构成 `[q, o, q*o]` 后用 Linear→GELU→Linear 共享 scorer，按每个问题候选做 softmax，损失是 CE+Brier。这是当前三行线性头最合理的下一项消融；先用已冻结的 mmBERT features 做小头，确认功能增益，再申请实现更大反传范围，避免把 GPU head 训练误称全模型训练。

源码给 encoder 的普通 attention mask **没有保证兄弟问题隔离**，因此其“一次多问题”不能直接作为隔离设计复制。还新增 special tokens，Sezika若新增 token就改变 tokenizer/embedding合同与权重兼容性。仅采用 span head 时不必照搬这些新 marker。其公共 NLP gold 数据比纯教师概率更适合独立 oracle，但 derived label 的关键词规则属于标签构造，不能让规则进入 Sezika runtime。比较 ModernBERT/DeBERTa/LLaDA 的作者数字显示 encoder 值得优先探索；不支持马上换 DeBERTa，因为那会新增大量 C# 算子与 AOT 验收。元数据 NOASSERTION，须分开复核 LICENSE、dataset 与 backbone 条款。

### Luce：真实数据、失败更正与目标设计

证据：[README 固定版本](https://github.com/scienthoon/luce/blob/bda3d21cdb537ae99662fdba275adc450e90f455/README.md)。

writer 生产 state、teacher 标注、记录 votes/disagreement、真实小集决定校准与选择，合成 validation 明确 `[in-synth]`：这些最值得迁移。它更正自己 maze 的 85% accuracy 只是多数类塌缩；更适当的目标是每动作的 `Q3(s,a)` 存活概率而不是将几个同样安全动作硬压成单个 choice label。Sezika 的 Score 必须说明是等级分布、实际量测还是教师打分；Boolean 也应区分无证据与否定，不能靠平滑标签制造“好看校准”。

需要警惕它 README headline 的对比：phishing Jev 为作者全 2,000 集，Luce 自测 500；tickets 两者也不是相同最终 test；不应转述为同题胜出。合成 task 优于零样本模型可能只是训练知道企业规则，而 baseline 没有规则。采纳真实人审回流、majority/shuffle baseline、正确目标和逐来源评测；teacher 输出许可及蒸馏条件单审。当前 API/LoRA implementation 不直接依赖。

### NanoJev：完整问题训练与独立轨迹验证

证据：[README 固定版本](https://github.com/TianyuCodings/NanoJev/blob/76fdfc9ecdca45a9bcef17991a07d3041a87685a/README.md)、[calibrated_objectives.py](https://github.com/TianyuCodings/NanoJev/blob/76fdfc9ecdca45a9bcef17991a07d3041a87685a/scripts/calibrated_objectives.py#L29)。

借鉴完整问题 CE、Choice set attention / Boolean sigmoid / Score expectation，train/dev/calibration/test/OOD 拆分、硬软目标配对及独立模拟器重放。动态候选 scorer 可用 C# 实验，比固定三行头更符合新 schema 和候选集合。其源码包含独立有放回动作采样的 paired Brier policy-gradient 候选，并明确不是已披露 TypeSafe 配方；适合后期做解析梯度与采样梯度对齐研究。当前 release 是 step-400 SFT 游戏模型，README 仍将 RLCD 后训练和共享前缀列为后续，不应把代码存在当训练支持已验收。

数字属于四个游戏与固定controller；maze大演示和held-out episode胜率并不是同一协议。对Sezika只采方法与数据审计规范，不能据游戏成绩推定中文文本泛化。MIT代码，Qwen与游戏数据/专家轨迹条款另审。

### Kev：优先继承和回归，避免每次从基座开始

证据：[README 固定版本](https://github.com/jaredpalmer/kev/blob/5e42a7a03f28134853dd3ff77461457e921e5ec1/README.md)。

当前版本已是 Qwen3.5/3.8 的 0.8B/4B/9B/27B，清单中早期 Qwen2.5 不是当前身份。可学：新域训练从已发布 adapter/head 继续；核对 base、revision、LoRA rank 和 head size；保存起点；用 new-source development 选 checkpoint，冻结 test 每个 release只读一次；旧能力回归防遗忘。其400条例子改善处于噪声内部，正好提醒Sezika八条dev不具备择优统计能力。

适合预先记录一个候选配置与固定模型选择预算，所有候选都用dev，最终test封存。27B与post-trained基座不是纯架构/规模对照；标签来源未知也影响公平比较。代码Apache，adapter/base和frozen suites分列许可。

### SemIf：性能与数值精度必须一起报告

证据：[canonical README 固定版本](https://github.com/TheoLeeCJ/SemIf-OpenJev/blob/23cf1f39fc9534fe81437200959b6dfc7106e45a/README.md)。

同模型、同state、同21题比较直接readout与compact JSON，是合理系统对照。作者公开prefix复用将777决策吞吐提高，但 BF16 fast路径相对fresh改变5至6个argmax；因此“数学形式等价”不足以证明实际精度质量等价。Sezika优化C# GPU算子时应记录logits误差、概率误差、argmax flip和低margin题回归，不止kernel耗时。

公开102-row与TypeSafe记录的对齐是published agreement，不是live Jev全711-row评测；temperature改善在某些来源区间重叠，在WANLI更明显。采纳分布变化和逐行可追溯报告，保留fp32 reference与bf16实际推理区分。MIT代码，所有upstream权重条款另管。

### jqv：共享prefix可以大幅加速，但先证隔离与等价

证据：[README 固定版本](https://github.com/Octalab-Inc/jqv/blob/40ac9b8ce4ed94d6442e4cf014bc3e6b2a028758/README.md)、[calibration.py](https://github.com/Octalab-Inc/jqv/blob/40ac9b8ce4ed94d6442e4cf014bc3e6b2a028758/jqv/calibration.py#L17)。

它把generate/naive/kvcache/packed/shared放在同模型同prompt比较，包含secret-code隔离负控、logits等价、option-order averaging，以及跨任务temperature失败，这是很好的实验范本。源码将温度拟合限制在有界log空间grid与40步golden-section，不用可发散的优化；metadata记录model/engine/prompt/dataset/dtype，实际check验证model与prompt并默认拒绝缺身份和不匹配。

Sezika还应在此基础上绑定**实际weights SHA、tokenizer hash、输入布局、head结构/版本、dtype和适用候选数**；仅model名字匹配不够。shared-prefix方法主要对decoder/Hydragen分支有效，不能不加证明照搬到双向mmBERT，因为state representations可能因候选上下文而变化。性能53至73倍依赖8k×100题大输入场景，不是普遍收益。Apache代码，可采测试方法，runtime重写。

### smalljev：head等变不等于整个模型无顺序偏差

证据：[README 固定版本](https://github.com/isHeSatoshi/smalljev/blob/65f74b49748f2663225a6fc68852c0a7d09fd0b9/README.md)、[heads.py](https://github.com/isHeSatoshi/smalljev/blob/65f74b49748f2663225a6fc68852c0a7d09fd0b9/smalljev/heads.py#L35)、[semantic.py](https://github.com/isHeSatoshi/smalljev/blob/65f74b49748f2663225a6fc68852c0a7d09fd0b9/smalljev/semantic.py#L1)。

确切token span由分别tokenize的pieces构造，池化option文本而不混A/B marker，shared linear scorer优于slot head是可实验的机制。源码head对输入feature顺序是等变；但causal prompt里各option的features会依赖此前option、letter和位置；代码也明确需shuffle augmentation去残余信号。不能照抄全模型无position bias的绝对宣称。

公开README的“rank6”是231公开题的作者私评、完整per-tier证据在私有位置，还承认一部分MASSIVE提升属同来源训练；清单另披露针对公开题型的hill-climbing。采纳头和确切span构造，不采未经独立rerun的名次。README自述Apache通用，GitHub识别NOASSERTION只是许可解析限制，复制前须核实际LICENSE及Hub。

### Verdict 2.0：两类概率目标应分开

证据：[README 固定版本](https://github.com/Heman10x-NGU/openJev-verdict-2.0/blob/bff28567cff463b833bf044f351a8b7945d53e07/README.md)。

151M ModernBERT+GLiClass 的Verdict与specialized Verdict2.0是两种模型，后者weights为LFS指针。1.44%ECE是OOF correctness-head结果；其distribution-head ECE另为0.1513，不能写“所有概率校准到1.44%”。可学习soft distribution vs empirical correctness的分离和训练期permutation-KL，但当前Sezika独立正确率标签太少，先补数据，避免再训练一个对8条dev过拟合的confidence MLP。

验收两个通道：分布拟合对独立真实soft targets或规范outcomes测NLL/Brier；正确率预测对未见hard outcomes测reliability/AUROC/risk-coverage，并按source+language分层。confidence训练使用OOF预测features，finaltest不参与。README关于20至25ms/77.1%均未复现，非三种基础任务所有场景胜出。代码标Apache，但权重/基座/soft标签分别核。

### Verdict：低成本头和预测集合可以先做

证据：[README 固定版本](https://github.com/Manavarya09/verdict/blob/76bc1fe9ba87ec5b746ed33faac48812f1d1a4ca/README.md)、[conformal.py](https://github.com/Manavarya09/verdict/blob/76bc1fe9ba87ec5b746ed33faac48812f1d1a4ca/src/verdict/calibration/conformal.py#L1)。

固定embedding的prototype/logistic头、独立1000校准slice、temperature、LAC/APS set和统计automation share很适合C#重写。源码明确coverage保证需calibration/test **exchangeable**，且为**marginal coverage**；不是每题正确率、承诺正确率、遇OOD仍有效或业务执行授权。未fit时源码返回top1集合，Sezika不应因此报告“已校准单元素集合”。本项目可以显式`Uncalibrated/Calibrated/Abstained/Unsupported`能力状态。

bi-encoder候选独立cache没有选项joint context，README承认结构化JSON字段组合、zero-shot Score/Noul弱点。不能为0.5ms batch数字舍弃语义主线，可在固定菜单routing试可选fast head，与mmBERT cross-encoder对照。Apache代码，e5模型和数据条款独立；ORT runtime不引入。

### CLM：借候选缓存和hard negatives，别借夸大的任务结果

证据：[README 固定版本](https://github.com/Contrastive-LM/CLM/blob/d5f9ef0fd9bde185df0ceaad4f4ecc6cfe8c34f6/README.md)。

双向InfoNCE与hard negatives、frozen LLM encoder+state/action projection heads、重复候选cache对工具菜单/固定队列有效；可以用现有mmBERT实验小projection，而非先迁8B与60M/30M/1M规模配方。缓存key应包含权重、tokenizer、question、候选完整语义、dtype、截断策略及head版本；冷cache和热cache分别测。

Terminal-Bench87.6%属于30个held-out任务上**强生成器产多个解+CLM验证器**组合，并非CLM独立解决终端任务或整个benchmark官方分数；DeepSWE81.6%是31/38。不能迁移成Sezika编码任务能力。双编码器将状态和action分离可能损失字段组合与细粒度关系，应在未见instructions/negation/相似候选hard集验证。Apache代码，head weights、Qwen以及Nemotron/synthetic轨迹条款分别核。

### Jevlike / JevForge / minojev / Bekko：可实现的小头、边界和规模账本

Jevlike 的 option-query attend context→shared score 适合C#，shuffled-context control尤其重要；它明确byte encoder弱、Doom/chess demo为挑选片段，不宣称同Jev。证据：[固定 README](https://github.com/vinnylarouge/jevlike/blob/94f5fd1b0b11d52bbdfdf4e0ee6aa96b568f8452/README.md)。

JevForge 的website-disjoint数据、K candidate rows、相同两层GELU scorer、question内归一及CE/Brier/RLCD实验账本可以采纳。raw baseline只首200，其他fullsplit，比较时应限制成同题；RLCD自报0.5787→0.5863不是足以立刻换训练目标的证据。证据：[固定 README](https://github.com/zwliJay/jev-forge/blob/6cf0dfe3b811d623331c0cc8b1c00ff13614772e/README.md)。

minojev 当前README已更新主打冻结Qwen3-1.7B、缓存候选features、约0.8M shared scorer+set attention；清单547k从头模型仅旧/旁支。其OOD31.7%低于生成baseline40%是非常相关的警告：冻结小头专训的高ID分不能代表新的decision能力。证据：[固定 README](https://github.com/zeredy879/minojev/blob/622dd952c3db2769a7a32d1a13cc89db04bba95e/README.md)。

Bekko的17/68/400M阶梯与shared-prefix候选结构值得后期试，权重协议与源码实现需另审。它明确137 benchmark聚合TaskAvg是**相对baseline的0至100得分，不是accuracy**；训练eval共享77subset名称不等于重复test但会混合已见来源，六种adaptation测试显示泛化大差距。采纳分层而不照搬winner标题。证据：[固定 README](https://github.com/hotchpotch/bekko-system-one/blob/0fccbb8568b67d47745d820319fe9a4a04e7fa95/README.md)。

## 可执行的提升顺序与验收建议

以下为建议的阶段和预算设计，不是本轮已经实施的训练/校准。固定 768 步诊断已结束，仓库证据明确新增训练与其他 ROADMAP 工作仍暂停；这些新架构/数据实验等待用户后续指令并先登记唯一 ROADMAP。本轮只是分析，不启动训练、采集、调参或修改 Sezika 核心。

1. **先建立独立质量pilot。** 首批约200条人工核查或可靠公共gold的独立记录，覆盖Choice/Boolean/Score、中文/英文及一个业务需要的第三语言；含否定、证据缺失、counterfactual pairs、结构化字段与unknown。保留教师分布，但标签agreement与真实正确率分开。不能把教师自评当oracle；人工与程序核查一致后再扩到约1000至3000条，并记录许可、PII处理、来源家族与时间。来源、近重复与反事实家族整组分到train/dev/calibration/test/OOD；同一项目/文档也不跨split。train为GPU批量feature计算与学习，CPU仅小数值参考。
2. **用现有mmBERT做受控head实验。** 现有三行线性head为baseline；加入共享span linear、`[q,o,q*o]`两层GELU、option-query attention三个候选。固定data、feature cache身份、seed和step预算，至少三seed的head训练用于检查方差。首先完成tiny输入梯度有限差分/CPU-reference，GPU forward/backward单独验收，再试真实dev提升；head过拟合小train只说明mechanics。关注NLL/Brier与逐task accuracy，不能只追train loss。
3. **冻结真正OOD检查。** 未见policy/instructions、候选数2/5/20/77、候选同义改写/顺序交换、加入无关/高度相似候选、跨语言input-label组合、长输入关键事实位置、多题兄弟信息隔离。随机与多数类、shuffled context/question及固定原模型对照一起跑。需证明模型听问题而非记slot；新增能力同时保持旧domain质量。
4. **独立校准再定义拒答。** calibration仅用于temperature/conformal；test仅用于最终报告。有限独立数据先global或per-primitive，不照抄Laya的2000样本bucket门槛或在稀疏language×K格拟合。报告样本量、bin定义、NLL/Brier/ECE和bootstrap区间；用risk-coverage说明按阈值保留比例与错误率。模型换权重、head、tokenizer、layout或精度时旧校准失效。数据漂移/OOD不继承coverage承诺。
5. **新权重重新AOT验收再优化。** CPU/CUDA logits、distribution、argmax与Score期望一致；原权重既有AOT证据不能自动覆盖新head/输入布局。计量真实warm/cold end-to-end、tokenize/transfer/kernel/readout、p50/p95、显存、输入长度和候选数，不与不同GPU/网络latency混比。共享state/prefix只有在等价、隔离和实际收益成立后做；不要把延迟的单个极端配置宣传成通用性能。
6. **最后研究更广训练。** 只有GPU更强head已改善且oracle/test成熟后，实验低秩adapter或encoder逐层反传；逐算子确认前向、梯度、有限精度和AOT。用同预算CE/Brier/RLCD对照，而非默认RLCD必要。结构性的候选预算/语义失败可能比loss选择更重要。27B全权重、视觉输入和微控制器部署暂排后，集中获得可证的中文及业务decision质量。

建议发布的能力账本包含：checkpoint+weights SHA、tokenizer版本、C#算子/训练支持矩阵、dataset/license/split manifest、独立oracle定义、frozen test hash、所有被尝试配置与失败、calibration身份/适用范围、逐语言逐类型质量以及新权重NativeAOT报告。Sezika仍返回决策建议，应用层完成权限与业务动作执行。

## 证据与执行清理

20个README快照、Laya完整README、4个仓库tree响应和8个源码固定版本位于本研究目录`models-owned`，作为可复核证据保留，没有把权重或大仓库clone到机器。所有互联网请求走单次调用的本地proxy、25秒超时、每URL仅一次尝试；没有训练、构建、模型下载、运行第三方脚本、递归磁盘扫描或Graphify。本组仅使用PowerShell7（7.6.6）和GitHub公开raw/API读取；短期PowerShell工具进程已经退出，未创建持续运行服务。记录文件包括`models-owned/process.json`和`completion-process.json`。生成决策循环上限67、30秒deadline，已输出恰好67条；源码文件只作文本证据，不执行。工具会话53857和39136均已exit0；没有需要删除的下载/模型/独占服务，研究快照是交付证据而非待清临时文件。
