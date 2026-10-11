# Jev 生态分析数据包

2026-10-08。总报告：[学习与采纳分析](../../jev-ecosystem-analysis-2026-10-08.md)。

`projects.csv` / `projects.json` 包含两份冻结清单所有仓库引用的逐项建议：555个URL身份归并为552个项目，其中Sezika为自身基线；550可访问，2不可访问。58个项目的README/片段重点审读，19个项目选定源码/协议核查；其余仅按清单初筛。没有执行外部模型或benchmark。

每行的 `project` 是当前仓库身份，`aliases` 保留旧URL；JSON `references` 保留原清单、行号、类别和说明，`decisions` 保留分组意见。commit与README hash限定来源版本，不能理解为全部源码审计。GitHub代码licenseInfo不覆盖模型、tokenizer或数据许可。

`priority` 表示Sezika相关性：P0基础缺口、P1条件实验/下一层改进、P2场景参考、P3暂缓或低证据；它不是质量评分。`adoption` 是研究建议，未表示已实施。部分“不可核实”项目仓库可访问，但特定能力/资产不能确认，因此数量与不可访问仓库数不同。

`external-sources.csv` / `.json` 覆盖231个其他主要URL，包含39官方规范、81讨论、53文章应用、24演示入口、15公告媒体、10模型研究、9评测校准。目录初筛不等于正文核验，其中Tenuo文章另有正文审读。

三份 `*-review.md` 和 `*-decisions.json` 保留原分工结果；67模型引用、41评测引用、447集成引用在总JSON中归并。模型分组里的JevBench由评测组补充源码核查，总表据此提升证据等级；Kev由主分析补充共享prefix与模型源码核查。不要直接相加分组条数或审读数来推算独立项目。

`provenance.json` 记录上游快照、输入hash、工作区最新768步证据hash、执行边界和研究限制。快照含此次待合并Sezika条目；前述两个投稿PR分别为 [中文PR29](https://github.com/yzfly/awesome-jev-zh/pull/29) 和 [英文PR352](https://github.com/yibie/awesome-jev/pull/352)。

本数据包新增研究文件，不修改ROADMAP、HANDOFF、训练源码或训练产物，也没有自动提交/push。表格CSV为UTF-8 BOM，可在Excel或编辑器按列检索。
