# 修题后 v3 工程与长度准入

2026-10-09，Asia/Shanghai。本会话 `01a11f25-335a-7571-a735-7171ace8c3b9` 从 `main / 4e7b304` 接管必要工程。原215题包完整保留。**C#合同、执行报告与Linux构建已完成，strict1024准入失败；尚未启动GPU训练、独立加载或原32题GPU回归。**

原生合同明确使用 `sezika/teacher-labelled-experiment-data/v3`、215/train162/dev53，不把v3当固定256题的v2。读取器绑定七个原文件的bytes/hash、完整256派生账本、固定父记录SHA、原33审核与7修题盲审的40组实际wire bytes，以及14组修题原始request/response。178复用、37修订、41删除，来源、source gold、state、family/split保持。两项语义排除的null修题hash仅以准确ID、理由和原始响应hash单独绑定。Catalog继承的256/192/64汇总明确属于父快照，实际数组及新manifest为215/162/53。

数据manifest SHA256 `E8E74CE0BBF1A72DA5FCDF4DCAD9D302513DC5DC42CB548BFD21D2569621AEB6`、账本SHA256 `55F3226946831C4B9CBD7F6EE10D1194764860C8343A9386CAA40C678CE24EC0` 均保持。独立审计核332个inventory条目/8,281,934 bytes；含inventory的333个永久Git对象与本地实际字节一致（8,339,780 bytes）。原18条argv缺失历史保留。

v3训练/独立验证报告分别绑定账本、实际分母和预计215+16=231次前向，仍限制384次；这些是实现及合同审查结果，**未获得本轮真实GPU执行结果**。固定24576次更新、seed17/LR0.001/strict1024/零head/encoder冻结保持。访问证明实现将使用实际连续commit回调及已审核的核心XorShift32/Fisher-Yates固定顺序重放，明确不是直接观测kernel选择ID；固定配置参考推算162题对应151完整循环+114前缀，114题将访问152次、48题151次，不称128完整epoch。独立新进程重编码及原修正32题回归工程已准备，Score没有新监督。

Release最终构建零警告/错误；v3合同1→24检查、旧v1八项/旧v2十六项兼容检查及一题H4 CPU微型参考通过真实退出码验证。预算守卫逐次检查取消/墙钟，每250ms核进程resident和peak≤4GiB，避免每guard调用OS查询；JSON/数据计数及所有时间上限保持。Linux-x64/.NET10.0.12自包含trainer、H4容量fixture、原32回归工具三项publish真实exit0，源码与产物身份冻结；这是managed工具构建，不是新head Native AOT或性能验收。

## 实际长度阻塞

正式renderer与固定tokenizer全215题检查：attempted215、accepted214、failed1、unattempted0，真实CLI exit1。通过题最长910tokens，是本版本新测量；旧256题914不是本版本结果。失败题 `massive-zh-cn-02996` 为train/zh/Choice。失败行token_count=null，保留错误码 `decision_v1_token_budget_exceeded`，不会用成功行的最大值替代完整失败分母。

独立一题C#参考以同一正式builder/tokenizer读取父题与修订题：父题482tokens，修订1037tokens，增加555。state保持51tokens；question225→483、candidate JSON198→494、控制项8→9，候选4→5。8192仅用于这次tokenizer计数诊断，没有改变生产1024上限，也没有encoder forward、GPU或教师调用。

具体可审阅方案是排除该一题并保留新删除账本，得到214/train161/dev53、Choice139/Boolean75，固定24576更新配置保持。该方案尚待用户明确选择；原交接指定215/162/53，不能把214冒充215，也不擅自改题、缩短文本、提高上下文或补发教师。原215包未修改。后续需要准确新版本身份、账本与分母合同及准入，再开始唯一固定实验。

## 资源与保留项

任务根 `.artifacts/repaired-training-20261009`；新远端根 `/root/sezika-experiments/20261009-repaired-training-v3` 只bootstrap，没有上传数据或运行GPU。最初只读预检核准确GPU UUID，显存空闲15294MiB、既有三个GPU服务保持；该数值是本轮预检快照，后续GPU阶段仍须重新核资源。教师调用0次，旧72/768训练及32/256审核均未重跑。

原未跟踪用户文档与暂停源码保留，ROADMAP旧诊断hunk不暂存。短命CPU/Git进程有guardian CleanupFailed/unknown1，真实child exit0及具体PID/start后核另存；不改写原回执或补造历史argv。初次合并前编译失败、两项null账本校验失败、30秒守卫开销超时、辅助脚本字段/布尔值错误及首次长度中止均保留，最终验收引用修正后的确切回执。

AI审题、合同/构建通过不等于模型真实正确率。正式人审、教师输出许可发布、质量、校准、封存、新head AOT/性能均pending/false。仅本地分项commit，不push、发布或部署；heartbeat只核真实交接和用户新选择，未变状态不重复派单、采集或训练。

本地工程提交：`afcca10`（原生v3数据/账本绑定）、`e808191`（报告、访问参考、长度失败分母）。新永久工程镜像 `docs/evidence/repaired-training-20261009-engineering` 包含206个字节原样文件/873,396 bytes，另有inventory；实际复制hash核验通过，不包含模型权重或运行时二进制。34回执/151登记身份/110唯一PID终核matching_live0/unknown_current0，closure reader随后另核absent；该分母不包括未同时登记OS身份的短命只读工具命令，不宣称完整历史观测。原closure字段 `historical_argv_observed` 的含义不精确，单独纠正sidecar明确仅表示recorded argv存在，spawn argv不冒充OS argv，原字节保留。

三个专属compiler-temp根检查24项：6个确切任务日志按hash移存，仅非递归移除确切空目录，全局TEMP/共享缓存/用户文件未动。子审查一次编译临时目录清理被自动审核在CreateProcess前拒绝，后续使用核绝对路径、hash移存及空目录非递归删除的更小范围方式真实Succeeded/exit0；保留原拒绝及审查证据写入稍超4分钟估算的局限。最后远端只读终核确认新root没有upload/data/bundle/capacity/regression或任何GPU stage；现有三个GPU服务保持，显存空闲仍15294MiB，仅当前快照。

原样镜像的 `Invoke-BoundedProcess.ps1:262` 保留源文件已有的一个行尾空格，完整 `git diff --cached --check` 因此真实exit2。补充原始stdout日志 `resume-doc-diff`、`resume-whitespace`、`third-doc-readonly-review` 也保留Git diff的空白上下文及该行。仅这四个确切原始证据副本有已核对的空白例外，其余暂存文件按默认规则检查真实exit0。单次 `core.whitespace=-blank-at-eol` 不能覆盖仓库 `.gitattributes` 的证据whitespace属性，检查仍exit2，原回执保留，不声称全量检查通过；不修改原证据、hash或仓库配置。上方34回执终核是06:04 UTC快照，后续归档、暂存与复核的准确登记PID/start另在 `repaired-training-20261009-closure` 补充，原guardian失败回执保持。

提交前补充终核43回执/179身份/131唯一PID，matching_start_live=0；probe与原closure reader均另核退出。补充镜像保存4个脚本/报告、24个新增原始回执文件及inventory，逐字节SHA匹配；11个暂停/用户文件及原v3 manifest/账本身份保持。该快照不包含随后提交进程，提交后仍须按准确PID/start再核；不补造短命进程历史OS argv或未知后代。
