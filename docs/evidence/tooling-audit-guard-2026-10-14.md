# T1a：原生回执不能降级为历史回执

`process audit` 现在拒绝将改名为 `*.result.json` 的原生执行回执按历史身份记录处理。原生执行、归属、启动器与 exec 转换字段必须使用原始 `result.json` 和出生登记链；重复或大小写冲突字段也会拒绝。普通历史 `Status`、`CleanupStatus` 等元数据仍保持原身份核对语义。

Windows 与 Ubuntu WSL 的 Native AOT 检查均先运行 1 项，再通过完整 17 项。新增负控覆盖改名、矛盾退出状态、丢失原生字段、大小写别名及真实历史元数据。两份原始原生回执和完整检查输出保存在 [验收目录](tooling-audit-2026-10-14/acceptance.json)，该文件绑定准确两源 SHA、冻结构建清单及原件 SHA；原件没有重写为新状态。

这只验收上述降级保护。冻结 90 文件的整体集成结果仍为 31 PASS / 3 FAIL / 19 PENDING；fixture 准备失败及原始审计失败保留。迁移整体和服务器实测没有据此完成。
