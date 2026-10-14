# Linux 原生进程准入与 v12 整合验收

2026-10-14 的 `native-exec-final-integrated-v12` 已完成本地有界执行器整合验收：Linux 22 项、Windows 19 项，以及实际只读 Git 生命周期 1 项和 8 项均通过。测试程序使用框架依赖 JIT；Linux CLI 发布为 Native AOT，并实际承担 broker/watchdog。此次结果不代表 Git 传输完整 16 项、包发布、生产部署、GPU 或训练验收，也不构成一般性能保证。

本文只依据本次原始文件和当前源码。历史失败没有被后续 PASS 覆盖。离线证据位于本机，不随源码仓库包含全部二进制；独立复核需要保留原目录：

```text
B = C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014
C = B\native-exec-final-integrated-v12
```

## 源码、运行文件与回执绑定

v12 冻结 57 个源码/构建输入文件和 25 个运行文件，另对 27 个顶层发布、门禁和审计回执记录 SHA-256。五个本次负责的编译源与 v11 完全一致。Windows 整合来自已提交的 `46d20572f5eb089a69e90734a3fe8a7e4853ecfe`；测试入口以 Root 的 exact-LF 文件为基线，仅增加 execution 套件 90 秒预算，保留 Windows lifecycle 的 45/240 秒预算。execution 内部仍为 70 秒，原有每例 3 秒、8 秒等限制没有放宽。

| 编译源 | SHA-256 |
|---|---|
| `tools/Sezika.Tooling/Execution/LinuxExecutionSession.cs` | `9f23da19a932eda0464f0f61a6f4d0cdb45c7f1212e165f49c5cbf8bd51581bf` |
| `tools/Sezika.Tooling/Execution/ProcessExecutor.cs` | `3f29eccdc5fe91c54a4012228e2974736e7b17a6557b417869261fb7652c3311` |
| `tools/Sezika.Tooling/Execution/LinuxWatchdog.cs` | `a3ab852f6280ed95e0f56a012a4ed1dfeb0923c1063124cd11be3386a7d652df` |
| `tools/Sezika.Tooling/Execution/ExecutionContracts.cs` | `499b4acbf63dbd99d379e4fa990809e36cc3fe354fd8df89cf4d4fee87fe4e74` |
| `tests/Sezika.Tooling.Tests/ExecutionChecks.cs` | `35bdf7c655a665ea2f0cc4a50e78c55ed8a247a67297d51ba9d4816b5e4a927e` |

| 证据 | 文件（相对 C） | SHA-256 / 内容 |
|---|---|---|
| E-01 | `source-manifest.json` | `7391b8ded7c523afbabe3ac4faa444d9d365f1d1c301b2231f7f5adee5e6dd6e`，57 文件 |
| E-02 | `runtime-files.json` | `d1d122d681d2b6c006ffae6c47848ae8aea2dbba080cec3e49bc179d1d174e8b`，25 文件 |
| E-03 | `gate-receipt-pins.json` | 27 个实际 gate/audit 回执的路径和 SHA-256 |
| E-04 | `binding.json`、`acceptance-summary.json` | exact 来源、各项状态、历史失败引用与范围 |

Windows 使用私有 SDK 10.0.401，Ubuntu 使用 SDK 10.0.112；文件固定值见 `sdk-file-pins-final.json`。`global.json` 仅设置 `allowPrerelease=false`，没有 SDK version/rollForward pin。四个发布严格串行且实际完成，之后才开始 Linux 3 秒业务检查。

## 准入与退出证明

首次 `/proc` 观察遇到空 argv 或精确等于活父进程的继承 argv 时，最多重观察 20 次，共享 100 毫秒，使用 5 毫秒可取消退避。PID、start ticks、boot ID、父关系和 scope 必须保持一致；不完整 argv 不产生注册身份，消失也不会把待确认成员升级为成功。watchdog 角色在首次不可变登记之前固定，要求声明 PID、完整原生 argv/birth/parent/scope 和保留的 pidfd 全部匹配。

祖先握手只遍历到最高声明的任务 observer，不延伸至系统/WSL 祖先；零声明不读父链。最多 64 个 observer、64 KiB 声明、100 次轮询、共享 2 秒和 20 毫秒退避。每个 observer 必须先完成自身独立原生登记、保留 pidfd 并写入不可变快照，才发布对应 birth/scope 的 ACK。ACK 只控制调度，不能导入、替代或改写原生身份证据。broker 在 payload exec 前等待，watchdog 在 ready 前等待。

取消 fence 在 watchdog 创建及 payload exec 前检查本 scope 的 `admission.cancel`。cleanup 中最多 2 秒的准入等待包含在原有共享 `CleanupSeconds` 内，不额外延长；无法确认原始父关系或 watchdog 准入时仍为 UNKNOWN。

对于 `/proc/<root>/stat` 的 ESRCH，仅已独立固定完整原生身份并保留 pidfd 的同一 root，可以在 `IOException.HResult == 3` 且 pidfd 明确返回 POLLIN/HUP 时记录退出。只覆盖 `discovery-stat` 和 `remaining-inventory` 两处；未注册成员、其他 IOException、POLLERR/POLLNVAL 仍为 UNKNOWN。`RootExitProofs` 明确标记 `NOT_PARENT_REAP_PROOF`，父执行器仍独立完成回收；严格的 `ProcessAudit.SameBirth` 没有放宽。

调用路径为：原生观察 → 完整登记与 pidfd → 不可变快照 → 各祖先 ACK → watchdog 原生登记/ready → payload exec → 原始退出/cleanup 回执 → 当前原生身份审计。E-01/E-02 绑定实现，E-03/E-04 绑定该路径的实际结果。

## v12 实际门禁与当前审计

| 门禁 | 实际结果 | 证据（相对 C） |
|---|---|---|
| 四个串行发布 | 全部 PASS，内外 EOF 完整、cleanup PASS、remaining 0 | `build-progress.json` 及四个 `*-publish-v1` 原始回执 |
| Linux tiny → small 2 → pressure 8 | 全部 PASS；原每例 3 秒保持 | `linux-execution-selected-{1,2,3}` |
| Linux 定向取消 13 → 完整 22 | 全部 PASS；完整外层内执行回执 41.4747025 秒，套件自身 39.708 秒 | `linux-execution-cancellation-13`、`linux-execution-full-22` |
| Windows tiny 1 → execution 19 | 全部 PASS，19 项外层执行 8.3271054 秒 | `windows-execution-selected-1`、`windows-execution-full-19` |
| Windows lifecycle 1 → 8 | 实际 Git rev-parse/version、detached/hidden 均按分母通过；8/8、0 失败、0 未处理 | `windows-lifecycle-{1,8}/client/windows-lifecycle-report.json` |
| Linux 当前 strict audit | 39 份回执、248 条身份记录、161 个不同 PID，live 0、unknown 0 | `audits/linux-batch-{1,2,3,4}/audit.json` |
| Windows 当前 strict audit | 34 份回执、129 条身份记录、65 个不同 PID，live 0、unknown 0 | `audits/windows-batch-{1,2,3}/audit.json` |

377 是身份记录数，包含重复被外层和内层观察的记录，不是 377 个独立进程。实际负例的原始 FAIL/CANCELLED/TIMED_OUT 语义保留，当前审计 PASS 只证明记录中的身份现已不存在或 PID 已复用，不改写原执行结论。

四个刻意杀死 supervisor 的输出目录无法写出最终 `result.json`，没有补造 final，也没有将其作为完整 native receipt 输入严格审计。其具体目录在 `audits/plan.json` 的 `ExcludedIncompleteResults`；验收范围是外层独立原生登记和 watchdog 原始终止回执覆盖的身份与回收，不能声称死去 supervisor 自行完成最终回执或父回收证明。

| Evidence → Finding → Path | 结论与复核路径 |
|---|---|
| E-01/E-02 → F-01 | 当前实现固定原生身份后才准入；从源码清单复核五文件，再对照运行清单，禁止用别的 candidate 替换运行文件。 |
| E-03/E-04 → F-02 | 本次有界整合门通过；按原请求 JSON、checks.txt、内外回执、audit.json 顺序复核，不能仅取顶层 PASS。 |
| 历史回执 → F-03 | 后续 PASS 不消除旧 FAIL/UNKNOWN；按下表原目录读取。当前仅完成 executor 门，Git 16 项传输门仍待实际执行。 |

复核只需读取文件；若要重现执行，必须使用新的输出根目录和本候选固定文件，不能覆写这些原始请求输出。Linux 五阶运行期间 Root 和其他 worker 没有启动 SDK、重哈希或大下载；这是一项已记录的执行条件，不外推为一般负载下的性能结论。

## 保留的 v8–v11 失败与未知项

以下目录均相对 B，原始文件保留。

| 候选 | 原观察与保留结论 |
|---|---|
| v8 | `native-exec-race-candidate-v8` 的 22 个 payload 检查曾通过，但受控 150 毫秒取消中外层 watchdog PID 151203 为 UNKNOWN；外层门没有因此通过。 |
| v9 | 只完成构建；运行门 NOT_RUN，不能借用前后候选结果。 |
| v10 | `linux-execution-full-22/client/execution-10-fast-git-pressure/watchdog-result.json` 为 UNKNOWN：root 152463 的 stat 返回 No such process，watchdog 152465。旧 raw 没有堆栈，不能断言发生于 discovery-stat 还是 remaining-inventory。 |
| v11 首次 Linux tiny | `linux-execution-selected-1/client/execution-01-success/result.json` 在原 3 秒限制下 TIMED_OUT，3.3241962 秒，cleanup PASS、remaining 0，只有 root 152929；没有 watchdog 生成标记或回执。 |
| v11 controlled-v2 | 全部四构建结束后，同一源码和产物的新隔离目录五阶通过，完整 22 门外层内执行 38.9215351 秒，当前 strict audit 157 条身份记录通过；不能据此把首次超时改为 PASS。 |
| v11 Windows | `windows-execution-full-19` 前 16/19 通过，第 17 项 hidden-console 的即时当前身份审计抛 LIVE_OR_UNKNOWN；root 80284、conhost 13296。第 18/19 项 NOT_RUN。随后当前审计 45 条身份记录通过，也不覆盖该原失败。v12 才实际整合已提交 Windows 修复并重新通过 19 项。 |

v11 首次 tiny 起点为 05:57:04.0609881 UTC；`registered.json` 最后写入 06.562396、自己 ACK 06.7186704、`start-gate/go` 06.7936853、最高声明祖先 ACK 06.8918109，取消标记 07.1705089（同为 05:57 UTC）。启动门释放前约消耗 2.733 秒。自身 Windows 编译确实与该 tiny 全程重叠，但没有当时 CPU/I/O 样本，也没有定位具体慢调用，所以资源因果保持 UNKNOWN。没有通过放宽 3 秒、挑选成功运行或重写原回执来消除这个问题。

## 清理与后续范围

18 个明确属于本任务的 v4–v12 `linux-owned-temp`/`windows-owned-temp` 目录，在核对绝对路径、归属、进程结束、数量/时间界限和无链接后已删除，见 `owned-temp-cleanup.json`。冻结源、运行文件、构建输出和原始回执保留；v1 的 Linux package cache/CLI home 与 Root 私有 SDK 保留。worker 没有执行 Git 写入。

下一阶段必须为 Git candidate v3 的独立 server/client 固定实际运行文件 SHA 后重新执行 Linux 1 项小样和完整 8 项，以及 Windows 对应分母；本文不能替代那些尚未完成的 Git 传输、LFS 或失败恢复门。
