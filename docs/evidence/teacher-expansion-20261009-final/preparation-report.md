# 扩充数据准备回执

本地准备完成 256 条：160 条 MASSIVE Choice（en/zh 各 80），96 条 BoolQ Boolean（en）；192 train / 64 development。256 个唯一 family；没有新增 Score，旧 32 条仍独立排除。

题文、候选、来源与 split 的原始准备文件冻结时间为 **2026-10-08T11:38:40.6891325Z**（pre-canonical 原 prepared 文件保留的 LastWriteTimeUtc）。原 source catalog 写入时间为 2026-10-08T11:38:40.7001326Z，原 preparation report 写入时间为 2026-10-08T11:38:40.7041339Z。冻结时间来自保存原字节的快照文件时间，并受成功 full preparation 回执约束；不是后续 schema 补齐或打包时间。

原 prepared SHA256：`9902DBC35D4281391666DF81D41CD8DC7AE72F82237294569A1F449EBC165BA2`。

后续只添加 host-side `source_canonical_id`，并将 catalog 的 `records` 从整数改为 256 个绑定项数组，计数字段为 `record_count`。MASSIVE canonical 为 `AmazonScience/MASSIVE/1.1/{locale}/{id}`；BoolQ 为 `google/boolq/{source_split}/{row_idx}`。原 prepared、catalog、report、tiny2 字节分别保存在 `.pre-canonical` 文件。

最终 prepared SHA256：`17946E4ED202F45832C4E0E90B62F2CD7AAEDED096896E97268CCDB82DF3C937`。

最终 source catalog SHA256：`313197600B9B823A246B9C971EC5F96D4DCFD365BA396F5F4B0DBF3737A1D360`。

`preparation-final-verification.json` 的 256 条逐行核对通过：request、split、target 未改变，catalog 每行身份对应，教师请求字段投影不包含原始答案。前两条为 en-US id 1 和 zh-CN id 2；中文 UTF8 内容核对正确。Choice 的四个 gold 位置在各语言 train 中每位置 16 条，在各语言 development 中每位置 4 条。BoolQ train 为 32 false / 32 true，development 为 16 false / 16 true。

数据仍为 `ai_review=pending`、`human_review=pending`、`formal=false`、`eligible_as_sealed_test=false`。HF viewer 的 repository revision 不能单独证明 rows snapshot revision；该限制保留在 source catalog。原始 gold 只供 host 对照，不能成为执行授权。

full preparation：PID 57184，bounded receipt `prepare-logs/20261008-113754-342-prepare-full.result.json`，exit 0，Succeeded。schema 首次 bounded launcher 在其自身份 Get-CimInstance（wrapper line 115）超时，发生在 Process.Start（line 124）之前，因此未启动 schema 子进程；随后直接 PS7执行 PID 47424，exit 0，实际完成 9.090 秒（tool receipt f05e6b）。最终验证 PID 55972，exit 0，3.356 秒（tool receipt 811da0）。详细身份文件保留，root 负责最终 PID 归属与退出闭环。

本准备子任务 API/GPU 调用均为 0，无下载、安装、Git 或 source 改动；未创建需删除的临时文件。
