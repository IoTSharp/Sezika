# 从模力方舟下载公开决策资产

数据集归属为 `maikebing/public-decision-data`。使用标准 Git 与 Git LFS 下载，公开下载不需要保存的账户令牌。本仓库的 Hugging Face 兼容 resolve 地址曾返回 404，因此不把它作为已验收下载路径。

固定资产版本为 `b8ea13b81aaaca722baf6a3c0f12e2217fc4353e`。发布回执单独作为后续提交保存，不改变该版本的数据、清单或许可说明。

```bash
git lfs version
GIT_LFS_SKIP_SMUDGE=1 git clone https://moark.com/maikebing/public-decision-data
cd public-decision-data
git checkout b8ea13b81aaaca722baf6a3c0f12e2217fc4353e
git -c lfs.standalonetransferagent= -c lfs.basictransfersonly=true lfs pull
```

上述环境变量写法适用于 Linux/Bash。Windows 请使用 PowerShell 7，在克隆命令之前临时设置 `$env:GIT_LFS_SKIP_SMUDGE='1'`，结束后还原原先值。先确认已安装 Git LFS；`git lfs pull` 完成后，大文件应为实际内容，不能把 LFS pointer 文本当成题库。

资产清单包含 18 个来源、275 个文件、1,076,726,933 字节。按 `ASSET_MANIFEST.json` 的 `path`、`bytes`、`sha256` 核对完整文件；原始数据文件不做就地修改。`RIGHTS_MANIFEST.json` 及各来源卡片保存逐源许可、署名、版本和限制，整个仓库不赋予统一宽松许可。

| 监督用途 | 主要来源 | 构造训练集时的边界 |
| --- | --- | --- |
| 意图与候选选择 | Banking77、CLINC、MASSIVE、Typed Decisions | 使用原始 train；保留 OOS、候选 ID、语言与来源；聚合配置须去重。 |
| 原生控件的 Choice / Noul / Score | Open-Jev 十个独立控件、System One 270M、SystemOne Lite Phase2 | 保留 group、family、split；合成参考标签及教师投票不是实测正确率。 |
| Boolean / NLI | BoolQ、SNLI | 保留原文与原始标签，遵守各自 ShareAlike 条件，隔离验证与测试。 |
| 评分与偏好 | HelpSteer / HelpSteer2、HH-RLHF | 保留评分维度、序数尺度或 chosen/rejected 语义；HH-RLHF 红队全文不直接充当偏好对。 |
| 多标签 | GoEmotions、Civil Comments | 保留多标签、标注者或软标签含义，不能随意合并成单一正确概率。 |
| 工具选择候选 | ToolACE | 先从对话和 API 定义派生有限候选决策，保留工具与参数监督的区别。 |
| 独立评测 | BFCL、XWinograd，以及上述来源原始测试划分 | 与训练样本及同源派生家族隔离。 |

Jev Decisions 仅镜像 CLEAN-50K 和来源说明；完整母语料仍在本地等待 Open-SWE 等组件许可审查。Open-Jev 仅镜像十个原生控件的全部公开 split，排除混合配置和许可未核的 customer-control 描述。S1MB、Sys1Cal、JevEmbed-Data 等来源已本地收集，但尚未作为本次公开镜像整体发布。

本次交付是可复现的原始资产镜像。跨来源去重、评测污染检查、标签转换及正式训练准入仍需独立完成；文件数或物理行数不代表去重训练题数。
