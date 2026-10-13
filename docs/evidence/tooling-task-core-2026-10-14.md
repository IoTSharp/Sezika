# T3 任务与断点基础切片

日期：2026-10-14，Asia/Shanghai。候选以 `c496974e3e3af6843547b167bedabc16594ffef8` 的准确索引导出为基础，只增加两个任务实现、专属检查及两个共享入口的本切片版本。其余未验模块和训练优化不进入候选。

## 已验证范围

独立候选位于 `C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\task-core-candidate-v1`。`compiled-source-manifest.json` 固定五份实际编译源码，共 **51,049 bytes**。提交前逐份核 SHA 与准确 Git blob；共享 `MigrationCommands.cs` 和 Tests `Program.cs` 仅暂存本切片接线，工作树中的其他接线保留。

| 门槛 | 结果与准确回执 |
| --- | --- |
| Windows 独立构建 | PASS，零警告/错误；`win-build-result.json` |
| Windows tiny / 全部任务检查 | PASS 1 / 11；`win-checks-1-result.json`、`win-checks-11-result.json` |
| 原有基础回归 | PASS 32；`baseline32-result.json` |
| Linux 自包含检查发布 | PASS；`linux-publish-result.json` |
| Ubuntu WSL tiny / 全部任务检查 | PASS 1 / 11；每项同时核 `linux-checks-N-inner-result.json` 和 `linux-checks-N-outer-result.json` |
| Windows Native AOT 发布 | PASS；`aot-win-publish-result.json`，原生 PE32+ / CLR directory RVA=0，完整映像 SHA 在 `native-image.json` |
| 实际原生宿主 / 任务准备 / 四文件检查 | PASS；`native-host/prepare/checkpoint-execution-result.json` 及对应命令回执 |
| 原生错误身份负例 | 原执行 **FAIL / exit 1**，精确 `InvalidDataException: Complete pinned checkpoint input identity differs.`；`native-bad-checkpoint-execution-result.json`，按预期拒绝验收通过 |

所有上述执行回执均核输入/输出 EOF、完整出生/argv/父链、清理 PASS 和 remaining 0。Native AOT 构建固定单节点、关闭构建服务器、明确 MSVC linker/lib 和 `IlcUseEnvironmentalTools=true`；实际 SDK 的 `cmd` 编译助手保留在进程树回执中。原生运行路径独立于编译助手。

11 项检查覆盖准备配置、已有输出保护、不可变全局分母/保存预留、原始与复制 SHA、脚本/变化拒绝、独立 TAR 解包、遗漏文件、宿主与 GPU 状态区分、四文件/全局游标、计划身份变化及 moments 变化。断点夹具使用明确的三个字节权重与 moments，仅测试运输身份，不代表实际模型、optimizer 加载或 GPU 恢复。

## 仍未完成的门槛

完整 SDK bundle 的既有磁盘耗尽失败及缺失最终外层回执保留；本切片不解除该门槛。resident、Git/LFS、远程 worker、资产发布、参考模型与全量退役分别验收。正式训练、租机、模型/数据发布、部署、性能和质量均为 **NOT_RUN**。本切片完成不等于 T3 或 T8-code 整项完成。
