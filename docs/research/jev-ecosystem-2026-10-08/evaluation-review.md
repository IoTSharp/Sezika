# Sezika：41 个评测项目的取舍与能力提升

研究日期：2026-10-08。全部 41 个分配仓库已逐项初筛，具体取舍见 `evaluation-decisions.json`。重点读了 17 个分配仓库的 README，以及跨组 `fstandhartinger/jevbench` 的 README；进一步审了 16 个固定 commit 的源码/协议文件，其中 10 个分配仓库有源码级核查。README 快照最多保留前 20,000 字符：JevBench、robustness、orderby 是重点片段阅读，不能称全文代码审计。没有下载数据/模型、执行外部项目、调用模型或复现任何 benchmark。

**最应采纳的是评测纪律和数据方法，而不是另建一套评测工具或借用竞品分数。** Sezika 已有数据隔离、人工审核入口、校准评估、开发切片、数值对齐、训练与资产身份工具；缺口是具备授权与真人审核的数据、独立封存测试，以及新训练权重在这些数据上的有效收益。最新已完成同32个教师标签的768次GPU更新与独立磁盘加载：train教师一致17/24、dev1/8，dev CE从初始化1.26879升为1.27403；训练Choice原6/8，换序4/8，6/8预测ID改变。它证明工程训练闭环，未证明能力提升，且当前位置敏感。本轮已停止新增训练。PAWS 250 / Nimble 324 已看过，只能继续 audit；其中 Boolean 负类召回 1/57 是历史审计的优先诊断目标，不应用总体准确率掩盖。最新依据为 `docs/evidence/gpu-learnability-2026-10-08.md`；旧72步结果作为历史，不混用新模型身份。

## 优先级与既有路线的连接

| 优先级 | 借鉴对象 | 在 Sezika 采用什么 | 接续已有任务 | 能证明什么 |
|---|---|---|---|---|
| P0 | phishing-bench、spam-eval、judgekit | 人工金标、同证据公平基线、家族隔离、反事实配对；明确已看错误后的问法优化 | S4-05、S4-11 | 数据可信与开发方法，尚非封存质量 |
| P0 | zh-decision-bench、acento、robustness | 否定/角色/词面相似、候选顺序与名称、简繁/语言、不可回答/OOD切片 | S4-06 | 找到错误机制与弱类，不以易题总分替代 |
| P0 | jevcompat | 概率归一、argmax、Score=Σi·p、问题ID/顺序/邻题不变量、错误分母 | S3 输入合同、S4-15；外部兼容层 S6-03 | 合同与数学一致，不能证明语义正确或校准 |
| P0 | dspy-lab、jevcal、assay、ood-calibration | 模型×题型×任务×语言的校准绑定；独立拟合/选阈值/确认；风险覆盖与漂移锁 | S4-03、S4-08、S4-12 | 在明确有效域内可接受的概率/拒答，不能跨模型搬阈值 |
| P0 | orderby-bench、S1MB | Score归一MAE与排序质量分开，query内反序、相关性、ties；Boolean balanced accuracy/负类召回 | S4-06、S4-08 | 给对了等级、排对了顺序、判对了负类分别验收 |
| P1 | JevBench、S1MB、DecisionBench | 固定版本的公开外部 audit adapter；公开榜只做广度诊断，另保留未曝光封存题 | S4-08；通过质量后 S6-02 | 外部可比较结果，不能证明训练未见或独立泛化 |
| P1 | medhallu、WindTunnel、fanout-bench | 去证据消融、端到端结果、同输入批/单差异、完整级联成本与失效率 | S4-08、S5-06、S5-07、S6-03 | 模型及真实调用工作流的边界；核心不加入执行器 |

## 如何提升能力

1. **先把错误变成可审核数据。** 复用 32 题人工审核表，真人独立判断，不把教师答案当真值，不自动改为 approved。这批数据已经暴露教师答案、没有 sealed 资格，适合实验训练和错误诊断。新增原始样本以业务实体、模板/家族、时间为单位分 train/development/calibration/sealed_test；中英翻译、简繁改写和反事实共享父链，不能分散到不同 split。数据家族数和题目数同时报告，避免千条模板改写冒充千条独立证据。
2. **先验证学得到，再验证学得对。** 已有72/768步及初始化、训练后、磁盘加载的完整CE和候选换序证据，不必重跑同24题诊断。下一次方案先依据这些证据定位位置捷径、输入表示、标签和梯度问题；本轮停止新增训练，不自动扩量或调dev选超参。真实人审开发集形成后，可另行设计固定基线、仅人工标签与可选教师硬标签的同预算消融。encoder LoRA/全量反传归 S4-13，只有 head 无法学到任务且数据证据支持时才考虑扩架构，不能仅因8条dev差就判定encoder不行。
3. **对负类和困难关系增加有效监督。** Boolean 必须包含“含某个词但命题不成立”“引用/辟谣”“双重否定”“缺少证据”“角色互换”等困难负类；Choice 包含语义近似候选和 None/unknown 情况；Score rubric 给出每级可检验边界而非宽泛“好/差”。配对同时保证标签翻转与不翻转两类，并由人审确认，不能按规则直接伪造语言质量金标。
4. **学习不变量，但不强行制造概率等价。** 保持 option ID 与描述对应关系，测试候选反向/循环旋转/明确 seed 的随机顺序，再对齐回语义标签；记录 flip、概率 TV、正确答案概率及首位偏好。问题ID改名、问题重排和邻题增减是合同探针。Boolean与Choice(2)、肯定与否定互补是诊断探针，不能未经训练与校准就要求两种原语内部概率完全一样。
5. **校准不代替能力。** 温度缩放可以改善概率可靠性，不能补回 Boolean 负类召回或修复错误排序。复用现有 S4-08 评估器，分别报告 accuracy/macro-F1/balanced accuracy/负类召回、AUROC/PR、NLL/Brier/ECE与各bin样本数；Choice全分布Brier、Boolean事件Brier和“被选答案是否正确”的二元Brier不可混称。Score增补归一MAE、等级混淆及query内排序指标时扩现有报告，避免另写重复框架。ECE必须附样本量/置信区间、bin规则；可加入噪声底作为诊断，不能单凭“接近噪声底”发布校准保证。
6. **把拒答作为可验证产品能力。** 拒答原因区分 invalid_input、length_rejected、unsupported、deadline/resource_failure、uncertain、out_of_domain、missing_evidence。输出 `uncalibrated` 时不把集中度解释成真实正确率。加入隐藏随机事件、缺失组织政策、无正确候选与与训练场景不同的 OOD，测风险覆盖曲线、接受样本准确率、OOD误接受率及各失败分母。可选 unknown 候选只是模型标签，不能等同外部拒答门槛。
7. **冻结阈值后才确认自动化收益。** calibration只拟合，development只选策略，sealed_test一次确认；不断在同“heldout”上挑改写/阈值也会过拟合。按任务/题型/语言及足够样本量建立 profile，不照搬0.7/0.8/0.9。98%接受准确率目标在零错误的理想情形也至少需要149个独立接受样本，才能达到95%单侧精确下界；这是一个固定阈值的样本量示例，多重选择、家族相关及 OOD 还需更多证据。32条实验远不足以证明。
8. **质量先于速度宣传。** 固定相同请求、模型/权重/hash、tokenizer、长度政策与阈值，测本地同硬件端到端 p50/p95、冷/热、1/8/32题、内存/显存、失败、实际 entered/returned 和清理。云API网络往返、列表价格及经验延迟调整不属于本地推理速度。多问题共享state的API计费优势也不证明cross-encoder可无损共享编码；多行拼一个state可能改变排序和语义，必须进入 S5-07 的质量回归。

## 值得注意的源码与证据细节

- **phishing-bench：** `bench/protocol.py` 确实把选择与评价分A/B两半，阈值/单信号选择及回归只用A；但划分仅按标签分层，没有 domain/template 家族隔离。95%是五信号加外部回归的结果，不能称模型直接判别95%；简易规则与同五信号LLM对照揭示了数据建设本身的捷径。采纳消融与公平对照，再加家族隔离。
- **spam-eval：** `regression.py` 使用 `StratifiedGroupKFold`、断言家族不重叠，并重构hash检查同证据输入；聚类基于旧文本，新增metadata关系仍可能跨fold。当前README主集5733、fresh3300、recent853；fresh并非时间新邮件，recent仅钓鱼无负类，旧2026邮件实验另保留。“看过错题写定义”是开发反馈，不能称未监督或独立封存。保留此限制才能公平学习。
- **judgekit：** 源码只传 `row["text"]` 给模型；当前README承认127/130是历史v1，v2只重新测rules107/130。spam120的82/120是在同集挑阈值，没有holdout。应学习修订和负面结果披露，避免重复历史97.7%宣传。
- **dspy-lab：** 校准资产确实校验请求hash、模型指纹、split完整性与重复，Platt拟合目标是“预测是否正确”；其 `_calibration_metrics` 在接受样本上算正确性指标，需要与全量事件概率质量分开。Sezika现有工具已有全量/拒答分母，不应回退为只报告高置信子集。
- **zh-decision-bench：** 候选概率对齐回canonical标签后测flip/TV的方法可用；`random.Random(hash(it["id"]) & 0xFFFF)`受Python进程hash salt影响，不能跨进程稳定复现。应使用显式seed/SHA和冻结permutation manifest。单人复核、小业务组及公开金标仍仅audit；CC BY4.0数据与Apache代码分别审核。
- **orderby-bench：** 当前 `pairwise_inversions` 将预测tie计为半反序，另报告 `sort_key_resolution`、Top-k切在tie组内和query内排序。README中旧“tie被忽略”的解释与当前实现需按版本分开。低ECE可以排序差，高ECE也可能排序好；仅温度校准不能承诺重排收益。
- **ood-calibration：** `endpoint_refit.py` 明确对1e-6、0.005、0.01三种零概率floor重拟合，说明原温度大小受返回值量化支配。保留raw logits/概率、量化与finite检查，不照抄某个T；作者README中“新seed绝无污染”也不能排除同模板家族被见过。隐藏政策是missing-evidence探针，不是模型应知道的规则。
- **does-not-play-dice：** 分析代码校验资产hash、隐藏结果、请求候选、argmax和缺失Noul记录；forecast reference明确标为 `stated_probability` 而非真实事件频率。骰子失败只能说明探针中的概率不确定性不诚实，不能证明所有语义任务无效。
- **assay-001：** 评分源码分别检验类型/归一和概率校准，并提供独立复算；不同任务校准结论相反。概率和合法JSON应分成两张验收表，Sezika不能把合同全通过当质量通过。
- **acento：** 当前 `PREREG.md` 登记2026-09-21并固定A/B/C、model、seed和指标；README未冻结段落是陈旧内容。已核协议正文，未核Git先后和hashmanifest，所以只学习配对方法，不宣称本轮独立证明其预注册先后。英文instructions优于西语也不能推广到 Sezika。
- **S1MB/JevBench：** S1MB `SCORING.md`承认整benchmark计算的oracle常量baseline及Borda排名随参赛模型变化，v1不提供CI；应取其原始指标与覆盖方法，不继承榜单权重。JevBench v1.4使用308封存题、公开只给aggregate，并记录operator API接触题文的暴露；保密题不发送金标仍然不是永不泄漏。源码v1.3有×2/+0.15秒延迟调整，那是评分假设，不是生产负载实测。Nimble提高长度上限后准确率、价格及网络条件同时变动，正说明必须保留长度拒绝分母并固定环境，而非套用综合名次。

## 应暂缓采纳的方向

暂缓把浏览器/Pong/会议转录/调查/产品网页点评当核心能力目标；暂缓把模型judge替代确定性代码测试、人审或动作授权；暂缓把关键词规则、远端API、Python/PyTorch/ONNX运行时导入 Sezika 核心；暂缓以公开榜单训练/调参后宣称未见泛化。现阶段少量有授权、经人审、按家族隔离的任务，比堆几十个集成更有价值。

最终验收应包含可复算数据/模型身份、新模型对初始化及简单基线的真实开发收益、独立封存表现、负类/OOD失效、分语言校准和风险覆盖率，以及相同权重的CPU/CUDA/AOT对齐与端到端资源画像。所需机制多数已实现，下一阶段重点是完成数据与实测闭环。

## 证据身份与执行清理

证据文件与固定commit链接附在下面。网络只读取GitHub，直连失败后逐次使用本地7890代理；请求最多2次、25秒/次。树查询10仓库、源码/协议16文件，读取与查询均有数量和墙钟界限及owned取消文件。使用PowerShell7.6.6与已明确的bundled Python，所有长命令记录PID、创建时间、完整命令、父链、结束和finally清理。未安装工具、未构建、未训练、未占GPU、未启动MCP或服务。`evaluation-owned`内快照/源码/脚本/进程JSON保留为本报告研究证据，不删除共享cache、用户文件或交付物；最终进程核对结果记录在 `evaluation-owned/cleanup.json`。

| 仓库 | 核查文件（固定commit） |
| --- | --- |
| mandu5/jevcompat | [src/jevcompat/checks.py](https://github.com/mandu5/jevcompat/blob/7635bacaa5b033651c2421379f5c78d8ae3a84ce/src/jevcompat/checks.py) |
| anisselbd/jev-phishing-bench | [bench/protocol.py](https://github.com/anisselbd/jev-phishing-bench/blob/1d56e8c64d029a9554a0874e2ef2901ed196e230/bench/protocol.py) |
| bitnovus/jev-spam-eval | [experiments/jev-context/regression.py](https://github.com/bitnovus/jev-spam-eval/blob/a3757a1b3412177a686c1b4d95227b50a8f73537/experiments/jev-context/regression.py) |
| jmanhype/jev-dspy-lab | [src/jev_dspy_lab/calibration.py](https://github.com/jmanhype/jev-dspy-lab/blob/b65a738c9da2e192197eb65c5af4405ac81b38df/src/jev_dspy_lab/calibration.py) |
| lexingtonhibiki/judgekit | [benchmarks/run_bench.py](https://github.com/lexingtonhibiki/judgekit/blob/f59764d4820acb5ad9c442beb968841de3149fb3/benchmarks/run_bench.py) |
| CodyQin/zh-decision-bench | [src/permute.py](https://github.com/CodyQin/zh-decision-bench/blob/5fd127f2e70b0f79bb4906a3a191dd20d04a82fa/src/permute.py) |
| scienthoon/jev-ood-calibration | [scripts/endpoint_refit.py](https://github.com/scienthoon/jev-ood-calibration/blob/48572305b384b53072bfc869de4420c5d9a6acf1/scripts/endpoint_refit.py) |
| KantaHayashiAI/jev-does-not-play-dice | [scripts/analyze.py](https://github.com/KantaHayashiAI/jev-does-not-play-dice/blob/2ef26b648cac22a4c9d6e38c656f254fbac2aec5/scripts/analyze.py) |
| yodablocks/jev-orderby-bench | [harness/metrics.py](https://github.com/yodablocks/jev-orderby-bench/blob/523979540d0fad2786208ba07cc8d0fe2711f9ed/harness/metrics.py) |
| fstandhartinger/jevbench | [jevbench/composite_v13.py](https://github.com/fstandhartinger/jevbench/blob/bb05a335bc809e61b20c0f745d25499a82b326fc/jevbench/composite_v13.py) |
| fstandhartinger/jevbench | [docs/METHOD-v1.4.md](https://github.com/fstandhartinger/jevbench/blob/bb05a335bc809e61b20c0f745d25499a82b326fc/docs/METHOD-v1.4.md) |
| hotchpotch/S1MB | [evaluator/SCORING.md](https://github.com/hotchpotch/S1MB/blob/be3492e262100a8f3192f6241a9486b844752b95/evaluator/SCORING.md) |
| stperic/jev-medhallu-benchmark | [bench/datasets/medhallu/PREREGISTRATION.md](https://github.com/stperic/jev-medhallu-benchmark/blob/8a7f2f88eb258fd0bc859ed4aeca3b80643e5d94/bench/datasets/medhallu/PREREGISTRATION.md) |
| jourdanlabs/assay-001 | [harness/score.py](https://github.com/jourdanlabs/assay-001/blob/b7f71053a88025a5af80af00801c5ce725c9547d/harness/score.py) |
| marcosmartinez/jev-acento | [PREREG.md](https://github.com/marcosmartinez/jev-acento/blob/7e007b4c2bd552471b56eff035f9a3df7593f9fe/PREREG.md) |
| AbdelStark/jev-benchmarks | [docs/PROTOCOL.md](https://github.com/AbdelStark/jev-benchmarks/blob/0d610cc53e79bcbec691312b0c4adb4a0e371642/docs/PROTOCOL.md) |
