# T1b：有界原生进程执行器

验收日期：2026-10-14，Asia/Shanghai。本项提交只包含统一执行器、独立进程 fixture、对应检查和最小 CLI 接线；T2–T7 的未验收源码留在工作区。旧流程调用者的最终退役仍按 T7 关闭。

候选来自准确索引导出目录 `.artifacts/tooling-migration-20261013-01/t1b-candidate/source/`。18 个候选文件、183,417 bytes 的实际编译输入，逐个核原始 byte、SHA256 与 Git blob，一致记录于 `compiled-staged-source-manifest.json`。Program、检查入口和项目文件在索引中只含 T1b 接线；工作区继续保留后续模块，未夹带进本项。

## 实现

`process run` 使用类型化 `ExecutionRequest`。显式原生 image、独立输出目录、结构化 argv、stdin/输出字节上限、数量、墙钟与取消合同共同准入；解释器、脚本、已有输出及过大 stdin 在启动前拒绝。

Windows 在 suspended CreateProcess 时原子加入独立 Job；每个已登记成员持有精确出生身份 HANDLE。最多两个独立 Job 同时工作，每个容器、环境、输出和回收分别管理。已完整登记父进程退出后，仅精确出生 HANDLE 且真实 signalled 才支持其已有父链；未读到的 child argv 或身份仍失败。瞬时 observation 的原始 notes 保留。

Linux 使用本项目 C# broker/watchdog、出生 ticks/boot ID、完整 argv、PDEATHSIG、pidfd 和 subreaper。初始身份完整后才释放 payload gate，独立监督器退出后仍回收任务成员。Linux 执行并发保持1，避免跨 scope 的全局 reap。两平台保留原始 stdout/stderr、配置、身份和不可覆盖回执。

## 当前候选实测

| 检查 | 结果 | 独占回执 |
| --- | --- | --- |
| Windows Tooling/检查与 fixture 构建 | 零 warning/error | `tests-build-result.json`、`fixture-build-result.json` |
| Linux x64 三工具自包含发布 | PASS，完整子树回收0 | `linux-{cli,tests,fixture}-result.json` |
| Windows baseline tiny/full | 1 / 32 PASS | `windows-baseline-{tiny,full}-result.json` |
| Windows 执行器 tiny/full | 1 / 18 PASS；full 6.720秒 | `windows-execution-tiny-result.json`、`windows-execution-full-ready-result.json` |
| Linux 执行器 tiny/full | 1 / 17 PASS；full 16.502秒 | `linux-execution-{tiny,full}-result.json` |
| Windows Native AOT 发布 | PASS；真实生成 native code | `aot-win-result.json` |
| Native AOT 正常 payload | PASS，内外回收0 | `aot-success-command-{result,host-result}.json` |
| Native AOT 父进程先退出、存活子进程 | PASS，内外回收0 | `aot-tree-command-{result,host-result}.json` |
| Native AOT sleep 超时 | 内层 TIMED_OUT，命令真实退出1，预期失败/回收通过 | `aot-timeout-command-{result,host-result}.json` |

回执路径均相对上述 `t1b-candidate` 目录。完整检查覆盖正常/非零退出、复杂 Unicode 与 literal shell argv、硬超时、取消、STOP/CANCEL、父进程先退出、stdout/stderr overflow、stdin EOF、解释器与 shebang 拒绝、旧证据保护、监督器先退场及延后生成子树。Windows 另核 hidden console 和两个真实 Job 的独立启动/分别取消；Linux 另核逃逸进程组的 pidfd 回收。各项内外回执的 RemainingProcesses 为0，预期负例保持原 FAIL/非零退出。

当前 Windows AOT 使用 `VSCMD_SKIP_SENDTELEMETRY=1`、`VSCMD_SKIP_VCPKG=1`，登记的子进程没有 PowerShell 5。原先其他候选的 VS 自动发现曾启动旧 PowerShell，其历史记录保持；不能用新环境倒填旧合规状态。

## 保留失败与范围

本候选第一次 Windows full 未提供必需的 broker 路径，在第15项前拒绝；该原 FAIL 和 stderr 保留于 `windows-execution-full*`。补齐执行配置、使用新输出根后，18项完整通过。原 Linux argv 初始身份失败、旧 Windows 观察 UNKNOWN 和旧构建自身锁定失败仍保留，不据当前成功改写。

Native AOT 对照最初将 `tree` 错当作硬超时用例；该 fixture 实际正常退出父进程并回收 child，真实命令回执是 PASS。另用 `sleep` 完成独立超时负例，保留第一次断言失败的工具输出。

这些结果只证明本机/WSL 原生生命周期和指定 Windows AOT 入口。实际 SSH 主机、租机、正式训练、GPU 吞吐/长稳、模型质量、校准、发布/部署及 Linux Native AOT 新候选均不由本项声明通过。
