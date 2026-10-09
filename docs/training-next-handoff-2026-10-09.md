# 修题后数据准入与固定 GPU 训练交接

更新：2026-10-09，Asia/Shanghai。规范工作区 `D:\source\Sezika`，用户明确要求完成 sonnet_vip 修题/删题后新建会话开始下一步。新会话创建后独占 Git/GPU/API 调度，旧会话停止实施。只本地 commit，不 push、发布、部署或上传模型平台。

## 数据与已完成工作

最终实际题量、分组、manifest hash 以 `docs/evidence/teacher-repair-20261009-final/final-summary.json`、`data-v3/experiment-manifest.json` 及 `docs/evidence/teacher-repair-2026-10-09.md` 为准。原256题中的178通过题复用原审核；78问题题由同一 `sonnet.vip / gpt-6.1-sol / max` 提出修题或删除，修订题再独立盲审。删除表示从新版本排除，原文本、source gold、来源证据及删除理由全部保留。数据使用实际分母和原 family/split，不补题、不重分组。

永久包 `docs/evidence/teacher-repair-20261009-final/data-v3`，工作包 `.artifacts/teacher-repair-20261009/data-v3`。原样归档中的绝对路径仍绑定工作包，复制时不能悄悄改写声明或原字节；C#需核实际文件名、bytes/hash及输入根。`derivation-ledger.json` 保留原256全分母。`source-catalog.json` 保存保留题的来源；旧源文件仍在 `.artifacts/teacher-expansion-20261008/sources`。

已有静态打包检查不是原生C#合同或长度验收。原256最长914tokens仅是旧版测量，新题instruction/candidates已改变，须重新使用正式renderer/tokenizer逐题strict1024验证。AI审题通过不是正式人审、模型质量或许可发布通过。

旧72步、768步A/B/C、原32审核、原256审核与本轮修题/盲审均禁止重跑。全部教师窗口已结束；除非用户另有新请求，不再调用教师、扩量或循环修题。

## 新会话立即执行

1. 先读 AGENTS.md、HANDOFF.md、ROADMAP.md、git status、本交接、最终证据与进程回执；核新包hash与来源。检查现有运行任务，不重复已完成阶段。
2. 实现最小必要 C# v3 合同：`sezika/teacher-labelled-experiment-data/v3`、实际train/dev分母、来源/响应/修订账本绑定、报告与CLI验证。保持旧v1/v2严格合同，不把v3冒充v2。重点检查 `tools/Sezika.ExperimentTrainer/ExperimentData.cs`、`ExpansionChecks.cs`、`ExpansionEvaluation.cs`、`ExperimentReport.cs`、`Program.cs` 及其实际引用；查找所有256/192/64/272/128epoch固定假设。
3. 先做相关合同反例、小参考与strict1024全量检查、源码审查，再发布Linux工具。不得用Windows exit0、fixture或旧前向代替训练。用户已经授权接续，必要工程/测试/训练不需逐项再问。
4. 固定一次 **24576 GPU updates、LR0.001、seed17、strict1024、零初始化head、encoder冻结**。实际train数改变后按24576 updates执行并如实记录样本访问，不再声称128完整epoch；禁止根据dev调参或自动加训、LoRA、encoder反传。
5. 保存head/checkpoint/report，独立新进程重新GPU编码并加载磁盘权重，完整CE、train/dev逐题、16道训练Choice固定换序检查。按新N记录前向数（N原题+16换序），每阶段最多384forward；CLI≤1500秒，SGD≤300秒。原修正32题另行回归，≤32forward/300秒，保持已曝光回归身份。Score没有本轮新增监督，不冒称五级质量通过。
6. 实际资源/退出码/身份/清理、源码与结果审查后分项本地commit，更新HANDOFF与证据；本次固定训练/独立验证完成后停止新增训练，等待用户新指令。

## GPU、工具与资源边界

福海 `root@211.149.170.42:2222` / `digisyn-gpu01`，GPU UUID `GPU-e786e313-bc92-a63d-a833-f4e48cbafb66`。每阶段先核真实资源：GPU并发1、owned≤3GiB、host≤4GiB、free≥5120MiB。15294MiB只是旧快照。用户已许可必要时停服务，但须先核准确PID、启动时间、argv、父链及影响；资源够用就保留已有服务。

冻结encoder package：`/root/sezika-experiments/20261008-real-head-v1/package`。旧扩量根 `/root/sezika-experiments/20261008-teacher-expansion-v3` 仅bootstrap，未上传/训练；其脚本含旧thread身份，不能直接冒充新会话。新会话建立准确新任务根/身份，传输和阶段运行都核hash、状态和真实exit。可参考 `.artifacts/teacher-expansion-20261008/remote`、`Prepare-ExpansionTransfer.ps1`、`regression-linux`、`regression-input`，按新v3增加账本文件，不能照搬旧固定分母。

模型、算子、调度C#；批量训练/特征/评价GPU，CPU仅tokenizer/合同/小参考。SSH脚本用单引号here-string，经 `C:\Users\mysti\.codex\skills\usm-gateshell-ssh\scripts\invoke-ssh-bash.ps1` stdin传入并传播真实退出码。Windows固定 `C:\Program Files\PowerShell\7\pwsh.exe`，先核版本；PATH/明确配置/已知路径发现工具，禁止广域扫描和Graphify。

所有批处理/轮询同时设置数量与墙钟上限、取消及进度，先小输入探针。登记PID/创建时间/argv/父链，用finally回收精确任务树，禁止按进程名批杀。临时对象核绝对路径和归属后处理，保留证据/用户文件/共享缓存；若委派必须逐项重述这些约束与准确文件归属，Git仍单一监督者所有。

## 保留项与独立验收

保留其他暂停源码、`ROADMAP.md`旧诊断hunk、用户未跟踪文档。原已提交工程e6f7979/d30655f与教师证据10dee08/7964593不回滚。当前只有数据AI审查结果，正式人审、教师输出许可发布、质量、校准、封存、新head AOT/性能另验，保持pending/false。不得将训练拟合率、分布集中度或教师一致率混称真实正确率。

heartbeat `sezika` 将由旧会话迁到新会话，每30分钟仅检查断线交接；无变化安静，实质完成/失败/证据损坏/新用户处理事项才通知，不因触发重新派单、重复训练或教师请求。
