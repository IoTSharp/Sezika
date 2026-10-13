# T1a：C# 工具迁移首批证据

日期：2026-10-13，Asia/Shanghai。范围是源码迁移和本机/WSL 验证，没有服务器、GPU、教师、模型上传或发布操作。训练及构建工具按用户最新要求允许 JIT；PTX 构建期生成不构成迁移阻塞。

## 已实现

新增 [Sezika.Tooling](../../tools/Sezika.Tooling/README.md)，使用 .NET 10、source-generated JSON、静态 `LibraryImport`，加入 `Sezika.slnx`。本批没有新增长期 PS/Bash/Python/CMD 代码。

| C# 命令 | 对应旧能力 | 迁移状态 |
| --- | --- | --- |
| `process audit` | `tools/repository-training/Audit-TaskProcesses.ps1` | 只读核查已登记 PID、创建身份、父 PID、argv；兼容旧 Windows 回执，增加 Linux start ticks/boot ID；缺证据保留 UNKNOWN，报告不复制 argv |
| `archive extract` | `tools/repository-training/Expand-RepositoryArchive.ps1` | TAR 外层 SHA、路径/类型/数量/字节限制、完整 files.sha256 和落盘重读；失败保留部分输出，成功保留新回执 |
| `StartupGate` | 现有监护器的启动登记握手 | 最多160次/40秒，准确 PID 的 go/cancel 协议；不启动解释器，供分批切换使用 |

本批不是完整进程执行器：没有启动、终止或回收任意子进程的接口。审计 PASS 只覆盖当前已登记身份，不证明未知子树的历史完整性。两个旧 PS 文件及旧调度链仍保留待退役，不能报告全部脚本已删除。

## 验证结果

任务证据根：`.artifacts/repository-training-tooling-migration-20261013-01/`。

| 验证 | 结果 | 原始证据 |
| --- | --- | --- |
| .NET 10 Release 构建 | PASS，0 warnings / 0 errors | `process-logs/*tooling-closure-build.*` |
| 极小检查 | Windows、Ubuntu WSL 均1项 PASS | `checks-tiny-gated/`、`checks-linux-tiny/` |
| Windows 完整检查 | **32 PASS** | `checks-windows-closure/checks.txt`，包含真实本机身份 |
| Ubuntu WSL 完整检查 | **32 PASS**，`/usr/bin/dotnet` 10.0.112 | `checks-linux-closure/checks.txt`、`self.result.json` |
| Windows x64 Native AOT 发布 | PASS，受控2线程 AOT 编译，使用本地缓存/空本地包源 | `native-win-x64-v2/Sezika.Tooling.exe`、`process-logs/*tooling-native-v2-publish.*` |
| AOT 进程审计命令 | PASS，26份回执/72个身份记录/70个PID，当前 live0/unknown0；保留5份历史根观测不完整 | `native-v2-final-audit.json`；审计器自身随后由外层已登记 runner 确认退出 |
| AOT TAR 解包命令 | PASS，2文件/108 bytes，完整文件 SHA | `native-v2-extraction.json`、`native-v2-extracted/` |
| Linux 测试进程补审 | PASS，两份原生 Linux PID/start ticks/boot ID 记录对应进程均已退出 | `linux-final-audit.json`；该命令的旧 Windows 外层短进程观测失败另保留 |
| Linux Native AOT、裸机服务器、完整模型归档 | **NOT_RUN** | WSL/.NET 检查不代替这些验收 |

最终 Windows Native AOT EXE SHA256：`8167be0e2a89cd7157fe222368cba693d613dadd014212df789be0b1b63d39e8`，大小 3,265,024 bytes。首版 `a19de128e7751c9cf934af72cbf953837c51ea919309e9c3db7750bd381712bb` 与其回执仍保留；最终版把仍有 `/proc` 元数据的退出/僵尸进程保持 UNKNOWN，并重跑 Windows/Linux32项及两个 AOT 命令。测试是合成小输入，不把16GiB上限的配置视作已经进行过16GiB吞吐测试。

源码文件 SHA、最终产物与精简结果见[结构化回执](tooling-csharp-migration-2026-10-13.json)。

32项包括短读/多余数据、整块和空文件 SHA、缺失/错误/重复清单、完整覆盖、路径逃逸、绝对路径、软/硬链接、大小写冲突、特殊文件名、取消、失败保留、当前真实 PID、PID 复用、父 PID/argv 不同、拒绝访问、跨 OS、Linux boot身份、空输入拒绝、历史不完整保留及 CLI 错误/不覆盖旧回执。

## 保留的失败与局限

- 初次 Windows tiny 测试自身 PASS，但旧 PS runner 在其退出前未完成 CIM 采样，记录 `CleanupFailed`/payload0。后续新 C# 启动握手明确等候 PID 登记后执行，gated Windows 检查和 AOT 两命令的外层回收均通过；原失败回执不修改。
- 初次完整负例测试的辅助 `Reject` 误以为 `InvalidDataException` 属于 `IOException`，导致第4项异常退出。修正显式异常分类并重新运行完整32项；同类 CLI 错误现在输出 FAIL 回执，已有失败目录保留。
- 部分极短 WSL 启动器仍被旧 Windows runner 记录为 `CleanupFailed`/payload0。Linux 实际检查输出、Linux自身身份补审及 Windows 后续补审分开记录，不把它们补写成旧外层历史完整。
- 审计的原生查询失败返回 UNKNOWN。期限为协作式，底层阻塞的硬终止和完整进程父链/子树生命周期仍由 T1b 解决。
- 归档入口保留旧 portable ASCII/未压缩 TAR 合同，不新增 gzip/任意 Unicode 路径支持；失败只保留本次新目录，不删除重试。
- 全仓库 `git diff --check` 报告既有 `src/Sezika.Cuda/Generated/EncoderBackward/backward-kernel-manifest.json` 的 CRLF/尾空白，本批未修改该文件；本批拥有路径单独检查，不把全树检查标 PASS。

## 接续

唯一计划是 ROADMAP 的 **T1b**：在本工具基础上迁移真正的执行器，逐项保留启动身份、准确父链、payload/cleanup 分离、硬期限、取消和 owned 子树回收。其检查通过后再切换调度调用者，随后推进 SSH/SFTP/凭据和训练/发布链；不把历史脚本批量删除当作完成迁移。
