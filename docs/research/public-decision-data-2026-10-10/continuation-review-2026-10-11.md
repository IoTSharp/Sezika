# 2026-10-11 接续核验记录

最后一批可访问文件已经完成下载并取得成功回执。重新按固定计划核对：75 个选定来源、3,333 个计划文件，其中 74 个来源完整，最终只缺 Salesforce xLAM 的一个 96,116,570 字节文件。该来源匿名请求返回 401，已有 HF 账户请求返回 403；没有接受访问条款或绕过 gate。历史失败事件保留，不用它们代替当前缺失清单。

从所有下载 ledger 刷新得到 3,492 个成功文件、34,375,333,787 字节。1,320 个文件取得物理行数，合计 37,870,718 行，包含不同格式、镜像、聚合与派生重复，不代表独立训练题数。

## 上传故障与已验证替代路径

官方 `gai v0.0.2-alpha.2` 的单对象观察显示：真实上传初始化、配额、push 和 upload-callback 接口返回 200，callback 响应正文为 `true`，但后续 missing-lfs 仍报告同一对象缺失。补齐客户端发送的空 `lfs_file_path` 也未消除缺失。探针最终 502 由本任务转发器达到请求边界后产生，不能归因于平台。原始回执全部保留。

标准 Git LFS 单对象上传成功，missing-lfs 从 26 降至 25；随后首包提交推送成功，缺失归零。官方数据集下载文档说明使用 Git clone；标准匿名 LFS batch 返回实际下载动作。首包 68 个资产已逐项完成匿名全内容 SHA256/长度校验。

最初验收脚本误把不存在的 `README.en.md` 列入预期路径，已修正并保留失败回执。Hugging Face 兼容 resolve 地址返回 404，正式验收使用标准匿名 Git LFS batch 和流式全内容校验。验收不把 HTTP200、客户端 complete 或传输字节数当作发布证明。

## 扩展来源的选择依据

| 来源 | 已读取的声明 | 本次处理 |
| --- | --- | --- |
| BoolQ | 来源卡明确 CC-BY-SA-3.0 | 原始划分、全文和卡片保留，适用 ShareAlike 条件。 |
| SNLI | 来源卡明确 CC-BY-SA-4.0，并声明 Flickr30k 来源 | 原始划分和署名保留，适用 ShareAlike 条件。 |
| GoEmotions | Google 来源卡明确 Apache-2.0 | 全部已选原始配置保留，不把多标签标注合并为单一正确概率。 |
| HH-RLHF | Anthropic 卡声明 MIT，偏好对和红队全文用途不同 | 完整保存公开资产；训练候选只按原始用途派生，红队全文不直接当偏好对。 |
| ToolACE | 原发布卡声明 Apache-2.0、合成 API/对话来源 | 完整原始对话和 API 定义保留，待工具选择标签派生。 |
| BFCL | 原发布卡明确数据 Apache-2.0 | 题库和答案全部保留，隔离作独立评测。 |
| XWinograd | 原发布卡明确多语言数据 CC-BY-4.0 | 全语言资产及署名保留，评测隔离。 |
| Open-Jev 十个原生控件 | 每个原始 manifest 的 configuration.license 为 CC0-1.0，type 为 synthetic；README/NOTICE 限定为原生成内容 | 仅十控件的完整 raw/Parquet split、原始 manifest 和相关卡片；排除复合混合配置及许可未核 customer-control。 |
| GhostScientist Jev | 卡标 Apache-2.0，同时说明有 BoolQ、MNLI、Banking77、AGNews、SST5 派生内容 | 不能把 tag 覆盖上游条件，保留本地等待逐组件审查。 |
| ARC | 卡的许可说明正文仍为 More Information Needed | 不仅依据顶层标签放入公开包，保留本地待核。 |

Open-Jev 十控件 manifest 声明共 245,834 typed rows，包含 train、calibration、validation、test、OOD。raw 与 Parquet 表示对应重复内容；此处是来源声明统计，没有宣称新增独立题目数或完成模型训练。

完整 Jev Decisions 中的 Open-SWE 部分需要逐仓库条件审查，本次仍只公开 CLEAN-50K 及来源说明。JevEmbed-Data 的处理报告含非商业组件，不按 README 顶层宽松标签整包公开。所有来源的实际再分发条件和训练准入分别记录。

扩展包固定资产提交为 `b8ea13b81aaaca722baf6a3c0f12e2217fc4353e`：18 个来源、275 个文件、1,076,726,933 字节、225 条 LFS 路径。正式发布结果以 [发布回执](publication-receipt.json) 为准；[下载说明](download-guide.md) 提供固定版本路径。未执行正式训练、批量教师调用或批量特征计算。
