# T3 前置：训练器保存与 resident 调用合同

日期：2026-10-14。本项独立提取工具调度必需的训练器合同，保留原HEAD调度、旧PTX/ABI、未优化构造器及GPU计算路径。未提交working tree的kernel、workspace、同步优化、新ABI或checkpoint迁移。

实际变化为resident/stop-file/checkpoint-every CLI参数、同卡profile完整保存耗时CheckpointSeconds、保守保存余量、阶段报告StopReason/CheckpointEvery，以及全局epoch结束时先核来源trailing EOF再落完成checkpoint标记。STOP在更新安全边界保存并返回partial，阶段上限和保存间隔不改全局TrainRecords×Epochs或AdamW游标；旧profile缺实测保存耗时时不能用于resident准入。

四个候选文件共70,384 bytes，从准确索引导出到 `.artifacts/tooling-migration-20261013-01/trainer-controls-candidate/source`。共享保护文件只暂存基于HEAD的明确合同hunks；工作区原始文件保持原字节。实际编译字节/SHA256/Git blob逐项对应，清单为候选目录 `compiled-source-manifest.json`。

独立RepositoryTrainer Release构建零warning/error。既有纯CPU `corpus-schedule-check` 实际68项PASS，`corpus-source-policy-check` 24项PASS；原生payload退出0、任务树回收0。各回执为 `build-result.json`、`schedule-cpu-result.json`、`source-policy-cpu-result.json`。68项和24项分别证明合成调度/游标/存储负例及离线policy/binding合同，保持GpuTrainingRun=false、RealCorpusValidated/Reviewed=false。

新Tooling controller的CPU原生进程模拟另在 `separate-cpu-resident-full-result.json` 完成8项内外PASS，包括配置到首保存、精确输入恢复/全局合成游标、源/模型/数据/预算拒绝、错误profile/checkpoint/report、STOP安全保存与CANCEL回收；这是单独尚待提交的controller源码范围，不能把它当成本项训练器真实GPU执行。

实际GPU profile、更新/保存/恢复、跨百万步optimizer数值与性能仍为NOT_RUN；本项只声明源码调用合同、独立构建及上述CPU范围。真实Inputs中的既有uppercase backward摘要必须保持原身份，不为适配新工具改写历史checkpoint。
