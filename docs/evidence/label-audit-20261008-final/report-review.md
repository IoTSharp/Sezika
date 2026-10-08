# 32题AI审题最终报告独立审查

结论：接受 `audit-report.json` 与 `../label-audit-2026-10-08.md` 作为本次32题独立AI审题的最终报告。未发现阻止记录此审题侧车的合同或标签映射问题。本审查没有调用共用验证函数，没有发送API、读取凭据/config、执行训练/GPU、修改原数据/旧报告/权重或使用Git。

## 独立核对

读取三个明确完整文件：最终 `audit-report.json`、正文 `../label-audit-2026-10-08.md`、原 `.artifacts/real-gpu-experiment-20261008/data/experiment-records.jsonl`。独立解析32行，核对32个唯一record_id及其question_id完全覆盖原题；family、language、split、type及完整original_request序列化逐字段保持一致，分母仍为train24/development8。

原target index及ID按原候选顺序独立重建：Choice使用原candidate ID，Score使用从0开始的等级ID字符串，Boolean为false/true。全部旧映射一致；新index独立使用System.Text.Json的TryGetInt32确认，32项ID/index均合法一致。全部32项为determinate且有理由，29项保留、3项建议改标，与报告summary一致。

49条引文逐一使用Ordinal匹配单个原始字段；每题最多4条，corpus最多32段，没有通过拼接不同字段接受引文。原始state、instruction、statement、when_false/when_true、levels及candidate.text均按单字段检查。

三项改标理由独立成立：

| record_id | 原ID → 建议ID | 独立判断 |
| --- | --- | --- |
| general-en-score-03 | 2 → 3 | 版本、复现步骤、观察结果明确出现，确切错误文本缺失，四项中有三项 |
| general-zh-boolean-02 | true → false | 12−5=7且无后续变化，剩8盒的陈述不成立 |
| general-en-boolean-01 | true → false | 同日同一时钟09:15早于10:00，drawing starts later的陈述不成立 |

这些是AI审题建议，未被改写为正式人工金标，原标签未修改。

## 身份、请求绑定与结论范围

顶层及逐题均为reviewer_kind=ai、human_review=pending、formal=false、eligible_as_sealed_test=false；顶层original_data_modified=false。原数据身份声明仍为 `A015A8FA182EE62223CAFC307C618C515A19E90F843290DB9DFB53A56B9A041C`。

报告实际requested model/effort为gpt-6.1-sol/max，initially_requested_effort为ultra。正文明确保留ultra被HTTP400拒绝、随后经用户批准改用max的历史；没有把max写成ultra成功。upstream_identity_verification为gateway_response_declaration_only，保留网关返回型号不能独立证明上游身份的限制。

只对任务原始五份成功response文件做哈希核对，全部与最终报告一致；未展开读取这五份API响应内容，未重新调用API：

| batch | response SHA-256 |
| --- | --- |
| tiny-max-01 | E37D90BBB8D6BD85F14189DDAE2D8019964C561D1BFC5A3715949E3F8F3BD843 |
| batch-max-02 | 4F862D533CCCB5DD2BFB48BB76663E508A806181B9C47CD6638F08EA30D2A1A7 |
| batch-max-03 | C21EF16200C29288A36E289D2D7E09B021C8FA30EE642BD9164CEF88EE42E7C8 |
| batch-max-04 | ED97EAEFAB0FDC97E2A4972A23EE0EABD5F59353504772BBD247BD0903757627 |
| batch-max-05 | 7D533DE985F47B67CEA8DEB181AA415B8F3CBCC023742A9EA774F1356E71A46E |

正文与JSON的旧权重事后比较一致：train原教师17/24→AI审核标签18/24，development原教师1/8→AI审核标签2/8。JSON明确weights_retrained=false、use=posthoc_ai_label_comparison_not_gold_accuracy。这里仅核对报告一致性与语义，未新增读取第四个GPU基线预测文件来独立重算18/24或2/8；这两个数字不能称人工金标正确率或新的质量改进证据，旧CE不随比较标签改写。

## 资源与保留局限

首次Md命名冲突、意外产生的general-zh-choice-01和delivery两个空目录，以及自动审批阻止Move/Remove，均已在正文如实保留。审查未尝试删除或绕过拒绝。旧部分归档保留，本次唯一最终入口为label-audit-20261008-final。

审查仅新建本报告和 `report-review-readers.json`。Windows使用固定PowerShell7路径，实测7.6.6；独立验证阶段实际340ms，循环明确限制32行、每题4引文、32 corpus及五项response hash，90秒局部deadline与cancel.request检查。没有创建子进程、临时文件或下载。reader的PID、创建时间、完整实际argv、父PID/父链与已知工具退出回执保存在同目录资源登记中；最后writer自身以实际工具退出回执收口，root负责最终精确身份absent核查。

本次验收关闭的是审题报告的记录与合同问题，不关闭人工金标准入、独立封存、泛化、校准、AOT或性能门槛，不启动新训练。
