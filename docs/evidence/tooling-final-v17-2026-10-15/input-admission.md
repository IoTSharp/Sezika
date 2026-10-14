# T3 全量输入准入与真实 resident 启动门：本机验收

日期：2026-10-15（Asia/Shanghai）。v17 冻结源码，两平台 Native AOT 客户端的实际 tiny/full 检查通过；共 30 次检查执行。原生退出、EOF、资源清理和逐项 SHA 见[验收回执](input-admission.json)。

完整 corpus 资产、policy/complete-scan/严格长度/父图与底座 readback，只读固定计划和实际摘要；真实 resident 在 GPU 启动前强制同计划及独立 profile 匹配。两平台 full14，包括真实 CLI 与 resident 共用 helper；没有运行真实 RunCore/GPU。不重算不可用的历史 scan-plan/chunk payload，不授予训练、许可、质量或发布批准。

Windows 与 Ubuntu WSL 的本地、fixture 和回环结果按记录范围成立。整体调用者退役和 T8-code 仍由最终检查单独确认；租用服务器、正式训练、批量教师、模型/数据发布均 NOT_RUN。旧失败、历史 UNKNOWN 和原始证明保持原字节。

另在同一 v17 runtime 上完成两平台 resident CPU 模拟 tiny1/full8，共4个实际客户端、18项检查。原生退出/EOF/所属进程清理通过；两平台审计当前 live0/unknown0。CPU 首项汇总行误读与 Linux 审计外层参数数目错误保留原件，已通过项未重跑；实际 GPU/训练仍 NOT_RUN。
