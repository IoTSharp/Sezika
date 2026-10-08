# Ubuntu 有界任务 runner 工程验收

新增 `tools/Invoke-BoundedLinuxProcess.ps1`、`tools/Invoke-BoundedLinux.sh` 与八项生命周期检查。Windows 包装器以结构化参数调用固定 Ubuntu WSL，不插值远端脚本；Linux 记录 PID/start tick、argv、父链及任务 token，再逐身份发送信号。适用范围是保留任务 token 的协作进程；它不是任意进程的隔离沙箱，也不保证捕捉极短寿命中间进程。

## 最终源码

| 文件 | SHA-256 |
| --- | --- |
| Bash | `534A54A2136E6E27A49E69C3F7B792EC90EC2FD8F89FDECF9A96B9065507FC79` |
| PowerShell 包装器 | `BCCBDAE0D5F676FB205C79AD39A0AD57100655146BAD3088E924C2FDDA055197` |
| 生命周期检查 | `784D59A988E341D0571F9DF41DE323862F0ECE6AF1B24DFE9A77C9D1D4F883D3` |

Bash 用 `/proc/uptime` 的单调时钟检查共享工作/清理 deadline。工作最多600秒、清理最多8秒/6 pass，并在同一清理 deadline 中留200ms写结果；每次 inventory 最多4096项，token/argv/父链读取分别有限制。最终 inventory 完整、逐身份 live/unknown/unchecked 全零、没有归属/预算/清理失败，才承认回收完成。缺少结果或未检查不能报告零残留。

v10 只增加清理时的安全延后：fresh identity_liveness 返回0（同身份仍存活）、阶段为 cleanup、理由为 ownership_unreadable 或 identity_refresh_failed 时，不发送信号；先成功写 audit，再记录计数。它不清除已有失败标志，不延长预算，不用缓存 proof 发送信号。其他阶段、身份未知及 syscall 错误仍走失败路径；timer 延后后仍存活也失败。root 三处机械逆变换精确还原 v9，执行 gate 仅版本字面量改变。

## 实际执行

固定 PowerShell7.6.6；没有模型、GPU、编译、下载、安装或网络请求。最终冻结 `freeze-v10.json` SHA `83F447C90A75BBEBE411440C5B8EEF30A0E6CE7B8D2D50080B326314F1F89A55`，root gate SHA `0C592CEE0FDC47653E80181CF4F926F867DCBBECAE2EAA6D92027004E49522A2`。

UTC04:03:56 启动 worker67128，外层190秒；先 tiny1，核原始 Windows bytes/hash/全部参数，再在原 QPC deadline 内立即执行2–8。套件最多8项/180秒、140秒后停新；实际37.930秒，worker成功退出，原始回执 elapsed41.705秒（工具摘要41.729秒）。

| 项目 | 结果 |
| --- | --- |
| 正常 parent/child | completed，完整终核且资源计数全零 |
| 忽略 TERM 的 parent/child | timed_out/124，完整终核；实际延后1次且没有向缺 proof 的父进程发送 KILL |
| 取消文件 | cancelled_file/125，完整终核 |
| 实际 TERM 取消 | cancelled_signal/125，完整终核 |
| 新 session 子进程 | timed_out/124，完整终核 |
| 清理预算到期 fixture | exit127，保留预算耗尽/unchecked，拒绝 complete |
| 截断最终检查 fixture | exit127，保留 unchecked，拒绝 complete |
| 身份 seam 与非 token control | cancelled_file/125；控制进程先保持不受任务信号影响，再通过独立 owner proof 回收 |

第6/7项是受控负例，不能写成完整回收成功。第8项的 mismatch/unreadable 是纯谓词 seam，不代表复现了内核错误或实际 PID 复用。第2项 audit 明确 `liveness=0 action=not_signalled`，随后最终完整零存活才允许成功；不能把 liveness 返回码0解释为零存活。

root 独立复核八份原始 TSV、receipt、Windows bytes/hash/全部 argv，记录 `root-v10-raw-evidence-review.json`。另以不发信号的 reader 分两组核28个已知 Linux 身份，全部 absent；每组44个 `/proc` 项、token live/unknown均0。执行/launcher 的67个 Windows 身份后核无匹配存活；两个外部 reader 自身的退出由各自成功回执另记录，不能倒填到较早的检查时刻。

独立验收 `v10-acceptance-review.md` SHA `EC81B0C1E3163876C517EA62C804B7D30884A50D39F3385BE2C551E17732D8EA`，九份固定输入完整复读且未截断；三处逆变换、真实延后审计、套件和原始资源终核均通过。67条Windows记录包含重复身份，不代表67个不同进程。

全部原始证据位于 `.artifacts/linux-runner-hardening-20261008/`：`suite-execution-v10/`、`suite-execution-v10-workerlogs/20261008-040356-640-root-worker-v10.result.json`、`root-v10-independent-closure-windows/20261008-040512-600-root-v10-closure-0.*` 与 `20261008-040515-718-root-v10-closure-1.*`。Windows runner 回收记录为空；没有新增教师服务、GPU任务、下载、独占 MCP 或待删除临时对象。

## 保留的失败与限制

v1 静态 Parser/TSV 失败、v2 时间窗口拒绝及真实 tiny 归属失败、v7 control 原字段丢失、v8 timer refresh/live1、v9 case2 cleanup_incomplete1 均保留。v8/v9 内部失败的确切阶段仍 unknown；之后进程自然退出不能倒填为当时回收成功。v10 准备有一次早于窗口的拒绝和一次冻结 writer 失败，均未运行测试；首次失败 writer PID 未捕获，明确保留 unknown。

同步 `/proc`/文件/Driver I/O 只能协作检查，不能硬抢占内核阻塞；Windows 完成也不能单独证明 Linux 后代退出，因此仍需要 Linux 身份/token终核。本任务只闭合 runner 工程，不能替代真实模型训练、质量、AOT 或性能验收。Sezika 仅提交，不 push、发布或部署。
