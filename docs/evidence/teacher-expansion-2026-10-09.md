# 256 题公开样本扩量：完整教师审核与质量阻塞

2026-10-09，规范工作区 `D:\source\Sezika`。固定 256/256 题全部取得有效盲审，178 题符合当前 AI 审核条件，78 题需要修订。真实数据打包检查拒绝准入，未生成训练 manifest，GPU 训练没有开始。质量门槛未通过，不能把 178 题重新包装成原定 192train/64development，也不能把 AI 审核等同人工金标。

## 真实分母

| 数据 | 冻结总数 | 已审 | 当前符合条件 | 需修订 | 未审 |
| --- | ---: | ---: | ---: | ---: | ---: |
| MASSIVE 中英 Choice | 160 | 160 | 117 | 43 | 0 |
| BoolQ 英文 Boolean | 96 | 96 | 61 | 35 | 0 |
| 合计 | 256 | 256 | 178 | 78 | 0 |
| train（上述总数内） | 192 | 192 | 137 | 55 | 0 |
| development（上述总数内） | 64 | 64 | 41 | 23 | 0 |

共有 8 条审核建议改标；这不是最终批准的标签。78 条阻塞的答案状态为 ambiguous 25、insufficient_info 35、invalid_question 15、determinate 3；最后 3 条仍因题目清晰度或改题要求不能准入。所有原 source gold、题文、split、family 保留，未静默剔除或改写。原 32 题另列已曝光回归，Score 没有新增监督。

## 数据和窗口身份

来源为 MASSIVE1.1 中英 160 条及 BoolQ 英文 96 条；沿官方 split 与 family 冻结。来源许可、原始下载及镜像局限见归档 `source-research.md` 和 `preparation-report.md`。BoolQ viewer 原字节已冻结，但未独立证明与 metadata revision 等价。

- prepared-records SHA-256：`17946E4ED202F45832C4E0E90B62F2CD7AAEDED096896E97268CCDB82DF3C937`。
- source-catalog SHA-256：`313197600B9B823A246B9C971EC5F96D4DCFD365BA396F5F4B0DBF3737A1D360`。
- split 冻结于 `2026-10-08T11:38:40.6891325Z`，没有以写包时刻替代。
- 用户批准的补取窗口：北京时间 2026-10-09 11:28:03.8319509 至 12:28:03.8319509；后续额外 4 次批准没有延长截止时间。
- 同一 sonnet.vip / gpt-6.1-sol / max，批量最多 8、并发 2、单请求最多 240 秒。接近截止时按剩余时长缩短，不重置计时。

首个补取窗口结束时，包含旧窗口，按持久化请求保守计数共 38/39 次 HTTP 尝试，本补取窗口 35/36 次；成功 31/33 批，覆盖 240 个唯一题号。另有 1 次 `/v1/models` 连通性 GET，不混作标注请求。3 次在 HTTP 之前失败（旧 auth 缺失 2 次，retry06 到期 1 次）。最后 retry05 仅剩 23 秒请求预算，超时且无响应；retry06 没有请求原字节，没有发出 HTTP。失败/中断的上游完成状态与计费未知。没有重发成功批次。

用户随后明确批准最后 16 题的新窗口：2026-10-09 北京时间 12:32:06.5190229 起、12:42:06.5190229 止，最多 2 次 HTTP、每批 8、并发 2、单次 240 秒。final05/06 因旧脚本错误依赖当前 Codex 全局模型，在 HTTP 前失败；无 request/provenance。Direct 分支修复为使用明确批准的教师模型、同一 CC Switch 选中 sonnet_vip 的凭据来源，不修改用户全局配置。以保留原失败的 final05v2/final06v2 各发一次，均 HTTP200、严格验证 8/8、guardian Succeeded/exit0/cleanup空，于北京时间12:36:44完成收口。最终累计 40 次 HTTP 尝试、33 成功批、256 唯一题；5 次 HTTP 前失败不混入请求计数。两个新请求在用户批准的 10 分钟内完成，旧时钟没有重置。

原成对队列在 batch22/23 响应保存、guardian 成功退出后有意停止；原 `failed/Wave phase deadline/cancellation` 回执保留。接续连续两槽完成 batch24..32、recovery01/02、retry03/04，retry05/06 失败后停止。最终工具真实退出，不把控制层 exit0 当作审核全部成功。

batch07 原 provenance 的 adapter/credential 描述过时，实际直连路径以 transport-result 为准；第一版 Python 的默认重定向行为与旧 automatic_redirects=false 声明不一致，不能据旧字段声称禁止了重定向。后续版本明确拒绝重定向。Direct 分支 result 的 children_started=false 也是继承字段，Python 子进程身份以 guardian 日志为准。纠正 sidecar 保留这些局限，不回写旧字节。请求/响应、版本 hash 保存，但并非所有历史 worker 版本都有源码快照。上游模型仅由服务响应声明。

## 提高质量的具体修订范围

逐题见 [78 题修订清单](teacher-expansion-20261009-complete/final16-repair-proposals.md)，对应 JSON 同时保存完整原题、原标签、family/split 及教师意见。建议尚未应用，不能作为已审核的新题。此前 240 题/73 阻塞的原窗口归档保留，最终分母以完整审核补充归档为准。

1. Choice 候选缺项：例如请求打电话却只有邮件候选，补充能表达原话的候选，并重新冻结候选 ID 和顺序。
2. Choice 表达含糊：例如 quiet 同时可能表示静音或减小音量；不能凭原 source gold 补造上下文。需明确判定标准、形成有修改声明的派生题，或提出同 split/family 约束下的替换方案。
3. BoolQ 证据不足或问法含糊：仅凭违法不能推出行为不发生，接吻也不能自动等同正式交往。应把问题改成段落能直接支持的命题，保留原问题及修改记录；不能把“不知道”硬标为 false。
4. 256 题已全部审核；下一步修订题形成新数据版本，仅对变更题复审。当前两次补取已终结，新的修题复审须另有明确范围与窗口，不能自动补采。

准入闭合后，仍按预先固定的一次 24576 GPU updates、LR0.001、seed17、strict1024、零 head、encoder 冻结执行，随后独立进程重新 GPU 编码、完整 CE、逐题输出及 16 条训练 Choice 换序，再做原 32 题回归。不能依据已曝光 development 调参，不能用加步数掩盖题目歧义，也不能将训练拟合等同泛化质量。正式人审、许可发布、校准、封存、新 head AOT/性能仍各自待验。

## 工程、资源与证据

工程已分别本地提交 `e6f7979`（容量/存储）和 `d30655f`（v2 训练/独立验证），详见对应工程证据。256 条实际 tokenizer/renderer 全通过，最长 914 tokens；小参考和 fixture 不冒称真实训练。原 32 题审核绑定的 3 条修正已验证。GPU 本轮容量、训练、加载验证和回归均未启动；没有停止已有 GPU 服务，旧空闲显存快照不代表当前可用量。

完整审核后收口对 86 份本任务回执的 403 条身份记录、331 个唯一 PID 精确查询，matching_live=0、unknown=0；checker 工具 exit0，另一次查询确认 checker 和打包进程均已退出。82 条历史身份缺失完整 argv，当前退出不能补造历史进程观测；未观测到的短命后代仍是审计范围局限。专属 compiler-temp 的两份日志按 hash 移存，七个空目录非递归删除；失败日志及构建交付物保留。regression publish 未指定专属 TEMP，未枚举或清理全局 TEMP。

真实打包程序从 33 份原响应重新严格验证全部记录，保存 78 条拒绝清单，因 Unclear/non-determinate/revision-needed rows 返回拒绝；外层明确 exit3，manifest_exists=false、training_started=false。此前 ledger 的独立重解析同样核对原请求 hash、保存结果与真实响应一致、重复成功题号为零。

证据由 [首窗口235文件](teacher-expansion-20261009-final/inventory.json) 与 [完整审核补充41文件](teacher-expansion-20261009-complete/inventory.json) 组成，后者绑定前者 inventory hash。包含真实请求/响应、失败、完整分母、修订建议、窗口授权、队列交接、准入拒绝及进程收口；新窗口 worker 版本已保存源码快照。归档按原字节复核 SHA-256。所有正式验收保持 false/pending；只本地提交，没有 push、发布、部署或模型上传。
