---
name: sezika-rental-training
description: 使用 Sezika C#/.NET 10 工具准备已授权限时 GPU 任务的完整资产、固定实验、优先保存下载及独立验收；适用于新实例或同一任务的故障收口。
---

# Sezika 限时 GPU 任务

先读仓库 AGENTS、ROADMAP、HANDOFF、dirty/index 与真实输入证据。按当轮用户范围准备资料、固定实验和验收，不自动扩量、加训、购买、续租、发布或复用旧连接。全量仓库训练默认转用 sezika-repository-training；本技能保留已批准固定实验的独立合同。

当前实现统一使用 [C# 工具调用合同](../../../docs/tooling-current-workflow.md)。旧技能与参考的原始字节见 [历史快照](../../../docs/history/tooling-skills-20261014/manifest.json)，其日期/主机/输出与成功值不作为新任务默认值。具体工具和实验范围见 [工具与合同](references/tools-and-contract.md)，故障处理见 [证据纪律](references/lessons-and-evidence.md)。

1. 冻结新 run、三个输入提交、逐文件 SHA、来源/许可及实际期限。记录租期末、用户提前目标、保存/下载/独立验收余量；没有新实例与当前连接授权时继续离线准备。
2. `task prepare`、`bundle build/freeze`、`archive pack/extract` 准备完整工具和资产，逐个核 PTX/ABI、权重、tokenizer、数据与账本。小型探针包不能称完整模型包；凭据不进包、日志、URL或 argv。
3. RemoteTool 使用准确 host/user/port、主机键 pin、唯一远端根和固定 C# Native AOT worker；SFTP 断点、上传、完整读回分别核。认证只经有界 stdin 或明确安全存储目标，不生成 askpass/helper 或 shell。
4. 先真实宿主/磁盘/GPU/driver/UUID/现有使用者预检，完整输入合同及 tiny/profile；预算覆盖全部剩余阶段、保存、运输和回收。GPU并发1，不停止他人消费者，不重置或升级设备。
5. 唯一训练使用不可变计划，恢复先核完整 checkpoint、optimizer/cursor及全部输入。STOP留给trainer安全保存，CANCEL由执行器硬取消；失败保留现场，不删除训练目录重训。
6. 新模型保存后优先下载/读回，再用新进程独立加载并验证。本机补齐冻结 encoder/tokenizer 仅适用于该训练合同仍冻结它们；完整微调不得用旧 encoder 替代新权重。
7. 收口核 payload/执行器退出、精确任务身份和资源回收，记录未执行分母和 NOT_RUN。模型绝对路径、真实期限/完成时间与许可、质量、校准、AOT、性能分别报告。

Windows只用PowerShell7；外部工作通过结构化 ExecutionRequest，数量/墙钟/取消/进展双界，先核循环比较再 tiny。工具发现限 PATH/配置/固定路径；禁止 Graphify、按名称杀进程、共享 TEMP 清理。临时对象核绝对路径及任务归属，只清本任务完整树。历史 UNKNOWN 不补造成功，现场、协议、本机 simulation 与发布可见性证据分开。
