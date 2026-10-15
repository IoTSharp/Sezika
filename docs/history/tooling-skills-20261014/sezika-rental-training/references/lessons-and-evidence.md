# 本轮故障与证据定位

2026-10-09 固定实验已经完成。本参考供恢复流程和改动相关 reader 时查阅；其中的任务名、SHA、时间只是历史定位，不能成为下一次默认身份或完成证明。

## 已观察的故障

| 现象 | 实际结论和下次处理 |
| --- | --- |
| 合同 runner 初始化 exit64、未执行 payload/GPU | 原具体失败行未确认；整数 `SECONDS < 1` 是可疑短窗，后续相同原 runner 的 `/usr/bin/true` 曾成功。保留原包/失败目录；v2 仅把 btime 读取改为单调3000ms、最多1024行，经独立 SHA 和小输入准入。不能写成已确认根因或借修复重训 |
| scp 两个路径被合成一个参数 | PowerShell 表达式在数组内直接拼接容易错误。先将远端路径赋独立变量，再传 `@($remoteArchive,$localArchive)` 两个数组元素；参数化小输入确认接收到两个 argv，再核实际下载 SHA |
| `.artifacts` 解包成 `artifacts` | `TrimStart` 按字符集合删前缀，会吃掉合法点。只在 `StartsWith('./')` 时用 `Substring(2)`，然后验证相对路径、允许的类型、上限与目标绝对路径。先用两文件探针验证隐藏目录布局；坏解包目录保留，从相同 SHA tar 解到新目录 |
| 215题 reader 误报82处错序 | 输入原行序不同于报告 train→dev 顺序。按唯一 record_id 核215题完整集合、原文与内容；对 seed/sample_visits 的训练顺序仍单独精确核验。先一题算术/二进制reader，再215题验收，不改原报告或模型 |
| UTC 日期二次 Parse | ConvertFrom-Json 可能已将 ISO 转成 DateTime；再次按本地文本 Parse 会移动 deadline。保持已解析类型及 offset，仅显式字符串做 RoundtripKind 解析，并核用户时区对应的绝对时间 |
| 短命下载进程 unknown、后续 PID 复用 | 原 `FinalCheckComplete=false/unknown1` 回执保留。补审使用登记 PID+创建时间+完整 argv+父链，20身份已退出、1 PID 被无关 MCP 复用，不能杀无关新进程或改写历史 unknown。只补记当前检查范围内 owned live0/unknown0及局限 |

以上修复涉及调度/读档；原训练、数据和模型保持原字节。失败即停后续 GPU、保留现场；不能删掉已有 train 目录再调用唯一训练。

## 可追溯入口

- [实验入口说明](../../../../docs/rental-training-2026-10-09.md)：完整包、认证、阶段和资源边界。
- [最终租用交接](../../../../docs/training-rental-handoff-2026-10-09.md)：真实模型保存时间、执行状态、失败及清理历史。
- [永久实验证据](../../../../docs/evidence/rental-training-2026-10-09.md)：输入/输出 SHA、资源、独立验收及结果限制。
- 仓库 `docs/evidence/rental-training-20261009-final/inventory.json`：95份远端原字节镜像和逐文件 SHA；原路径映射须以 inventory 为准。
- ignored 任务根 `.artifacts/rental-training-20261009-01a12099/`：`package-preparation.json`、`deadline.json`、`pipeline-*.json`、`model-download.json`、`verification.json`、原 process-logs、失败目录和原 tar；含敏感连接/argv，不能整体发布。
- 完整模型在该任务根 `trained-model/`；它是外部资产，不能复制进本技能、程序集、NuGet或代码 Git 提交。

历史结果：35.2160222秒固定训练、28.9658498秒独立231次重新编码、8.6122972秒32次回归；本机证据验收10/10 PASS。train 教师一致118/162、dev27/53、换序7/16及选中ID改变6/16；原32 Boolean8/10、Choice4/12、Score0/10。不能把这些值当作下次预期输出、正式准确率或发布准入。

用户当时给定创建时间为2026-10-09 20:22 +08:00，4小时租期末2026-10-10 00:22，提前目标2026-10-09 23:22；本机完整模型21:17:08保存并核 SHA。未来期限必须重新计算，不能沿用这些绝对时间。
