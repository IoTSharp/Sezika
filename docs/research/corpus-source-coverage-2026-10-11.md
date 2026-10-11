# 全来源训练覆盖审查（2026-10-11）

本审查核对 75 个已选来源；74 个完成原计划下载。原目录有 3,492 个资产，记录的物理行总数为 37,870,718，含镜像、聚合配置和不同载体；独立题目数尚未测定。

已有许可初审通过来源 18 个，其中 16 个在当前转换适配范围。下表的“转换终态待主任务清单”及 `manifest_not_available_at_audit_snapshot` 是 2026-10-11 的初始审查快照；2026-10-12 已完成选定适配范围的转换与 18 来源组合，终态分母、家族和剩余准入条件见[最终证据](../evidence/corpus-preparation-2026-10-12.md)。本表不授予正式训练资格，也不把下载完成解释为每条标签可用。

原始 train 可整理为训练/开发候选；原始 validation/test、评测专用来源和校准集保留。MASSIVE、CLINC 的原始归档中有内部分区，ToolACE 原文件未分区，不能仅凭文件名判定训练行数。表中行数来自既有 inventory，0 表示未在该字段统计到，不能解释为源数据为空。

| 来源 | 文件完成 | 原 train 物理行 | 保留评测物理行 | 其他物理行 | 声明许可 | 当前覆盖范围 | 重叠/适配备注 |
|---|---:|---:|---:|---:|---|---|---|
| allenai/ai2_arc | 7/7 | 3,370 | 4,417 | 0 | cc-by-sa-4.0 | 追加完成原 train 私人整备；CC-BY-SA-4.0 声明/归因已保留；正式准入待验 | 跨来源语义重复未审；不承诺独立题目数 |
| allenai/openbookqa | 7/7 | 9,914 | 2,000 | 0 | unknown | 许可/组件条款及格式待审核 | main 与 additional 同题；选择一种载体，不累加两套4957行 |
| allenai/reward-bench | 12/12 | 5,123 | 0 | 8,729 | odc-by | 评测/校准保留 | 跨来源语义重复未审；不承诺独立题目数 |
| allenai/social_i_qa | 3/3 | 0 | 0 | 0 | 未声明 | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| allenai/winogrande | 19/19 | 63,238 | 18,204 | 0 | 未声明 | 许可/组件条款及格式待审核 | train_xs/s/m/l/xl 为嵌套训练子集，debiased 另需按题号去重 |
| AmazonScience/massive | 6/6 | 0 | 0 | 0 | cc-by-4.0 | 适配训练部分；转换终态待主任务清单 | 跨来源语义重复未审；不承诺独立题目数 |
| Anthropic/hh-rlhf | 10/10 | 0 | 0 | 0 | mit | 适配训练部分；转换终态待主任务清单 | 跨来源语义重复未审；不承诺独立题目数 |
| aps/super_glue | 30/30 | 147,076 | 49,237 | 0 | other | 许可/组件条款及格式待审核 | BoolQ 与 google/boolq 同源；其余配置分别审查 |
| C-MTEB/AFQMC | 4/4 | 34,334 | 8,177 | 0 | 未声明 | 许可/组件条款及格式待审核 | 与 clue/clue 的 afqmc 同源；需按句对内容核对跨载体重复 |
| C-MTEB/CMNLI | 2/2 | 0 | 1 | 0 | 未声明 | 许可/组件条款及格式待审核 | 与 clue/clue 的 cmnli 同源；本镜像只有评测资产 |
| C-MTEB/IFlyTek-classification | 4/4 | 12,133 | 5,199 | 0 | 未声明 | 许可/组件条款及格式待审核 | 与 clue/clue 的 iflytek 同源；完整类别超过32，需显式任务设计 |
| C-MTEB/LCQMC | 4/4 | 238,766 | 21,302 | 0 | 未声明 | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| C-MTEB/OCNLI | 2/2 | 0 | 1 | 0 | 未声明 | 许可/组件条款及格式待审核 | 与 clue/clue 的 ocnli 同源；本镜像只有评测资产 |
| C-MTEB/PAWSX | 4/4 | 49,401 | 4,000 | 0 | 未声明 | 许可/组件条款及格式待审核 | 与 google-research-datasets/paws-x 中文部分同源；需核对句对内容 |
| C-MTEB/STSB | 4/4 | 5,231 | 2,819 | 0 | 未声明 | 许可/组件条款及格式待审核 | 相似度连续标签；需显式 Score 语义及分桶策略 |
| C-Tianyu/NanoJev-Data | 240/240 | 35,423 | 15,955 | 19,514 | 未声明 | 许可/组件条款及格式待审核 | 混合来源 typed corpus；来源组件许可与跨来源重复待审 |
| cais/mmlu | 180/180 | 199,684 | 31,716 | 0 | mit | 评测/校准保留 | all 与学科配置复用题目；保留作评测，不归入训练 |
| cardiffnlp/tweet_eval | 34/34 | 120,270 | 80,515 | 0 | unknown | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| cardiffnlp/tweet_sentiment_multilingual | 26/26 | 14,712 | 9,552 | 0 | 未声明 | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| clinc/clinc_oos | 11/11 | 33,475 | 25,800 | 0 | cc-by-3.0 | 适配训练部分；转换终态待主任务清单 | 跨来源语义重复未审；不承诺独立题目数 |
| clue/clue | 36/36 | 696,947 | 99,181 | 1,002 | unknown | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| dair-ai/emotion | 5/5 | 432,809 | 4,000 | 0 | other | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| dwidlee/systemone-lite-phase2 | 3/3 | 240,800 | 4,700 | 0 | apache-2.0 | 适配训练部分；转换终态待主任务清单 | 跨来源语义重复未审；不承诺独立题目数 |
| facebook/anli | 10/10 | 162,865 | 6,400 | 0 | cc-by-nc-4.0 | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| facebook/xnli | 52/52 | 6,283,232 | 120,000 | 0 | 未声明 | 许可/组件条款及格式待审核 | all_languages 与各语言配置复用题目；跨语言按官方题号分组 |
| fancyzhx/ag_news | 3/3 | 120,000 | 7,600 | 0 | unknown | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| fancyzhx/dbpedia_14 | 3/3 | 560,000 | 70,000 | 0 | cc-by-sa-3.0 | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| GhostScientist/jev-decisions-v1 | 3/3 | 8,702 | 400 | 0 | apache-2.0 | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| google-research-datasets/go_emotions | 5/5 | 254,635 | 10,853 | 0 | apache-2.0 | 适配训练部分；转换终态待主任务清单 | raw 与 simplified 有内容复用；当前仅 simplified train |
| google-research-datasets/paws | 7/7 | 725,450 | 26,000 | 0 | other | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| google-research-datasets/paws-x | 22/22 | 345,807 | 28,000 | 0 | other | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| google/boolq | 3/3 | 9,427 | 3,270 | 0 | cc-by-sa-3.0 | 适配训练部分；转换终态待主任务清单 | 跨来源语义重复未审；不承诺独立题目数 |
| google/civil_comments | 5/5 | 1,804,874 | 194,640 | 0 | cc0-1.0 | 适配训练部分；转换终态待主任务清单 | 跨来源语义重复未审；不承诺独立题目数 |
| gorilla-llm/Berkeley-Function-Calling-Leaderboard | 51/51 | 0 | 0 | 0 | apache-2.0 | 评测/校准保留 | 跨来源语义重复未审；不承诺独立题目数 |
| HIT-TMG/JevEmbed-Data | 13/13 | 1,401,157 | 66,482 | 200,000 | apache-2.0, cc0-1.0, cc-by-4.0 | 许可/组件条款及格式待审核 | 混合来源 typed corpus；来源组件许可与跨来源重复待审 |
| hotchpotch/bekko-system-one-dataset-v0 | 1010/1010 | 5,343,504 | 22,705 | 1,245,684 | 未声明 | 许可/组件条款及格式待审核 | 多配置 typed corpus；组件许可及重叠待审 |
| hotchpotch/S1MB | 3/3 | 0 | 0 | 0 | 未声明 | 评测/校准保留 | 跨来源语义重复未审；不承诺独立题目数 |
| hotchpotch/s1mb-dataset | 551/551 | 0 | 15,609 | 200 | 未声明 | 评测/校准保留 | 题型/领域/聚合配置可能复用；评测保留 |
| HuggingFaceH4/ultrafeedback_binarized | 8/8 | 183,405 | 4,000 | 0 | mit | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| kaivoss/system-one-270m-data | 2/2 | 25,002 | 0 | 0 | apache-2.0 | 适配训练部分；转换终态待主任务清单 | 跨来源语义重复未审；不承诺独立题目数 |
| little-g-ai/Sys1Cal-v1 | 4/4 | 0 | 0 | 730 | 未声明 | 评测/校准保留 | 跨来源语义重复未审；不承诺独立题目数 |
| loaiabdalslam/VEJI-Synth-v2-200K | 10/10 | 190,000 | 10,000 | 110 | 未声明 | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| LocalLLaMA/typed-decisions | 12/12 | 2,400 | 800 | 0 | apache-2.0 | 适配训练部分；转换终态待主任务清单 | all 聚合与各类配置复用；当前排除 all 聚合 |
| mteb/amazon_counterfactual | 14/14 | 17,748 | 2,953 | 0 | cc-by-4.0 | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| mteb/amazon_massive_intent | 155/155 | 0 | 0 | 0 | apache-2.0 | 许可/组件条款及格式待审核 | MASSIVE 意图派生镜像；与 AmazonScience/massive 按原始 id/语言去重 |
| mteb/amazon_massive_scenario | 154/154 | 0 | 0 | 0 | apache-2.0 | 许可/组件条款及格式待审核 | MASSIVE 场景派生镜像；与 AmazonScience/massive 按原始 id/语言去重 |
| mteb/banking77 | 6/6 | 19,996 | 6,156 | 0 | mit | 许可/组件条款及格式待审核 | Banking77 镜像；优先 PolyAI/banking77，不计为新题 |
| mteb/mtop_intent | 39/39 | 138,444 | 49,346 | 0 | unknown | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| mteb/tweet_sentiment_extraction | 6/6 | 54,213 | 6,966 | 0 | unknown | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| Muennighoff/xwinograd | 7/7 | 0 | 4,449 | 0 | cc-by-4.0 | 评测/校准保留 | 跨来源语义重复未审；不承诺独立题目数 |
| nvidia/HelpSteer | 3/3 | 0 | 0 | 0 | cc-by-4.0 | 适配训练部分；转换终态待主任务清单 | 跨来源语义重复未审；不承诺独立题目数 |
| nvidia/HelpSteer2 | 5/5 | 0 | 0 | 0 | cc-by-4.0 | 适配训练部分；转换终态待主任务清单 | 跨来源语义重复未审；不承诺独立题目数 |
| nyu-mll/glue | 35/35 | 949,733 | 535,310 | 0 | other | 许可/组件条款及格式待审核 | MRPC/QQP/MNLI/RTE/QNLI/STS-B/SST-2 等配置分别审查；与独立 MNLI 等来源存在同源配置 |
| nyu-mll/multi_nli | 4/4 | 392,702 | 19,647 | 0 | cc-by-3.0, cc-by-sa-3.0, mit, other | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| ought/raft | 36/36 | 0 | 0 | 0 | other | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| PKU-Alignment/BeaverTails | 5/5 | 0 | 0 | 0 | cc-by-nc-4.0 | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| PolyAI/banking77 | 5/5 | 0 | 0 | 0 | cc-by-4.0 | 适配训练部分；转换终态待主任务清单 | 跨来源语义重复未审；不承诺独立题目数 |
| Praveenrajus/jev-bench | 66/66 | 133,953 | 32,101 | 0 | other | 评测/校准保留 | 跨来源语义重复未审；不承诺独立题目数 |
| Rowan/hellaswag | 4/4 | 39,905 | 20,045 | 0 | 未声明 | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| Salesforce/xlam-function-calling-60k | 1/2 | 0 | 0 | 0 | cc-by-4.0 | 下载未完整；不接入 | 跨来源语义重复未审；不承诺独立题目数 |
| samatv256/jev-decisions-v1 | 25/25 | 7,944,408 | 0 | 3,425,000 | cc-by-4.0 | 适配训练部分；转换终态待主任务清单 | full 与 clean50k 父子子集；当前仅 clean50k train |
| SetFit/20_newsgroups | 4/4 | 11,314 | 7,532 | 0 | 未声明 | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| SetFit/SentEval-CR | 3/3 | 3,012 | 753 | 0 | 未声明 | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| SetFit/sst5 | 4/4 | 8,544 | 3,311 | 0 | 未声明 | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| SetFit/subj | 3/3 | 8,000 | 2,000 | 0 | 未声明 | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| SetFit/TREC-QC | 5/5 | 5,452 | 500 | 0 | 未声明 | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| stanfordnlp/SHP | 55/55 | 0 | 0 | 0 | 未声明 | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| stanfordnlp/snli | 4/4 | 550,152 | 20,000 | 0 | cc-by-sa-4.0 | 适配训练部分；转换终态待主任务清单 | 跨来源语义重复未审；不承诺独立题目数 |
| takala/financial_phrasebank | 3/3 | 0 | 0 | 0 | cc-by-nc-sa-3.0 | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| tau/commonsense_qa | 4/4 | 9,741 | 2,361 | 0 | mit | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| Team-ACE/ToolACE | 2/2 | 0 | 0 | 0 | apache-2.0 | 适配训练部分；转换终态待主任务清单 | 跨来源语义重复未审；不承诺独立题目数 |
| TIGER-Lab/MMLU-Pro | 6/6 | 0 | 12,102 | 0 | mit | 评测/校准保留 | 跨来源语义重复未审；不承诺独立题目数 |
| Yelp/yelp_review_full | 3/3 | 650,000 | 50,000 | 0 | other | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |
| ZefanCai/Open-Jev | 232/232 | 345,909 | 62,549 | 111,741 | cc0-1.0 | 适配训练部分；转换终态待主任务清单 | 配置及原 JSON/Parquet 载体可能复用；当前内容去重，语义跨来源分组待审 |
| zeroshot/twitter-financial-news-sentiment | 4/4 | 0 | 0 | 0 | mit | 许可/组件条款及格式待审核 | 跨来源语义重复未审；不承诺独立题目数 |

机器可读覆盖文件：`C:\Users\mysti\.codex\artifacts\sezika-corpus-20261011-01a12b4c\source-coverage-v2\coverage.json` 和 `C:\Users\mysti\.codex\artifacts\sezika-corpus-20261011-01a12b4c\source-coverage-v2\coverage.csv`。本表只读固定元数据，未重扫训练内容、未运行教师模型、未训练、未发布。

完全相同字节的计行文件共有 119 组，额外重复物理行 147,818；仅按文件 SHA 去重仍不足以识别 main/additional、镜像和语义近似题。

## 四个候选来源的实际格式审查

6 个固定原 train 文件各检查首 10 行，共 60 行候选映射通过；这是格式试验，未把抽样成功解释为整套标签正确。原始样本仅保留在本机私有 artifact。

`allenai/ai2_arc`：revision `210d026faf9955653af8916fad021475a3f00453`；声明许可 `["cc-by-sa-4.0"]`；审核状态 `declared_cc_by_sa_4_0_requires_attribution_sharealike_derivation_review`。
- 文件：`C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10\raw\hf\allenai\ai2_arc\ARC-Challenge\train-00000-of-00001.parquet`；SHA-256 `e488c1587ffdcfc8443f916c53488a95cd471c5790e0746c6bfe4cecf20962cb`；原始行数 `1119`。
- 文件：`C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10\raw\hf\allenai\ai2_arc\ARC-Easy\train-00000-of-00001.parquet`；SHA-256 `b315db8a4be597dc7daa50a4e70d48dd7c990c32085629e6ccd8c926beaa80b5`；原始行数 `2251`。

`allenai/openbookqa`：revision `388097ea7776314e93a529163e0fea805b8a6454`；声明许可 `["unknown"]`；审核状态 `unresolved_original_or_component_rights`。
- 文件：`C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10\raw\hf\allenai\openbookqa\additional\train-00000-of-00001.parquet`；SHA-256 `d16d719e87efb86ed0a2ac4c8cdf380f7bfb94b602088393674c0a64ce9ed3d3`；原始行数 `4957`。
- 文件：`C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10\raw\hf\allenai\openbookqa\main\train-00000-of-00001.parquet`；SHA-256 `98148f8a54e62eb862346a75192d5fb824d6cbb68f2f59aecd793d39ecb5cd8b`；原始行数 `4957`。

`C-MTEB/AFQMC`：revision `b44c3b011063adb25877c13823db83bb193913c4`；声明许可 `null`；审核状态 `unresolved_original_or_component_rights`。
- 文件：`C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10\raw\hf\C-MTEB\AFQMC\data\train-00000-of-00001-da0bf29c7c279c45.parquet`；SHA-256 `f237911dbc019f4edc4965bb22802fcdffd55354551e68dfe3e3fe04bb13ab69`；原始行数 `34334`。

`C-MTEB/LCQMC`：revision `17f9b096f80380fce5ed12a9be8be7784b337daf`；声明许可 `null`；审核状态 `unresolved_original_or_component_rights`。
- 文件：`C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10\raw\hf\C-MTEB\LCQMC\data\train-00000-of-00001-83493a6b9e29a4d0.parquet`；SHA-256 `6370aa4a06c64933041d70226ddf4e7755217c4e2d45d36dde5597a24877b055`；原始行数 `238766`。

适配建议：ARC 使用 `question`、`choices.{label,text}`、`answerKey`，按 label 身份匹配答案，不能假定字母标签。OpenBookQA 使用 `question_stem`；additional 可提供 `fact1`，但 main/additional 须二选一，`humanScore`、`clarity`、`turkIdAnonymized` 不送入输入。AFQMC/LCQMC 使用句对和二值 `score`，0→否、1→是；保留评测，负值/无标签排除，跨载体同句对需统一 family。

ARC 已核对固定卡片的 CC-BY-SA-4.0 声明，归因、许可链接、变更说明和派生数据同许可条件已写入私人整备 notice。卡片正文的 Licensing Information 仍标 More Information Needed，因此只确认声明与文件身份，未独立证明所有上游题目权利。OpenBookQA 卡片声明 unknown；两份 C-MTEB 卡片未声明许可，继续待审。

## ARC 完整原训练整理结果

原 ARC-Challenge/train 1,119 行和 ARC-Easy/train 2,251 行全部读取；输出 3,369 物理规范记录。完全重复、冲突和隔离以 `arc-full-v1/manifest.json` 计数为准；原 validation/test 未进入输出。输出状态 `COMPLETE_PREPARATION_ONLY`，formal/token-length/GPU 均未验收。

题号或规范题干相同的记录归为同一连通 family，确保两配置的同题不跨 train/development；候选按文本确定性排序并保留原候选映射，再使用共同 Writer 的循环旋转。完全重复只保留一份，冲突标签通过强制 exclusions 账本排除。

字节保留上游 README、归因/变更 notice、source-rights、source-assets、manifest、exclusions 及 corpus-bindings 已保存；这批资产保持私有，未发布。

## 验证与资源收口

ARC 输出训练 3,058 条、开发 311 条；1 条完全重复，0 冲突、0 隔离。四项 manifest/exclusions/source-rights/source-assets 绑定 SHA 已单独复核一致；完整冷加载的 C# 验证由主任务执行，这份审查不将离线整理升级为 GPU 训练或准确率证明。

本子任务截至 2026-10-12（Asia/Shanghai）共保留 12 份有界进程收据（2 次启动失败、10 次成功）：`C:\Users\mysti\.codex\artifacts\sezika-corpus-20261011-01a12b4c\source-coverage-process-logs`。两次初始失败原因是未预建 cooperative start gate，失败收据未删除；预建专属 gate 后重试成功。全部收据 `FinalCheckComplete=true`、`VerifiedLiveCount=0`、`UnknownCount=0`。10 个确认归属的临时 gate 目录已按精确路径回收；数据、试运行、历史失败与交付物保留。没有运行 GPU、教师、训练、发布、提交或推送。补查作业整体成功表示受控执行及保留逐项结果，不能消除其中保留的格式/载体失败。

## 75 来源的任务与答案语义复核

逐来源检查 75 个冻结来源，读取固定元数据和此前小样本的字段证据。没有重新解码全部数据、没有教师或训练。任务形状可转换与许可/标签质量准入是两项不同判断；本次所有来源 `formal_training_admitted=false`、`full_training_done=false`。

任务形状统计：`{"finite_with_derivation": 6, "pair_preference": 3, "finite_boolean": 8, "holdout_only_snapshot": 2, "continuous_score_adapter": 1, "typed_adapter_pending": 5, "typed_supported_subset": 5, "evaluation_reserved": 9, "multilabel_boolean": 2, "finite_choice": 18, "tool_derivation_and_generation_unsupported": 2, "ordinal_score": 4, "mixed_configuration": 5, "annotation_fraction_boolean": 1, "mirror_duplicate": 3, "classification_view_pending": 1}`。

许可审查状态统计：`{"initial_declared_terms_review_passed_formal_admission_pending": 18, "missing_or_unknown_license_component_review_required": 30, "declared_terms_exist_but_not_yet_individually_admitted": 23, "restricted_noncommercial_terms_scope_review_required": 3, "private_preparation_declared_cc_by_sa_4_0_notice_preserved": 1}`。

格式证据统计：`{"catalog_task_only_explicit_schema_validation_pending": 29, "bounded_decoded_sample_and_metadata": 17, "frozen_metadata_features": 29}`。只有 catalog 任务说明而无实际字段证据的来源仍须解码验证，不能按名称猜定答案。

| 来源 | 任务形状 | 已识别字段（最多12个） | 答案语义及接入/保留理由 | 许可状态 |
|---|---|---|---|---|
| AmazonScience/massive | finite_with_derivation | 本次未确认字段 | utt→18场景可直接选择；60意图需明确候选派生；归档partition与跨语言id保留；original_train_inside_archive_or_json_needs_internal_partition_review | initial_declared_terms_review_passed_formal_admission_pending |
| Anthropic/hh-rlhf | pair_preference | chosen, rejected | chosen/rejected共享Human前缀后2选1；仅偏好监督，不代表回复事实正确；original_train_inside_archive_or_json_needs_internal_partition_review | initial_declared_terms_review_passed_formal_admission_pending |
| C-MTEB/AFQMC | finite_boolean | idx, score, sentence1, sentence2 | sentence1/sentence2→二值score；0否1是；与CLUE AFQMC同源，先统一family；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| C-MTEB/CMNLI | holdout_only_snapshot | labels, sent1, sent2 | 中文句对三关系；本次目录只有test载体，禁止改写为train；许可与标签身份待核；no_confirmed_original_train_carrier_in_this_snapshot | missing_or_unknown_license_component_review_required |
| C-MTEB/IFlyTek-classification | finite_with_derivation | idx, label, text | 中文应用分类121类，超过32候选上限；需分层或明确训练候选派生；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| C-MTEB/LCQMC | finite_boolean | score, sentence1, sentence2 | sentence1/sentence2→二值score；0否1是；句对对称family，维持原train/validation/test；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| C-MTEB/OCNLI | holdout_only_snapshot | labels, sent1, sent2 | 中文自然语言推断三关系；本次目录只有test载体，禁止纳入训练；no_confirmed_original_train_carrier_in_this_snapshot | missing_or_unknown_license_component_review_required |
| C-MTEB/PAWSX | finite_boolean | score, sentence1, sentence2 | 中文句对→释义等价二值score；与PAWS-X中文部分跨载体核对；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| C-MTEB/STSB | continuous_score_adapter | score, sentence1, sentence2 | 句对相似度为连续score；不能把连续标注强制整数one-hot，需要显式分桶/插值语义；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| C-Tianyu/NanoJev-Data | typed_adapter_pending | 本次未确认字段 | hard/soft决策标签、游戏与合成来源；只取原train，calibration/test/ood保留，逐题型与组件条款待审；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| GhostScientist/jev-decisions-v1 | typed_adapter_pending | 本次未确认字段 | 决策类state/questions与引用答案需按真实字段逐任务审核；不可仅凭jev名字假定可训练；original_train_carriers_listed | declared_terms_exist_but_not_yet_individually_admitted |
| HIT-TMG/JevEmbed-Data | typed_adapter_pending | 本次未确认字段 | 多来源typed监督；source组件、答案结构与聚合复用待逐配置审核；original_train_carriers_listed | declared_terms_exist_but_not_yet_individually_admitted |
| HuggingFaceH4/ultrafeedback_binarized | pair_preference | chosen, messages, prompt, prompt_id, rejected, score_chosen, score_rejected | chosen/rejected消息序列→偏好2选1；须验证共享输入，保留教师偏好来源，不称人类正确答案；original_train_carriers_listed | declared_terms_exist_but_not_yet_individually_admitted |
| LocalLLaMA/typed-decisions | typed_supported_subset | action__confidence, action__label, action__probabilities, factors, gold, id, label_agreement, n_questions, needs_review__confidence, needs_review__label, needs_review__probabilities, needs_review__probability_true | state+questions.criteria+gold.probabilities；排除all聚合，引用分布保持来源语义；original_train_carriers_listed | initial_declared_terms_review_passed_formal_admission_pending |
| Muennighoff/xwinograd | evaluation_reserved | answer, option1, option2, sentence | 多语言Winograd评测题；保留原评测分区与跨语言同题family；no_confirmed_original_train_carrier_in_this_snapshot | initial_declared_terms_review_passed_formal_admission_pending |
| PKU-Alignment/BeaverTails | multilabel_boolean | 本次未确认字段 | prompt+response安全标注和类别可多标签Boolean；30k/330k子集复用；CC-BY-NC许可需单独范围审查；train_named_carrier_present_row_count_unavailable | restricted_noncommercial_terms_scope_review_required |
| PolyAI/banking77 | finite_with_derivation | 本次未确认字段 | 77意图超过32；当前训练含正确意图和最多31负类，不能宣称77类完整测评；train_named_carrier_present_row_count_unavailable | initial_declared_terms_review_passed_formal_admission_pending |
| Praveenrajus/jev-bench | evaluation_reserved | 本次未确认字段 | jev-bench混合引用；目前隔离/评测保留，未知答案语义与组件许可不能自动准入；original_train_carriers_listed | declared_terms_exist_but_not_yet_individually_admitted |
| Rowan/hellaswag | finite_choice | activity_label, ctx, ctx_a, ctx_b, endings, ind, label, source_id, split, split_type | ctx+4个endings→label；这是有限续写选择题，原test标签缺失须保留；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| Salesforce/xlam-function-calling-60k | tool_derivation_and_generation_unsupported | 本次未确认字段 | 单工具有限候选可派生；arguments/多调用/文本生成当前不适配；原数据文件未完整下载；no_confirmed_original_train_carrier_in_this_snapshot | declared_terms_exist_but_not_yet_individually_admitted |
| SetFit/20_newsgroups | finite_choice | 本次未确认字段 | 新闻组text→20类；固定标签字典，保持原train/test；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| SetFit/SentEval-CR | finite_choice | 本次未确认字段 | 客户评论text→正负类别；原划分和小样本重复核查，不当开放式问答；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| SetFit/TREC-QC | finite_with_derivation | 本次未确认字段 | 问题text→粗/细问题类型；粗6类可选，细粒度若>32则另设计；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| SetFit/sst5 | ordinal_score | 本次未确认字段 | 评论text→5档情感强度；有序情感任务，不当客观事实正确率；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| SetFit/subj | finite_boolean | 本次未确认字段 | 句子text→主观/客观类别；先核原label名字与布尔映射，避免正负反转；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| TIGER-Lab/MMLU-Pro | evaluation_reserved | answer, answer_index, category, cot_content, options, question, question_id, src | 多学科10候选增强基准；当前保留评测，不拿validation/test做训练；no_confirmed_original_train_carrier_in_this_snapshot | declared_terms_exist_but_not_yet_individually_admitted |
| Team-ACE/ToolACE | tool_derivation_and_generation_unsupported | 本次未确认字段 | 只派生有唯一reference工具且候选2..32的单工具选择；参数/多调用/生成隔离，原unsplit须family留出；original_unsplit_requires_family_holdout | initial_declared_terms_review_passed_formal_admission_pending |
| Yelp/yelp_review_full | ordinal_score | label, text | 评论text→5档星级label；source ordinal不当单纯正确率，保持原train/test；original_train_carriers_listed | declared_terms_exist_but_not_yet_individually_admitted |
| ZefanCai/Open-Jev | typed_supported_subset | group_id, id, kind, metadata_json, options, original_line_number, question, record_json, source, split, state_json, target | state_json+question/options/target；不把record_json/metadata中的隐藏答案当输入；跨配置内容去重；original_train_carriers_listed | initial_declared_terms_review_passed_formal_admission_pending |
| allenai/ai2_arc | finite_choice | answerKey, choices, id, question | question+choices.label/text→answerKey；按标签身份匹配，已追加完整私人整备；original_train_carriers_listed | private_preparation_declared_cc_by_sa_4_0_notice_preserved |
| allenai/openbookqa | finite_choice | answerKey, choices, clarity, fact1, humanScore, id, question_stem, turkIdAnonymized | question_stem+choices→answerKey；main/additional同题选一份；fact1可作已提供事实；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| allenai/reward-bench | evaluation_reserved | chosen, chosen_model, id, prompt, rejected, rejected_model, subset | 偏好基准 chosen/rejected；默认保留测评，不能以评测题扩充训练来提高同题分数；original_train_carriers_listed | declared_terms_exist_but_not_yet_individually_admitted |
| allenai/social_i_qa | finite_choice | answerA, answerB, answerC, context, label, question | 社会情境context/question/answerA-C与标签可转3选1；先补查当前原train资产是否齐全；train_named_carrier_present_row_count_unavailable | missing_or_unknown_license_component_review_required |
| allenai/winogrande | finite_choice | answer, option1, option2, sentence | 掩码sentence+option1/2→answer1/2；训练子集嵌套，选明确配置并按题号去重；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| aps/super_glue | mixed_configuration | answer, answers, choice1, choice2, end1, end2, entities, entity_spans, hypothesis, idx, label, paragraph | BoolQ/WiC/RTE/CB/COPA等配置分别映射；MultiRC多答案、ReCoRD实体选择不能直接混作同一题型；original_train_carriers_listed | declared_terms_exist_but_not_yet_individually_admitted |
| cais/mmlu | evaluation_reserved | answer, choices, question, subject, train | 学科多选题；all与分学科复用，现任务保留评测，不能混入训练再报告同基准提升；original_train_carriers_listed | declared_terms_exist_but_not_yet_individually_admitted |
| cardiffnlp/tweet_eval | mixed_configuration | label, text | sentiment/emotion/emoji/stance等有限类别；逐配置固定标签名字、保留原split；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| cardiffnlp/tweet_sentiment_multilingual | finite_choice | label, text | 各语言tweet→负/中性/正3类；固定标签映射，跨语言及复用tweet分组待核；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| clinc/clinc_oos | finite_with_derivation | intent, text | 150意图+OOS超过32；仅train/oos_train，当前为含正确类与负类的训练派生，非全类别测评；original_train_inside_archive_or_json_needs_internal_partition_review | initial_declared_terms_review_passed_formal_admission_pending |
| clue/clue | mixed_configuration | 本次未确认字段 | AFQMC/NLI/TNEWS/WSC/CSL可有限决策；C3/CHID需上下文候选；CMRC/DRCD抽取答案需另设计有限任务；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| dair-ai/emotion | finite_choice | label, text | 情绪text→有限label；采用原标签字典和原train，single_label监督不能当多标签概率；original_train_carriers_listed | declared_terms_exist_but_not_yet_individually_admitted |
| dwidlee/systemone-lite-phase2 | typed_supported_subset | criteria_keys, criteria_values, instructions, label_alias, label_key, meta, state, task | state/instructions/criteria_keys+values→label_alias；已实现选择题映射，合成引用非人类gold；original_train_carriers_listed | initial_declared_terms_review_passed_formal_admission_pending |
| facebook/anli | finite_choice | 本次未确认字段 | adversarial context/hypothesis→三关系label；R1/R2/R3 train可用，保留各轮dev/test；original_train_carriers_listed | restricted_noncommercial_terms_scope_review_required |
| facebook/xnli | finite_choice | hypothesis, label, premise | 多语言premise/hypothesis→三关系label；all_languages与逐语言配置去重，翻译同题统一family；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| fancyzhx/ag_news | finite_choice | 本次未确认字段 | 新闻text→4个topic label；保留类别名字，题目是分类而非开放域问答；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| fancyzhx/dbpedia_14 | finite_choice | 本次未确认字段 | 百科title/content→14个ontology类；有限分类，维持原split；original_train_carriers_listed | declared_terms_exist_but_not_yet_individually_admitted |
| google-research-datasets/go_emotions | multilabel_boolean | admiration, amusement, anger, annoyance, approval, author, caring, confusion, created_utc, curiosity, desire, disappointment | simplified labels为多标签；每种情绪二值问题，不能把多个情绪one-hot成单选；raw复用不再累计；original_train_carriers_listed | initial_declared_terms_review_passed_formal_admission_pending |
| google-research-datasets/paws | finite_boolean | id, label, sentence1, sentence2 | sentence1/sentence2→paraphrase二值label；词面高重叠不是标签依据；原作条款card other待审；original_train_carriers_listed | declared_terms_exist_but_not_yet_individually_admitted |
| google-research-datasets/paws-x | finite_boolean | id, label, sentence1, sentence2 | 多语言句对→paraphrase二值label；跨语言同源题号分组，避免C-MTEB中文副本加权；original_train_carriers_listed | declared_terms_exist_but_not_yet_individually_admitted |
| google/boolq | finite_boolean | answer, passage, question | passage+question→answer布尔值；false/true固定顺序；validation保留；original_train_carriers_listed | initial_declared_terms_review_passed_formal_admission_pending |
| google/civil_comments | annotation_fraction_boolean | identity_attack, insult, obscene, severe_toxicity, sexual_explicit, text, threat, toxicity | 7个属性为报告的人类标注比例；保留[1-p,p]，不宣称是已校准正确率；original_train_carriers_listed | initial_declared_terms_review_passed_formal_admission_pending |
| gorilla-llm/Berkeley-Function-Calling-Leaderboard | evaluation_reserved | 本次未确认字段 | 函数调用基准；保留评测与无关调用语义，不能训练后仍称未见测评；no_confirmed_original_train_carrier_in_this_snapshot | initial_declared_terms_review_passed_formal_admission_pending |
| hotchpotch/S1MB | evaluation_reserved | 本次未确认字段 | 主要benchmark/代码资料；当前无可确认原train监督，不能把资料文件计作题目；no_confirmed_original_train_carrier_in_this_snapshot | missing_or_unknown_license_component_review_required |
| hotchpotch/bekko-system-one-dataset-v0 | typed_adapter_pending | 本次未确认字段 | 多配置state/questions/probability类目标；聚合、组件来源及硬/软标签语义待核；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| hotchpotch/s1mb-dataset | evaluation_reserved | 本次未确认字段 | System1 benchmark；按题型/领域聚合去重，现任务保留测评；train_named_carrier_present_row_count_unavailable | missing_or_unknown_license_component_review_required |
| kaivoss/system-one-270m-data | typed_supported_subset | ambiguity, ambiguity_requested, domain, entropy, label, letters, n_options, prompt, qtype, score, state_id, target | 原prompt+letters+target教师分布；Choice/Boolean/有序Score边界已适配，概率不是实测正确率；original_train_carriers_listed | initial_declared_terms_review_passed_formal_admission_pending |
| little-g-ai/Sys1Cal-v1 | evaluation_reserved | 本次未确认字段 | 校准用途名称且元数据未可确认；保留研究/校准，不把未知载体纳入训练；no_confirmed_original_train_carrier_in_this_snapshot | missing_or_unknown_license_component_review_required |
| loaiabdalslam/VEJI-Synth-v2-200K | typed_adapter_pending | 本次未确认字段 | 合成VEJI题目；须检验消息输出是否有限决策及真实答案字段，未适配且许可未知；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| mteb/amazon_counterfactual | finite_boolean | label, label_text, text | 评论句子→是否反事实类别；标签id须匹配原字典，不能把情感极性当反事实答案；original_train_carriers_listed | declared_terms_exist_but_not_yet_individually_admitted |
| mteb/amazon_massive_intent | mirror_duplicate | 本次未确认字段 | MASSIVE意图视图；复用AmazonScience/massive，不能计作新增题；no_confirmed_original_train_carrier_in_this_snapshot | declared_terms_exist_but_not_yet_individually_admitted |
| mteb/amazon_massive_scenario | mirror_duplicate | 本次未确认字段 | MASSIVE场景视图；复用AmazonScience/massive，不能计作新增题；no_confirmed_original_train_carrier_in_this_snapshot | declared_terms_exist_but_not_yet_individually_admitted |
| mteb/banking77 | mirror_duplicate | label, label_text, text | Banking77分类镜像；优先PolyAI原载体，来源同题不双倍训练；original_train_carriers_listed | declared_terms_exist_but_not_yet_individually_admitted |
| mteb/mtop_intent | finite_with_derivation | id, label, label_text, text | 多语言MTOP意图类别可能超过32；核原label字典后设计分层/受限候选任务；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| mteb/tweet_sentiment_extraction | classification_view_pending | id, label, label_text, text | 若当前MTEB视图提供情感label可转分类；原抽取情感span目标需要生成/抽取能力，不能混为单选；original_train_carriers_listed | missing_or_unknown_license_component_review_required |
| nvidia/HelpSteer | ordinal_score | coherence, complexity, correctness, helpfulness, prompt, response, verbosity | prompt+response→5属性各0..4；复杂度/冗长是描述量，不等同回复好坏；train_named_carrier_present_row_count_unavailable | initial_declared_terms_review_passed_formal_admission_pending |
| nvidia/HelpSteer2 | ordinal_score | coherence, complexity, correctness, helpfulness, prompt, response, verbosity | prompt+response→5属性各0..4；每属性独立题目及明确rubric，原dev保留；train_named_carrier_present_row_count_unavailable | initial_declared_terms_review_passed_formal_admission_pending |
| nyu-mll/glue | mixed_configuration | hypothesis, idx, label, premise, question, question1, question2, sentence, sentence1, sentence2 | SST-2/MRPC/QQP/MNLI/RTE/QNLI有限标签；STS-B连续评分另适配；原test无标签不能作为监督；original_train_carriers_listed | declared_terms_exist_but_not_yet_individually_admitted |
| nyu-mll/multi_nli | finite_choice | genre, hypothesis, hypothesis_binary_parse, hypothesis_parse, label, pairID, premise, premise_binary_parse, premise_parse, promptID | premise/hypothesis→entailment/neutral/contradiction；与GLUE MNLI同源，matched/mismatched保留；original_train_carriers_listed | declared_terms_exist_but_not_yet_individually_admitted |
| ought/raft | mixed_configuration | 本次未确认字段 | 11个少样本分类任务，逐任务标签及候选数审核；原test无标签保留，银行77类需派生；train_named_carrier_present_row_count_unavailable | declared_terms_exist_but_not_yet_individually_admitted |
| samatv256/jev-decisions-v1 | typed_supported_subset | answer_options, id, question, question_type, source, state, target_index | clean50k state+answer_options→target_index；只取clean train，不能加上其父full重复；动作仅预测不授权；original_train_carriers_listed | initial_declared_terms_review_passed_formal_admission_pending |
| stanfordnlp/SHP | pair_preference | 本次未确认字段 | history+human_ref_A/B→投票偏好label；核label=哪一边及投票筛选，偏好非事实正确性；train_named_carrier_present_row_count_unavailable | missing_or_unknown_license_component_review_required |
| stanfordnlp/snli | finite_choice | hypothesis, label, premise | premise/hypothesis→3关系；负label未标注行隔离，前提family保证开发不复用；original_train_carriers_listed | initial_declared_terms_review_passed_formal_admission_pending |
| takala/financial_phrasebank | finite_choice | label, sentence | 金融句子→3档情感；不同annotator-agreement配置嵌套，选一份或显式分歧分布，不能累加；no_confirmed_original_train_carrier_in_this_snapshot | restricted_noncommercial_terms_scope_review_required |
| tau/commonsense_qa | finite_choice | answerKey, choices, id, question, question_concept | question+5个choices→answerKey；固定id映射，测试无gold不作训练；original_train_carriers_listed | declared_terms_exist_but_not_yet_individually_admitted |
| zeroshot/twitter-financial-news-sentiment | finite_choice | 本次未确认字段 | 金融tweet→3档情感label；核固定label名字与编号，保持validation；train_named_carrier_present_row_count_unavailable | declared_terms_exist_but_not_yet_individually_admitted |

`source-assets.json` 是原始文件身份清单，不是权限合同；许可决定在 `source-rights.json`。2026-10-11 初始观察：ARC 本地清单包含 2 个 train Parquet 和 1 个原 README，当时通用 `bind_corpus.py` 引用外部 `ASSET_MANIFEST.json`，`Corpus.cs` 未复算 `SourceAssetsSha256` 且缺少 `SourceRoot`。2026-10-12 主任务已修复本地绑定位置、显式 SourceAssetsPath/旧 SourceRoot 兼容及复算 SHA，并完成最终组合全量结构读取；原发现保留为历史，当前结果见[最终证据](../evidence/corpus-preparation-2026-10-12.md)。清单本身通过不代表逐个原文件的许可或语义已正式准入。

机器可读逐来源清单：`C:\Users\mysti\.codex\artifacts\sezika-corpus-20261011-01a12b4c\source-semantic-review-v1\semantics.json`。保留全部未接入来源理由；没有宣称 75 个来源都已整理或训练。

## 29 个仅目录来源的真实训练字段补查

目标 29 来源，处理 29 来源；最多每源2行，累计 37 行。结果统计 `{"unsupported_train_selection_from_archive_prefix": 1, "sample_schema_decoded": 19, "no_confirmed_original_train_data_carrier": 6, "FAILED_SAMPLE_PRESERVED": 3}`。只读冻结 inventory 标注的原 train 候选载体；其中 3 个样本实际是索引/处理收据，不能作为原训练题。没有运行来源 loader、教师、模型、训练或新增下载。每源最多一次20秒，整体上限600秒。

| 来源 | 补查状态 | 样本行数 | 实际字段/答案语义或未做原因 |
|---|---|---:|---|
| AmazonScience/massive | unsupported_train_selection_from_archive_prefix | 0 | ValueError('first_archive_member_prefix_not_original_train') |
| C-Tianyu/NanoJev-Data | sample_schema_decoded | 2 | id, split, metadata；answer_semantics_not_identified_by_bounded_field_pattern;explicit_source_adapter_review_required |
| GhostScientist/jev-decisions-v1 | sample_schema_decoded | 2 | state, question, options, answer, answer_key, source, seed_dataset, messages；dialogue_or_message_carrier;inspect reference answer for finite decision versus arguments/free text generation |
| HIT-TMG/JevEmbed-Data | sample_schema_decoded | 2 | id, group, request_json, answers_json, question_type, source, source_repo, source_revision, source_row, domain, license, original_split；answer_semantics_not_identified_by_bounded_field_pattern;explicit_source_adapter_review_required |
| PKU-Alignment/BeaverTails | sample_schema_decoded | 2 | prompt, response, is_safe, category；response_safety_boolean_and_optional_multilabel_category;not free text generation supervision |
| PolyAI/banking77 | sample_schema_decoded | 2 | text, category；utterance_class_category;original class space may exceed32 and requires explicit training derivation |
| Praveenrajus/jev-bench | sample_schema_decoded | 2 | id, source, primitive, split, state, question, label, soft_label, meta；labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping |
| Salesforce/xlam-function-calling-60k | no_confirmed_original_train_data_carrier | 0 | Only metadata/evaluation/unsplit or missing training asset in frozen inventory; no path guessed and no loader run. |
| SetFit/20_newsgroups | sample_schema_decoded | 2 | text, label, label_text；labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping |
| SetFit/SentEval-CR | sample_schema_decoded | 2 | text, label, label_text；labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping |
| SetFit/TREC-QC | sample_schema_decoded | 2 | text, label, label_text, label_original, label_coarse, label_coarse_text, label_coarse_original；labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping |
| SetFit/sst5 | sample_schema_decoded | 2 | text, label, label_text；labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping |
| SetFit/subj | sample_schema_decoded | 2 | text, label, label_text；labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping |
| Team-ACE/ToolACE | no_confirmed_original_train_data_carrier | 0 | Only metadata/evaluation/unsplit or missing training asset in frozen inventory; no path guessed and no loader run. |
| clue/clue | sample_schema_decoded | 2 | sentence1, sentence2, label, idx；sentence_pair_binary_label;verify source class names before false/true mapping |
| facebook/anli | sample_schema_decoded | 2 | uid, premise, hypothesis, label, reason；three_relation_nli_label;class dictionary must fix integer relation mapping |
| fancyzhx/ag_news | sample_schema_decoded | 2 | text, label；labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping |
| fancyzhx/dbpedia_14 | sample_schema_decoded | 2 | label, title, content；labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping |
| gorilla-llm/Berkeley-Function-Calling-Leaderboard | no_confirmed_original_train_data_carrier | 0 | Only metadata/evaluation/unsplit or missing training asset in frozen inventory; no path guessed and no loader run. |
| hotchpotch/S1MB | no_confirmed_original_train_data_carrier | 0 | Only metadata/evaluation/unsplit or missing training asset in frozen inventory; no path guessed and no loader run. |
| hotchpotch/bekko-system-one-dataset-v0 | sample_schema_decoded | 1 | annotation_kinds, catalog_sha256, code_sha256, counts, decision_types, output, output_sha256, persisted_content_sha256, protected_content_sha256, reuse_proof, rule_counts, source, source_sha256；answer_semantics_not_identified_by_bounded_field_pattern;explicit_source_adapter_review_required |
| hotchpotch/s1mb-dataset | no_confirmed_original_train_data_carrier | 0 | Only metadata/evaluation/unsplit or missing training asset in frozen inventory; no path guessed and no loader run. |
| little-g-ai/Sys1Cal-v1 | no_confirmed_original_train_data_carrier | 0 | Only metadata/evaluation/unsplit or missing training asset in frozen inventory; no path guessed and no loader run. |
| loaiabdalslam/VEJI-Synth-v2-200K | sample_schema_decoded | 2 | veji_version, id, language, family, state, questions, metadata；answer_semantics_not_identified_by_bounded_field_pattern;explicit_source_adapter_review_required |
| mteb/amazon_massive_intent | FAILED_SAMPLE_PRESERVED | 0 | JSONDecodeError('Extra data: line 2 column 1 (char 126)') |
| mteb/amazon_massive_scenario | FAILED_SAMPLE_PRESERVED | 0 | JSONDecodeError('Extra data: line 2 column 1 (char 118)') |
| ought/raft | sample_schema_decoded | 2 | Sentence, Label, ID；labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping |
| stanfordnlp/SHP | FAILED_SAMPLE_PRESERVED | 0 | ValueError('json_prefix_4MiB_bound_columnar_or_large_row_requires_adapter') |
| zeroshot/twitter-financial-news-sentiment | sample_schema_decoded | 2 | text, label；labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping |

真实样本及逐字节来源SHA只保存在本机私人 `C:\Users\mysti\.codex\artifacts\sezika-corpus-20261011-01a12b4c\source-schema-supplement-v1\supplement.json`。前2行格式成功不是全源标签质量、完整训练或正式准入证明；多配置只检查第一个 inventory 标注的 train 候选载体，其他配置仍须逐项适配。

### 已保存样本的语义澄清

上述37个前缀对象中，34个来自17个带任务/标签字段的来源；另外3个是2来源的索引/处理收据，不能作为训练记录。原始补查清单保持不变，澄清清单 `C:\Users\mysti\.codex\artifacts\sezika-corpus-20261011-01a12b4c\source-schema-supplement-v1\interpretation.json`；本阶段没有再读取原始文件、重试失败、调用教师或训练。

| 来源 | 澄清状态 | 答案字段或未适配原因 |
|---|---|---|
| AmazonScience/massive | unsupported_train_selection_from_archive_prefix | ValueError('first_archive_member_prefix_not_original_train') |
| C-Tianyu/NanoJev-Data | benchmark_index_without_supervised_answer | 首个benchmark/train.jsonl只含id/split/metadata及环境状态索引，无question/target/答案；不能训练此载体。 |
| GhostScientist/jev-decisions-v1 | labelled_or_task_sample_schema_observed | 已见state/question/options及answer_key；可候选选择适配，先核answer_key对应options.key或label，messages中的答案不送入模型输入。 |
| HIT-TMG/JevEmbed-Data | labelled_or_task_sample_schema_observed | 已见request_json/answers_json与question_type，需解码嵌入请求和答案，再按问题id/候选顺序映射；license/source_repo/source_revision逐组件审查。 |
| PKU-Alignment/BeaverTails | labelled_or_task_sample_schema_observed | 已见prompt/response/category/is_safe，安全Boolean与多标签类别可适配；CC-BY-NC范围及30k/330k复用待审。 |
| PolyAI/banking77 | labelled_or_task_sample_schema_observed | utterance_class_category;original class space may exceed32 and requires explicit training derivation |
| Praveenrajus/jev-bench | labelled_or_task_sample_schema_observed | 已见state/question/primitive/label/soft_label/meta；是有限题型引用，但bench组件来源和原评测边界仍须保留，不能因文件名train就自动正式准入。 |
| Salesforce/xlam-function-calling-60k | no_confirmed_original_train_data_carrier | Only metadata/evaluation/unsplit or missing training asset in frozen inventory; no path guessed and no loader run. |
| SetFit/20_newsgroups | labelled_or_task_sample_schema_observed | labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping |
| SetFit/SentEval-CR | labelled_or_task_sample_schema_observed | labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping |
| SetFit/TREC-QC | labelled_or_task_sample_schema_observed | 实际有label/label_text细类与label_coarse/label_coarse_text粗类；当前32候选上限下应明确选粗6类或设计细类派生。 |
| SetFit/sst5 | labelled_or_task_sample_schema_observed | labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping |
| SetFit/subj | labelled_or_task_sample_schema_observed | text+label+label_text表明主观/客观分类；应据label_text固定布尔映射，不猜0/1对应哪一类。 |
| Team-ACE/ToolACE | no_confirmed_original_train_data_carrier | Only metadata/evaluation/unsplit or missing training asset in frozen inventory; no path guessed and no loader run. |
| clue/clue | labelled_or_task_sample_schema_observed | sentence_pair_binary_label;verify source class names before false/true mapping |
| facebook/anli | labelled_or_task_sample_schema_observed | three_relation_nli_label;class dictionary must fix integer relation mapping |
| fancyzhx/ag_news | labelled_or_task_sample_schema_observed | labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping |
| fancyzhx/dbpedia_14 | labelled_or_task_sample_schema_observed | labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping |
| gorilla-llm/Berkeley-Function-Calling-Leaderboard | no_confirmed_original_train_data_carrier | Only metadata/evaluation/unsplit or missing training asset in frozen inventory; no path guessed and no loader run. |
| hotchpotch/S1MB | no_confirmed_original_train_data_carrier | Only metadata/evaluation/unsplit or missing training asset in frozen inventory; no path guessed and no loader run. |
| hotchpotch/bekko-system-one-dataset-v0 | processing_receipt_not_training_record | 首个带train路径的JSON是counts/输出SHA/复用证明收据，不是问题与答案；未把1个资料对象算作训练样本。 |
| hotchpotch/s1mb-dataset | no_confirmed_original_train_data_carrier | Only metadata/evaluation/unsplit or missing training asset in frozen inventory; no path guessed and no loader run. |
| little-g-ai/Sys1Cal-v1 | no_confirmed_original_train_data_carrier | Only metadata/evaluation/unsplit or missing training asset in frozen inventory; no path guessed and no loader run. |
| loaiabdalslam/VEJI-Synth-v2-200K | labelled_or_task_sample_schema_observed | 已见state与questions数组，每题含type/criteria/options/answer_index/target_probabilities，有限决策可适配；confidence_target不是已校准正确率，evidence/answer须隔离于输入。 |
| mteb/amazon_massive_intent | FAILED_SAMPLE_PRESERVED | JSONDecodeError('Extra data: line 2 column 1 (char 126)') |
| mteb/amazon_massive_scenario | FAILED_SAMPLE_PRESERVED | JSONDecodeError('Extra data: line 2 column 1 (char 118)') |
| ought/raft | labelled_or_task_sample_schema_observed | labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping |
| stanfordnlp/SHP | FAILED_SAMPLE_PRESERVED | ValueError('json_prefix_4MiB_bound_columnar_or_large_row_requires_adapter') |
| zeroshot/twitter-financial-news-sentiment | labelled_or_task_sample_schema_observed | labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping |

保留的格式失败：两个MTEB MASSIVE视图`.json.gz`按单JSON载体读取时出现第2行Extra data，提示实际可能是JSONL载体；SHP `.json`未在4MiB单JSON前缀中完成解析。没有为消除失败重复尝试来源。MASSIVE归档首个JSONL成员的第1行非train，本次2行约束下不继续扫描；完整转换主任务已有独立分区适配，不能混淆两项证据。

C-Tianyu的正确train候选与bekko更合适的Parquet/JSONL路径仅从原inventory列出，尚未读取；主任务可在下一次明确选择原始训练载体。

补查终验补充：GhostScientist 两条样本的 `answer_key` 均只对应唯一 `options.key`，没有对应 `options.label`，后续适配应按 key 身份。HIT 两条样本的 request 根字段为 `state/questions`，answers 根字段为 `decision`，已见 `choice` 和 `noul`；VEJI 已见 `choice/noul/score`，四个子问题的候选/目标维度分别为 4/4、4/4、2/2、3/3。两来源样本嵌入的 `license=CC0-1.0` 是逐行声明证据，不能代替源卡片/组件权利审查或正式准入。
