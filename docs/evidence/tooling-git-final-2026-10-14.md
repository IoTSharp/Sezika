# T2b 原生 Git / LFS 本地双平台验收

2026-10-14，Root 实际完成 Windows tiny 1 项及全新完整 8 项、Linux v4 tiny 1 项及全新完整 8 项。两个平台的正式矩阵分母分别为 8，不把 tiny 重复计入 8，也不以当前进程消失替代业务执行成功。Linux 的 7 个独立服务器均正常收到 scoped cancel，退出码 0，没有强停。业务程序为本次冻结的 FDD 包；执行器/审计器使用已验 Native AOT 产物，不由此宣称 Git 业务应用本身已通过 AOT 验收。

本次只覆盖独立本地 fixture 仓库、原生 Git/LFS/HTTP backend、原生 hook 的受限传输和拒绝路径。没有访问真实 origin、发布包、部署、训练或教师服务。模型质量与校准不在验收范围。

离线证据根目录：

```text
G = C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\git-candidate-v3
A = G\native-resource-audit-final-v1
```

## 固定输入与真实执行

| 证据 | 文件（相对 G） | 绑定 / 观察 |
|---|---|---|
| E-01 | `final-source-manifest.json` | 63 文件；SHA-256 `14cb38c3bf0d72c58c9aec9e5554e67e2aae080b309d5025adcd7ef34a50cc53` |
| E-02 | `runtime-readback-v2/runtime-manifest.json` | 四套 FDD 包共 28 文件；SHA-256 `d7e7157073928d5e9dac83df44fc8fcd8682bfcaad2958681d3d6b216311c6a7` |
| E-03 | `linux-final-v4/READY.json`、`request-manifest.json` | 39 份请求；request manifest SHA-256 `c2ebdff99043eabffbc9ebe8dea46b2e2655760872e6aae3b98660aeae5adaf4`；READY 仅表示静态可执行准备 |
| E-04 | `windows-full8-progress.json`、`windows-tiny-1`、`windows-cases/case-01` 至 `case-08` | Root 实际 Windows tiny 及 8/8；独立输出根与原始执行回执保留 |
| E-05 | `linux-final-v4/matrix-progress.json` 与各 case 原件 | Root 实际 Linux tiny 及 8/8；16 个 client/server 内层和 16 个 WSL 外层的退出、EOF、cleanup 均核对通过 |
| E-06 | `windows-final-native-audit-v1/native-audit.json` | Root Windows 当前资源审计：9 回执、658 身份记录、628 不同 PID，全部 ABSENT、live 0、unknown 0 |
| E-07 | `native-resource-audit-final-v1` | 本 worker 的当前资源审计、原件哈希复核、服务器与 launcher 证明，见下文 |

FDD apphost 的 SHA 可能与旧模板相同，不能代表应用代码相同。E-02 独立固定新的 DLL、PDB、deps/runtimeconfig 等完整文件。未来 native `pre-push` 的 ELF image pin 是新 CLI host 的实际 SHA，同时要求复制的 DLL/deps/runtimeconfig 与该 CLI 包匹配；不把 host SHA 当作完整包身份。运行文件的 Ubuntu 原生独立读回在 `runtime-readback-v2/linux-tool-hashes.json`。

Linux v4 的业务 WSL 外层采用默认用户，诊断实际 UID 为 1000，fixture 仓库 UID/GID 为 1000/1000。内层进程上限预先固定为 256，WSL 外层为 32；没有临场无限升限。7 个服务器与 client 使用不同进程、不同输出根，避免共享 Linux 串行执行租约。

## 完整 8 项的业务含义

| case | 实际验收路径 | Windows / Linux |
|---|---|---|
| 1 | 原生 Git 与 git-lfs 的真实版本调用及完整进程回执 | PASS / PASS |
| 2 | 普通文件与 LFS 的混合发布、实际 native pre-push、提交和远端 fixture 字节读回 | PASS / PASS |
| 3 | 全新普通文件 clone 与固定字节核验 | PASS / PASS |
| 4 | 全新 LFS clone、hydrate 与实际字节独立核验 | PASS / PASS |
| 5 | 隔离继承的 Git directory/worktree/index 重定向 | PASS / PASS |
| 6 | 未知 filter 在 staging/commit 前拒绝，原 index/ref 保持 | PASS / PASS |
| 7 | 脚本及额外 hook 在原生操作前拒绝；惰性测试文件从未执行 | PASS / PASS |
| 8 | 缺失 LFS object 的全新 clone 实际失败、进程证据保留、原 object 恢复 | PASS / PASS |

预期拒绝或 clone 失败是对应负例通过的条件，不将底层 FAIL 改写成成功。每个选定 case 的 `case-selection.json` 应读为 `DeclaredCases=8`、对应 `SelectedCase`、`CompletedCases=1`；矩阵再汇总精确 1..8，而不是以 8 份“只跑了第一项”的回执充数。

这些是本地 basic LFS 协议场景，不代表完整 LFS 服务、锁服务、TLS、认证或真实托管平台验收。Linux server final 的 LFS 计数状态在 case 2/4/8 为 `PASS_protocol_fixture_only`，case 3/5/6/7 为 `NOT_RUN`；后者保持原义，普通 Git CGI 或拒绝路径的通过不能改成 LFS 协议通过。

## 当前资源审计与服务器结束证明

审计输入分为实际 v4、历史失败/诊断、手工 launcher 三种证据。`A/plan.json` 与 `execution-plan-correct-platform-v2.json` 固定目录范围；正确平台的 6 批结果在 `progress-correct-platform-v2.json`。每批最多 12 个输入目录，有独立墙钟和取消限制。

| 范围 | 回执 | 身份记录 | 不同 PID | 当前观察 |
|---|---:|---:|---:|---|
| Linux v4，9 client + 7 server | 16 | 547 | 547 | 547 ABSENT；live 0、unknown 0 |
| Linux v3 既有 4 树 + ownership 2 + runtime hash 1 | 7 | 28 | 28 | 28 ABSENT；原业务 FAIL 保留 |
| Windows 的 v4 WSL 外层 | 16 | 80 | 80 | 79 ABSENT、1 REUSED；live 0、unknown 0 |
| Windows 旧外层/诊断 7 树 + 7 手工 server launcher 投影 | 8 | 42 | 42 | 42 ABSENT；投影是 1 份身份回执，不含执行成功结论 |

上述 6 批合计 697 条身份记录；按平台分别去重后为 Linux 575 个 PID、Windows 118 个 PID，即 693 个平台/PID 对。Windows 历史与新范围存在数字 PID 重用，不能把各行去重数相加当作独立进程总数。Root 的 Windows 业务审计 E-06 是另一证据范围，也不能与本表简单相加宣称更多独立进程。

Linux 审计器通过 `-u root` 只读观察跨 UID 的当前原生身份；业务仍为默认 UID 1000。审计没有发信号、终止被审计 PID 或修改运行用户。审计器固定为 Linux v12 AOT SHA-256 `8e94f4b8b12f6bd96130944c446a0fa6d53073a961ccf0ef497bc647ed1b79e5`；Windows AOT 为 `341e075512d56598d0630f6706772155bba31388b7d1e7186a7b6f38bc778a61`。

7 份 `server/server-result.json` 均与计划 scope 和原始 fixture request SHA 一致，`Errors=[]`、`ShutdownReason=SCOPED_CLIENT_CANCEL`、`Status=PASS_server_lifecycle_only`。服务器 final 的 native PID/start ticks/boot ID/父关系/完整 argv 与外层登记相符。client/server 的 4 份内外回执均保留原退出、EOF 和 cleanup；服务器生命周期 PASS 不能单独代表 Git 业务 PASS。逐项核对在 `A/server-final-verification.json`。

7 个 Root 手工 server supervisor 另从真实 `server-outer-result.Launcher` 提取 PID、creation time、父 PID、完整 argv，与 `root-server-supervisor-launch.json` 精确关联，再做当前身份审计。`A/manual-server-launcher-provenance.json` 保存来源；没有从计划配置反造 native 身份，也没有把该身份投影写成 execution PASS。

本 worker 创建的 7 个审计 controller，也分别从其真实 native Launcher 和保留进程 StartTime/父 PID/实际 argv 建立来源。第 8 次审计只查询这些 controller，7/7 ABSENT；最后审计器自身以实际保留进程句柄退出码 0、stdout/stderr EOF 和当前 CIM 已无对象闭合，不继续无穷审计自身。短控制器的可选 CIM 采集可能晚于退出，身份来源仍为原始 native Launcher，绝不由 argv 单独推造。来源及最后证明分别见 `audit-controller-provenance.json` 与 `windows-audit-controller-final-batch-8`。

E-07 的主汇总为 `A/summary.json`，最后 8 次有界尝试及自身 controller 闭合在 `A/summary-final-v3.json`。选定的 60 份既有原件在 `raw-receipt-index.json` 固定 SHA，审计后再次核对全部未变。

## 历史失败及后续通过的边界

Linux v3 的 tiny 及 full case 1 曾通过，full case 2 在 `git add` 返回 128，stderr 明确 `dubious ownership`；测试执行器内层最终退出 134、WSL 外层退出 1，native birth、EOF 与 cleanup 仍通过，server 正常 scoped cancel。case 3..8 在该次运行未处理。原目录 `linux-final-v3` 不修改，当前资源审计也保留两份 native `OriginalStatus=FAIL`。

`linux-ownership-diagnosis-v1` 的原生只读 `id -u`、`stat` 两项实际通过：默认用户 1000，仓库 owner 1000/1000。v3 明确 `-u root` 与 `/mnt/c` owner 不匹配；v4 唯一行为修正是移除 16 份 WSL 外层的 `-u root`，argv 从 14 项变为 12 项，另使用全新路径/scope。没有修改源码、仓库 owner 或全局 `safe.directory`。v4 的实际 8/8 不覆盖 v3 原失败。

本轮首个审计封装使用了与 PowerShell 只读自动变量冲突的名称，误选 Windows observer 查询 Linux 回执，得到 409 条“平台不同”的 UNKNOWN。该原件保留在 `A/linux-batch-1/audit.json`，没有覆盖。改用非系统变量后，正确 Linux 首批位于 `linux-batch-1-correct-platform-v2/audit.json`，409/409 ABSENT。总次数为 1 次误平台 + 6 次正确平台 + 1 次 controller 最终审计，恰为 8；误平台结果不计入 697 条正确观察，也不借后续 PASS 擦除。

| Evidence → Finding → Path | 可复核结论 |
|---|---|
| E-01/E-02/E-03 → F-01 | 固定同一源码与完整 FDD 包，再检查各请求实际路径、用户及预算；host SHA 不是整包证明。 |
| E-04/E-05 → F-02 | 读取 tiny/full 原件和 8 个 case-selection、业务字节及负例回执，才能判断双平台本地 8/8。 |
| E-06/E-07 → F-03 | 原始执行 → scoped server final → 不可变 native 登记 → 当前 ABSENT/REUSED → launcher 关闭，分别核验业务与资源闭合；旧 FAIL/UNKNOWN 仍保留。 |

本文件是本地 T2b 证据，不表示代码审查、后续 retirement、发布或部署已完成。重现必须使用新的隔离输出根，并由 Root 按有界 server/client 控制序列执行；不得覆写此处原件。

## Root 独审和准确提交接线

独立只读审查在 `git-t2b-independent-review-v1/audit.json`：状态 `PASS_T2b_local_synthetic_Git_LFS_fixture_scope`，SHA256 `6b3a5480bd2ae39095ee19f5cfa26f484dcbc07be3bf45945bc64d5fec0d9833`。审查8个源码及28个完整运行文件，无阻断发现；PDB源码checksum绑定未完成，保持 `NOT_VERIFIED`。

Root 新 `git-stage-integration-v4` 使用原63文件及已提交 WindowsLifecycleChecks，Tests Program 从当前HEAD只加Git服务/套件/300秒预算和返回码，保留Windows生命周期接线；MigrationCommands仅加入git命令、typed读取/回执。64输入manifest SHA256 `9eca85a2dda16f1a6caa5dcc9c70d97819dee5b7a5a0517ad6f5dabc63b12848`。实际Windows fresh publish通过，随后baseline32、git tiny1、Windows lifecycle tiny1均通过，4原生回执25身份终核全ABSENT，live0/unknown0；源64项再核不变。首次Root汇总误读不存在的UnknownRecords字段时停止，改用真实Unknown后只重读原PASS审计，没有重跑测试或改写原回执。此步核准确共享接线，完整双平台8项仍由上文原件证明。最终Git只暂存该准确partial，不夹带其他待验套件。