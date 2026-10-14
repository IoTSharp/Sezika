# T1a 原生回执审计独立验收

日期：2026-10-14（Asia/Shanghai）。本切片扩展只读 `process audit`，兼容当前 C# 执行器的完整最终回执及登记快照。它只核查回执覆盖的当前进程身份，不证明任意未登记进程不存在，不把旧执行失败改成成功，不代表全量调用者迁移完成。

## 实现与候选

固定基线 `a23a8087aa408c2d23151bc56880efd181cf5779`。独立候选位于 `C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\native-audit-candidate-v1`，从该 HEAD 导出，加入两个限定源码及测试入口的最小接线；未加入工作树中其他待验模块。

| 编译源码 | 字节 | SHA-256 |
| --- | ---: | --- |
| `tools/Sezika.Tooling/ProcessAudit.cs` | 22,285 | `45a54cf05045d619069e358505937ffc268818ba01686f7a7f9a9190d1260fbf` |
| `tests/Sezika.Tooling.Tests/NativeAuditChecks.cs` | 13,870 | `43aee1ae78fdcd05c7971b63891a84e9fe01be4b8e7eecd07a492eb384c9bcdf` |
| `tests/Sezika.Tooling.Tests/Program.cs` 候选接线 | 14,813 | `edee54c894d88b7c6ae08d73628e2bcb3f42b3b53dc2af734effcb7a68433068` |

原生 JSON 必须包含明确身份及结论字段。最终结果与所有有效登记快照逐项核对出生、平台、父链、scope、原始 argv；累计登记必须反向覆盖所有最终子进程。检查包含删去唯一子进程登记但保留两进程最终结果的负例，拒绝发生在观察前，并保留原结果字节。

报告保留原 `Status`、`ExecutionStatus`、`CleanupStatus`、退出码及错误。登记快照不重复计数。Linux exec 前后 argv、boot ID/start ticks 与 Windows 微秒创建时间保持各自语义，跨平台身份不冒充当前观测。

## 实际检查

| 门槛 | 结果 |
| --- | --- |
| Windows Release 独立候选构建 | PASS，0 warning / error |
| Windows audit tiny 1 / full 15 | PASS / PASS |
| Windows 原 baseline 32 | PASS |
| Ubuntu WSL audit tiny 1 / full 15，实际 Linux .NET 10 FDD | PASS / PASS；内外层分别通过 |
| Ubuntu WSL 原 baseline 32 | PASS；内外层分别通过 |
| Windows x64 Native AOT 实际发布及原生执行 | PASS |
| Linux x64 Native AOT 实际发布及原生执行 | PASS；内外层分别通过 |

Windows 原生映像为 12,239,360 字节，SHA-256 `bb33455efd974fd800f17e2c328a99caa3f986ec74e93399827814c50feff6ed`，PE32+ 且 CLR directory RVA 为 0。实际原生审计读取本次真实构建回执及真实失败的参考导出回执：6 个身份 ABSENT、LIVE/UNKNOWN 为 0；第二份原结论保持 `FAIL / FAILED / exit 2`，清理状态保持原 PASS。

Linux 原生映像为 12,752,544 字节，ELF64 x86-64，SHA-256 `5e45152ec1b4fefa819ccc55318224905dcb9e661a930441ae6cb83b11f0f179`。实际审计读取两份真实 Linux 原生编译回执：31 个身份 ABSENT、LIVE/UNKNOWN 为 0。另有 JIT 读取真实 worker 构建回执的检查：16 个身份 ABSENT、LIVE/UNKNOWN 为 0。原结论均按原值保存。

所有通过的执行回执均确认输入和输出完整、退出码 0、清理 PASS、剩余进程 0。构建串行、`-m:1`、关闭构建服务器，AOT 编译并行上限 2。Windows SDK 自身的编译助手按真实进程记录保留；此证据不声称整个 SDK 构建树没有解释器。

## 失败、保留与边界

- 第一版 Linux 启动请求因 PowerShell 数组中的拼接表达式形成错误 argv 而 FAIL；内层未启动。原请求、stderr 与外层失败保留。第二版使用逐项括号和准确参数数量验证，再从 tiny 1 开始通过完整检查。
- 本次旧 worker 所用 audit 源在修复后通过精确逆转四行补丁恢复，22,010 字节 / SHA-256 `101b2e5c5d369bb1ccdaa35092a15b411847ae188a296650a7d63aeb8a60f74f` 与旧源码清单完全一致；标明为事后准确恢复，不冒称旧时已备份，未改写旧清单或回执。
- 仅回收已结束且核实归属的任务编译 obj。原生 exe、PDB/dbg、源清单、全部原回执和失败保留；参考证明的 v6/v7/v8 生成源码未动。
- 本切片没有租机、服务器访问、正式训练、批量教师、模型发布或部署。全量 T1a 调用者切换和 T2–T8-code 仍由监督合同继续验收；服务器实测为 NOT_RUN。
