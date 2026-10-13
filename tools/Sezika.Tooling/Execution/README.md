# 有界原生进程执行

T1b 使用 C# / .NET 10 管理 Windows Job 和 Linux 进程身份、父链、超时、取消及完整子树回收。CLI、远端 worker 和本仓库的 C# 调度调用同一个 `ProcessExecutor.ExecuteAsync`。执行器源码和本机检查通过，不代表旧脚本调用者已全部退役；当前入口切换由根 [ROADMAP](../../../ROADMAP.md) 的 T1b/T7 独立收口。

## 执行一个明确目标

```text
Sezika.Tooling process run --request <execution.json> --output <新的命令回执.json> --timeout-seconds <秒数>
```

请求使用 `ExecutionRequest` 的大小写字段，路径必须是实际存在的绝对路径；工作目录必须存在，输出目录必须全新。下面是当前 Ubuntu WSL 工具候选的原生探针配置，执行前须核准工具候选身份、确认输出目录未使用，并将请求保存为新的 JSON 文件：

```json
{
  "Executable": "/usr/bin/true",
  "ArgumentList": [],
  "WorkingDirectory": "/tmp",
  "OutputDirectory": "/tmp/sezika-execution-probe-20261013-01",
  "TimeoutSeconds": 3,
  "CleanupSeconds": 5,
  "MaximumProcesses": 8,
  "MaximumInputBytes": 0,
  "MaximumOutputBytes": 65536,
  "BrokerExecutable": "/mnt/d/source/Sezika/.artifacts/tooling-migration-20261013-01/linux-cli-v4/Sezika.Tooling"
}
```

`ArgumentList` 是逐个参数，不拼接 shell 命令。空格、双引号、中文和 `$HOME $(literal) ; | &&` 都保持为参数内容。拒绝已知解释器、shebang 文件和脚本后缀参数；接受明确的 PE/ELF 原生映像，包括 .NET apphost。执行器不替代调用者的来源、许可、SHA 和业务准入核验。

Linux 使用明确的 C# broker。CLI 由 native apphost 启动时可默认使用自身作为 broker；通过 `dotnet` 启动时必须显式提供兼容 apphost。默认 broker/watchdog 参数前缀分别为 `["process", "broker"]` 和 `["process", "watchdog"]`，它们是内部生命周期入口。

Windows 默认 `WindowsConsoleMode="detached"`；目标需要隐藏控制台时显式使用 `"hidden"`。两种模式都登记并回收完整 Job 成员。仅系统目录真实 `conhost.exe` 允许登记有限的辅助命令行变化，普通子进程的命令行不能因此放宽。

## 数量、时间和流上限

| 字段 | 默认值 | 接受范围及语义 |
| --- | --- | --- |
| `TimeoutSeconds` | 60 | 1–86,400 秒；排队、登记和执行共用墙钟 |
| `CleanupSeconds` | 10 | 1–30 秒；取消后仍执行独立的有界回收 |
| `MaximumProcesses` | 64 | 1–256；登记、清单及平台归属约束 |
| `MaximumInputBytes` | 1 MiB | 0–16 MiB；超限在启动前拒绝 |
| `MaximumOutputBytes` | 1 MiB | 1–64 MiB；stdout/stderr 分别计数，超限失败 |
| `ArgumentList` | 空数组 | 最多 256 项，累计最多 30,000 字符，拒绝 NUL |
| `Environment` | 空覆盖表 | 最多 256 项；专用启动门和归属标记不能覆盖 |
| `CancelFile` / `StopFile` | 未设置 | 明确绝对路径；观察到文件即取消执行 |

CLI `process run` 的 `--timeout-seconds` 接受 1–86,400 秒，未指定时为 1,800 秒；它与请求的执行期限同时约束任务。CLI 的 `--cancel-file`、Ctrl+C 和传入 `CancellationToken` 也会触发取消。轮询有次数、墙钟和休眠上限，每 10 秒输出进度。系统调用或文件 I/O 的不可中断阻塞不具有独立定时器验收，不能把这些检查描述为任何阻塞条件下都能按秒杀掉监督器。

`StandardInput` 在 JSON 中使用 byte[] 的 base64 表达，直接写入目标 stdin 并关闭形成 EOF。stdout/stderr 原始字节分别保存为 `stdout.bin` / `stderr.bin`，执行器不转码。凭据不得放在 argv；调用者还须核准 payload 是否会把输入凭据回显到输出日志。

## 判读执行与回收

`ExecutionResult` 分别保存 `ExecutionStatus`、`ExitCode`、`CleanupStatus`、输入/输出完成状态、溢出状态、`RemainingProcesses`、身份和错误。总 `Status="PASS"` 要求 payload 实际退出 0、流处理完成、没有错误、完整回收通过且剩余成员为 0。payload 非零、超时或取消均保留失败，即使回收通过也不会改成成功。

| 状态 | 含义 |
| --- | --- |
| `SUCCEEDED` / `FAILED` | 已取得实际 payload 退出码；另核最终回收 |
| `TIMED_OUT` / `CANCELLED` | 执行工作被终止；继续有界回收 |
| `CleanupStatus="PASS"` | 平台完整清单为空，Linux 同时完成实际 reaping |
| `CleanupStatus="UNKNOWN"` | 身份、清单、终止或 reaping 未得到完整证明 |
| `NOT_NEEDED` | 尚未启动归属进程，不作为子树回收证据 |

Windows 在创建时原子加入 `KILL_ON_JOB_CLOSE` Job，先悬停、登记完整 PID/出生时间/argv/父链，再恢复线程。保留已登记成员的出生 HANDLE。退出期间短暂无法读取 argv 只登记待核查状态；必须由随后完整身份、实际消失或匹配出生的 signalled HANDLE 解除，否则保持 UNKNOWN/FAIL。

Linux 通过 `posix_spawn` 创建独立进程组，在 broker 门后登记 PID/start ticks/boot ID/原始 NUL 分隔 argv，再 `execve` 目标；同一出生身份记录声明和实际 exec 参数。主进程使用 subreaper、pidfd 和真实 `waitpid`，登记逃逸进程组与晚生子树。独立 C# watchdog 在监督器退出后仅向核实归属的 pidfd 发信号；它的 `TERMINATED_PENDING_PARENT_REAP` 只证明终止阶段，不能冒称已经 reap。无完整身份或父链仍失败。

新目录保存 `ownership.json`、初始 `registered.json`、有限登记快照、最终 `result.json`、原始流日志及 Linux 门/broker/watchdog 证据。元数据先以独占 `.pending` 写入、flush、关闭后 no-overwrite Move 发布，旧失败回执不覆盖。只关闭本次持有的句柄并回收本次核实归属树；交付物和失败输出保留。

## 当前调用者与旧入口

| 当前 C# 调用者 | 执行器承担的工作 |
| --- | --- |
| `ToolingCommand` 的 `process run` | 请求与执行回执入口 |
| `RemoteWorker` | 固定 SHA/allowlist 的 SSH payload 原生执行 |
| `ValidationBatch` | 明确 native apphost 或固定 managed assembly 的验证进程 |
| `BundleBuild` | 受控 Git ref 核查及最多 5 项 .NET publish |
| `ResidentController` | 两项 GPU 预检和一个准入训练进程 |
| `NativeGit` / `NativeGitHook` | 结构化 Git/LFS 调用和原生 pre-push 委托 |
| `Sezika.ProcessFixture` / `Sezika.SshFixture` | 独占的实际生命周期和 SSH 集成检查 |

训练的 `ResidentController` 将 STOP 留给 trainer 安全保存，不设置执行器的 `StopFile`；CANCEL 才触发硬终止。真实训练、GPU 长稳与保存期限仍须现场验收。

旧 `tools/Invoke-BoundedProcess.ps1`、`Invoke-BoundedLinuxProcess.ps1`、`Invoke-BoundedLinux.sh`、rental-training 两代 guardian，以及 corpus 的 `bounded_start.py` / `run_guarded.py` 都由本模块提供生命周期替代能力。两个训练技能、旧 bundle/下载/发布调度、工具 README 和外部 MoArk/SSH 技能还有引用，需要随其所属 T2–T7 完成切换或明确退休；历史冻结包和原始回执不能改写。

## 可复现检查与证据边界

构建由主控串行调度，fixture 必须是本平台的实际 native apphost。运行检查前设置 `SEZIKA_PROCESS_FIXTURE` 和 `SEZIKA_PROCESS_BROKER` 的明确路径；Linux 和监督器死亡检查需要 broker。

```text
Sezika.Tooling.Tests --output <新的任务目录> --maximum-checks 1 --suite execution
Sezika.Tooling.Tests --output <另一个新的任务目录> --maximum-checks 17 --suite execution
```

两个平台各 17 项，平台专属项分别是 Windows hidden console、Linux 逃逸进程组。其余覆盖实际成功、复杂 argv、非零失败、超时、父先退出、token/CANCEL/STOP、stdout/stderr 超限、stdin/EOF、超限启动前拒绝、解释器/shebang 拒绝、旧证据保护，以及监督器死亡后的普通/晚生树回收。单套有 35 秒取消和 33 秒新增检查界；根检查入口还有 45 秒取消与 40 秒检查段界。

2026-10-13 本机实际回执在 `.artifacts/tooling-migration-20261013-01/`：Windows `execution-hidden/checks.txt` 为 17/17，Linux 新 `linux-execution-v4-tiny/checks.txt` 为 1/1、`linux-execution-v4-full/checks.txt` 为 17/17；对应 `linux-execution-v4-full-host-result.json` 是独立 Windows WSL 驱动 PASS/回收 0。Windows 驱动回收不能替代 Linux 内层归属验收。上述检查为 JIT/self-contained 运行，不能作为最终 Windows/Linux Native AOT 验收；最终源码变更后的平台复验、调用者切换和受控提交由主控记录。
