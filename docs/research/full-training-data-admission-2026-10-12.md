# 全量训练来源范围与私有实验准入复核（2026-10-12）

本报告只复核已组合的 18 个来源。母集合的全部 75 来源不因下载完成而自动获准训练。本轮没有读取全量题目、运行 GPU、调用教师、下载新资产或改写原始语料、历史 rights 和既有报告。

## 固定范围与仍待完成的门槛

组合 `combined-corpus-v1` 的 manifest SHA 为 `8421a5adb63e79f752865edecff49db653a51eb82f37fac4aa0ffa4c062dcabe`。完整结构回执记录 16,276,841 个物理规范记录、15,337 个唯一排除，剩余 train 14,637,199、development 1,624,305，提供的 family 共 2,411,660。记录数包括同一原问题的多个属性问题，不能当独立题目数。

194 条 rights 是组合保存的历史组件合同条目，不是 194 个来源，也不是统一许可。四个组件的原合同仍按原 bytes/SHA 保留。ARC 的旧 pending 记录与后续 root-02 私有整理合同同时存在；后者仅覆盖绑定的原 train 派生，不能据此统一混合语料许可或批准其他 ARC 数据。

可立即固定的私有实验候选范围是 **17 来源、34 个固定原始资产**。暂隔离整个 `samatv256/jev-decisions-v1` 后，长度及进一步质量排除前的可结构消费分母为 train **14,592,758**、development **1,619,175**，合计 **16,211,933**。这不是已经完成本轮正式训练准入的数量。完整 tokenizer 长度、逐行合同、GPU 训练及恢复仍由主任务独立验收。

私有机器策略位于：

`C:\Users\mysti\.codex\artifacts\sezika-full-training-20261012-01a12b4c\source-admission-v1\private-experiment-source-policy.json`

最终策略 SHA：`57dfc53a758ad32d7ebcc0cb3b426a80e7da40e6f24be01d1bf7bb13b31655ed`。

策略采用默认拒绝，明确列出 Source.Dataset、Source.Path、原始 FileSha256、原资产 SourceRevision；卡片 commit 另存为 CardMetadataRevision。MASSIVE 官方 release 1.1、Banking77/CLINC 的内容冻结 upstream master 载体不能冒称 HF 卡片 commit。初版误用卡片 revision 的三项已纠正，初版策略和失败说明另存，不覆盖历史来源合同。

## 逐来源建议

下表数量来自已完成的 C# 结构回执，包含派生 train 和 development，已经扣除原冲突排除；下表的“候选”均须通过后续长度与逐行 gate。许可证列仅记录冻结声明与本轮观察到的适用边界，不表示独立权利审计完成。

| 来源 | 可结构消费记录 | 本次选择与 family | 声明及实验边界 |
| --- | ---: | --- | --- |
| AmazonScience/massive | 1,172,388 | 原归档中 partition=train；原 id 跨语言及 scenario/intent 共 family | CC BY 4.0；候选。Intent 是 gold 加最多 31 负候选的派生任务，不代表原全类基准 |
| Anthropic/hh-rlhf | 160,305 | harmless-base、helpful-base、helpful-online、helpful-rejection-sampled 四个 train；共同对话前缀 family | MIT；仅偏好 Choice 候选。原卡明确偏好数据用途；red-team transcripts 与对话生成监督不在本范围 |
| LocalLLaMA/typed-decisions | 6,000 | 四个原 train 配置；排除 all 聚合；原 id 将各子问题共 family | Apache 2.0；候选，保留上游合成 soft target，不能升级为人工正确率 |
| PolyAI/banking77 | 10,003 | 固定 upstream train.csv；同输入 state family | CC BY 4.0；派生 intent Choice 候选，最多 32 候选，不冒称 77 全类分类验收 |
| Team-ACE/ToolACE | 5,208 | 固定原 unsplit data.json；首段会话 family；仅明确单工具参考调用 | Apache 2.0；候选。显式派生 family holdout；不承诺原生 test 隔离、参数生成或多工具调用 |
| ZefanCai/Open-Jev | 158,169 | 仅下节列出的十个原 train Parquet；group_id/document_family_id/state family | 生成内容 CC0 1.0；仅固定十配置候选，拒绝未知路径及未审上游组件 |
| allenai/ai2_arc | 3,369 | Challenge/Easy 原 train；官方 id 或规范题干连通 family | CC BY-SA 4.0 冻结声明；root-02 私有整理候选，保留归因、变更、同许可 notice；卡片许可正文仍有信息不足标记 |
| allenai/quartz | 2,672 | 原 train；para_id/id family；冲突账本生效 | CC BY 4.0；候选，原 validation/test 保留 |
| clinc/clinc_oos | 15,100 | 固定 data_full.json 的 train+oos_train；state family | CC BY 3.0 冻结声明，卡片许可正文信息不足；仅派生 gold+负候选 Choice 候选，其他 split 不用作监督 |
| dwidlee/systemone-lite-phase2 | 168,760 | 固定原 train；state family；只转换明确 Choice；恢复部分仍用同分组及排除合同 | Apache 2.0；候选。Stockfish 与合成参考标签分别保留，不能当独立教师或人工 gold |
| google-research-datasets/go_emotions | 1,210,237 | simplified 原 train；原 id 将每情绪 Boolean 共 family | Apache 2.0；候选，raw/其他 split 不进入本范围；属性展开不当独立问题增长 |
| google/boolq | 9,427 | 原 train；passage family | CC BY-SA 3.0；候选；原许可、归因与共享条件分别保留 |
| google/civil_comments | 12,452,439 | 原 train；同 text 的各属性共 family | CC0 1.0；候选，target 是人工标注比例，不是校准正确率。占原组合可消费记录约 76.58%，需明确训练混合策略 |
| kaivoss/system-one-270m-data | 22,000 | 固定原 train；state_id family；合同可解析部分 | Apache 2.0；候选，保留 teacher distribution，未知布局继续隔离 |
| nvidia/HelpSteer | 165,564 | 原 train；共同 prompt 将不同 response/rubric 共 family | CC BY 4.0；候选，五属性 ordinal 0–4 分别建模；复杂度/冗长度不是正确率 |
| nvidia/HelpSteer2 | 101,615 | 原 train；共同 prompt family | CC BY 4.0；候选；同上，保留 rubric 和 ordinal 标签语义 |
| samatv256/jev-decisions-v1 | 49,571 | 原 clean50k 五个 train Parquet；trajectory_id/task_id/state family | **本轮整个来源暂隔离**。整体 CC BY 4.0 不覆盖未核实的逐 repository SPDX 组件；当前元数据审查无法可靠逐行区分 |
| stanfordnlp/snli | 548,677 | 原 train；共同 premise family | CC BY-SA 4.0 冻结声明及 Flickr30k 相关归因保留；仅 entailment Choice 候选，其他 split 不进入监督 |

## Open-Jev 精确范围及第三方 notice

当前转换实际只读取以下十个配置，均为 `raw/hf/ZefanCai/Open-Jev/data/<config>/train-00000-of-00001.parquet`：

`amount-extraction-control-v1`、`citation-control-v1`、`context-retention-control-v1`、`email-selection-control-v1`、`entity-alignment-control-v1`、`ir-control-v1`、`mailroom-control-v1`、`phone-extraction-control-v1`、`silent-failure-control-v1`、`sponsor-segment-control-v1`。

这些固定路径不包含 customer-control 或 Wikispeedia task 载体。原 README 明确 CC0 仅涵盖原作者生成内容，不覆盖上游措辞、外部资产、原始来源数据或权重。TypeSafe customer-control 描述没有核实训练/再分发许可，Wikispeedia graph/path 也没有核实单独许可；这些配置及任何未列入精确资产 allowlist 的路径以 `source_or_configuration_component_rights_not_reviewed` 隔离，不能通过字段转换或教师改写解除原权利边界。

原 `THIRD_PARTY_NOTICES.md` 已存在于冻结 source-assets 清单，但旧组合 notices 归档未收录它。本轮按原字节另存于私有 audit 目录的 `Open-Jev-THIRD_PARTY_NOTICES.md`，SHA：`36350b78946d29f443cb03e276eed3934aca01079de769ed4655b93ad5031c4a`。原始文件及组合身份不改；主任务应把该额外 notice 连同原合同纳入新训练包。

## Jev clean50k 的保留和隔离

冻结 `SOURCE_LICENSES.md` 列出四类 NVIDIA 轨迹来源，并明确 Open-SWE-Traces 源行带逐 repository SPDX，不能统一改成一份整体许可。本轮只读元数据没有验证转换记录到每个上游 repository、具体 license、文本组件及对应 notices 的逐行映射，因此采用完整来源隔离，原因 `Jev_clean50k_per_repository_component_rights_unverified`。这暂扣 train 44,441、development 5,130 条。

原始 clean50k 数据、派生记录、family、冲突账本和 notice 全部保留。后续若能从原 provenance 精确识别并验证组件，可建立新版本的逐资产/逐记录白名单再追加；未知组件继续隔离。不能把这些暂扣项计成已经通过全量训练的题目。

## 划分、泄漏与执行合同

实际转换的原 train 内容按 `sha(source|declared_family)` 决定派生划分，family 哈希模 10 为 0 的记录进入 development，其余 train。同题候选换序、多个属性、同轨迹子问题不得跨派生划分。ARC 还对两配置的官方 id/规范题干做连通分组；ToolACE 明确是原 unsplit 的派生 holdout。

原 test、原 development/validation、calibration、OOD 以及本轮派生 development 保留作相应用途，不回流为训练监督。已验证的是提供的 exact family 隔离；跨来源语义 family、不同来源改写、原基准跨 split 重叠仍未全面审查。当前可固定私有实验候选范围，并记录这些局限；不能以它声称已经具有未泄漏、代表性的通用质量评测。

逐行执行须同时核对 allowlist 的 Source.Dataset/Path/FileSha256/Revision、Split/OriginalSplit、排除身份、类型/候选映射和 tokenizer 长度。无法表示或超长内容入新隔离账本，不静默截断；教师分布、合成参考、人工类别、人工偏好、标注比例、ordinal 和 Stockfish 参考保持不同监督语义。模型权重、tokenizer 与代码的身份/许可由各自合同管理，本来源报告不替代模型准入。

## 证据与资源边界

私有 `source-admission-v1/metadata-audit.json` 保存 44 个固定 metadata/notice 的读取身份，加一个明确定位的 Open-Jev 第三方 notice；逐文件限制 16 MiB，不遍历目录、不追溯下载。34 个允许原始资产的 Path/SHA/asset revision 已复核；四组件 16 个 manifest/bindings/rights/assets 元数据 SHA 与组合封存身份相符。新机器策略不修改原 formal_training_admitted=false 状态。

metadata 作业 guardian 上限 180 秒，交付与身份复核作业各 120 秒，脚本自身 deadline 分别 150/90 秒，均设置固定项目上限、取消、进度和两项元数据 smoke。PID、start、完整 argv、父链及关闭结果在 source-admission logs 与原专属 source-coverage logs 保存。初版策略的 revision 检查失败以 `metadata-identity-correction-v1.json` 和原策略版本保留。仅清理本子任务精确标记的 startup gates；所有交付物、原始输入及失败保留。
