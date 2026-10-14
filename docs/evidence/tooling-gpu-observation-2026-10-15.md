# T3 有界 GPU 观察入口验收

日期：2026-10-15（Asia/Shanghai；实际运行回执为 2026-10-14 UTC）。

`host sample-gpu` 已接入 C# 工具。请求固定原生可执行文件 SHA、完整 GPU UUID、最多120个串行样本、采样间隔、总墙钟、单次查询和清理预留。报告保存每次请求、原始流、原生回执及 SHA；未知清理、EOF缺失、超量输出和解析错误均保留失败，不自动重试。输出回执必须在观察目录外，错误回执也遵守此约束。

Windows 与 Ubuntu 的同一冻结源码通过 Native AOT 发布，两平台分别完成20项 synthetic 合同检查和1项真实 native input-echo 检查。检查覆盖UUID与九列CSV、NaN/单位/缺测、多卡及物理范围拒绝、统计分母、节拍、取消、失败与原件保全。独立 native echo验证真实执行器与流回执；其内容仍是明确的合成观测数据。

此次没有查询实际GPU，未接受训练、性能、SM occupancy、模型质量或服务器实测。旧异常分类遗漏、Windows取消信号与目录大小写别名、Linux亚毫秒等待耗尽的原FAIL均保留。后续Windows Job快照问题另行修复和验收。

固定源码、四份原始结果的逐字节副本、实际出处和检查列表见 [验收清单](tooling-gpu-observation-2026-10-15/acceptance.json)。本提交只纳入GPU入口及其必要保护；共享入口的安装、Git和语料增量另提交。全量调用者切换及T7/T8-code仍待收口。
