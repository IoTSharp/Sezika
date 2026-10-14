# Windows 已登记 HANDLE 生命周期修复候选，2026-10-14

本记录保留首次源码冻结时的待执行状态，并在末节记录其后独立 Windows 候选的实际验收。下列原始失败保持原样，不以源码审阅或另一候选的 PASS 改写历史。

## 原始证据

原始生产路径回执：`C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\reference-stage-t6-v1\committed-head-result.json`。开始时间为 `2026-10-14T04:51:07.9598438+00:00`。

- `Status=FAIL`，`ExecutionStatus=SUCCEEDED`，`ExitCode=0`。
- `InputComplete=true`，`OutputComplete=true`，标准输出 41 字节，标准错误 0 字节。
- `CleanupStatus=PASS`，`RemainingProcesses=0`。
- launcher PID 19544，Git root PID 50292。
- conhost PID 48884，创建时间 `2026-10-14T04:51:08.0078376+00:00`，已有完整出生、父链、命令行和 `windows-job-console-host` 登记。
- 该 conhost 最终状态为 `UNKNOWN_PENDING_RETAINED_EXIT`，待定详情为 `observer=Unknown; detail=CommandLineInformation:c000010a`；原始 Errors 中保留 UNKNOWN。

源码中原来的 `Reap()` 仅要求 root HANDLE 已退出且活动 Job 列表为空。活动列表为空并不能代替每个已完整登记、保留的 HANDLE 的退出信号。上列回执证明最终仍存在未解决的登记身份，不能单凭回执证明该竞态的全部时序或断言任何原生 API 故障原因。

### 同号 case06 的不同候选必须按绝对路径区分

本工作片段实际读取的历史 PASS 属于 `git-windows-cases-v2`，与主代理当前定位的 `git-candidate-v2\windows-cases` 不是同一执行。2026-10-14 05:16 UTC 重新核对的历史原件如下：

- 请求：`C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\git-windows-cases-v2\case-06-request.json`；1149 字节，SHA256 `7776cd147e96eefe329918bb4c219e63505f9d4d59340c2087385367bafc4f33`；timeout 280 秒、cleanup 10 秒、max processes 256。
- 外层原生回执：`C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\git-windows-cases-v2\case-06-result.json`；26314 字节，SHA256 `7db031be3aa4fe9d669c759972cf6ba2c70d0f6bd1ceff508c647505e21cf01b`；开始 `2026-10-13T21:20:04.8563663+00:00`，root PID 59060，耗时 3.3609009 秒，PASS/SUCCEEDED、exit 0、cleanup PASS、remaining 0、Errors 空。
- suite 断言记录：`C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\git-windows-cases-v2\case-06\checks.txt`；196 字节，SHA256 `be0b0504f3f1741ac59e68fbac370ab12a97f42cb0c7913caa17e133fa6ae97c`；unknown-filter 用例 `PASS checks=1; elapsed=3.076s`，`native_aot=False`。3.076 秒是 suite 自报时间，不能写成外层原生执行耗时。

主代理从 executor 接收并定位的当前失败原件另在：

- `C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\git-candidate-v2\windows-cases\case-06\result.json`。
- 对应 `C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\git-candidate-v2\windows-cases\case-06\outer-execution\result.json`。
- 主代理提供两者 SHA256 均为 `c7afd1a1efdab1c3c7972c563772b4f0b808443406fb3da108ed21979e90e584`，Status FAIL，开始 `2026-10-14T03:43:23.8069+00:00`，root PID 4432，耗时约 2.2836 秒，max processes 128，错误为 `Job member list is incomplete or over budget.`。本次三原件限额内未再读取这组失败原件，以上失败指针及字段来自主代理交接。

因此不能泛称“case06 已通过”。suite check PASS 只说明对应断言完成，不能覆盖整个 outer 资源与进程验收的 FAIL；历史另一目录的 PASS 也不能解除当前候选的失败。该 Job 列表不完整的具体原因仍为 UNKNOWN，本候选不增加查询重试或提高进程上限；Reap 最小改动仍只依据前述 conhost PID 48884 的已证实未解决状态。

## 变更范围

仅本工作片段拥有以下路径：

1. `tools/Sezika.Tooling/Execution/WindowsExecutionSession.cs`。
2. 新增 `tests/Sezika.Tooling.Tests/WindowsLifecycleChecks.cs`。
3. 本记录。

`Reap()` 在 root 退出及活动列表为空后，对每个已完整登记的保留 HANDLE 执行 `WaitForSingleObject(handle, 0)`。全部返回 0 才完成；258 返回 false，由现有调用者继续有界清理；失效 HANDLE 或其他返回值仍报错。登记数量超过配置进程上限立即失败。

调用者原有共享 `CleanupSeconds` 期限、最大 `CleanupSeconds * 20 + 1` 次迭代及 50 毫秒退避保持原有语义。新检查显式使用 cleanup 10 秒。没有逐 PID 长等待，没有 UNKNOWN 豁免，也没有按名称或系统范围终止 conhost。

`Members()` 只追加 assigned/count/maximum/bufferBytes/returnedBytes 诊断字段；失败继续报错，不新增重试，不放宽完整性条件。通知收集器、ProcessExecutor、既有 ExecutionChecks、共享 Program、项目文件和 Git 均不属于本片段修改范围。

## 待接线的实际 Windows 检查

入口签名：

```csharp
WindowsLifecycleChecks.Run(string root, string repositoryDirectory,
    int maximumCases, Action<string, Action> check,
    CancellationToken cancellation = default, string? cancelFile = null);
```

主代理在共享测试入口接线。`root` 和 `repositoryDirectory` 必须是明确、现存的绝对目录；报告及 case 子目录必须全新。先以 `maximumCases=1` 完成 tiny，再在新的根目录以 `maximumCases=8` 完成全组，不能覆盖失败现场。

- 固定 Git 为 `C:\Program Files\Git\cmd\git.exe`，运行前后核对 SHA256 和字节数。只执行本地 `rev-parse HEAD` 或 `--version`。
- tiny 仅一次 detached `rev-parse HEAD`，共享期限 45 秒。
- full 共八次，交替 HEAD/version；console mode 为 detached、detached、hidden、hidden，重复两组。共享期限 240 秒。
- 每个请求 timeout 10 秒、cleanup 10 秒、最多 8 个进程、stdin 0 字节、stdout/stderr 合计上限 65536 字节；支持取消 token 和显式取消文件。
- 直接调用生产 `ProcessExecutor`。要求 PASS、SUCCEEDED、exit 0、输入/输出完成、Errors 空、remaining 0；验证真实 stdout 内容和字节数、root 原始 argv、launcher 与完整父链。
- 所有记录必须具备真实 PID、Windows 创建时间、argv、scope、所有权和父链；状态只接受 EXITED 或 EXITED_RETAINED_HANDLE，且无待定错误。
- 逐 PID 独立重新打开 HANDLE 并核对创建时间。同一出生必须已发出退出信号；仅 error87 加独立 `Absent` 可证明无法重新打开的旧出生已消失；不同出生的 PID 复用保留其进程；访问拒绝、无法核对出生或原生等待失败继续 FAIL。
- 执行生产 `ProcessAudit.Run` 并保存审计 JSON，要求完整身份分母、live 0、unknown 0、原始 native receipt PASS、SUCCEEDED、cleanup PASS、exit 0、Errors 空。
- 请求、原生回执及审计均固定路径、字节数及 SHA256；报告保留 PASS、FAIL 和 UNPROCESSED。报告分别记录实际观察到的 conhost 数量；若为 0，不宣称 conhost 覆盖。

逐 PID 回读发生在生产 executor 返回并释放其保留 HANDLE 后，使用重新打开的独立 HANDLE；它不是对生产保留 HANDLE 的直接复查。生产保留 HANDLE 的等待条件由本次 `Reap()` 源码变化约束，实际运行仍需新回执证明。

## 首次源码冻结时的状态（05:11 UTC）

| 项目 | 本片段状态 |
| --- | --- |
| 原始失败及 case6 外层回执核对 | 已完成，只读 |
| Reap 与诊断源码候选 | 已写入，静态审阅 |
| Windows tiny 1 | NOT_RUN |
| Windows full 8 | NOT_RUN |
| 编译 | NOT_RUN；等待主代理分配编译时段 |
| 既有 Windows execution 19 | NOT_RUN |
| Native AOT | NOT_RUN |
| Linux/训练/完整语料/发布验收 | 本片段未执行 |

首次冻结时只使用 PowerShell 7 进行本地有界只读检查及哈希，未新建脚本、安装工具、访问网络、使用 Graphify 或修改 Git。仅凭上表的首次冻结状态不能声称运行验收通过；之后主代理明确授权的独立 Windows 实测见下一节。

## 后续独立 Windows 候选实际验收

2026-10-14 05:41 UTC 起，在主代理明确分配的唯一构建时段内，使用独立目录 `C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\windows-lifecycle-candidate-v1` 进行实际构建与运行。构建槽于 05:48:58 UTC 释放。后续聚合审计在主代理明确续至 06:05 UTC 的短窗口内完成。

候选精确取自 `native-audit-candidate-v1\source` 的 56 文件基线。只替换 WindowsExecutionSession、加入 WindowsLifecycleChecks、使用 canonical 无 SDK version/rollForward 的 global.json，并在候选 Program 增加 suite 允许项、45/240 秒预算及独立 dispatch 三处接线。其余 53 个基线文件逐项相同；逆转这三处 Program 改动后 SHA256 恢复基线 `edee54c894d88b7c6ae08d73628e2bcb3f42b3b53dc2af734effcb7a68433068`。没有加入正在变化的 canonical Linux 握手源码。

候选实际分母为 57 文件（已包含 global.json）、48 个 C# 文件、26 处 partial 声明及 15 个不同 partial 类型；不计尚未物化时的生成源码。`candidate-source-manifest.json` 为 13113 字节，SHA256 `8df628029ef60b98647487ebcb10536ccd830c228d7ec6f82c5276023dc0b0b1`，构建后 57/57 哈希复核相同。

固定私有 SDK 为 `C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\toolchain\dotnet-10.0.401-win-x64\dotnet.exe`，版本 10.0.401；每次 JIT/构建进程显式设置同目录 DOTNET_ROOT。dotnet.exe SHA256 为 `21a46f1e5235cf4e844b9de5429f0e198b9c97a41f0503a66442f1d639ca3ee6`。Windows19 使用主代理核实的 `e6\src\tools\Sezika.ProcessFixture\bin\Release\net10.0\Sezika.ProcessFixture.exe`，SHA256 `9a80ce0b2b0c7311835fd95d43575d0a51eff16fd351bb17dec1610025618c9e`。

| 实际操作 | 结果 | 原生外层身份数 | 产物目录，相对独立候选根目录 |
| --- | --- | --- | --- |
| JIT build | PASS，0 警告、0 错误 | 8 | build-execution |
| JIT tiny1 | 1/1 PASS，3 个 case 身份、1 个 conhost | 4 | tiny-1 |
| JIT full8 | 8/8 PASS，24 个 case 身份、8 个 conhost | 25 | full-8 |
| JIT Windows19 | 19/19 PASS，8.310 秒 | 34 | regression-19 |
| Tooling Windows AOT publish | PASS | 13 | aot-tools |
| Tests Windows AOT publish | PASS | 15 | aot-tests |
| AOT tiny1 | 1/1 PASS，3 个 case 身份、1 个 conhost | 4 | aot-tiny-1 |
| AOT full8 | 8/8 PASS，24 个 case 身份、8 个 conhost | 25 | aot-full-8 |
| AOT Windows19 | 19/19 PASS，7.448 秒 | 34 | aot-regression-19 |

上述所有外层原生回执均为 exit 0、输入/输出 EOF 完成、cleanup PASS、remaining 0、Errors 空；所有实际生命周期 case 原生结果及逐 PID 独立回读通过。计时仅为测试墙钟，未作为性能验收。AOT 检查日志明确 `native_aot=True`，两份已发布 EXE 的 PE CLR data directory 均为 RVA 0、size 0；`aot-pe-verification.json` 记录对应文件 SHA256。原始生命周期报告的 `NativeAotVerified=false` 字段保持原值，AOT 证明独立记录，没有回写报告或为改变该字段重构。

运行时清单 `runtime-manifest.json` 覆盖指定 JIT/AOT 输出、fixture、私有 dotnet host、hostfxr 及私有 Microsoft.NETCore.App 单版本目录的全部文件，实际 217 文件，SHA256 `8824171027034343c1479ce9d96d8679bdc968e9704d7923b76a46e4584d8664`。该清单不声称覆盖 Windows 全部系统 DLL。

### 聚合审计、原始 FAIL 与精确输入选择

生产 AOT Tooling 执行独立审计；每个审计本身再由生产 native executor 包裹，保存 launcher、出生、argv、父链及退出/清理回执。

- build/publish：3 回执、36 身份，live 0、unknown 0。
- JIT 生命周期：11 回执、56 身份，live 0、unknown 0。
- AOT 生命周期：11 回执、56 身份，live 0、unknown 0。
- 初次 Windows19 第二批审计 `native-audits\regression-19-2.json` 为 FAIL，原因是 JsonReaderException：`F` 不是有效 JSON 起始字符。其 native wrapper 为 exit 1，但 EOF 完成、cleanup PASS、remaining 0。原始失败完整保留。

精确定位的非 native 输入分别是 JIT 与 AOT 的 `checks\execution-15-success\result.json`。它们都是 19 字节文本 `FAIL prior evidence`，SHA256 均为 `7ceaee769b8dcd2cc58a187f20c4a6b0ef98ddd98c6029aad6d585d0a7846e0f`，并且没有 registered.json。冻结的 `ExecutionChecks` 中 `execution existing evidence preserved` 明确在 Execute 前写入该标记，并断言返回 FAIL 且原文本未被覆盖。这是先前输出保护测试的惰性标记，不是某次 native execution receipt。

`native-audits-v2\selection.json` 逐项固定全部真实原件路径、字节数、SHA256，以及仅排除上述两个标记的原因。生产 audit 支持精确目录输入，因此无需复制 snapshot、修改原目录或重构代码。JIT 与 AOT 各 18 份真实 native 回执全部进入新审计，共 36 份，包含 18 个预期原始 FAIL；没有因为状态为 FAIL/UNKNOWN 而排除真实回执。六批输入各为 8/8/2 个目录，每批有明确数量、取消与墙钟上限。

新审计 `native-audits-v2\summary.json` 为 `PASS_ALL_36_TRUE_NATIVE_REGRESSION_RECEIPTS_CURRENT_ABSENT_OR_REUSED`：134 个身份记录，live 0、unknown 0。原始 audit FAIL、原始 fixture 标记及全部预期执行 FAIL 都保留。这里的身份记录包含外层与内层的重复记录，不能误写成 134 个不同 OS 进程。

`windows-runtime-summary.json` 保留发现审计输入问题时的 PARTIAL 状态；后续结果另存 `windows-runtime-summary-v2.json`。验收范围为本独立 Windows HANDLE 修复、实际 Git 生命周期和既有 Windows19 回归，未包含新的 Linux 协议、完整 Sezika 推理、训练、性能或发布验收。
