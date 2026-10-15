# 保存后的读回和模型镜像

文件运输、SHA、Git/LFS与逻辑重组不需要GPU。原始流程和历史发布结果见 [原文快照](../../../../docs/history/tooling-skills-20261014/sezika-repository-training/references/local-mirror.md)；下一任务重新固定ref、权限和准确发布payload。

在唯一任务根通过 `git run` 的固定Git/LFS native目标克隆准确源提交，认证从安全存储/内存进入任务环境，禁止持久helper。分别核Git ref、canonical pointer、LFS object和最终物理bytes/SHA；缓存成功不能替代工作树materialize。未知本地filter、未审hook或无关index拒绝。

使用 `transport verify/reassemble` 检查每个物理块及逻辑全流SHA，另在新目录重组完整checkpoint；原块、原失败和原始权重不覆盖。完整模型需要encoder/head/tokenizer/配置/来源许可/package/checkpoint/训练报告，不能只交head。

上游具名模型资产使用 `model mirror` 的外部SHA固定manifest，显式声明repository/revision/license及config、tokenizer、weights、license四角色，每个资产有原始bytes/SHA256；完整缓存亦须重验。完成后通过 `model verify` 在新进程核完整bundle。它只证明调用者固定资产集合完整，不推断模型质量、来源许可批准或公开发布。当前命令的验收范围以ROADMAP和分项证据为准。

小型原始回执使用 `task snapshot` 的准确路径清单采集；保留原FAIL/UNKNOWN原字节和分母，不从当前快照恢复历史可信基线。提供方模型卡YAML校验由 `publication validate-card` 单独执行，须当轮允许的网络范围；本机fake校验不算平台验收。

只复制审核过的模型白名单到已批准目标准确父提交。public只保留汇总验证信息，逐题ID/题文/候选/标签/概率/教师预测和loss留在private；private corpus/derived records/ledger不进公开模型。新汇总生成新SHA，原权重与optimizer保持原SHA。

`publication prepare/preview`只准备并冻结；upload/commit或Git publication须对应已授权不可变payload。标准native LFS pre-push不能绕过，网络写不确定先读ref。发布后用新的独立checkout/空缓存及适用认证完整读回owned和保留文件，再验逻辑optimizer全流。

当前树修复与历史/平台缓存清理是不同门槛；历史改写需要本次明确范围。文件运输PASS不改变模型质量、校准、新权重AOT或性能状态。历史授权不延续为新模型发布许可。
