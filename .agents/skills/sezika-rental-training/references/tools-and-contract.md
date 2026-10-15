# 工具与固定实验合同

长期实现统一C#/.NET10，命令与typed请求见 [工作流](../../../../docs/tooling-current-workflow.md)。旧调度入口和当时215题/24576更新/期限的原始字节见 [原合同](../../../../docs/history/tooling-skills-20261014/sezika-rental-training/references/tools-and-contract.md)，仅用于追溯，不自动扩展当轮任务。

| 工作 | 当前入口 |
| --- | --- |
| 配置准备/完整冻结 | Tooling task prepare、bundle build/freeze、archive pack/extract |
| 全量输入前置检查 | task validate-inputs固定plan/policy/readback及完整资产；真实resident必须携带同计划InputAdmission，未通过不能启动；新增路径按ROADMAP实际验收 |
| 精确进程生命周期 | Tooling process run；Job或C#broker/watchdog，真实字节/argv/出生身份/父链与finally回收 |
| SSH/SFTP | RemoteTool固定主机键、typed upload/download/dispatch，独立NativeAOT worker及完整代码/runtime清单 |
| 输入仓库与发布 | 原生Git/LFS和C#HTTP准备/预览/授权写入/独立fresh读回 |
| 数据/教师与验证 | DatasetTool、TeacherTool、Corpus/Validation命令；不读取仓库loader/helper |
| 来源采集与模型资产 | source acquire固定输入/完整ledger；model mirror/verify固定四角色manifest、cache及完整SHA读回 |
| 初始化小回执 | task snapshot准确具名metadata字节，不重写原失败；提供方模型卡校验单列publication validate-card |
| GPU及保存恢复 | RepositoryTrainer及task resident，真实同卡profile与完整checkpoint合同 |

deadline记录真实创建时间/时区、租期末、提前保存目标、下载/回收预留和停止新增GPU时间；C#使用DateTimeOffset，PowerShell7只做交互时ConvertFrom-Json -DateKind String。历史日期不能复用，网络、字节与预算应从当轮探针决定。

预算覆盖剩余全部必要阶段、完整保存、运输及独立验收。GPU并发1，核实际UUID/driver/显存和已有消费者；不停止别人工作，不reset或修改驱动。余量不足保存并报告，不能自动缩数据或续租。

最低证据为源/包清单、许可、完整bytes/SHA、宿主/GPU身份、真实进程和原始流、前后资源、checkpoint恢复/完整模型、独立验证和期限。HTTP200、文件存在、退出0、CE下降都不能代替质量/校准/AOT/性能验收。
