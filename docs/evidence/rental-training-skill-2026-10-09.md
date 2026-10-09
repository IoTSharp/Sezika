# 租用 GPU 训练技能验收

2026-10-09，Asia/Shanghai。按用户要求，将本轮完整准备、预检、唯一固定训练、优先模型下载、独立验收及故障收口整理为仓库技能：[`sezika-rental-training`](../../.agents/skills/sezika-rental-training/SKILL.md)。技能与两个参考文件复用现有 `tools/rental-training/`，不携带权重或认证资料。

技能元数据通过官方 `skill-creator` 的 `quick_validate`；5 个引用路径及 `agents/openai.yaml` 检查通过。独立智能体另以未联系的 TEST-NET 地址、新用户、新 GPU、新日期和四小时租期进行离线行为审查：技能行为 **PASS**；实际新实例执行准入 **FAIL_NOT_READY**；SSH、传输、GPU、训练均 **NOT_RUN**。这是一项假设场景审查，不是已购买或已运行的新实验。

审查使用实际 PowerShell AST 参数与源码合同确认：现有入口没有统一的 task ID/remote root 参数；新远端根不能通过下载器的旧任务正则；验收器仍绑定旧 GPU；旧 deadline 在新场景下过期。技能明确要求先统一这些绑定、用极小输入检查，再接入新实例；仅改 `TaskDirectory` 不足。未来任务仍需真实认证、GPU 预检、完整资产和时间预算。当前技能的通过不等于现有脚本已经成为任意服务器的通用入口。

完整离线审查回执保留于 ignored 任务目录 `.artifacts/skill-rental-training-20261009/independent-behavior-review.json`，SHA256 `C2DBF87769F21705DA4DCB5A70A02A3355A0ECEE533061835577ABD667CD17F9`。检查 12 个唯一文件，审查进程 PID 37464 正常退出，没有网络、Git、SSH、GPU 或源码写入；仅保存允许的审查 JSON。创建技能的验证任务也已精准核实退出，历史短命进程 unknown 原回执保留。

本轮训练、模型和原始报告保持原字节；真实完成时间与质量边界见[租用实验完整证据](rental-training-2026-10-09.md)。技能保存于当前仓库，没有安装到用户全局技能目录。
