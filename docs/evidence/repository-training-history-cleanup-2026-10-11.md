# IoTSharp 模型公开分支历史清理（2026-10-11）

用户明确授权的 **IoTSharp/Sezika-Decision-Base `master` 历史清理已完成并独立匿名验收通过**。最新提交为 `9c522c4e227c52c86f493a90595a894a41e1935a`。本次只执行一次精确 `force-with-lease`，没有修改模型文件、私有仓库、标签或子模块；无需 GPU、旧服务器连接或新租期。平台旧对象与缓存的物理删除仍为 **NOT_VERIFIED**，本次未执行该项清理，不能推断旧 SHA 直链已失效。

| 检查 | 实测结果 |
| --- | --- |
| 精确旧 master | `c68049c735c24da5f1c611c2b229619bb4b391ac`，推送前全部公开 refs 仅此 master |
| 新 master | `9c522c4e227c52c86f493a90595a894a41e1935a`，推送后与独立匿名最终 refs 一致 |
| 内容保持 | tree 为 `d7743fde522c22352aee1048b29f303924f6b5b1`，与旧汇总版本相同，文件 diff 为 0 |
| 安全父提交 | `2a410d64a70955364439514cba60207b8b9ee796`，原介绍及安全初始历史保留 |
| 独立历史验收 | fresh 无认证 bare clone 全部 4 个可达提交；旧详细报告提交 `dfef170bc9e26dff7e4317fc08686d0abd013438` 和旧汇总提交均不在其中；`runs/encoder-full-20261011-121827/validation.json` 在公开分支全部历史中无记录；Git objects fsck PASS |
| 模型匿名验收 | 原 fresh 匿名 checkout 与独立 LFS 缓存读取新提交，未复用 private 缓存；55 个 owned 文件与 1 个保留文件、6,174,855,372 bytes 全部大小/SHA256 PASS；HEAD/origin/index/clean、LFS objects/pointers PASS |
| 进程收口 | publish PID 49188、readback PID 74496 均退出 0；两次 runner 终检完成，live 0 / unknown 0 / cleanup errors 0 |

资产根为 `C:\Users\mysti\.codex\artifacts\sezika-repository-local-mirror-20261011-192617`。原回执是 `history-cleanup-authorization-v1.json`、`mirror-state/transport-push-history-cleanup-publish-v2.json` 与 `mirror-state/transport-anonymous-readback-history-cleanup-readback-v2.json`；结构化证据见[本文件 JSON](repository-training-history-cleanup-2026-10-11.json)，汇总快照为资产根 `final-publication-readback-v3.json`。此前 v1/v2 发布快照、所有 FAIL 和私有详细报告均保留，不能改写其历史结果。

协同审查发现首版使用 `push origin` 未限制有效 push URL 与自动跟随标签，首版只执行本地 tiny 检查，未推送。保留该原件 SHA `7d8beead4d83db970f2a153641b4d772e58433bd738acffd6868803e7f1050ea`；最终 v2 SHA `656de54a2f6104d47303bf5882b089ac38802cff6ef049de3bae86bfa17b3b14`，改用显式目标 URL、禁用 followTags/mirror/子模块附带推送，保持普通 LFS pre-push hook。v2 tiny 与独立复核通过后，执行唯一一次有准确旧 ref 的授权改写；失败不自动再次写入。

私有模型底座18文件与本run33文件、私有数据275原始资产与8新增派生文件的完整验收及optimizer恢复证据保持[本机接续记录](repository-training-local-mirror-2026-10-11.md)。本轮仍为48步全参数短程实验；正式质量、校准、新权重 AOT/性能仍 **NOT_ACCEPTED**。模型加载与同题集正确率对比由新会话 `01a12b1d-ddfc-7693-94a5-9f245eb7db4d` 独立执行，本回执不预先宣称其验收结果。
