# 本轮模型发布材料审查

2026-10-09，Asia/Shanghai。此文件记录只读来源审查及官方通知准备，不代表律师意见、正式许可证审签或已上传回执。实际发布由当前主智能体统一执行。

用户本次明确要求上传 Hugging Face 和模力方舟，并指定模力方舟组织 `IoTSharp`。本次授权满足项目“对外发布须当前请求明确授权”的要求，覆盖此前仅本地实验、不上传平台的范围限制；保持历史报告原字节。正式人审、质量、校准与 AOT/性能尚未验收，可以在实验模型卡中分别如实披露，不自动成为这次公开上传的追加确认门槛。

## 审查证据

本轮模型目录为 `.artifacts/rental-training-20261009-01a12099/trained-model`。训练报告 SHA256 为 `efafe1e006e3dd7a3d8bec3a2327651406749fa0bf348c987783ec658ba51e4d`；固定上游模型卡 SHA256 为 `35724415037ee3159aa733c16050620cc19a76219112a6ceeb3949572e0b37e9`。报告、来源锁、准备包 provenance、数据来源研究和第三方通知都保持原字节。

| 分项 | 已核事实 | 尚未能作出的结论 |
| --- | --- | --- |
| 项目代码 | 仓库 LICENSE 为 Apache-2.0，NOTICE 保留 Sezika 归属 | 不能据此给全部模型资产 Apache 许可。 |
| encoder | 固定 `jhu-clsp/mmBERT-base` 模型卡声明 MIT；转换 encoder 与原权重各有独立 SHA | 固定卡未附完整 MIT 授权文本；本次官方代码仓库 LICENSE 请求也未取得版权通知。官方 MIT 标准文本可附，但不能杜撰具体版权人。 |
| tokenizer | 上游固定卡只写 Gemma 2；本机字节有锁定 SHA | 卡中没有 Google 精确 tokenizer model ID/revision，不能用某个 Gemma 权重版本代替这一身份。当前官方条款快照不证明原取得时版本。 |
| 数据 | 来源目录声明 MASSIVE CC BY 4.0，BoolQ CC BY-SA 3.0；原文及修改边界保留 | 数据许可不自动决定最终权重许可；公开逐题 token_ids 能还原题文，不能当作不含数据。 |
| 教师输出 | 报告记 `sonnet_vip/gpt-6.1-sol`、真实身份仅网关声明、许可 unknown | 未找到适用于采集时账户/网关的教师条款，不能断言允许或禁止下游 head 发布。新用户发布授权不是第三方权利授予。 |

以上未核项目需要的最小补证是：mmBERT 权重适用的完整 MIT notice/版权人声明；本 tokenizer 对应的 Google 来源身份与适用条款；采集时 `sonnet_vip` 对教师输出用于训练并分发模型的条款或权利方许可。当前没有取得明确禁止 encoder/head 分发的第三方条款，也没有把 unknown 推断成禁止。完整包不能宣传为“许可正式批准”“全 MIT”“全 Apache”或“所有用途无限制”。

当前官方 Gemma 条款允许在遵守条件的情况下分发，包含条款副本、用途政策限制、修改声明及 Notice 等要求。准备资料保留 Google 原文快照和 Notice，声明本机 tokenizer 的精确来源尚未补证；不把这些准备动作表述为已完成法律审签。

公开通知资料位于 `.artifacts/model-publishing-20261009-01a12099/license-materials/`，仅含官方许可/来源快照、标准模板与 `ASSET-NOTICES.md`、来源 URL/SHA 索引。`mmbert-code-MIT-LICENSE.txt` 请求失败，未造出文件；`MIT-LICENSE-TEMPLATE.txt` 明确是标准模板。完整源请求及真实成功/失败状态见 `sources.json`，不能把模板称为上游实际 LICENSE。

## 两平台实际托管规则

模力方舟当前官方域名为 `moark.com`，用户指定组织控制台为 <https://moark.com/IoTSharp/dashboard/models>。官方[模型仓库说明](https://moark.com/docs/products/models/repositories)确认其以 Git/Git LFS 存储模型文件，支持公开/私有仓库、HTTPS/SSH 和 `git push`。[上传说明](https://moark.com/docs/products/models/upload)给出的 clone 格式为 `https://moark.com/<namespace>/<model>`；大文件推荐官方 `gai` Git LFS 自定义传输工具。`gai` 使用 Gitee `projects` 权限令牌及独立模力方舟访问令牌；创建仓库和实际上传仍须由已登录账户执行。此前 `ai.gitee.com` 的官方文档可作为实现历史来源，门面应使用成功发布回执中的 `moark.com` / `IoTSharp` 真链接。未取得可核的仓库创建 REST schema 时，不猜测 API 路径或 JSON 字段。

Hugging Face 官方[许可证元数据说明](https://huggingface.co/docs/hub/repositories-licenses)支持 `license: unknown` 和自定义许可证说明；[模型卡说明](https://huggingface.co/docs/hub/model-cards)要求在 README 描述用途、限制、训练与评估资料。来源声明不替代许可证本身。模型卡适合采用 `experimental`、`uncalibrated` 及分项许可声明，base model 指向固定 mmBERT 来源。平台类型字段不得虚构为已支持 Transformers/PyTorch 推理；本模型的实际执行路线为 Sezika C# / CUDA Driver。

上传采用显式允许列表：encoder、tokenizer、新 head/checkpoint、模型 manifest、真实模型卡、SHA 清单和分项许可材料；训练摘要可只保留统计、实验边界和原 report SHA。省略原始逐题报告、token_ids、教师原件、题文、连接账号、凭据和内部路径。每个平台成功后都核仓库身份/组织、实际 commit、文件列表及大文件远端摘要/字节，能核 SHA 时逐件与本地清单对照，再将成功公开 URL 写到项目门面。HTTP 200、仓库已创建或 LFS 指针存在，均不足以单独证明模型完整上传。

本轮实际数字采用租用机原报告：215 题（train162/dev53）、24576 updates、encoder 冻结、seed17、LR0.001；教师一致 train118/162、dev27/53，换序7/16且候选 ID 改变6/16，Score 新监督为0。10/10 证据核验是原训练交付的工程与数值验收；本审查不重跑 GPU，也不把教师一致率称为正式准确率。
