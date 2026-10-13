# Sezika.Tooling

C# / .NET 10 工具迁移入口，提供进程回执只读核查、带完整 SHA 清单的 TAR 解包及有界原生进程执行。执行器使用结构化参数、Windows Job 与 Linux C# broker/watchdog，详见 [执行合同](Execution/README.md)。SSH、上传及训练调度分别按根 ROADMAP 验收。

## 构建与检查

在仓库根构建；原生发布是独立步骤，训练工具不强制 AOT：

```text
dotnet build tests/Sezika.Tooling.Tests/Sezika.Tooling.Tests.csproj -c Release --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet publish tools/Sezika.Tooling/Sezika.Tooling.csproj -c Release -r win-x64 -p:PublishAot=true --disable-build-servers -m:1 -p:IlcMaxDegreeOfParallelism=2
```

检查程序接受 `--output <新的任务专属目录> --maximum-checks 1..256` 和 `--suite`。baseline 保留原 32 项；执行器检查启动、登记和回收独占 C# fixture 进程，先 tiny 1 项，再 Windows 18 项 / Linux 17 项。保留原始流、失败回执、`checks.txt` 和进程身份；各套件有数量和墙钟边界。运行已构建 DLL 的示例：

```text
dotnet tests/Sezika.Tooling.Tests/bin/Release/net10.0/Sezika.Tooling.Tests.dll --output .artifacts/tooling-checks-new --maximum-checks 1
```

## 核查已登记进程

```text
Sezika.Tooling process audit --log-directory <logs> --output <new-audit.json> --timeout-seconds 90
```

`--log-directory` 最多重复 12 次；每目录最多 256 个顶层 `*.result.json`，最多 2,048 个身份和 1,024 个 PID，每文件最多 16 MiB。不递归枚举，不扫描全机进程。

兼容旧 Windows 回执的 `Processes[]` / 根 `Pid`、`Started`、`CreationDate`、`ParentPid`、`CommandLine`。零回执或无 PID 的回执拒绝。Windows 使用只读进程句柄；Linux 使用 `/proc` start ticks、boot ID、父 PID 和带 NUL 分隔符的原始 argv。Windows 回执不能在 Linux 上证明回收。

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

旧 `Audit-TaskProcesses.ps1` 和 `Expand-RepositoryArchive.ps1` 本批保留为待退役入口，尚有旧调度调用者。新的独立核查/解包使用本 CLI；实现迁移、调用者切换和旧入口删除分别验收，不宣称整个流程已无脚本。
