# 有界原生进程执行

T1b 使用 C# / .NET 10 管理 Windows Job 和 Linux 进程身份、父链、超时、取消及完整子树回收。CLI、远端 worker 和本仓库的 C# 调度调用同一个 `ProcessExecutor.ExecuteAsync`。执行器源码和本机检查通过，不代表旧脚本调用者已全部退役；当前入口切换由根 [ROADMAP](../../../ROADMAP.md) 的 T1b/T7 独立收口。

## 执行一个明确目标

```text
Sezika.Tooling process run --request <execution.json> --output <新的命令回执.json> --timeout-seconds <秒数>
```

请求使用 `ExecutionRequest` 的大小写字段，路径必须是实际存在的绝对路径；工作目录必须存在，输出目录必须全新。下面是 Ubuntu 原生探针的配置模板；先将 broker 路径改为本任务已冻结并部署的 C# 工具，独立核完整运行包身份，再选择新的输出目录。示例路径不表示工具已安装：

```json
{
  "Executable": "/usr/bin/true",
  "ArgumentList": [],
  "WorkingDirectory": "/tmp",
  "OutputDirectory": "/tmp/sezika-execution-probe-new-run",
  "TimeoutSeconds": 3,
  "CleanupSeconds": 5,
  "MaximumProcesses": 8,
  "MaximumInputBytes": 0,
  "MaximumOutputBytes": 65536,
  "BrokerExecutable": "/opt/sezika-tools/Sezika.Tooling"
}
```

`ArgumentList` 是逐个参数，不拼接 shell 命令。空格、双引号、中文和 `$HOME $(literal) ; | &&` 都保持为参数内容。拒绝已知解释器、shebang 文件和脚本后缀参数；接受明确的 PE/ELF 原生映像，包括 .NET apphost。执行器不替代调用者的来源、许可、SHA 和业务准入核验。

Linux 使用明确的 C# broker。CLI 由 native apphost 启动时可默认使用自身作为 broker；通过 `dotnet` 启动时必须显式提供兼容 apphost。默认 broker/watchdog 参数前缀分别为 `["process", "broker"]` 和 `["process", "watchdog"]`，它们是内部生命周期入口。

跨 native apphost 的 Linux nested broker 使用 `NestedBrokers` 明确准入，最多四项，每项包含绝对 `Executable`、完整 native 映像的 `Sha256` 和准确 `ArgumentPrefix`。允许声明任务随后复制的固定路径；实际识别时必须存在且 SHA 匹配，原生 `/proc/<PID>/exe` 与该路径相同，并核已登记父的实际出生/boot/父边/argv、最多 256KiB 的显式 broker 配置、scope marker 和准确 exec argv。每个映像最多 128MiB，参数前缀 1–8 项。任意程序自行使用 `process broker` 字样不能取得准入；完整 code/runtime bundle 由调用者独立冻结验证，单个 entrypoint SHA 不冒充整包身份。

Windows 默认 `WindowsConsoleMode="detached"`；目标需要隐藏控制台时显式使用 `"hidden"`。两种模式都登记并回收完整 Job 成员。仅系统目录真实 `conhost.exe` 允许登记有限的辅助命令行变化，普通子进程的命令行不能因此放宽。

Windows 同一监督进程最多同时运行两项独立 Job，用于一项有界 fixture 服务和一项客户端；每项仍有独立环境、出生 HANDLE、输出、取消和回收。容量不会自动启动第二项工作，构建仍由主控串行调度。Linux 的同进程执行容量保持一项，避免放宽全局 subreaper 的回收边界。

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

Job 在 `CreateProcess` 前关联私有 completion port，由一个有界 pump 提前观察 `NEW_PROCESS`，捕获实际完整出生身份、父 PID 和 argv，并保留精确出生 HANDLE。最多 `MaximumProcesses` 条缓存及 `4 × MaximumProcesses + 16` 条事件，共用执行期限加回收期限；原生等待 20ms，单个初始身份至多 25 次/50ms，结束以私有 wake 及最多 2 秒 join 回收。主循环仍核实际 Job 成员，合并早采样身份。完整原生观测后来解除的暂态缺口保留说明；完全未观察到 argv 的通知即使目标已退出也保留 UNKNOWN，不读取内层成功回执补造身份。

Linux 通过 `posix_spawn` 创建独立进程组，在 broker 门后登记 PID/start ticks/boot ID/原始 NUL 分隔 argv，再 `execve` 目标；同一出生身份记录声明和实际 exec 参数。主进程使用 subreaper、pidfd 和真实 `waitpid`，登记逃逸进程组与晚生子树。独立 C# watchdog 在监督器退出后仅向核实归属的 pidfd 发信号；它的 `TERMINATED_PENDING_PARENT_REAP` 只证明终止阶段，不能冒称已经 reap。无完整身份或父链仍失败。

Linux 清单扫描在发现所属成员时立即核完整出生、父链、argv、scope 与 pidfd，并先写登记快照，再发送祖先准入信号。已声明的 watchdog PID 只用于安排观察顺序，仍走相同身份门；优先查询也计入原8192项/2秒清单预算。未能完整登记的短进程仍保留 UNKNOWN，不能从内层成功或后续消失补造身份。原生元数据读取、子预算和登记回调共享调用者期限与取消；取消后的回收使用独立清理预算。

成员数未变化时，首次实际观察到的 Linux `exec` 转换另写 `observed-exec-<PID>.json`，每个成员至多一份。它来自实际完整原生观察，不把声明的预期命令当作已执行。原登记快照保持不可变及原数量/命名，最终审计仍核原登记与最终执行回执；监督器死亡夹具另把该转换证据与当时仍存活的 `/proc` 出生、父边、实际NUL argv及完整登记父链逐项对照。该新路径的运行验收与历史候选分别记录。

Linux 原生取消/STOP文件宜放在任务自己的Linux文件系统，并让所有控制写入者指向同一实际文件；Windows监督者可用准确的WSL UNC映射写入。Windows外层保留独立控制文件。控制路径也是固定运行输入，改变位置须保留旧回执并使用新scope；不能因此延长原压力、执行或清理期限。

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

历史证据中的 `tools/Invoke-BoundedProcess.ps1`、`Invoke-BoundedLinuxProcess.ps1`、`Invoke-BoundedLinux.sh`、rental-training 两代 guardian，以及 corpus 的 `bounded_start.py` / `run_guarded.py` 均不再调用；本模块承接其生命周期能力。两个训练技能及当前 bundle/下载/发布流程改用 C# 调用合同，逐项范围由 T7 复核；历史冻结包和原始回执保持原字节。

## 可复现检查与证据边界

构建由主控串行调度，fixture 必须是本平台的实际 native apphost。运行检查前设置 `SEZIKA_PROCESS_FIXTURE` 和 `SEZIKA_PROCESS_BROKER` 的明确路径；Linux 和监督器死亡检查需要 broker。

```text
Sezika.Tooling.Tests --output <新的任务目录> --maximum-checks 1 --suite execution
Sezika.Tooling.Tests --output <另一个新的任务目录> --maximum-checks <Windows为19，Linux为22> --suite execution
```

当前 Windows 19 项、Linux 22 项。平台专属项分别是 Windows hidden console、两 Job 独立回收及八个实际快速 native 子查询的外层完整身份观察；Linux 增加真实 Git 两次/八次压力、三个原生祖先观察器及缺失 ACK 的拒绝，保留逃逸进程组和显式 nested broker 错 SHA 的实际拒绝。Linux 两个监督器退出正控也绑定 native broker SHA。两 Job 检查要求两个实际 payload 均发布启动标记，先回收第一项后核第二项的原始出生身份/argv 仍然存活，再独立回收第二项；不以串行启动冒称并发通过。其余覆盖实际成功、复杂 argv、非零失败、超时、父先退出、token/CANCEL/STOP、stdout/stderr 超限、stdin/EOF、超限启动前拒绝、解释器/shebang 拒绝、旧证据保护，以及监督器死亡后的普通/晚生树回收。执行套件自身为70秒取消、68秒新增检查界；根入口对execution设置90秒期限，原每例3秒等限制保持。

2026-10-13 本机实际回执在 `.artifacts/tooling-migration-20261013-01/`：Windows `execution-hidden/checks.txt` 为 17/17，Linux 新 `linux-execution-v4-tiny/checks.txt` 为 1/1、`linux-execution-v4-full/checks.txt` 为 17/17；对应 `linux-execution-v4-full-host-result.json` 是独立 Windows WSL 驱动 PASS/回收 0。Windows 驱动回收不能替代 Linux 内层归属验收。上述检查为 JIT/self-contained 运行，不能作为最终 Windows/Linux Native AOT 验收；最终源码变更后的平台复验、调用者切换和受控提交由主控记录。

2026-10-14 的 Windows19、Linux22及Windows lifecycle1/8已由主控在准确v12候选实际复验，Linux CLI Native AOT承担broker/watchdog；证据见[最终Linux准入与双平台整合](../../../docs/evidence/tooling-linux-admission-final-2026-10-14.md)。上述2026-10-13旧17项原件保持原义。进程身份与回收通过不替代真实训练、GPU或其他调用者验收。

2026-10-15 v17新增实际 exec 转换证明后，两平台当前进程套件、Linux early7 及 audit17 连同 tiny 合计96次检查执行通过；Root独立核500份原始身份文件和201个已登记birth的完整资源覆盖，当前live=0/unknown=0。详见[本轮生命周期验收](../../../docs/evidence/tooling-final-v17-2026-10-15/process-lifecycle.md)。新scope成功不改写旧PID914484 UNKNOWN、旧失败或2个缺final观察目录的完整性限制；整体调用者与服务器验收另列。
