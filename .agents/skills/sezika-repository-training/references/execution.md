# 固定输入到安全保存

命令字段和当前实现见 [C# 工作流](../../../../docs/tooling-current-workflow.md)；原始执行合同见 [历史原文](../../../../docs/history/tooling-skills-20261014/sezika-repository-training/references/execution.md)。历史租期、GPU、commit 和参数不得作为新默认值。

1. 固定完整私有数据发布提交及独立 fresh 内容回执，源来源提交不改。服务器按实际 PublishedDatasetCommit 读取明确派生路径，逐文件核 manifest/bindings/length-report/notices/shards 的真实字节。
2. `task prepare` 创建 run/三个commit/全局记录与epochs/最大token/checkpoint间隔/总预算及运输预留；没有服务器与训练授权时状态保持配置准备。
3. `bundle build` 使用 SourceRoot 下完整工程清单，包含项目、root props/global/许可、两个kernel compiler及PTX/ABI。冻结快照才是实际编译输入，dirty工程字节不能冒充已在SourceCommit中。发布和冻结最多5项目，输出新候选，原失败保留。
4. `archive pack` 后由 `archive extract` 在新目录完整核SHA。SFTP与RemoteTool dispatch只针对固定worker及已核C#payload。服务器先实际宿主/磁盘和GPU准入，再做RepositoryTrainer `profile-corpus`。
5. 先以 `task validate-inputs` 固定计划、policy和model-readback的路径/SHA，核完整corpus、complete-scan、严格长度及底座资产；`task prepare`纯配置不能代替它。真实 `task resident` 请求的 `InputAdmission` 必须匹配同一plan/SHA，启动前复用同一准入器；该新增代码门的实测状态以ROADMAP为准。随后 `train-corpus`/`task resident` 使用同一固定plan/profile、显式stage-updates与GPU；换卡重新profile，阶段不能超过全局剩余。所有更新、AdamW及order/global cursor保持同一身份。
6. STOP是trainer的保存请求，CANCEL是硬取消。保存余量取prepared配置与同卡checkpoint/最长step实测的保守较大值。预算不足拒绝新增训练，不降低全局分母。
7. 四文件weights/moments/checkpoint/corpus-cursor及全部Inputs必须一致；`checkpoint verify`只验字节、调度结构及游标，不声称实际optimizer加载或GPU执行。全局结束与所有来源EOF独立确认。

CPU模拟只通过内部测试入口，profile/report/资源及回执明确SIMULATION；正式CLI拒绝其作为真实profile。用户未开机时不触碰旧主机，不以旧现场成功补充新流程NOT_RUN。
