# 公开决策数据搜集记录（2026-10-10 启动，2026-10-11 更新）

本轮目标是为 Sezika 完整收集公开决策数据，保留题文、标签、原始 split、来源和派生关系，随后将具备再分发条件的来源上传模力方舟。用户最后指定目标为 maikebing 账户下的 public-decision-data 仓库，替代最初的 IoTSharp 组织。下载体积和文件数量不设人为覆盖上限；每个执行批次有时间、取消和进程清理边界，未完成文件可接续。

2026-10-11 已核对最后下载批次：75 个选定来源中 74 个完整，最终仅缺访问授权尚未批准的 Salesforce xLAM 文件。本地成功清单为 3,492 个文件、34,375,333,787 字节；统计到的 37,870,718 物理行含重复表示和派生重叠，不能解释为独立训练题数。

模力方舟 `maikebing/public-decision-data` 已公开上传 18 个经过逐源条件审查的来源，共 275 个资产、1,076,726,933 字节。225 条 LFS 路径的实际内容及所有普通资产均已匿名回读，逐项长度与 SHA256 校验通过；平台 missing-lfs 为 0。固定数据提交 `b8ea13b81aaaca722baf6a3c0f12e2217fc4353e`，回执提交 `08f1fdad70add4e0dd442ec3c796242df596c669`。后者只改变发布回执，数据和清单与已验收提交一致。

详见 [发布回执](publication-receipt.json)、[固定版本下载说明](download-guide.md) 和 [接续核验记录](continuation-review-2026-10-11.md)。完整母语料、未知或混合组件条件的来源仍保留本地。此次发布不代表整份本地搜集目录已公开，也不代表正式训练准入、去重和评测污染检查通过。

原始文件位于 `C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10`。D 盘剩余空间不足以保存选定资产，因此使用 C 盘独立数据目录。研究目录不包含令牌值。

## 目录与证据

- [发现目录](discovery-catalog.csv)：平台检索记录与来源 URL；搜索命中不等于训练准入。
- [来源目录](source-catalog.json)：重点来源的固定 revision、元数据 SHA、许可声明和待核验项。
- [下载文件清单](download-inventory.csv)、[统计快照](collection-summary.json)：已下载文件、字节数和物理行数。
- [未成功事件](failed-and-pending-attempts.json)：历史失败或接续事件；某次失败不一定代表最终缺失。
- [训练优先级与使用边界](training-priorities.json)：监督类型、评测隔离和来源优先级。
- 下载计划、逐文件 ledger、批次回执位于 C 盘数据根；进程身份、日志和清理回执位于 `.artifacts/public-decision-data-20261010/processes`。

所有数量按文件内标明的 UTC 快照时间解释。Parquet footer 行数和非空 JSONL 行数包含镜像、聚合配置及派生重复，不代表去重题目数、人工审题量或正式训练样本量。

## 关键来源

| 来源 | 核验结果 | 计划用途 |
| --- | --- | --- |
| [S1MB](https://huggingface.co/datasets/hotchpotch/s1mb-dataset) | 固定 revision 的 manifest 为 110 个 evaluation 子集、15,609 cases、29,469 decisions；train 为空。137 benchmarks 属于另一统计口径，不能替代文件实测。 | 独立评测，逐源许可核验后再分发。 |
| [Sys1Cal-v1](https://github.com/little-g-ai/Sys1Cal-v1) | binary 与 parallel-primitives 各声明 365 个渲染样本；两者共享潜在问题，不能当成 730 个独立问题。精确 gold probability 来自构造。 | 概率恢复与校准评测；latent_instance_id 家族隔离，独立数据许可待确认。 |
| [Byrne-Jev-79M](https://huggingface.co/Quazim0t0/Byrne-Jev-79M) | 正确发布者为 Quazim0t0，这是模型仓库；本轮只保留模型卡和元数据，未下载权重。 | 查找评测来源，不能算新增题库。 |
| [Jev Decisions v1](https://huggingface.co/datasets/samatv256/jev-decisions-v1) | 发布清单声明 11,978,080 records；train 11,269,408，validation 354,037，test 354,635，另有 CLEAN-50K 派生子集。 | 依据 training.choice_eligible/value_eligible/completion_eligible 选择监督；逐个遵守 SOURCE_LICENSES。 |
| [Jev-bench](https://huggingface.co/datasets/Praveenrajus/jev-bench) | 22 个来源配置；声明 train 133,953、validation 9,328、test 22,773。来源许可混合。 | 保留上游 split；完成与 S1MB、上游测试集的污染检查前隔离。 |
| [Typed Decisions](https://huggingface.co/datasets/LocalLLaMA/typed-decisions) | 合成工作流、typed probability distribution；各领域配置与 all 配置存在聚合重复。 | 原始 train 候选；test 独立评测。 |
| Banking77 / MASSIVE / CLINC | 已取得上游官方文件；意图、场景、OOS 标签可转为有限候选决策。 | 优先原始训练划分，保留语言和候选标签。 |
| HelpSteer / HelpSteer2 | 人类评分来源；评分维度、等级和偏好必须保留原语义。 | Score/Choice 候选；评分不能直接当成已校准正确概率。 |

其他候选包含 Open-Jev、JevEmbed-Data、NanoJev-Data、bekko、中文 CLUE/C-MTEB、多语言 NLI/匹配、情绪、安全和偏好数据，完整列表见来源目录。

## 使用与上传约束

1. 来源、数据、模型、tokenizer、校准文件分别管理许可和版本。HF 的 license 标签只是声明；上游条件、署名、NOTICE、非商业或 share-alike 义务仍分别记录。
2. 评测、校准和训练分开保存，镜像与 CLEAN-50K 等派生集合按原始来源/trajectory/task 家族去重和隔离。文件名含 train 不足以证明不存在评测污染。
3. 保留原始 state.text、标签、ID、split、语言与 family；转换或排除用独立派生 ledger，不覆盖原文件。
4. 无现成决策标签的 Discofuse 生成语料按任务相关性排除，非容量原因。已保存来源声明；若以后设计明确的决策监督，可另建完整收集阶段。
5. 可上传资产按逐源许可打包；未知或受限资产留在本地待核验。不得给整个混合语料套单一宽松许可。
6. 本轮未执行正式训练、批量教师调用或批量特征计算，未宣称模型质量、概率校准或训练准入通过。

GitHub 令牌已验证为 maikebing，并安全保存在 Windows Credential Manager 的 Codex:GitHub:maikebing。模力方舟仓库通过专用网页登录会话创建，数据集 ID 46105，底层仓库 ID 50739386，maikebing/public-decision-data，public=true，默认分支 master。首个表单提交返回 200，但监测代码等待了错误的接口路径而超时；随后只读查询确认实际创建成功，没有重复提交。保存的 Gitee Git 凭据已核身份为 maikebing。官方 gai 回调返回 true 但对象仍缺失，原失败回执保留；标准 Git LFS 完成上传，实际提交、缺失数和匿名完整回读均通过后，已审查公开子集的 moark_published 才改为 true。密钥值不进入文档、数据或上传包。

## 当前 C# 入口与历史配方

当前调用合同见[工具工作流](../../tooling-current-workflow.md)。原脚本、原始下载/发布回执和当时失败均保留为历史证据，不再执行旧配方。下列入口承接显式输入的当前职责；不自动重放本目录的账号、日期、公开可见性或旧授权，实际模型/数据发布仍须当轮明确范围。

| 当前工作 | C# 入口与边界 |
| --- | --- |
| 固定元数据生成完整计划 | `corpus research`；保留requested/canonical身份、固定revision、split和未知摘要 |
| 多来源采集、缓存及Range恢复 | `source acquire`；固定metadata/plan SHA，区分SHA256与Git blob SHA1，保留全部计划和partial/失败分母 |
| 来源完整性与ledger统计 | `corpus research-check`；消费采集产生的research-download-plan与source-ledger，独立核实际hash及原digest |
| Sys1Cal/S1MB冻结种子检查 | `corpus seed-check`；核题库结构、声明分母及跨primitive一致性 |
| 审查包构造与独立读回 | `transport prepare/verify/reassemble`、`publication prepare/preview`；完整资产与逐源许可另核，不推定未知许可可发布 |
| 已授权的Git/LFS运输 | `git run`；固定父提交、准确所属路径、原生pre-push与fresh内容读回 |
| 真实平台提交 | `publication upload/commit`；绑定当前授权、manifest、private/public声明和独立读回；本次迁移仅验协议fixture |

固定历史发布回执生成和旧数据集创建配方已撤销重放职责，不宣称上表复现其全部历史语义。来源许可、准入、教师、人审、质量和公开发布分别验收。
本轮发现记录覆盖 GitHub API、Hugging Face 名称检索/仓库元数据、模力方舟三个相关类别的全部已见分页及普通网页搜索。Google 返回 JavaScript/challenge 页面，未作为已审核搜索结果计数。此记录不宣称全网穷尽。
