# Sezika.Tooling

C# / .NET 10 工具迁移入口，提供进程回执核查、完整 SHA 清单的 TAR 解包、有界原生执行，以及类型化任务、资产、语料和验证命令。入口和请求见[当前工作流](../../docs/tooling-current-workflow.md)。执行器使用结构化参数、Windows Job 与 Linux C# broker/watchdog，详见 [执行合同](Execution/README.md)；各项实际验收按根 ROADMAP 记录。

## 构建与检查

在仓库根构建；原生发布是独立步骤，训练工具不强制 AOT：

```text
dotnet build tests/Sezika.Tooling.Tests/Sezika.Tooling.Tests.csproj -c Release --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet publish tools/Sezika.Tooling/Sezika.Tooling.csproj -c Release -r win-x64 -p:PublishAot=true --disable-build-servers -m:1 -p:IlcMaxDegreeOfParallelism=2
```

检查程序接受 `--output <新的任务专属目录> --maximum-checks 1..256` 和 `--suite`。baseline 保留原 32 项；执行器检查启动、登记和回收独占 C# fixture 进程，先 tiny 1 项，再按该准确源码版本的完整平台分母运行。分母、已验版本及新增加固状态见根 ROADMAP 和各套件证据，保留原始流、失败回执、`checks.txt` 和进程身份；各套件有数量和墙钟边界。运行已构建 DLL 的示例：

```text
dotnet tests/Sezika.Tooling.Tests/bin/Release/net10.0/Sezika.Tooling.Tests.dll --output .artifacts/tooling-checks-new --maximum-checks 1
```

## 核查已登记进程

```text
Sezika.Tooling process audit --log-directory <logs> --output <new-audit.json> --timeout-seconds 90
```

`--log-directory` 最多重复 12 次；每目录最多 256 个顶层回执及登记文件，最多 2,048 个身份和 1,024 个 PID，每文件最多 16 MiB。不递归枚举，不扫描全机进程。

兼容旧 Windows 回执的 `Processes[]` / 根 `Pid`、`Started`、`CreationDate`、`ParentPid`、`CommandLine`。零回执或无 PID 的回执拒绝。Windows 使用只读进程句柄；Linux 使用 `/proc` start ticks、boot ID、父 PID 和带 NUL 分隔符的原始 argv。Windows 回执不能在 Linux 上证明回收。

当前 C# 执行目录使用 `result.json`、`registered.json` 和可选 `registered-NNN.json` 快照。审计严格绑定最终身份与累计出生登记的双向覆盖、平台、父链、scope 和原始 argv；缺失子进程登记、歧义 JSON 字段及互相矛盾的 PASS 结论拒绝。报告的 `NativeReceipts` 保留原 `Status`、`ExecutionStatus`、`CleanupStatus`、退出码和错误；当前进程已退出不能把旧 FAIL/UNKNOWN 提升为执行成功。

原生回执检查使用 `--suite audit`：先 1 项，再完整 17 项（含后续2项降级保护）；原 32 项 baseline 独立保留。Windows/Linux Native AOT 审计见[首批证据](../../docs/evidence/tooling-native-audit-2026-10-14.md)及[降级保护](../../docs/evidence/tooling-audit-guard-2026-10-14.md)。调用者切换仍按 ROADMAP 验收。

缺字段、访问被拒和身份不一致保持 UNKNOWN；创建身份不同为 REUSED。报告仅保存 command line 的 SHA。LIVE 或 UNKNOWN 时退出 1，记录均已退出或 PID 已复用才退出 0。历史未观测根回执数量保留，不回写旧失败记录。

本命令只核当前已登记身份，不证明未登记子树不存在，也不执行进程终止；完整父链和子树回收仍是执行器 T1b 的责任。

## 解包完整归档

```text
Sezika.Tooling archive extract --archive <model.tar> --sha256 <trusted-sha256> --destination <new-directory> --output <new-extraction.json> --timeout-seconds 900
```

最多 1,000 个普通文件、1,006 个 TAR 条目、16 GiB 归档及展开数据。只接收未压缩 TAR；拒绝链接、设备、重复/大小写冲突、路径逃逸、链接祖先、Windows 特殊文件名及已有目标。

归档必须含 `files.sha256`：每行 64 个小写 hex、两个空格和 portable ASCII 相对路径，覆盖清单自身之外的全部普通文件。解包计算 SHA 后重读落盘文件，最后再次检查原归档 SHA。回执必须放在展开目录之外。

失败保留部分目录及 `.sezika-extraction-in-progress` 标记；成功才删除本次独占标记并输出 PASS。不删除用户目录或覆盖旧回执。解包通过不等于模型质量、训练或发布通过。

## 执行边界

各命令都有数量/字节上限、`--cancel-file`、Ctrl+C、墙钟检查和 10 秒进度。外部原生工作通过 `process run` 登记任务容器及精确身份；只读审计仍只核回执覆盖的身份。

可选 `SEZIKA_START_GATE` 对接旧 guardian：等候准确 PID 的 `go`，最多 160 次/40 秒，支持 `cancel.request`，通过后移除该环境值。此握手不启动解释器，不赋予服务器/发布权限。

旧 `Audit-TaskProcesses.ps1` 和 `Expand-RepositoryArchive.ps1` 原字节作为历史证据保留；现行核查/解包调用本 CLI，完整流程按[当前工作流](../../docs/tooling-current-workflow.md)接线。全部旧调用者及466条退役门槛须以实际 `inventory review` 结果验收，不能由命令实现或历史自检推断整个迁移完成。
